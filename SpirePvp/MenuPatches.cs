using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Platform.Null;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpirePvp;

internal static class HostMenuPatch
{
    internal static void StartHost(NSubmenu menu, Control button, ushort lanPort)
    {
        if (PvpSession.HostingPvp || NativeLanHost.Hosting) return;
        LanMenu.SetEnabled(button, false);
        PvpSession.HostingPvp = true;
        Control? overlay = null;
        NetHostGameService? service = null;
        bool handedOff = false;
        try
        {
            overlay = (Control)AccessTools.Field(menu.GetType(), "_loadingOverlay").GetValue(menu)!;
            var stack = (NSubmenuStack)AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(menu)!;
            overlay.Visible = true;
            service = new NetHostGameService(PeerVersionInfo.LocalDefault());
            var error = LanHostBinding.StartHost(service, lanPort);
            if (error.HasValue)
            {
                var popup = NErrorPopup.Create(error.Value);
                if (popup != null) NModalContainer.Instance?.Add(popup);
                return;
            }
            LanRunSaves.Activate(native: false);
            var screen = stack.GetSubmenuType<NCustomRunScreen>();
            screen.InitializeMultiplayerAsHost(service, 2);
            var settings = PvpSettings.Current;
            screen.Lobby.SetModifiers(PvpLobbyModifiers.Create(settings.LastConstructionMode, settings.Multipliers, settings.Development, settings.CardLimitEnabled, settings.MaxCardsPerTurn, settings.DeferredDamage, settings.CooperativeDevelopment, settings.CustomCommonCards, settings.CustomUncommonCards, settings.CustomRareCards, settings.SameSeed));
            screen.Lobby.SyncAscensionChange(0);
            PvpUi.ConfigureLobby(screen);
            stack.Push(screen);
            handedOff = true;
        }
        catch (Exception exception)
        {
            Log.Error("[SpirePvp] PvP LAN host failed: " + exception);
            var popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            if (popup != null) NModalContainer.Instance?.Add(popup);
        }
        finally
        {
            PvpSession.HostingPvp = false;
            if (GodotObject.IsInstanceValid(overlay)) overlay!.Visible = false;
            if (GodotObject.IsInstanceValid(button)) LanMenu.SetEnabled(button, true);
            if (!handedOff) LanRunSaves.ClearActive();
            if (!handedOff && service != null) service.Disconnect(NetError.Quit, true);
        }
    }
}

[HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready))]
internal static class LanMenuPatch
{
    static void Postfix(NMultiplayerSubmenu __instance)
    {
        Configure(__instance, "HostButton", "HOST", "原版 LAN · 创建 / 继续", "双人原版 · A1 房主", true, true);
        Configure(__instance, "JoinButton", "JOIN", "原版 LAN · 加入", "双人原版 · A2 客机", true, false);
        Configure(__instance, "LoadButton", "HOST", "PvP LAN · 创建", "双人构筑决斗 · 房主", false, true);
        Configure(__instance, "AbandonButton", "JOIN", "PvP LAN · 加入", "双人构筑决斗 · 客机", false, false);
        var container = __instance.GetNode<HBoxContainer>("ButtonContainer");
        container.OffsetLeft = -780;
        container.OffsetRight = 780;
        string[] order = ["HostButton", "JoinButton", "LoadButton", "AbandonButton"];
        for (int index = 0; index < order.Length; index++)
        {
            var button = container.GetNode<Control>(order[index]);
            container.MoveChild(button, index);
            button.FocusNeighborLeft = container.GetNode<Control>(order[(index + order.Length - 1) % order.Length]).GetPath();
            button.FocusNeighborRight = container.GetNode<Control>(order[(index + 1) % order.Length]).GetPath();
        }
        container.GetNode<TextureRect>("LoadButton/Icon").Texture = container.GetNode<TextureRect>("HostButton/Icon").Texture;
        container.GetNode<TextureRect>("AbandonButton/Icon").Texture = container.GetNode<TextureRect>("JoinButton/Icon").Texture;
        Refresh(__instance);
    }

