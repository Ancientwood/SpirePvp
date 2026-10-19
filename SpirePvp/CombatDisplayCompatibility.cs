using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace SpirePvp;

public static class CombatDisplayCompatibility
{
    public const string HarmonyId = "spirepvp.combatdisplaycompat";
    public const string MintyRenderer = "MintySpire2.MintySpire2Code.combat.SummedIncomingDamageRender";
    public static bool Apply(Type? renderer)
    {
        if (renderer?.FullName != MintyRenderer) return false;
        var method = renderer.GetMethod("CalculateIncomingDamage", BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance, null, [typeof(Creature)], null);
        if (method == null || method.ReturnType == typeof(void)) return false;
        if (Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true) return true;
        new Harmony(HarmonyId).Patch(method,
            prefix: new HarmonyMethod(typeof(CombatDisplayCompatibility), nameof(CalculateIncomingDamage)) { priority = Priority.First });
        return true;
    }
    private static bool CalculateIncomingDamage(Creature __0)
    {
        // Minty's forecast assumes each opposing creature has a Monster model.
        // PvP creatures are players; return the default zero forecast and let the
        // native health refresh finish. Deferred PvP damage has its own intent UI.
        return !PvpSession.IsArena(__0?.CombatState);
    }
    internal static void ApplyLoaded()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (Apply(assembly.GetType(MintyRenderer)))
                Log.Info("[SpirePvp] 已兼容 MintySpire2 血条：PvP 使用决斗伤害显示。");
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class CombatDisplaysReadyPatch
{
    [HarmonyPriority(Priority.Last)]
    static void Prefix() => CombatDisplayCompatibility.ApplyLoaded();
}
