using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;

namespace SpirePvp;

// The native local-character picker calls SetLocalCharacter, so both host and
// client keep the game's character portrait/selection and network replication.
[HarmonyPatch]
internal static class RandomCharacterReadinessPatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[]
        { "OnEmbarkPressed", "OnUnreadyPressed", "OnSubmenuOpened", "PlayerChanged" }
        .Select(name => AccessTools.Method(typeof(NCustomRunScreen), name));
    static void Postfix(NCustomRunScreen __instance) => PvpUi.UpdateRandomCharacter(__instance);
}
