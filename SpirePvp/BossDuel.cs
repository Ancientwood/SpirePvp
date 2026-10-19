using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpirePvp;

public sealed record DuelHealthSnapshot(Player Player, decimal Hp, decimal MaxHp)
{
    public static DuelHealthSnapshot Capture(Player p) => new(p, p.Creature.CurrentHp, p.Creature.MaxHp);
    public async Task Restore()
    {
        await CreatureCmd.SetMaxHp(Player.Creature, MaxHp);
        await CreatureCmd.SetCurrentHp(Player.Creature, Hp);
    }
}

internal static class BossDuel
{
    internal static bool Active;
    internal static bool Fighting;
    internal static bool Bypass;
    internal static DuelMultipliers Multipliers = DuelMultipliers.Default;
    private static CancellationTokenSource? _cancellation;
    private static TaskCompletionSource? _combatDone;
    private static Control? _overlay;

    // Temporarily disabled: no EnterNextAct patch, choices, or extra combat.
    internal static bool Eligible(IRunState? state) => false;
    internal static Task Begin(RunManager manager) => Task.CompletedTask;

    private static async Task Transition(RunManager manager)
    {
        var state = PvpSession.GetState(manager)!;
        _cancellation = new CancellationTokenSource();
        manager.NetService.Disconnected += OnDisconnected;
        var token = _cancellation.Token;
        DuelHealthSnapshot[]? health = null;
        bool participated = false;
        try
        {
            var me = LocalContext.GetMe(state)!;
            // Clients send false; only the host's persisted setting can enable this stage.
            // Disabled rooms still consume this same small handshake on both peers.
            bool isHost = manager.NetService.Type != NetGameType.Client;
            var flags = await PvpVotes.Exchange(manager.PlayerChoiceSynchronizer, state.Players, me,
                () => Task.FromResult(isHost && PvpSettings.Current.BossDuels), token);
            if (flags.Any(enabled => enabled))
            {
                Multipliers = await PvpVotes.HostMultipliers(manager.PlayerChoiceSynchronizer, state.Players, me,
                    isHost, isHost ? PvpSettings.Current.Multipliers : DuelMultipliers.Default, token);
                // Boss rewards are already complete; remove their waiting overlay
                // before putting the opt-in prompt on the run screen.
                NOverlayStack.Instance?.Clear();
                var votes = await PvpVotes.Exchange(manager.PlayerChoiceSynchronizer, state.Players, me,
                    () => Prompt("幕间 PvP · 可选决斗", $"生命 ×{Multipliers.Health:0.#} · 格挡 ×{Multipliers.Block:0.#}，双方同意才开打。\n胜者获得一个遗物、一瓶药水和一次精英卡牌奖励。\n结束后恢复入场前生命与生命上限，继续下一幕。药水照常消耗。",
                        "参加决斗", "跳过，继续爬塔", token), token);
                ClosePrompt();
                if (votes.All(yes => yes))
                {
                    health = state.Players.Select(DuelHealthSnapshot.Capture).ToArray();
                    Active = true;
                    Fighting = true;
                    participated = true;
                    PvpSession.Result = DuelOutcome.InProgress;
                    PvpSession.Winner = null;
                    PvpSession.PreparingRound = true;
                    _combatDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    await RoundSynchronizer.WaitForBoth(manager.PlayerChoiceSynchronizer, state.Players, me, 0, token);
                    await manager.FadeOut();
                    // Exit the finished boss through the normal room stack. This
                    // resets its CombatManager and detaches both player creatures.
                    await manager.EnterRoom(new MapRoom());
                    NMapScreen.Instance?.Close(animateOut: false);
                    // Both peers enter together
                    // through the native combat checksum/loading barrier, never a local debug move.
                    await manager.EnterRoomWithoutExitingCurrentRoom(
                        new CombatRoom(ModelDb.Encounter<PvpArena>().ToMutable(), state)
                        { ShouldResumeParentEventAfterCombat = false }, fadeToBlack: true);
                    await _combatDone.Task.WaitAsync(token);
                    foreach (var snapshot in health) await snapshot.Restore();
                    health = null;
                    await PvpVotes.Exchange(manager.PlayerChoiceSynchronizer, state.Players, me,
                        () => Prompt(PvpSession.Winner == null ? "决斗平局" : PvpSession.Winner == me.NetId ? "决斗胜利" : "决斗失败",
                            PvpSession.Winner == null ? "双方生命已恢复。平局不发放奖励，继续爬塔。" : "双方生命已恢复。胜者领取奖励后，双方继续下一幕。",
                            "继续", null, token), token);
                    ClosePrompt();
                    if (PvpSession.Winner is { } winnerId)
                    {
                        var winner = state.Players.Single(p => p.NetId == winnerId);
                        // Offer on ALL peers; native RewardsSetSynchronizer handles the owner's
                        // screen, relic grab bags, potion slots and card selection replication.
                        if (winner != me) ShowWaiting("等待胜者领取决斗奖励…");
                        await new RewardsSet(winner).WithCustomRewards(CreateRewards(winner)).Offer().WaitAsync(token);
                        ClosePrompt();
                    }
                    await RoundSynchronizer.WaitForBoth(manager.PlayerChoiceSynchronizer, state.Players, me, 0, token);
                    Active = false;
                }
            }
            token.ThrowIfCancellationRequested();
            // Re-enter the original method exactly once; the next act still uses its
            // original map, ancient, act-change hooks, save and loading sequence.
            Bypass = true;
            Task next;
            try { next = manager.EnterNextAct(); }
            finally { Bypass = false; }
            await next;
            if (participated && state == PvpSession.GetState(manager))
                await SaveManager.Instance.SaveRun(null);
        }
        finally
        {
            try
            {
                ClosePrompt();
                if (health != null && PvpSession.GetState(manager) == state)
                    foreach (var snapshot in health) await snapshot.Restore();
            }
            finally
            {
                Active = false;
                Fighting = false;
                PvpSession.PreparingRound = false;
                _combatDone = null;
                manager.NetService.Disconnected -= OnDisconnected;
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }
    }

    internal static List<Reward> CreateRewards(Player winner) =>
    [
        new RelicReward(winner), new PotionReward(winner),
        new CardReward(CardCreationOptions.ForRoom(winner, RoomType.Elite)
            .WithFlags(CardCreationFlags.IsFromCombat), 3, winner)
    ];

    internal static void CombatFinished()
    {
        Fighting = false;
        _combatDone?.TrySetResult();
    }
    internal static void CombatFailed(Exception error) => _combatDone?.TrySetException(error);
    private static void OnDisconnected(NetErrorInfo error) => _cancellation?.Cancel();
    internal static void Reset()
    {
        Multipliers = DuelMultipliers.Default;
        _cancellation?.Cancel();
        _combatDone?.TrySetCanceled();
        ClosePrompt();
        Active = false;
        Fighting = false;
        Bypass = false;
    }

    private static Task<bool> Prompt(string title, string text, string accept, string? decline, CancellationToken token)
    {
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var box = ShowPanel(title, text);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddChild(buttons);
        var yes = new Button { Text = accept, CustomMinimumSize = new Vector2(300, 64) };
        var no = decline == null ? null : new Button { Text = decline, CustomMinimumSize = new Vector2(300, 64) };
        void Choose(bool value)
        {
            yes.Disabled = true;
            if (no != null) no.Disabled = true;
            box.GetNode<Label>("Description").Text = "等待另一名玩家确认…";
            decision.TrySetResult(value);
        }
        yes.Pressed += () => Choose(true);
        buttons.AddChild(yes);
        if (no != null) { no.Pressed += () => Choose(false); buttons.AddChild(no); }
        yes.GrabFocus();
        return decision.Task.WaitAsync(token);
    }

    private static void ShowWaiting(string text) => ShowPanel("幕间 PvP", text);
    private static VBoxContainer ShowPanel(string title, string text)
    {
        ClosePrompt();
        if (NRun.Instance == null) throw new InvalidOperationException("找不到当前游戏画面。");
        _overlay = new PanelContainer { Name = "SpirePvpInterlude", ZIndex = 300, MouseFilter = Control.MouseFilterEnum.Stop };
        NRun.Instance.AddChild(_overlay);
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        _overlay.AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(850, 200) };
        box.AddThemeConstantOverride("separation", 32);
        center.AddChild(box);
        var label = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 40);
        box.AddChild(label);
        box.AddChild(new Label { Name = "Description", Text = text, HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        return box;
    }
    private static void ClosePrompt()
    {
        if (_overlay != null && GodotObject.IsInstanceValid(_overlay))
        {
            _overlay.Visible = false;
            _overlay.QueueFree();
        }
        _overlay = null;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.ShouldStopCombatFromEnding))]
