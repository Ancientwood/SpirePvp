using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpirePvp;

[HarmonyPatch(typeof(RupturePower), nameof(RupturePower.AfterDamageReceived))]
internal static class PvpRuptureSourcePatch
{
    static bool Prefix(RupturePower __instance, CardModel? cardSource, Creature? dealer, ref Task __result)
    {
        if (!PvpSession.IsArena(__instance.Owner?.CombatState)
            || PvpSelfDamage.IsCardSelfDamage(__instance.Owner!, cardSource, dealer)) return true;
        // Both players share CombatSide.Player. Native side checks cannot tell
        // the owner's card self-damage apart from the opponent's attacks.
        __result = Task.CompletedTask;
        return false;
    }
}

public static class PvpSelfDamage
{
    public static bool IsCardSelfDamage(Creature target, CardModel? card, Creature? dealer = null)
        => card?.Owner?.Creature == target && (dealer == null || PvpTargets.Team(dealer) == PvpTargets.Team(target));
}
