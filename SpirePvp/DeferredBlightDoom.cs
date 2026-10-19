using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpirePvp;

[HarmonyPatch]
internal static class DeferredBlightDoomPatch
{
    static MethodBase TargetMethod() => typeof(PowerCmd).GetMethods().Single(m => m.Name == "Apply"
        && !m.IsGenericMethod && m.GetParameters()[1].ParameterType == typeof(PowerModel));

    [HarmonyPriority(Priority.First)]
    static bool Prefix(PowerModel power, Creature target, CardModel? cardSource, ref Task __result)
    {
        if (power is not DoomPower || cardSource == null || !DuelDamage.DeferBlightDoom(cardSource, target)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
