using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpirePvp;

public sealed class PvpFortitudePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override LocString Title => new("powers", "SPIRE_PVP_FORTITUDE.title");
    public override LocString Description => new("powers", "SPIRE_PVP_FORTITUDE.description");

    // Native stacking skips BeforeApplied for an existing instance. Preserve the
    // current HP ratio instead of healing a wounded player to the new maximum.
    public override async Task BeforeApplied(Creature target, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (amount <= 0 || target.HasPower<PvpFortitudePower>()) return;
        var rules = DuelMultipliers.ForRun(target.Player?.RunState);
        decimal hp = target.CurrentHp > 0 ? Math.Max(1, decimal.Floor(target.CurrentHp * rules.Health)) : 0;
        await CreatureCmd.SetMaxHp(target, Math.Max(1, decimal.Floor(target.MaxHp * rules.Health)));
        await CreatureCmd.SetCurrentHp(target, hp);
    }

    // Use the same multiplicative hook as Unmovable, with a separate model ID
    // and no per-turn card limit. Native powers retain their own effects/counters.
    public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props,
        CardModel? cardSource, CardPlay? cardPlay) => target == Owner ? DuelMultipliers.ForRun(Owner.Player?.RunState).Block : 1m;

    public override decimal ModifySummonAmount(Player summoner, decimal amount, AbstractModel? source)
        => summoner.Creature == Owner ? amount * DuelMultipliers.ForRun(Owner.Player?.RunState).Summon : amount;

    // Same hook as native Barricade: prevent turn-boundary clearing, while
    // damage and effects that explicitly remove block still work normally.
    public override bool ShouldClearBlock(Creature creature) => creature != Owner || !DuelMultipliers.ForRun(Owner.Player?.RunState).Barricade;

    public override bool TryModifyPowerAmountReceived(PowerModel canonicalPower, Creature target, decimal amount,
        Creature? applier, out decimal modifiedAmount)
    {
        // Receive resistance applies after the source's modifiers. Reductions
        // and cleansing keep their original amounts; other debuffs are unchanged.
        bool resist = target == Owner && (canonicalPower is DoomPower or PoisonPower) && amount > 0;
        var rules = DuelMultipliers.ForRun(Owner.Player?.RunState);
        modifiedAmount = resist ? amount * (canonicalPower is DoomPower ? rules.Doom : rules.Poison) : amount;
        return resist;
    }
}
