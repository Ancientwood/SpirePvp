using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Multiplayer;
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

    private static async Task StartHost(NMultiplayerHostSubmenu menu, Button button)
    {
        if (PvpSession.HostingPvp) return;
        button.Disabled = true;
        PvpSession.HostingPvp = true;
        var overlay = (Control)AccessTools.Field(typeof(NMultiplayerHostSubmenu), "_loadingOverlay").GetValue(menu)!;
        var stack = (NSubmenuStack)AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(menu)!;
        overlay.Visible = true;
        NetHostGameService? service = null;
        bool handedOff = false;
        try
        {
            service = new NetHostGameService(PeerVersionInfo.LocalDefault());
            if (SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp"))
            {
                var error = await service.StartSteamHost(2);
                if (error.HasValue)
                {
                    var popup = NErrorPopup.Create(error.Value);
                    if (popup != null) NModalContainer.Instance?.Add(popup);
                    return;
                }
            }
            else service.StartENetHost(33771, 2);
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
