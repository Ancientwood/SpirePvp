using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace SpirePvp;

public sealed class PvpSettings
{
    // Legacy preference retained for file compatibility; the feature has no entry point.
    public bool BossDuels { get; set; }
    public bool Development { get; set; }
    public bool CooperativeDevelopment { get; set; } = true;
    public bool CardLimitEnabled { get; set; }
    public bool DeferredDamage { get; set; }
    public bool SameSeed { get; set; }
    public string LastLanAddress { get; set; } = string.Empty;
    private int _lanPort = LanAddress.DefaultPort;
    public int LanPort { get => _lanPort; set => _lanPort = value is >= 1 and <= 65535 ? value : LanAddress.DefaultPort; }
    private int _maxCardsPerTurn = 15;
    public int MaxCardsPerTurn { get => _maxCardsPerTurn; set => _maxCardsPerTurn = value is >= 1 and <= 100 ? value : 15; }
    private decimal _healthMultiplier = 2m;
    private decimal _blockMultiplier = 1.5m;
    public decimal HealthMultiplier
    {
        get => _healthMultiplier;
        set => _healthMultiplier = DuelMultipliers.Normalize(value, 2m);
    }
    public decimal BlockMultiplier
    {
        get => _blockMultiplier;
        set => _blockMultiplier = DuelMultipliers.Normalize(value, 1.5m);
    }
    private decimal _summonMultiplier = 1.5m, _doomMultiplier = .5m, _poisonMultiplier = 1m;
    public decimal SummonMultiplier { get => _summonMultiplier; set => _summonMultiplier = DuelMultipliers.NormalizeEffect(value, 1.5m); }
    public decimal DoomMultiplier { get => _doomMultiplier; set => _doomMultiplier = DuelMultipliers.NormalizeEffect(value, .5m); }
    public decimal PoisonMultiplier { get => _poisonMultiplier; set => _poisonMultiplier = DuelMultipliers.NormalizeEffect(value, 1m); }
    public bool Barricade { get; set; } = true;
    internal DuelMultipliers Multipliers => new(HealthMultiplier, BlockMultiplier, SummonMultiplier,
        DoomMultiplier, PoisonMultiplier, Barricade);
    private int _customCommonCards = 10, _customUncommonCards = 5, _customRareCards = 3;
    public int CustomCommonCards { get => _customCommonCards; set => _customCommonCards = NormalizeCardCount(value, 10); }
    public int CustomUncommonCards { get => _customUncommonCards; set => _customUncommonCards = NormalizeCardCount(value, 5); }
    public int CustomRareCards { get => _customRareCards; set => _customRareCards = NormalizeCardCount(value, 3); }
    public static int NormalizeCardCount(int value, int fallback) => value is >= 0 and <= 100 ? value : fallback;
    private int _lastConstructionMode;
    public int LastConstructionMode
    {
        get => _lastConstructionMode;
        set => _lastConstructionMode = value is >= 0 and <= 4 ? value : 0;
    }
    private static readonly Lazy<PvpSettings> Loaded = new(() => Load(SettingsPath));
    internal static PvpSettings Current => Loaded.Value;
    internal static string SettingsPath => ProjectSettings.GlobalizePath("user://spire_pvp_settings.json");

    internal static void RememberConstruction(int mode)
    {
        Current.LastConstructionMode = mode;
        try { Current.Save(SettingsPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Keep the selection for this session even if persistence is unavailable.
            Log.Error("[SpirePvp] 开局模式保存失败：" + e.Message);
        }
    }

    internal static void RememberDevelopment(bool enabled)
    {
        Current.Development = enabled;
        try { Current.Save(SettingsPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Log.Error("[SpirePvp] 战前发育设置保存失败：" + e.Message); }
    }

    internal static void RememberLan(string? address, ushort? port)
    {
        if (address != null) Current.LastLanAddress = address;
        if (port.HasValue) Current.LanPort = port.Value;
        try { Current.Save(SettingsPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Log.Error("[SpirePvp] LAN 设置保存失败：" + e.Message); }
    }

    public static PvpSettings Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<PvpSettings>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn("[SpirePvp] 设置读取失败，幕间决斗默认关闭：" + e.Message);
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, overwrite: true);
    }
}
