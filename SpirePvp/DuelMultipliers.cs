using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

public sealed record DuelMultipliers(decimal Health, decimal Block, decimal Summon = 1.5m,
    decimal Doom = .5m, decimal Poison = 1m, bool Barricade = true)
{
    public static DuelMultipliers Default { get; } = new(2m, 1.5m);
    public int HealthSteps => (int)(Health * 2);
    public int BlockSteps => (int)(Block * 2);
    public int SummonSteps => (int)(Summon * 2);
    public int DoomSteps => (int)(Doom * 2);
    public int PoisonSteps => (int)(Poison * 2);
    public int Encode() => ((((HealthSteps * 21 + BlockSteps) * 21 + SummonSteps) * 21 + DoomSteps) * 21 + PoisonSteps) * 2 + (Barricade ? 1 : 0);
    public static decimal Normalize(decimal value, decimal fallback) => value is >= .5m and <= 10m
        ? Math.Round(value * 2, 0, MidpointRounding.AwayFromZero) / 2 : fallback;
    public static decimal NormalizeEffect(decimal value, decimal fallback) => value is >= 0m and <= 10m
        ? Math.Round(value * 2, 0, MidpointRounding.AwayFromZero) / 2 : fallback;
    public static DuelMultipliers FromSteps(int health, int block, int summon = 3, int doom = 1,
        int poison = 2, bool barricade = true)
    {
        if (health is < 1 or > 20 || block is < 1 or > 20 || summon is < 0 or > 20
            || doom is < 0 or > 20 || poison is < 0 or > 20)
            throw new InvalidOperationException("PvP 倍率数据超出允许范围。");
        return new(health / 2m, block / 2m, summon / 2m, doom / 2m, poison / 2m, barricade);
    }
    public static DuelMultipliers Decode(int encoded)
    {
        if (encoded < 0) throw new InvalidOperationException("PvP 倍率数据无效。");
        bool barricade = encoded % 2 != 0;
        int value = encoded / 2;
        int poison = value % 21; value /= 21;
        int doom = value % 21; value /= 21;
        int summon = value % 21; value /= 21;
        return FromSteps(value / 21, value % 21, summon, doom, poison, barricade);
    }
    public static DuelMultipliers FromModifier(PvpDuel duel) => FromSteps(duel.HealthSteps, duel.BlockSteps,
        duel.SummonSteps, duel.DoomSteps, duel.PoisonSteps, duel.Barricade);
    public static DuelMultipliers ForRun(IRunState? run)
    {
        if (run?.Modifiers.FirstOrDefault(m => m is PvpDuel) is PvpDuel duel)
            return FromModifier(duel);
        return BossDuel.Active ? BossDuel.Multipliers : Default;
    }
}