internal static class ArenaVictoryPatch
{
    static bool Prefix(ICombatState combatState, ref bool __result)
    {
        if (!PvpSession.IsArena(combatState)) return true;
        __result = combatState.Players.Count == 2 && combatState.Players.All(p => p.Creature.IsAlive);
        return false;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
internal static class ArenaFortitudePatch
{
    static void Postfix(ICombatState? combatState, ref Task __result)
    {
        if (PvpSession.IsArena(combatState)) __result = Apply(__result, combatState!);
    }
    private static async Task Apply(Task original, ICombatState state)
    {
        await original;
        foreach (var p in state.Players)
            await PowerCmd.Apply<PvpFortitudePower>(new BlockingPlayerChoiceContext(), p.Creature, 1, null, null);
        PvpUi.RefreshHealthBars();
    }
}

// Arena completion is handled by the duel result/continuation UI, never the
// native "no rewards -> open map" path that could advance one peer prematurely.
[HarmonyPatch(typeof(NCombatUi), "OnCombatWon")]
internal static class ArenaProceedPatch
{
    static bool Prefix(CombatRoom room) => !PvpSession.IsArena(room.CombatState);
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
internal static class InterludeMapOpenPatch
{
    static bool Prefix() => !BossDuel.Active;
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetTravelEnabled))]
internal static class InterludeMapTravelPatch
{
    static void Prefix(ref bool enabled)
    {
        if (BossDuel.Active) enabled = false;
    }
}

[HarmonyPatch]
internal static class InterludeSavePatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(SaveManager), nameof(SaveManager.SaveRun),
        [typeof(AbstractRoom), typeof(bool)]);
    static bool Prefix(ref Task __result)
    {
        if (!BossDuel.Active) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
internal static class InterludeVictoryRewardsPatch
{
    static bool Prefix(ICombatState? combatState, ref Task __result)
    {
        if (!BossDuel.Active || !PvpSession.IsArena(combatState)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
