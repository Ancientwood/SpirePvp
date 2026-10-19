using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpirePvp;

public static class WatcherWrathCompatibility
{
    public const string WrathTypeName = "WatcherMod.Wrath";
    private static Type? _wrathType;
    internal static Type? WrathType => _wrathType ??= AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(WrathTypeName)).FirstOrDefault(t => t != null);
    internal static bool Eligible(ValueProp props) => props.IsPoweredAttack() && WrathType != null;
    internal static decimal Factor(Creature target, Creature? dealer, ValueProp props, CardModel? card, CardPlay? play)
    {
        if (!Eligible(props)) return 1;
        var powers = (dealer?.Powers ?? []).Concat(target.Powers).Distinct();
        decimal factor = 1;
        foreach (var power in powers.Where(p => p.GetType() == WrathType))
            factor *= power.ModifyDamageMultiplicative(target, 0, props, dealer, card, play);
        return factor;
    }
    internal static void ApplyLoaded()
    {
        var type = WrathType;
        if (type == null) return;
        var harmony = new Harmony("spirepvp.wrath");
        foreach (var name in new[] { "AfterApplied", "AfterRemoved" })
        {
            var method = AccessTools.DeclaredMethod(type, name);
            if (method != null && Harmony.GetPatchInfo(method)?.Owners.Contains(harmony.Id) != true)
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(WatcherWrathCompatibility), nameof(Changed)));
        }
    }
    private static void Changed(ref Task __result) => __result = Refresh(__result);
    private static async Task Refresh(Task original) { await original; DuelDamage.RefreshIntents(); }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class WatcherWrathReadyPatch
{
    static void Prefix() => WatcherWrathCompatibility.ApplyLoaded();
}
