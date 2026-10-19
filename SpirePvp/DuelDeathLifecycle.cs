using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

// Native Kill(collection) opens the co-op loss screen even when the deferred
// private kill is still queued. PvP keeps the death hooks but owns its ending.
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), new[] { typeof(IReadOnlyCollection<Creature>), typeof(bool) })]
internal static class DuelKillLifecyclePatch
{
    static bool Prefix(IReadOnlyCollection<Creature> creatures, bool force, ref Task __result)
    {
        if (force || creatures.Count == 0 || !creatures.All(c => PvpSession.IsArena(c.CombatState))) return true;
        __result = Kill(creatures.ToArray());
        return false;
    }
    private static async Task Kill(IEnumerable<Creature> creatures)
    {
        var nativeKill = AccessTools.Method(typeof(CreatureCmd), "KillWithoutCheckingWinCondition");
        foreach (var creature in creatures) await (Task)nativeKill.Invoke(null, [creature, false, 0])!;
    }
}

[HarmonyPatch(typeof(NRun), nameof(NRun.ShowGameOverScreen))]
internal static class DuelGameOverScreenPatch
{
    static bool Prefix() => RunManager.Instance.IsAbandoned
        || (!DuelDamage.IsSettling && !DuelResultChannel.Finished)
        || PvpSession.GetState(RunManager.Instance)?.CurrentRoom is not CombatRoom { Encounter: PvpArena };
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.LocalPlayerDisconnected))]
internal static class DuelFinishedDisconnectPatch
{
    static bool Prefix() => !DuelResultChannel.Finished;
}
