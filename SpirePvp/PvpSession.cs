using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

internal static class PvpSession
{
    public static bool HostingPvp;
    public static bool PreparingRound;
    public static bool SelectingDecks;
    public static bool LocalDeckReady;
    public static DuelOutcome Result;
    public static ulong? Winner;
    public static RunState? GetState(RunManager manager) => (RunState?)AccessTools.Property(typeof(RunManager), "State").GetValue(manager);
    public static bool IsPvp(IRunState? state) => state?.Modifiers.Any(m => m is PvpDuel) == true;
    public static bool IsArena(ICombatState? state) => state?.Encounter is PvpArena;
    public static CombatState TurnState(object turn) => (CombatState)AccessTools.Property(turn.GetType(), "State").GetValue(turn)!;
    public static bool TurnLive(object turn) => (bool)AccessTools.Property(turn.GetType(), "IsLive").GetValue(turn)!;
    public static CancellationToken TurnToken(object turn) => (CancellationToken)AccessTools.Property(turn.GetType(), "Ct").GetValue(turn)!;
    public static Task CallTask(Type type, string name, object instance, params object?[] args)
    {
        var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        return (Task)method.Invoke(instance, args)!;
    }

    public static async Task Prepare(RunManager manager)
    {
        var state = GetState(manager)!;
        if (state.Players.Count != 2) throw new InvalidOperationException("PvP 需要且仅支持两名玩家。");
        Result = DuelOutcome.InProgress;
        Winner = null;
        PreparingRound = true;
        SelectingDecks = true;
        LocalDeckReady = false;
        await manager.SetActInternal(0);
        // Native reward history requires an active map-point entry, even though no map is played.
        state.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Ancient, RoomType.Event, ModelDb.Event<Neow>().Id);
        if (state.Modifiers.OfType<PvpDuel>().Single() is { Development: true, CooperativeDevelopment: false })
        {
            IndependentDevelopment.Ensure(manager);
            var solo = await IndependentDevelopment.Current!.Begin(manager);
            NMapScreen.Instance?.Close(animateOut: false);
            await manager.FadeIn(false);
            // Only this player's draft executes: the other player is in a
            // separate singleplayer run and may already be exploring.
            await SelectNative(LocalContext.GetMe(solo)!);
            LocalDeckReady = true;
            SelectingDecks = false;
            PreparingRound = false;
            NMapScreen.Instance?.SetTravelEnabled(true);
            NMapScreen.Instance?.Open();
            return;
        }
        await manager.EnterRoom(new MapRoom());
        NMapScreen.Instance?.Close(animateOut: false);
        await manager.FadeIn(false);
        // Draft's SelectUnsynchronized still exchanges per-player card choices.
        // Every peer must execute BOTH players' callbacks to consume those messages
        // and advance both choice counters; only the owner's selection UI is shown.
        var me = LocalContext.GetMe(state)!;
        var selections = state.Players.Select(SelectNative).ToArray();
        await DraftCompletion.WaitForBoth(selections, state.Players.ToList().IndexOf(me), () =>
        {
            LocalDeckReady = true;
            NMapScreen.Instance?.Open(isOpenedFromTopBar: true);
        });
        // Each peer has consumed both decks. Exchange an explicit readiness
        // signal as well so no map vote can race the other peer's final UI work.
        await RoundSynchronizer.WaitForBoth(manager.PlayerChoiceSynchronizer, state.Players, me, 0, CancellationToken.None);
        SelectingDecks = false;
        PreparingRound = false;
        NMapScreen.Instance?.SetTravelEnabled(true);
        NMapScreen.Instance?.Open();
        // Remain on the map. Both players explicitly click the boss node to vote;
        // only the native host-authoritative move action starts the duel.
    }

    private static Task SelectNative(Player player)
    {
        // The native modifier callback only reads EventModel.Owner. No Neow option or
        // blessing is executed. Preserve the game's own selection UI and card generation.
        var placeholder = ModelDb.Event<Neow>().ToMutable();
        AccessTools.PropertySetter(typeof(EventModel), nameof(EventModel.Owner)).Invoke(placeholder, [player]);
        ModifierModel mode = player.RunState.Modifiers.First(m => m is Draft or SealedDeck);
        return mode.GenerateNeowOption(placeholder)!();
    }

    public static async Task RoundBarrier(object turn)
    {
        if (!TurnLive(turn)) return;
        var state = TurnState(turn);
        var me = LocalContext.GetMe(state)!;
        var round = me.PlayerCombatState!.TurnNumber;
        if (RoundReadyChannel.Current == null) RoundReadyChannel.Register(RunManager.Instance);
        await RoundReadyChannel.Current!.Wait(state.RunState.ActFloor, round, TurnToken(turn));
        if (!TurnLive(turn)) return;
        PvpUi.RefreshHealthBars();
        PreparingRound = false;
        foreach (var p in state.Players) p.PlayerCombatState!.Phase = PlayerTurnPhase.Play;
        AccessTools.Field(typeof(CombatManager), "_playerActionsDisabled").SetValue(CombatManager.Instance, true);
        AccessTools.PropertySetter(typeof(CombatManager), nameof(CombatManager.PlayerActionsDisabled)).Invoke(CombatManager.Instance, [false]);
        PvpUi.SetStatus(DuelDamage.Enabled(state) ? $"第 {round} 回合 · 伤害累计，双方结束后结算" : $"第 {round} 回合 · 双方可自由出牌");
    }

    public static void CaptureResult(CombatState state)
    {
        Result = DuelRules.Outcome(state.Players[0].Creature.IsAlive, state.Players[1].Creature.IsAlive);
        if (Result == DuelOutcome.Draw && DuelDamage.Enabled(state)) Result = DuelDamage.SimultaneousResult(state);
        Winner = Result switch
        {
            DuelOutcome.FirstWins => state.Players[0].NetId,
            DuelOutcome.SecondWins => state.Players[1].NetId,
            _ => null
        };
    }
}
