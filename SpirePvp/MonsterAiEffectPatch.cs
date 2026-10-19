using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using System.Reflection;

namespace SpirePvp;

// The native stun command edits a monster's next AI move and throws for a
// player. Preserve Whistle's damage without invoking nonexistent player AI.
[HarmonyPatch]
internal static class MonsterAiEffectPatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Stun),
        [typeof(Creature), typeof(Func<IReadOnlyList<Creature>, Task>), typeof(string)]);
    static bool Prefix(Creature creature, ref Task __result)
    {
        if (!creature.IsPlayer || !PvpSession.IsArena(creature.CombatState)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
