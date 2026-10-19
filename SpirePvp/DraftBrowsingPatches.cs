using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace SpirePvp;

internal static class DraftBrowsing
{
    internal static bool OpeningDeck;
    internal static bool Preserve(IOverlayScreen? screen)
    {
        if (!PvpSession.SelectingDecks || (!OpeningDeck && NCapstoneContainer.Instance?.CurrentCapstoneScreen is not NDeckViewScreen)) return false;
        return Pending(screen);
    }
    internal static bool Pending(IOverlayScreen? screen)
    {
        if (screen is NRewardsScreen) return PvpSession.SelectingDecks;
        if (screen is not (NCardRewardSelectionScreen or NCardGridSelectionScreen)) return false;
        var source = AccessTools.Field(screen.GetType(), "_completionSource").GetValue(screen);
        var task = source?.GetType().GetProperty("Task")?.GetValue(source) as Task;
        return task == null || !task.IsCompleted;
    }
}

[HarmonyPatch(typeof(NCapstoneContainer), nameof(NCapstoneContainer.Close))]
internal static class ResumeDraftAfterBrowsingPatch
{
    static void Postfix()
    {
        if (!PvpSession.SelectingDecks || !DraftBrowsing.Pending(NOverlayStack.Instance?.Peek())) return;
        // ShowOverlays normally refuses to restore a selection while the map is
        // open. Restore the pending selection after leaving the deck capstone.
        NMapScreen.Instance?.Close(animateOut: false);
        NOverlayStack.Instance?.ShowOverlays();
    }
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen.ShowScreen))]
internal static class BrowseDraftDeckPatch
{
    [HarmonyPriority(Priority.First)]
    static void Prefix(out bool __state)
    {
        __state = DraftBrowsing.OpeningDeck;
        if (PvpSession.SelectingDecks) DraftBrowsing.OpeningDeck = true;
    }
    static void Finalizer(bool __state) => DraftBrowsing.OpeningDeck = __state;
}

[HarmonyPatch(typeof(NOverlayStack), nameof(NOverlayStack.Clear))]
internal static class PreserveDraftStackPatch
{
    static bool Prefix(NOverlayStack __instance) => !DraftBrowsing.Preserve(__instance.Peek());
}

[HarmonyPatch(typeof(NOverlayStack), nameof(NOverlayStack.Remove))]
internal static class PreserveDraftSelectionPatch
{
    static bool Prefix(IOverlayScreen screen) => !DraftBrowsing.Preserve(screen);
}
