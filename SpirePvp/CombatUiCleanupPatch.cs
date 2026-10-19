using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace SpirePvp;

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
internal static class CombatUiCleanupPatch
{
    static void Prefix() => PvpUi.DetachCombatUi();
}