    private static void Configure(NMultiplayerSubmenu menu, string node, string icon, string title, string description,
        bool native, bool host)
    {
        var button = menu.GetNode<NSubmenuButton>("ButtonContainer/" + node);
        foreach (var connection in button.GetSignalConnectionList(NClickableControl.SignalName.Released))
            button.Disconnect(NClickableControl.SignalName.Released, connection["callable"].AsCallable());
        button.SetMeta("spirepvp_title", title);
        button.SetMeta("spirepvp_description", description);
        button.SetIconAndLocalization(icon);
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
        {
            if (host) LanMenu.ShowHost(menu, button, native);
            else LanMenu.ShowJoin(menu, native);
        }));
    }

    internal static void Refresh(NMultiplayerSubmenu menu)
    {
        foreach (string name in new[] { "HostButton", "JoinButton", "LoadButton", "AbandonButton" })
            menu.GetNode<Control>("ButtonContainer/" + name).Visible = true;
    }
}

[HarmonyPatch(typeof(NMultiplayerSubmenu), "UpdateButtons")]
internal static class LanMenuButtonsPatch
{
    static void Postfix(NMultiplayerSubmenu __instance) => LanMenuPatch.Refresh(__instance);
}

[HarmonyPatch(typeof(NMultiplayerSubmenu), "OnSubmenuShown")]
internal static class LanMenuSessionResetPatch
{
    static void Postfix() => LanRunSaves.ClearActive();
}

[HarmonyPatch(typeof(NMultiplayerSubmenu), "InitialFocusedControl", MethodType.Getter)]
internal static class LanMenuFocusPatch
{
    static bool Prefix(NMultiplayerSubmenu __instance, ref Control __result)
    {
        __result = __instance.GetNode<Control>("ButtonContainer/HostButton");
        return false;
    }
}

[HarmonyPatch(typeof(NSubmenuButton), nameof(NSubmenuButton.RefreshLabels))]
internal static class LanMenuLabelsPatch
{
    static void Postfix(NSubmenuButton __instance)
    {
        if (!__instance.HasMeta("spirepvp_title")) return;
        __instance.GetNode<MegaLabel>("%Title").SetTextAutoSize(__instance.GetMeta("spirepvp_title").AsString());
        __instance.GetNode<MegaRichTextLabel>("%Description").Text = __instance.GetMeta("spirepvp_description").AsString();
    }
}

