using BaseLib.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace SpirePvp;

// Static properties are BaseLib's supported settings contract. Keep the original
// settings store as well, so upgrading preserves construction mode and multipliers.
public sealed class PvpModConfig : SimpleModConfig
{
    [ConfigSlider(.5, 10, .5, Format = "{0:0.0} 倍")]
    public static double 生命倍率
    {
        get => (double)PvpSettings.Current.HealthMultiplier;
        set { PvpSettings.Current.HealthMultiplier = double.IsFinite(value) && value is >= .5 and <= 10 ? (decimal)value : 2m; Persist(); }
    }

    [ConfigSlider(.5, 10, .5, Format = "{0:0.0} 倍")]
    public static double 格挡倍率
    {
        get => (double)PvpSettings.Current.BlockMultiplier;
        set { PvpSettings.Current.BlockMultiplier = double.IsFinite(value) && value is >= .5 and <= 10 ? (decimal)value : 1.5m; Persist(); }
    }

    [ConfigSlider(0, 10, .5, Format = "{0:0.0} 倍")]
    public static double 召唤倍率
    {
        get => (double)PvpSettings.Current.SummonMultiplier;
        set { PvpSettings.Current.SummonMultiplier = EffectValue(value, 1.5m); Persist(); }
    }

    [ConfigSlider(0, 10, .5, Format = "{0:0.0} 倍")]
    public static double 受到灾厄倍率
    {
        get => (double)PvpSettings.Current.DoomMultiplier;
        set { PvpSettings.Current.DoomMultiplier = EffectValue(value, .5m); Persist(); }
    }

    [ConfigSlider(0, 10, .5, Format = "{0:0.0} 倍")]
    public static double 受到中毒倍率
    {
        get => (double)PvpSettings.Current.PoisonMultiplier;
        set { PvpSettings.Current.PoisonMultiplier = EffectValue(value, 1m); Persist(); }
    }

    public static bool 壁垒
    {
        get => PvpSettings.Current.Barricade;
        set { PvpSettings.Current.Barricade = value; Persist(); }
    }

    public static bool 相同种子
    {
        get => PvpSettings.Current.SameSeed;
        set { PvpSettings.Current.SameSeed = value; Persist(); }
    }

    public static bool 战前发育
    {
        get => PvpSettings.Current.Development;
        set { PvpSettings.Current.Development = value; Persist(); }
    }
    public static bool 回合末统一结算伤害
    {
        get => PvpSettings.Current.DeferredDamage;
        set { PvpSettings.Current.DeferredDamage = value; Persist(); }
    }
    [ConfigVisibleIf(nameof(战前发育))]
    public static bool 联机发育
    {
        get => PvpSettings.Current.CooperativeDevelopment;
        set { PvpSettings.Current.CooperativeDevelopment = value; Persist(); }
    }
    public static bool 限制每回合出牌数
    {
        get => PvpSettings.Current.CardLimitEnabled;
        set { PvpSettings.Current.CardLimitEnabled = value; Persist(); }
    }
    [ConfigSlider(1, 100, 1, Format = "{0:0} 张")]
    public static double 每回合最多出牌
    {
        get => PvpSettings.Current.MaxCardsPerTurn;
        set { PvpSettings.Current.MaxCardsPerTurn = double.IsFinite(value) && value is >= 1 and <= 100 ? (int)Math.Round(value) : 15; Persist(); }
    }

    [ConfigSlider(0, 100, 1, Format = "{0:0} 张")]
    public static double 自定义开局白卡数量
    {
        get => PvpSettings.Current.CustomCommonCards;
        set { PvpSettings.Current.CustomCommonCards = double.IsFinite(value) && value is >= 0 and <= 100 ? (int)Math.Round(value) : 10; Persist(); }
    }
    [ConfigSlider(0, 100, 1, Format = "{0:0} 张")]
    public static double 自定义开局蓝卡数量
    {
        get => PvpSettings.Current.CustomUncommonCards;
        set { PvpSettings.Current.CustomUncommonCards = double.IsFinite(value) && value is >= 0 and <= 100 ? (int)Math.Round(value) : 5; Persist(); }
    }
    [ConfigSlider(0, 100, 1, Format = "{0:0} 张")]
    public static double 自定义开局金卡数量
    {
        get => PvpSettings.Current.CustomRareCards;
        set { PvpSettings.Current.CustomRareCards = double.IsFinite(value) && value is >= 0 and <= 100 ? (int)Math.Round(value) : 3; Persist(); }
    }

    private static decimal EffectValue(double value, decimal fallback)
        => double.IsFinite(value) && value is >= 0 and <= 10 ? (decimal)value : fallback;

    private static void Persist()
    {
        try { PvpSettings.Current.Save(PvpSettings.SettingsPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("[SpirePvp] 倍率保存失败，本次会话仍使用新设置：" + e.Message);
        }
    }
}

// Register before the menu becomes usable, without calling Godot during model
// initialization or offline tests. BaseLib is loaded first by manifest dependency.
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class PvpModConfigRegistrationPatch
{
    static void Prefix()
    {
        if (ModConfigRegistry.Get("SpirePvp") == null)
            ModConfigRegistry.Register("SpirePvp", new PvpModConfig());
    }
}
