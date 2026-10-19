using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace SpirePvp;

// Code-only localization for the duel's models; no asset pack is required.
[HarmonyPatch]
internal static class LocalizationPatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[] { nameof(LocString.GetRawText), nameof(LocString.GetFormattedText) }
        .Select(name => AccessTools.Method(typeof(LocString), name));
    static bool Prefix(LocString __instance, ref string __result)
    {
        if (__instance.LocTable == "combat_messages" && __instance.LocEntryKey == "SPIRE_PVP_CARD_LIMIT")
        {
            __result = "我被出牌上限所阻挡";
            return false;
        }
        if (__instance.LocTable is not ("modifiers" or "powers")) return true;
        if (__instance.LocTable == "modifiers" && __instance.LocEntryKey.StartsWith("SPIRE_PVP_CUSTOM_SELECT."))
        {
            var parts = __instance.LocEntryKey.Split('.');
            if (parts.Length == 3 && int.TryParse(parts[2], out var count))
            {
                var rarity = parts[1] switch { "Common" => "白卡", "Uncommon" => "蓝卡", "Rare" => "金卡", _ => "卡牌" };
                __result = $"{rarity}：从 {count * 3} 张中选择 {count} 张";
                return false;
            }
        }
        __result = __instance.LocEntryKey switch
        {
            "SPIRE_PVP.title" => "PvP · 构筑决斗",
            "SPIRE_PVP.description" => "双方同步回合、抽牌和恢复能量，自由抢速出牌。",
            "SPIRE_PVP_DRAFT15.title" => "PvP 选牌：15 张",
            "SPIRE_PVP_DRAFT15.description" => "15 次三选一，清空初始牌组。",
            "SPIRE_PVP_DRAFT20.title" => "PvP 选牌：20 张",
            "SPIRE_PVP_DRAFT20.description" => "20 次三选一，清空初始牌组。",
            "SPIRE_PVP_CUSTOM_DRAFT.title" => "PvP 自定义选牌",
            "SPIRE_PVP_CUSTOM_DRAFT.description" => "按房主配置的白、蓝、金卡数量分三批构筑，每批从三倍数量的候选中选择指定张数，清空初始牌组。",
            "SPIRE_PVP_FORTITUDE.title" => "决斗坚韧",
            "SPIRE_PVP_FORTITUDE.description" => FortitudeDescription(),
            _ => ""
        };
        return __result.Length == 0;
    }
    private static string FortitudeDescription()
    {
        var rules = DuelMultipliers.ForRun(PvpSession.GetState(MegaCrit.Sts2.Core.Runs.RunManager.Instance));
        return $"开战时最大生命与当前生命变为 {rules.Health:0.#} 倍。获得的格挡变为 {rules.Block:0.#} 倍，召唤量变为 {rules.Summon:0.#} 倍，可与其他能力牌的效果叠加。"
            + (rules.Barricade ? "回合交替时格挡不会消失。" : "本状态不保留回合间格挡。")
            + $"受到的新增灾厄层数变为 {rules.Doom:0.#} 倍，新增中毒层数变为 {rules.Poison:0.#} 倍；减少或清除层数的效果不变。小数沿用原版向下取整；存活玩家缩放后的生命至少为 1。持续整场决斗，不叠加。";
    }
}