[HarmonyPatch(typeof(NJoinFriendScreen), nameof(NJoinFriendScreen._Ready))]
internal static class LanJoinPagePatch
{
    static void Postfix(NJoinFriendScreen __instance)
    {
        var panel = __instance.GetNode<Control>("Panel");
        foreach (var child in panel.GetChildren().OfType<Control>()) child.Hide();
        __instance.GetNode<Control>("%RefreshButton").Hide();
        __instance.GetNode<MegaLabel>("TitleLabel").SetTextAutoSize("双人 LAN 联机");
        var margin = new MarginContainer { Name = "SpireLanJoin" };
        foreach (string edge in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride("margin_" + edge, 28);
        panel.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var fields = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        fields.AddThemeConstantOverride("separation", 18);
        margin.AddChild(fields);
        var mode = new OptionButton { Name = "Mode", CustomMinimumSize = new Vector2(0, 54) };
        mode.AddItem("双人原版 LAN · A2", 0);
        mode.AddItem("双人 PvP LAN", 1);
        fields.AddChild(mode);
        fields.AddChild(new Label { Text = "房主 IPv4 / IPv4:端口" });
        var address = new LineEdit
        {
            Name = "Address", Text = PvpSettings.Current.LastLanAddress ?? string.Empty,
            PlaceholderText = "192.168.1.2:33771", MaxLength = 64, CustomMinimumSize = new Vector2(0, 54)
        };
        fields.AddChild(address);
        var error = new Label { Name = "Error", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        fields.AddChild(error);
        var join = new Button { Name = "Connect", Text = "连接", CustomMinimumSize = new Vector2(0, 60) };
        fields.AddChild(join);
        void Submit()
        {
            if (join.Disabled) return;
            if (!LanAddress.TryParseEndpoint(address.Text, out string host, out ushort port))
            {
                error.Text = "IPv4 地址或端口无效。";
                return;
            }
            error.Text = string.Empty;
            PvpSettings.RememberLan($"{host}:{port}", null);
            TaskHelper.RunSafely(LanMenu.Join(__instance, join, host, port, mode.Selected == 0));
        }
        join.Pressed += Submit;
        address.TextSubmitted += _ => Submit();
    }
}

[HarmonyPatch(typeof(NJoinFriendScreen), nameof(NJoinFriendScreen.OnSubmenuOpened))]
internal static class LanJoinOpenedPatch
{
    static bool Prefix(NJoinFriendScreen __instance)
    {
        __instance.GetNode<Control>("%LoadingOverlay").Hide();
        __instance.GetNode<Control>("%LoadingIndicator").Hide();
        __instance.GetNode<Control>("%NoFriendsText").Hide();
        var fields = __instance.GetNode("Panel/SpireLanJoin").GetChild(0);
        bool native = !__instance.HasMeta("spirepvp_native") || __instance.GetMeta("spirepvp_native").AsBool();
        fields.GetNode<OptionButton>("Mode").Select(native ? 0 : 1);
        fields.GetNode<Label>("Error").Text = string.Empty;
        Callable.From(fields.GetNode<LineEdit>("Address").GrabFocus).CallDeferred();
        return false;
    }
}

internal static class LanMenu
{
    private const string DialogName = "SpirePvpLanDialog";

    internal static void ShowHost(NSubmenu menu, Control button, bool native = false)
    {
        if (menu.HasNode(DialogName) || PvpSession.HostingPvp || NativeLanHost.Hosting) return;
        var dialog = CreateDialog(menu, native ? "原版 · LAN 建房" : "PvP · LAN 建房", "创建房间", out var fields);
        bool hasSave = native && LanRunSaves.HasSave(native: true);
        OptionButton? operation = null;
        CheckButton? overwrite = null;
        if (hasSave)
        {
            operation = new OptionButton { CustomMinimumSize = new Vector2(0, 48) };
            operation.AddItem("继续 A1 / A2 对局", 0);
            operation.AddItem("开始新对局", 1);
            fields.AddChild(operation);
            overwrite = new CheckButton { Text = "确认覆盖已有原版 LAN 存档", Visible = false };
            fields.AddChild(overwrite);
            dialog.OkButtonText = "继续对局";
        }
        OptionButton? mode = null;
        if (native)
        {
            fields.AddChild(new Label { Text = "游戏模式（双人）" });
            mode = new OptionButton { CustomMinimumSize = new Vector2(0, 48) };
            mode.AddItem("普通爬塔", (int)GameMode.Standard);
            bool dailyUnlocked = NativeLanHost.IsUnlocked(GameMode.Daily);
            bool customUnlocked = NativeLanHost.IsUnlocked(GameMode.Custom);
            mode.AddItem(dailyUnlocked ? "每日挑战" : "每日挑战（未解锁）", (int)GameMode.Daily);
            mode.AddItem(customUnlocked ? "自定义" : "自定义（未解锁）", (int)GameMode.Custom);
            mode.SetItemDisabled(1, !dailyUnlocked);
            mode.SetItemDisabled(2, !customUnlocked);
            mode.Disabled = hasSave;
            fields.AddChild(mode);
        }
        if (operation != null)
        {
            operation.ItemSelected += selected =>
            {
                bool creating = selected == 1;
                mode!.Disabled = !creating;
                overwrite!.Visible = creating;
                overwrite.SetPressedNoSignal(false);
                dialog.OkButtonText = creating ? "创建房间" : "继续对局";
            };
        }
        fields.AddChild(new Label { Text = "本机 IPv4" });
        fields.AddChild(new TextEdit
        {
            Text = LocalAddresses(), Editable = false, CustomMinimumSize = new Vector2(0, 90)
        });
        fields.AddChild(new Label { Text = "UDP 端口" });
        var portInput = new LineEdit
        {
            Text = PvpSettings.Current.LanPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MaxLength = 5, CustomMinimumSize = new Vector2(0, 48)
        };
        fields.AddChild(portInput);
        var error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        fields.AddChild(error);
        bool submitted = false;
        void Submit()
        {
            if (submitted) return;
            if (!LanAddress.TryParsePort(portInput.Text, out ushort port))
            {
                error.Text = "端口必须是 1–65535 的整数。";
                return;
            }
            SerializableRun? savedRun = null;
            if (hasSave && operation!.Selected == 0)
            {
                var result = LanRunSaves.Load(native: true);
                if (!result.Success || result.SaveData == null)
                {
                    error.Text = "存档无法恢复或不属于 A1/A2 双人原版对局。原文件已保留。";
                    return;
                }
                savedRun = result.SaveData;
            }
            else if (hasSave && overwrite?.ButtonPressed != true)
            {
                error.Text = "请先确认覆盖已有原版 LAN 存档。";
                return;
            }
            submitted = true;
            var selectedMode = mode == null ? GameMode.Standard : (GameMode)mode.GetSelectedId();
            PvpSettings.RememberLan(null, port);
            dialog.Hide();
            dialog.QueueFree();
            if (native) NativeLanHost.StartHost(menu, button, port, selectedMode, savedRun);
            else HostMenuPatch.StartHost(menu, button, port);
        }
        dialog.Confirmed += Submit;
        portInput.TextSubmitted += _ => Submit();
        dialog.PopupCentered(new Vector2I(640, hasSave ? 600 : native ? 460 : 360));
        portInput.GrabFocus();
    }

    internal static void ShowJoin(NSubmenu menu, bool native)
    {
        if (menu.HasNode(DialogName) || PvpSession.HostingPvp || NativeLanHost.Hosting) return;
        var stack = (NSubmenuStack)AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(menu)!;
        var screen = stack.GetSubmenuType<NJoinFriendScreen>();
        screen.SetMeta("spirepvp_native", native);
        stack.Push(screen);
    }

    internal static void SetEnabled(Control button, bool enabled)
    {
        if (button is BaseButton basic) basic.Disabled = !enabled;
        else if (button is NButton themed) themed.SetEnabled(enabled);
    }

    private static ConfirmationDialog CreateDialog(NSubmenu menu, string title, string confirm, out VBoxContainer fields)
    {
        var dialog = new ConfirmationDialog
        {
            Name = DialogName, Title = title, OkButtonText = confirm, CancelButtonText = "取消",
            DialogHideOnOk = false, Exclusive = true, Transient = true
        };
        fields = new VBoxContainer();
        fields.AddThemeConstantOverride("separation", 12);
        dialog.AddChild(fields);
        menu.AddChild(dialog);
        dialog.Canceled += dialog.QueueFree;
        dialog.CloseRequested += dialog.QueueFree;
        return dialog;
    }

    private static string LocalAddresses()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(info => info.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(address => address.ToString()).Distinct().Order().ToArray();
            return addresses.Length > 0 ? string.Join("\n", addresses) : "未检测到可用 IPv4 地址";
        }
        catch (NetworkInformationException)
        {
            return "无法读取本机 IPv4 地址";
        }
    }

    internal static async Task Join(NJoinFriendScreen screen, Button button, string address, ushort port, bool native)
    {
        button.Disabled = true;
        try
        {
            LanRunSaves.Activate(native);
            await screen.JoinGameAsync(new ENetClientConnectionInitializer(
                native ? LanAddress.NativeClientId : LanAddress.ClientId, address, port));
        }
        catch
        {
            var popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            if (popup != null) NModalContainer.Instance?.Add(popup);
            throw;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(button)) button.Disabled = false;
        }
    }
}

