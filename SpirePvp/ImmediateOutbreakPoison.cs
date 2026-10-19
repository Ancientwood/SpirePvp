using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpirePvp;

// Async-local scopes follow the native awaited card/poison commands, while
// restoring the caller immediately so other queued actions are unaffected.
[HarmonyPatch(typeof(Outbreak), "OnPlay")]
internal static class ImmediateOutbreakScopePatch
{
    internal static readonly AsyncLocal<bool> Scope = new();
    static void Prefix(Outbreak __instance, out bool __state)
    {
        __state = Scope.Value;
        Scope.Value = DuelDamage.Enabled(__instance.CombatState ?? __instance.Owner.Creature.CombatState);
    }
    static void Postfix(bool __state) => Scope.Value = __state;
    static Exception? Finalizer(bool __state, Exception? __exception)
    {
        Scope.Value = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(PoisonPower), nameof(PoisonPower.Trigger))]
internal static class ImmediateOutbreakPoisonPatch
{
    private static readonly AsyncLocal<bool> Scope = new();
    internal static bool Active => Scope.Value;
    static void Prefix(PoisonPower __instance, out bool __state)
    {
        __state = Scope.Value;
        Scope.Value = ImmediateOutbreakScopePatch.Scope.Value && DuelDamage.Enabled(__instance.Owner.CombatState);
    }
    static void Postfix(bool __state) => Scope.Value = __state;
    static Exception? Finalizer(bool __state, Exception? __exception)
    {
        Scope.Value = __state;
        return __exception;
    }
}
