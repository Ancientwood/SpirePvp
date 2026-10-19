using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Logging;

namespace SpirePvp;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        new Harmony("spirepvp.duel").PatchAll(typeof(ModEntry).Assembly);
        // Native ModelDb.Init discovers AbstractModel subclasses in loaded mods.
        // Injecting here constructs them twice during startup and is fatal.
        Log.Info("[SpirePvp] 已加载双人构筑决斗。双方必须安装相同版本。");
    }
}
