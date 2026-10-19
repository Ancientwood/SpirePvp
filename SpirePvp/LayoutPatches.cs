using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SpirePvp;

[HarmonyPatch]
internal static class CombatLayoutPatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(NCombatRoom), "AdjustCreatureScaleForAspectRatio"),
        AccessTools.Method(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature))
    };
    static void Postfix(NCombatRoom __instance)
    {
        if (AccessTools.Field(typeof(NCombatRoom), "_visuals").GetValue(__instance) is CombatRoom room
            && PvpSession.IsArena(room.CombatState)) PvpUi.PositionPlayers(__instance);
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
internal static class CreatureStatePatch
{
    static void Postfix(NCreature __instance)
    {
        if (PvpSession.IsArena(__instance.Entity.CombatState)) PvpUi.ShowCreatureState(__instance);
    }
}

[HarmonyPatch(typeof(NCreature), "UpdatePhobiaMode")]
internal static class BodyVariantPatch
{
    static void Postfix(NCreature __instance)
    {
        if (PvpSession.IsArena(__instance.Entity.CombatState) && NCombatRoom.Instance != null)
        {
            var room = NCombatRoom.Instance;
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(room) && room.IsInsideTree()) PvpUi.PositionPlayers(room);
            }).CallDeferred();
        }
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.OstyScaleToSize))]
internal static class PetScaleLayoutPatch
{
    static void Postfix(NCreature __instance)
    {
        if (!PvpSession.IsArena(__instance.Entity.CombatState)) return;
        if (AccessTools.Field(typeof(NCreature), "_scaleTween").GetValue(__instance) is not Tween tween) return;
        // Native pet growth also moves its node; restore the PvP position once that tween completes.
        tween.TweenCallback(Callable.From(() =>
        {
            var room = NCombatRoom.Instance;
            if (room != null && GodotObject.IsInstanceValid(room) && room.IsInsideTree())
                PvpUi.PositionPlayers(room);
        }));
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.GetOstyOffsetFromPlayer))]
internal static class PetGrowthDestinationPatch
{
    static bool Prefix(Creature osty, ref Vector2 __result)
    {
        var room = NCombatRoom.Instance;
        if (!PvpSession.IsArena(osty.CombatState) || room == null || !LocalContext.NetId.HasValue || osty.PetOwner == null) return true;
        var pet = room.GetCreatureNode(osty);
        var player = room.GetCreatureNode(osty.PetOwner.Creature);
        if (pet == null || player == null || pet.GetParent() is not CanvasItem parent) return true;
        var index = room.CreatureNodes.Where(n => n.Entity.PetOwner == osty.PetOwner).OrderBy(n => n.Entity.CombatId).ToList().IndexOf(pet);
        var placement = DuelLayout.Place(LocalContext.NetId.Value, osty.PetOwner.NetId, index);
        var global = room.GetGlobalTransform() * new Vector2(room.Size.X * placement.X, room.Size.Y * placement.Y);
        __result = parent.GetGlobalTransform().AffineInverse() * global - player.Position;
        return false;
    }
}
