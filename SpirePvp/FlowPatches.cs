using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterAct))]
internal static class SkipTowerPatch
{
    static bool Prefix(RunManager __instance, ref Task __result)
    {
        if (!PvpSession.IsPvp(PvpSession.GetState(__instance))) return true;
        __result = PvpSession.Prepare(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewMultiplayerRun))]
internal static class NoPvpSavePatch
{
    static void Prefix(IReadOnlyList<ModifierModel> modifiers, ref bool shouldSave)
    {
        if (modifiers.Any(m => m is PvpDuel)) shouldSave = false;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class CleanupPatch
{
    static void Prefix() => PvpUi.DetachCombatUi();
    static void Postfix()
    {
        RoundReadyChannel.Reset();
        DuelResultChannel.Reset();
        if (IndependentDevelopment.Transitioning) return;
        IndependentDevelopment.Reset();
        BossDuel.Reset();
        PvpSession.HostingPvp = false;
        PvpSession.PreparingRound = false;
        PvpSession.SelectingDecks = false;
        PvpSession.LocalDeckReady = false;
        DraftBrowsing.OpeningDeck = false;
        PvpSession.Winner = null;
        PvpSession.Result = DuelOutcome.InProgress;
    }
}
