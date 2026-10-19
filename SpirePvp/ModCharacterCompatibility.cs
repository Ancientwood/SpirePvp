using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace SpirePvp;

// Standard registered characters/cards already use native selection, pools,
// save data and targeting. Extend only owner-relative effect checks, once all
// mods have loaded, without requiring a dependency on any character mod.
public static class ModCharacterCompatibility
{
    public const string HarmonyId = "spirepvp.charactercompat";
    private static readonly Harmony Patches = new(HarmonyId);
    public static int Apply(Assembly assembly)
    {
        if (assembly == typeof(CardModel).Assembly || assembly == typeof(ModCharacterCompatibility).Assembly || assembly.IsDynamic) return 0;
        if (!assembly.GetReferencedAssemblies().Any(a => a.Name == typeof(CardModel).Assembly.GetName().Name)) return 0;
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { types = error.Types.OfType<Type>().ToArray(); }
        int count = 0;
        foreach (var method in NativeEffectRelationPatch.FindMethods(types))
        {
            if (Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true) continue;
            try
            {
                Patches.Patch(method, transpiler: new HarmonyMethod(typeof(NativeEffectRelationPatch), "Transpiler"));
                count++;
            }
            catch (Exception error)
            {
                // A custom method with unsupported IL is a per-effect problem;
                // do not prevent every other character from starting the game.
                Log.Warn($"[SpirePvp] 角色效果需单独适配：{method.DeclaringType?.FullName}.{method.Name}：{error.Message}");
            }
        }
        return count;
    }
    public static void ApplyLoaded()
    {
        int count = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name ?? "";
            if (name == "mscorlib" || name == "netstandard" || name.StartsWith("System") || name.StartsWith("Microsoft")
                || name.StartsWith("Godot") || name.StartsWith("Harmony")) continue;
            count += Apply(assembly);
        }
        if (count > 0) Log.Info($"[SpirePvp] 已适配 {count} 个外部 Mod 的通用效果方法。");
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class ModCharacterEffectsReadyPatch
{
    [HarmonyPriority(Priority.Last)]
    static void Prefix() => ModCharacterCompatibility.ApplyLoaded();
}