[HarmonyPatch(typeof(NullPlatformUtilStrategy), nameof(NullPlatformUtilStrategy.GetPlayerName))]
internal static class LanPlayerNamesPatch
{
    static void Postfix(ulong playerId, ref string __result)
    {
        if (LanRunSaves.ActiveFile != LanRunSaves.NativeFile) return;
        if (playerId == LanAddress.HostId) __result = "A1";
        else if (playerId == LanAddress.NativeClientId) __result = "A2";
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), "AfterInitialized")]
internal static class LobbyInitPatch
{
    static void Postfix(NCustomRunScreen __instance)
    {
        if (__instance.Lobby?.Modifiers.Any(m => m is PvpDuel) == true) PvpUi.ConfigureLobby(__instance);
        else PvpUi.RestoreLobby(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.ModifiersChanged))]
internal static class LobbySyncPatch
{
    static bool Prefix(NCustomRunScreen __instance)
    {
        if (__instance.Lobby?.Modifiers.Any(m => m is PvpDuel) != true) return true;
        PvpUi.ConfigureLobby(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), "OnModifiersListChanged")]
internal static class LockModifierListPatch
{
    static bool Prefix(NCustomRunScreen __instance) => __instance.Lobby?.Modifiers.Any(m => m is PvpDuel) != true;
}

[HarmonyPatch(typeof(StartRunLobby), nameof(StartRunLobby.IsAboutToBeginGame))]
internal static class TwoPlayersPatch
{
    static void Postfix(StartRunLobby __instance, ref bool __result)
    {
        if (__instance.Modifiers.Any(m => m is PvpDuel)) __result &= __instance.Players.Count == 2;
    }
}
