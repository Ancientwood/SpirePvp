using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline.Epochs;

namespace SpirePvp;

internal static class NativeLanHost
{
    internal const int MaxPlayers = 4;
    internal static bool Hosting { get; private set; }

    internal static bool IsUnlocked(GameMode mode) => mode switch
    {
        GameMode.Standard => true,
        GameMode.Daily => SaveManager.Instance.IsEpochRevealed<DailyRunEpoch>(),
        GameMode.Custom => SaveManager.Instance.IsEpochRevealed<CustomAndSeedsEpoch>(),
        _ => false
    };

    internal static void StartHost(NSubmenu menu, Button button, ushort port, GameMode mode)
    {
        if (Hosting || PvpSession.HostingPvp) return;
        Hosting = true;
        button.Disabled = true;
        Control? overlay = null;
        NetHostGameService? service = null;
        bool handedOff = false;
        try
        {
            if (!IsUnlocked(mode)) throw new InvalidOperationException("LAN game mode is locked or unsupported.");
            overlay = (Control)AccessTools.Field(menu.GetType(), "_loadingOverlay").GetValue(menu)!;
            var stack = (NSubmenuStack)AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(menu)!;
            overlay.Visible = true;
            service = new NetHostGameService(PeerVersionInfo.LocalDefault());
            var error = LanHostBinding.StartHost(service, port, MaxPlayers);
            if (error.HasValue)
            {
                ShowError(error.Value);
                return;
            }
            stack.Push(InitializeLobby(stack, service, mode));
            handedOff = true;
        }
        catch (Exception exception)
        {
            Log.Error("[SpirePvp] Native LAN host failed: " + exception);
            ShowError(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
        }
        finally
        {
            Hosting = false;
            if (GodotObject.IsInstanceValid(overlay)) overlay!.Visible = false;
            if (GodotObject.IsInstanceValid(button)) button.Disabled = false;
            if (!handedOff && service != null) service.Disconnect(NetError.Quit, true);
        }
    }

    internal static NSubmenu InitializeLobby(NSubmenuStack stack, NetHostGameService service, GameMode mode)
    {
        switch (mode)
        {
            case GameMode.Standard:
                var standard = stack.GetSubmenuType<NCharacterSelectScreen>();
                standard.InitializeMultiplayerAsHost(service, MaxPlayers);
                return standard;
            case GameMode.Daily:
                var daily = stack.GetSubmenuType<NDailyRunScreen>();
                daily.InitializeMultiplayerAsHost(service);
                return daily;
            case GameMode.Custom:
                var custom = stack.GetSubmenuType<NCustomRunScreen>();
                custom.InitializeMultiplayerAsHost(service, MaxPlayers);
                return custom;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private static void ShowError(NetErrorInfo error)
    {
        var popup = NErrorPopup.Create(error);
        if (popup != null) NModalContainer.Instance?.Add(popup);
    }
}