using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;

namespace SpirePvp;

public static class DuelVfx
{
    internal static readonly AsyncLocal<Player?> Caster = new();
    public static IReadOnlyList<Creature> SideTargets(ICombatState state, CombatSide side, Player? caster, ulong? local)
    {
        var owner = (state as PvpCombatView)?.Owner ?? caster ?? state.Players.FirstOrDefault(p => p.NetId == local);
        if (owner == null) return [];
        return state.Creatures.Where(c => c.IsHittable && PvpTargets.Team(c) is { } team
            && (side == CombatSide.Player ? team == owner : team != owner && PvpTargets.IsEnemyTarget(c))).ToArray();
    }
    public static Vector2? Center(IEnumerable<Vector2> positions, bool floor)
    {
        var points = positions.Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y)).ToArray();
        return points.Length == 0 ? null : new Vector2(points.Average(p => p.X), floor ? points.Max(p => p.Y) : points.Average(p => p.Y));
    }
}

[HarmonyPatch]
internal static class DuelVfxCasterPatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(CardModel), nameof(CardModel.OnPlayWrapper)),
        AccessTools.Method(typeof(AttackCommand), nameof(AttackCommand.Execute)) };
    static void Prefix(object __instance, out Player? __state)
    {
        __state = DuelVfx.Caster.Value;
        var creature = __instance is CardModel card ? card.Owner?.Creature : (__instance as AttackCommand)?.Attacker;
        DuelVfx.Caster.Value = PvpSession.IsArena(creature?.CombatState) ? PvpTargets.Team(creature!) : null;
    }
    static void Postfix(Player? __state) => DuelVfx.Caster.Value = __state;
    static Exception? Finalizer(Player? __state, Exception? __exception)
    { DuelVfx.Caster.Value = __state; return __exception; }
}

[HarmonyPatch]
internal static class DuelVfxSideCenterPatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(VfxCmd), nameof(VfxCmd.GetSideCenter)),
        AccessTools.Method(typeof(VfxCmd), nameof(VfxCmd.GetSideCenterFloor)) };
    static bool Prefix(CombatSide side, ICombatState combatState, MethodBase __originalMethod, ref Vector2? __result)
    {
        if (!PvpSession.IsArena(combatState)) return true;
        var room = NCombatRoom.Instance;
        var floor = __originalMethod.Name == nameof(VfxCmd.GetSideCenterFloor);
        var positions = new List<Vector2>();
        if (room != null)
            foreach (var creature in DuelVfx.SideTargets(combatState, side, DuelVfx.Caster.Value, LocalContext.NetId))
            {
                var node = room.GetCreatureNode(creature);
                if (node != null && GodotObject.IsInstanceValid(node)) positions.Add(floor ? node.GetBottomOfHitbox() : node.VfxSpawnPosition);
            }
        __result = DuelVfx.Center(positions, floor);
        return false;
    }
}

// Heavy attacks such as Bludgeon spawn the falling impact at the target base.
// Player artwork need not be centered on its root, unlike most monster art.
[HarmonyPatch(typeof(VfxCmd), nameof(VfxCmd.PlayOnCreature))]
internal static class DuelVfxBasePatch
{
    static bool Prefix(Creature target, string path)
    {
        if (!PvpSession.IsArena(target.CombatState)) return true;
        if (TestMode.IsOn || target.IsDead) return false;
        var node = target.GetCreatureNode();
        if (node != null && GodotObject.IsInstanceValid(node))
            VfxCmd.PlayVfx(node.GetBottomOfHitbox(), path, target.GetVfxContainer());
        return false;
    }
}
