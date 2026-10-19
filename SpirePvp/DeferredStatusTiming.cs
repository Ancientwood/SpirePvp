using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpirePvp;

[HarmonyPatch(typeof(PoisonPower), nameof(PoisonPower.AfterSideTurnStart))]
internal static class DeferredPoisonStartPatch
{
    static bool Prefix(PoisonPower __instance, ref Task __result)
    {
        if (!DuelDamage.Enabled(__instance.Owner?.CombatState)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterSideTurnEnd))]
internal static class DeferredPoisonEndPatch
{
    static void Postfix(ICombatState combatState, CombatSide side, IEnumerable<Creature> participants, ref Task __result)
    {
        if (side == CombatSide.Player && DuelDamage.Enabled(combatState))
            __result = Trigger(__result, participants.ToArray());
    }
    private static async Task Trigger(Task original, Creature[] participants)
    {
        await original;
        foreach (var creature in participants)
            if (creature.IsAlive && creature.GetPower<PoisonPower>() is { } poison)
                await poison.Trigger();
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
internal static class DeferredOutgoingReplayPatch
{
    static bool Prefix(Creature? dealer, Creature target, MegaCrit.Sts2.Core.Models.CardModel? cardSource, ref Task __result)
    {
        if (!DuelDamage.SkipReplayedOutgoingHooks) return true;
        // This pending hit already fired the whole outgoing hook chain at play
        // time. Unrelated reaction damage during settlement still gets its hooks.
        __result = Task.CompletedTask;
        return false;
    }
}

// Native player debuffs skip one enemy-phase tick because normal combat has
// separate turns. PvP uses one shared round, so its first tick must count.
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.PowerCmd), nameof(MegaCrit.Sts2.Core.Commands.PowerCmd.TickDownDuration))]
internal static class PvpDurationTickPatch
{
    static void Prefix(MegaCrit.Sts2.Core.Models.PowerModel power)
    {
        if (PvpSession.IsArena(power.Owner?.CombatState)) power.SkipNextDurationTick = false;
    }
}
