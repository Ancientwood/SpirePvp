using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpirePvp;

[HarmonyPatch]
internal static class FortitudeIconPatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.PropertyGetter(typeof(PowerModel), nameof(PowerModel.PackedIconPath)),
        AccessTools.PropertyGetter(typeof(PowerModel), nameof(PowerModel.ResolvedBigIconPath))
    };
    static bool Prefix(PowerModel __instance, MethodBase __originalMethod, ref string __result)
    {
        if (__instance is not PvpFortitudePower) return true;
        var placeholder = ModelDb.Power<UnmovablePower>();
        __result = __originalMethod.Name == "get_PackedIconPath" ? placeholder.PackedIconPath : placeholder.ResolvedBigIconPath;
        return false;
    }
}
