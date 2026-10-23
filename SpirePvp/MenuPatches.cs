using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

[HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu._Ready))]
internal static class HostMenuPatch
{
    static void Postfix(NMultiplayerHostSubmenu __instance)
    {
        if (__instance.HasNode("SpirePvpButton")) return;
        var original = __instance.GetNode<Control>("CustomRunButton");
        var button = new Button
        {
            Name = "SpirePvpButton", Text = "PvP · 构筑决斗",
            Position = original.Position + new Vector2(0, original.Size.Y + 12),
            Size = new Vector2(original.Size.X, 64), FocusMode = Control.FocusModeEnum.All
        };
        button.AddThemeFontSizeOverride("font_size", 28);
        __instance.AddChild(button);
        button.Pressed += () => TaskHelper.RunSafely(StartHost(__instance, button));
        original.FocusNeighborBottom = button.GetPath();
        button.FocusNeighborTop = original.GetPath();
    }

    internal static async Task StartHost(NSubmenu menu, Button button, ushort? lanPort = null)
    {
        if (PvpSession.HostingPvp || NativeLanHost.Hosting) return;
        button.Disabled = true;
        PvpSession.HostingPvp = true;
        var overlay = (Control)AccessTools.Field(menu.GetType(), "_loadingOverlay").GetValue(menu)!;
        var stack = (NSubmenuStack)AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(menu)!;
        overlay.Visible = true;
        NetHostGameService? service = null;
        bool handedOff = false;
        try
        {
            service = new NetHostGameService(PeerVersionInfo.LocalDefault());
            if (!lanPort.HasValue && SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp"))
            {
                var error = await service.StartSteamHost(2);
                if (error.HasValue)
                {
                    var popup = NErrorPopup.Create(error.Value);
                    if (popup != null) NModalContainer.Instance?.Add(popup);
                    return;
                }
            }
            else if (lanPort.HasValue)
            {
                var error = LanHostBinding.StartHost(service, lanPort.Value);
                if (error.HasValue)
                {
                    var popup = NErrorPopup.Create(error.Value);
                    if (popup != null) NModalContainer.Instance?.Add(popup);
                    return;
                }
            }
            else service.StartENetHost(LanAddress.DefaultPort, 2);
            var screen = stack.GetSubmenuType<NCustomRunScreen>();
            screen.InitializeMultiplayerAsHost(service, 2);
            var settings = PvpSettings.Current;
            screen.Lobby.SetModifiers(PvpLobbyModifiers.Create(settings.LastConstructionMode, settings.Multipliers, settings.Development, settings.CardLimitEnabled, settings.MaxCardsPerTurn, settings.DeferredDamage, settings.CooperativeDevelopment, settings.CustomCommonCards, settings.CustomUncommonCards, settings.CustomRareCards, settings.SameSeed));
            screen.Lobby.SyncAscensionChange(0);
            PvpUi.ConfigureLobby(screen);
            stack.Push(screen);
            handedOff = true;
        }
        catch
        {
            var popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            if (popup != null) NModalContainer.Instance?.Add(popup);
            throw;
        }
        finally
        {
            PvpSession.HostingPvp = false;
            overlay.Visible = false;
            button.Disabled = false;
            if (!handedOff && service != null) service.Disconnect(NetError.Quit, true);
        }
    }
}

[HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready))]
internal static class LanMenuPatch
{
    static void Postfix(NMultiplayerSubmenu __instance)
    {
        if (__instance.HasNode("SpirePvpLanButtons")) return;
        var row = new GridContainer
        {
            Name = "SpirePvpLanButtons", Columns = 2,
            AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = -290, OffsetRight = 290, OffsetTop = -176, OffsetBottom = -36
        };
        row.AddThemeConstantOverride("h_separation", 12);
        row.AddThemeConstantOverride("v_separation", 12);
        var nativeHost = new Button
        {
            Name = "NativeHost", Text = "原版 · LAN 建房", CustomMinimumSize = new Vector2(280, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var nativeJoin = new Button
        {
            Name = "NativeJoin", Text = "原版 · LAN 加入", CustomMinimumSize = new Vector2(280, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var host = new Button
        {
            Text = "PvP · LAN 建房", CustomMinimumSize = new Vector2(280, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        var join = new Button
        {
            Text = "PvP · LAN 加入", CustomMinimumSize = new Vector2(280, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        host.AddThemeFontSizeOverride("font_size", 26);
        join.AddThemeFontSizeOverride("font_size", 26);
        nativeHost.AddThemeFontSizeOverride("font_size", 26);
        nativeJoin.AddThemeFontSizeOverride("font_size", 26);
        row.AddChild(nativeHost);
        row.AddChild(nativeJoin);
        row.AddChild(host);
        row.AddChild(join);
        __instance.AddChild(row);
        nativeHost.Pressed += () => LanMenu.ShowHost(__instance, nativeHost, native: true);
        nativeJoin.Pressed += () => LanMenu.ShowJoin(__instance, nativeJoin, native: true);
        host.Pressed += () => LanMenu.ShowHost(__instance, host);
        join.Pressed += () => LanMenu.ShowJoin(__instance, join);
        nativeHost.FocusNeighborRight = nativeJoin.GetPath();
        nativeJoin.FocusNeighborLeft = nativeHost.GetPath();
        nativeHost.FocusNeighborBottom = host.GetPath();
        nativeJoin.FocusNeighborBottom = join.GetPath();
        host.FocusNeighborTop = nativeHost.GetPath();
        join.FocusNeighborTop = nativeJoin.GetPath();
        host.FocusNeighborRight = join.GetPath();
        join.FocusNeighborLeft = host.GetPath();
    }
}

internal static class LanMenu
{
    private const string DialogName = "SpirePvpLanDialog";
    private static readonly ulong NativeClientId = LanAddress.CreateNativeClientId();

    internal static void ShowHost(NSubmenu menu, Button button, bool native = false)
    {
        if (menu.HasNode(DialogName) || PvpSession.HostingPvp || NativeLanHost.Hosting) return;
        var dialog = CreateDialog(menu, native ? "原版 · LAN 建房" : "PvP · LAN 建房", "创建房间", out var fields);
        OptionButton? mode = null;
        if (native)
        {
            fields.AddChild(new Label { Text = "游戏模式（最多 4 人）" });
            mode = new OptionButton { CustomMinimumSize = new Vector2(0, 48) };
            mode.AddItem("普通爬塔", (int)GameMode.Standard);
            bool dailyUnlocked = NativeLanHost.IsUnlocked(GameMode.Daily);
            bool customUnlocked = NativeLanHost.IsUnlocked(GameMode.Custom);
            mode.AddItem(dailyUnlocked ? "每日挑战" : "每日挑战（未解锁）", (int)GameMode.Daily);
            mode.AddItem(customUnlocked ? "自定义" : "自定义（未解锁）", (int)GameMode.Custom);
            mode.SetItemDisabled(1, !dailyUnlocked);
            mode.SetItemDisabled(2, !customUnlocked);
            fields.AddChild(mode);
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
            submitted = true;
            var selectedMode = mode == null ? GameMode.Standard : (GameMode)mode.GetSelectedId();
            PvpSettings.RememberLan(null, port);
            dialog.Hide();
            dialog.QueueFree();
            if (native) NativeLanHost.StartHost(menu, button, port, selectedMode);
            else TaskHelper.RunSafely(HostMenuPatch.StartHost(menu, button, port));
        }
        dialog.Confirmed += Submit;
        portInput.TextSubmitted += _ => Submit();
        dialog.PopupCentered(new Vector2I(640, native ? 460 : 360));
        portInput.GrabFocus();
    }

    internal static void ShowJoin(NSubmenu menu, Button button, bool native = false)
    {
        if (menu.HasNode(DialogName) || PvpSession.HostingPvp || NativeLanHost.Hosting) return;
        var dialog = CreateDialog(menu, native ? "原版 · LAN 加入" : "PvP · LAN 加入", "连接", out var fields);
        fields.AddChild(new Label { Text = "房主 IPv4 / IPv4:端口" });
        var addressInput = new LineEdit
        {
            Text = PvpSettings.Current.LastLanAddress ?? string.Empty, PlaceholderText = "192.168.1.2:33771",
            MaxLength = 64, CustomMinimumSize = new Vector2(0, 48)
        };
        fields.AddChild(addressInput);
        var error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        fields.AddChild(error);
        bool submitted = false;
        void Submit()
        {
            if (submitted) return;
            if (!LanAddress.TryParseEndpoint(addressInput.Text, out string address, out ushort port))
            {
                error.Text = "IPv4 地址或端口无效。";
                return;
            }
            submitted = true;
            PvpSettings.RememberLan($"{address}:{port}", null);
            dialog.Hide();
            dialog.QueueFree();
            TaskHelper.RunSafely(Join(menu, button, address, port, native ? NativeClientId : LanAddress.ClientId));
        }
        dialog.Confirmed += Submit;
        addressInput.TextSubmitted += _ => Submit();
        dialog.PopupCentered(new Vector2I(640, 260));
        addressInput.GrabFocus();
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

    private static async Task Join(NSubmenu menu, Button button, string address, ushort port, ulong clientId)
    {
        button.Disabled = true;
        try
        {
            var stack = (NSubmenuStack)AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(menu)!;
            var screen = stack.GetSubmenuType<NJoinFriendScreen>();
            stack.Push(screen);
            await screen.JoinGameAsync(new ENetClientConnectionInitializer(clientId, address, port));
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
