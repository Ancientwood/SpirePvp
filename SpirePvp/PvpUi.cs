using Godot;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;

namespace SpirePvp;

internal static class PvpUi
{
    private const string PanelName = "SpirePvpSettings";
    private static Label? _status;
    private static DuelCardLimitSubscription? _cardLimitSubscription;
    internal static void DetachCombatUi()
    { _cardLimitSubscription?.Dispose(); _cardLimitSubscription = null; }
    private sealed class BodyPose(Node2D body)
    {
        public Vector2 Position { get; } = body.Position;
        public float Sign { get; } = Math.Sign(body.Scale.X);
    }
    private static readonly ConditionalWeakTable<Node2D, BodyPose> BodyPoses = new();
    private sealed record AnchorPose(Vector2 Position);
    private static readonly ConditionalWeakTable<Marker2D, AnchorPose> AnchorPoses = new();
    public static void ConfigureLobby(NCustomRunScreen screen)
    {
        var list = screen.GetNode<Control>("%ModifiersList");
        list.Visible = false;
        var parent = list.GetParent();
        var panel = parent.GetNodeOrNull<VBoxContainer>(PanelName);
        if (panel == null)
        {
            panel = new VBoxContainer { Name = PanelName, Position = list.Position, Size = list.Size };
            panel.AddThemeConstantOverride("separation", 10);
            panel.AddChild(new Label { Text = "PvP · 双人构筑决斗", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            var selector = new OptionButton { Name = "Construction", CustomMinimumSize = new Vector2(330, 60) };
            selector.AddItem("选牌：10 次三选一", 0);
            selector.AddItem("现开套牌：30 张选 10 张", 1);
            selector.AddItem("选牌：15 次三选一", 2);
            selector.AddItem("选牌：20 次三选一", 3);
            selector.AddItem("自定义开局：白／蓝／金分批现开", 4);
            panel.AddChild(selector);
            var development = new CheckButton
            {
                Name = "Development", Text = "PvP 战前发育（12 层）",
                TooltipText = "发育方式由模组配置中的「联机发育」决定。第 6 层宝箱、第 11 层商店、第 12 层火堆；其他问号、宝箱、火堆概率 4∶3∶3。"
            };
            development.Toggled += enabled =>
            {
                var lobby = screen.Lobby;
                if (lobby == null || lobby.NetService.Type == NetGameType.Client || !lobby.Modifiers.Any(m => m is PvpDuel)) return;
                lobby.SetReady(false);
                var rules = lobby.Modifiers.OfType<PvpDuel>().Single();
                lobby.SetModifiers(PvpLobbyModifiers.Create(PvpLobbyModifiers.GetMode(lobby.Modifiers), DuelMultipliers.FromModifier(rules), enabled, rules.CardLimitEnabled, rules.MaxCardsPerTurn, rules.DeferredDamage, enabled ? PvpSettings.Current.CooperativeDevelopment : rules.CooperativeDevelopment, rules.CustomCommonCards, rules.CustomUncommonCards, rules.CustomRareCards, rules.SameSeed));
                PvpSettings.RememberDevelopment(enabled);
            };
            panel.AddChild(development);
            var sameSeed = new CheckButton { Name = "SameSeed", Text = "相同种子",
                TooltipText = "同角色、同牌组及相同操作顺序使用相同构筑、事件与洗牌随机序列；双方独立推进随机数，操作快慢不影响对方。" };
            sameSeed.Toggled += enabled =>
            {
                var lobby = screen.Lobby;
                if (lobby == null || lobby.NetService.Type == NetGameType.Client) return;
                var rules = lobby.Modifiers.OfType<PvpDuel>().Single();
                lobby.SetReady(false);
                lobby.SetModifiers(PvpLobbyModifiers.Create(PvpLobbyModifiers.GetMode(lobby.Modifiers), DuelMultipliers.FromModifier(rules),
                    rules.Development, rules.CardLimitEnabled, rules.MaxCardsPerTurn, rules.DeferredDamage, rules.CooperativeDevelopment,
                    rules.CustomCommonCards, rules.CustomUncommonCards, rules.CustomRareCards, enabled));
                PvpModConfig.相同种子 = enabled;
            };
            panel.AddChild(sameSeed);
            panel.AddChild(new CheckButton { Text = "局内 PvP（后续开放）", Disabled = true,
                TooltipText = "暂未开放；普通多人爬塔不会触发 PvP。" });
            var randomCharacter = new Button
            {
                Name = "RandomCharacter", Text = "随机角色", CustomMinimumSize = new Vector2(330, 54),
                TooltipText = "从本方已解锁角色中随机选择；只改变自己的角色。"
            };
            randomCharacter.Pressed += () =>
            {
                var lobby = screen.Lobby;
                if (lobby == null || lobby.LocalPlayer.isReady || !lobby.Modifiers.Any(m => m is PvpDuel)) return;
                AccessTools.Method(typeof(NCustomRunScreen), "RandomizeLocalCharacter").Invoke(screen, null);
            };
            panel.AddChild(randomCharacter);
            panel.AddChild(new Label { Name = "CardLimitSummary", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            panel.AddChild(new Label
            {
                Name = "CombatFlow",
                Text = "开启发育：各自选牌、各自前进，最后会合。\n决斗中同步恢复能量、抽牌，自由抢速出牌。\n支持两人；双方须安装相同版本 Mod。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });
            selector.ItemSelected += index =>
            {
                var lobby = screen.Lobby;
                if (lobby == null || lobby.NetService.Type == NetGameType.Client || !lobby.Modifiers.Any(m => m is PvpDuel)) return;
                lobby.SetReady(false);
                var rules = lobby.Modifiers.OfType<PvpDuel>().Single();
                lobby.SetModifiers(PvpLobbyModifiers.Create((int)index, DuelMultipliers.FromModifier(rules), rules.Development, rules.CardLimitEnabled, rules.MaxCardsPerTurn, rules.DeferredDamage, rules.CooperativeDevelopment, rules.CustomCommonCards, rules.CustomUncommonCards, rules.CustomRareCards, rules.SameSeed));
                PvpSettings.RememberConstruction((int)index);
            };
            parent.AddChild(panel);
        }
        panel.Visible = true;
        var limitRules = screen.Lobby.Modifiers.OfType<PvpDuel>().Single();
        var sameSeedOption = panel.GetNode<CheckButton>("SameSeed");
        sameSeedOption.SetPressedNoSignal(limitRules.SameSeed);
        sameSeedOption.Disabled = screen.Lobby.NetService.Type == NetGameType.Client;
        panel.GetNode<Label>("CombatFlow").Text = (!limitRules.Development ? "发育已关闭：构筑后直接决斗。\n"
            : limitRules.CooperativeDevelopment ? "联机发育：共同前进，事件、宝箱与商店按原版多人规则。\n"
            : "独立发育：各自选牌、各自前进，最后会合。\n")
            + (limitRules.DeferredDamage ? "伤害累计、格挡立即生效；双方结束后统一结算。\n" : "同步恢复能量、抽牌，自由抢速出牌。\n")
            + "支持两人；双方须安装相同版本 Mod。";
        panel.GetNode<Label>("CombatFlow").Text += limitRules.SameSeed ? "\n相同种子：开启（同角色、同决策得到同结果）" : "\n相同种子：关闭";
        panel.GetNode<Label>("CardLimitSummary").Text = limitRules.CardLimitEnabled
            ? $"每人每回合最多出牌 {limitRules.MaxCardsPerTurn} 张（房主规则）" : "每回合出牌数不限（房主规则）";
        if (PvpLobbyModifiers.GetMode(screen.Lobby.Modifiers) == 4)
            panel.GetNode<Label>("CardLimitSummary").Text += $"\n自定义开局：白卡 {limitRules.CustomCommonCards * 3} 选 {limitRules.CustomCommonCards} · 蓝卡 {limitRules.CustomUncommonCards * 3} 选 {limitRules.CustomUncommonCards} · 金卡 {limitRules.CustomRareCards * 3} 选 {limitRules.CustomRareCards}";
        var option = panel.GetNode<OptionButton>("Construction");
        option.Select(PvpLobbyModifiers.GetMode(screen.Lobby.Modifiers));
        option.Disabled = screen.Lobby.NetService.Type == NetGameType.Client;
        var developmentOption = panel.GetNode<CheckButton>("Development");
        developmentOption.SetPressedNoSignal(screen.Lobby.Modifiers.OfType<PvpDuel>().Single().Development);
        developmentOption.Disabled = screen.Lobby.NetService.Type == NetGameType.Client;
        UpdateRandomCharacter(screen);
        var multipliers = DuelMultipliers.FromModifier(screen.Lobby.Modifiers.OfType<PvpDuel>().Single());
        var summary = panel.GetNodeOrNull<Label>("Multipliers");
        if (summary == null) { summary = new Label { Name = "Multipliers" }; panel.AddChild(summary); }
        summary.Text = $"房主规则：生命 ×{multipliers.Health:0.#} · 格挡 ×{multipliers.Block:0.#}\n召唤 ×{multipliers.Summon:0.#} · 受到灾厄 ×{multipliers.Doom:0.#} · 受到中毒 ×{multipliers.Poison:0.#}\n壁垒：{(multipliers.Barricade ? "开启" : "关闭")}";
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summary.Text += limitRules.DeferredDamage ? "\n伤害：回合末统一结算" : "\n伤害：实时手速对拼";
        var random = AccessTools.Field(typeof(NCustomRunScreen), "_randomizeButton").GetValue(screen) as Control;
        if (random != null) random.Visible = false;
    }
    public static void UpdateRandomCharacter(NCustomRunScreen screen)
    {
        var list = screen.GetNodeOrNull<Control>("%ModifiersList");
        var button = list?.GetParent().GetNodeOrNull<VBoxContainer>(PanelName)?.GetNodeOrNull<Button>("RandomCharacter");
        if (button != null) button.Disabled = screen.Lobby == null || screen.Lobby.LocalPlayer.isReady;
    }
    public static void RestoreLobby(NCustomRunScreen screen)
    {
        var list = screen.GetNode<Control>("%ModifiersList");
        list.Visible = true;
        var panel = list.GetParent().GetNodeOrNull<Control>(PanelName);
        if (panel != null) panel.Visible = false;
        if (AccessTools.Field(typeof(NCustomRunScreen), "_randomizeButton").GetValue(screen) is Control random) random.Visible = true;
    }
    public static void SetupCombat(CombatState state)
    {
        DetachCombatUi();
        var room = NCombatRoom.Instance;
        if (room == null) return;
        _status = new Label
        {
            Name = "SpirePvpStatus", Text = "等待双方完成抽牌…",
            Position = new Vector2(600, 115), Size = new Vector2(720, 50),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _status.AddThemeFontSizeOverride("font_size", 28);
        room.AddChild(_status);
        if (state.Modifiers.OfType<PvpDuel>().FirstOrDefault() is { CardLimitEnabled: true } rules)
        {
            var counter = new Label { Name = "SpirePvpCardLimit", Position = new Vector2(600, 160), Size = new Vector2(720, 40),
                HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            counter.AddThemeFontSizeOverride("font_size", 24);
            room.AddChild(counter);
            var subscription = new DuelCardLimitSubscription(CombatManager.Instance.History, state, LocalContext.NetId,
                rules.MaxCardsPerTurn, text => { if (GodotObject.IsInstanceValid(counter)) counter.Text = text; });
            _cardLimitSubscription = subscription;
            counter.TreeExiting += subscription.Dispose;
        }
        PositionPlayers(room);
        DuelDamage.RefreshIntents();
    }
    public static void PositionPlayers(NCombatRoom room)
    {
        if (!LocalContext.NetId.HasValue) return;
        foreach (var node in room.CreatureNodes)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsNodeReady()) continue;
            var owner = node.Entity.Player ?? node.Entity.PetOwner;
            if (owner == null) continue;
            var petIndex = node.Entity.PetOwner == null ? -1 : room.CreatureNodes
                .Where(n => n.Entity.PetOwner == owner).OrderBy(n => n.Entity.CombatId).ToList().IndexOf(node);
            var placement = DuelLayout.Place(LocalContext.NetId.Value, owner.NetId, petIndex);
            node.GlobalPosition = room.GetGlobalTransform() * new Vector2(room.Size.X * placement.X, room.Size.Y * placement.Y);
            FaceInward(node, placement.Mirror);
            node.ToggleIsInteractable(true);
        }
        if (room.IsNodeReady())
            AccessTools.Method(typeof(NCombatRoom), "UpdateCreatureNavigation").Invoke(room, null);
        DuelDamage.RefreshIntents();
    }
    private static void FaceInward(NCreature node, bool mirror)
    {
        var body = node.Body;
        if (body == null) return;
        var original = BodyPoses.GetValue(body, value => new BodyPose(value));
        // Flip artwork around its center; keep health bars, hitboxes and text upright.
        body.Scale = new Vector2(Math.Abs(body.Scale.X) * original.Sign * (mirror ? -1 : 1), body.Scale.Y);
        var bounds = node.Visuals.Bounds;
        var centerX = bounds.Position.X + bounds.Size.X * .5f;
        body.Position = new Vector2(DuelLayout.MirrorAnchorX(original.Position.X, centerX, mirror), original.Position.Y);
        // CenterPos can be outside the artwork. Such markers do not inherit the
        // body's mirror, unlike markers already attached to its skeleton.
        foreach (var marker in new Marker2D?[] { node.Visuals.VfxSpawnPosition, node.Visuals.OrbPosition, node.Visuals.TalkPosition })
        {
            if (marker == null || !GodotObject.IsInstanceValid(marker) || body.IsAncestorOf(marker)) continue;
            var anchor = AnchorPoses.GetValue(marker, value => new(node.Visuals.ToLocal(value.GlobalPosition)));
            marker.GlobalPosition = node.Visuals.ToGlobal(new Vector2(
                DuelLayout.MirrorAnchorX(anchor.Position.X, centerX, mirror), anchor.Position.Y));
        }
        if (node.OrbManager != null) AccessTools.Method(typeof(NCreature), "SetOrbManagerPosition").Invoke(node, null);
    }
    public static void ShowCreatureState(NCreature node)
    {
        if (node.Entity.IsDead) return;
        AccessTools.Field(typeof(NCreature), "_isRemotePlayerOrPet").SetValue(node, false);
        if (AccessTools.Field(typeof(NCreature), "_stateDisplay").GetValue(node) is NCreatureStateDisplay display)
        {
            AccessTools.Method(typeof(NCreatureStateDisplay), "RefreshValues").Invoke(display, null);
            display.AnimateIn(HealthBarAnimMode.FromHidden);
        }
        node.ToggleIsInteractable(true);
    }
    public static void RefreshHealthBars()
    {
        var room = NCombatRoom.Instance;
        if (room == null) return;
        foreach (var node in room.CreatureNodes)
            if (GodotObject.IsInstanceValid(node) && node.IsNodeReady()) ShowCreatureState(node);
    }
    public static void SetStatus(string text)
    {
        if (_status != null && GodotObject.IsInstanceValid(_status)) _status.Text = text;
    }
    public static void ShowResult(string? message = null)
    {
        DetachCombatUi();
        Node? root = (Node?)NRun.Instance ?? NCombatRoom.Instance;
        if (root == null || root.HasNode("SpirePvpResultLayer")) return;
        var localId = DuelResultChannel.FinishedLocalId ?? LocalContext.NetId;
        var text = message ?? (PvpSession.Result == DuelOutcome.Draw ? "平局" : PvpSession.Winner == localId ? "决斗胜利" : "决斗失败");
        SetStatus(text);
        var layer = new CanvasLayer { Name = "SpirePvpResultLayer", Layer = 100, ProcessMode = Node.ProcessModeEnum.Always };
        root.AddChild(layer);
        var overlay = new PanelContainer { Name = "SpirePvpResult", ZIndex = 100,
            ProcessMode = Node.ProcessModeEnum.Always, MouseFilter = Control.MouseFilterEnum.Stop };
        layer.AddChild(overlay);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        overlay.AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 180) };
        box.AddThemeConstantOverride("separation", 36);
        center.AddChild(box);
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 42);
        box.AddChild(label);
        var back = new Button { Text = "返回主菜单", CustomMinimumSize = new Vector2(360, 64) };
        back.Pressed += () => TaskHelper.RunSafely(ReturnFromResult(back));
        box.AddChild(back);
        back.GrabFocus();
    }

    private static async Task ReturnFromResult(Button back)
    {
        if (back.Disabled) return;
        back.Disabled = true;
        try
        {
            if (NGame.Instance == null) throw new InvalidOperationException("游戏主节点不可用。");
            DetachCombatUi();
            back.GetTree().Paused = false;
            await NGame.Instance.ReturnToMainMenuAfterRun().WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (Exception error)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[SpirePvp] 返回主菜单失败：{error}");
            try
            {
                if (NGame.Instance?.Transition is { } transition && GodotObject.IsInstanceValid(transition))
                {
                    (AccessTools.Field(typeof(NTransition), "_tween").GetValue(transition) as Tween)?.Kill();
                    transition.Hide();
                    transition.MouseFilter = Control.MouseFilterEnum.Ignore;
                    AccessTools.PropertySetter(typeof(NTransition), nameof(NTransition.InTransition)).Invoke(transition, [false]);
                }
            }
            finally
            {
                if (GodotObject.IsInstanceValid(back)) back.Disabled = false;
                SetStatus("返回主菜单失败，请重试");
            }
        }
    }

    public static void ShowDevelopmentFailure(string reason)
    {
        var root = NRun.Instance;
        if (root == null || root.HasNode("SpirePvpDevelopmentFailure")) return;
        var overlay = new PanelContainer { Name = "SpirePvpDevelopmentFailure", ZIndex = 100,
            MouseFilter = Control.MouseFilterEnum.Stop };
        root.AddChild(overlay);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        overlay.AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(600, 200) };
        box.AddThemeConstantOverride("separation", 30);
        center.AddChild(box);
        box.AddChild(new Label { Text = reason, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center });
        var back = new Button { Text = "返回主菜单", CustomMinimumSize = new Vector2(360, 64) };
        back.Pressed += () => { back.Disabled = true; if (NGame.Instance != null)
            TaskHelper.RunSafely(NGame.Instance.ReturnToMainMenuAfterRun()); };
        box.AddChild(back);
    }
}
