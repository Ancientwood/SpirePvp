using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace SpirePvp;

public sealed class PvpCumulativeAttackIntent(Player player) : SingleAttackIntent(() => DuelDamage.Outgoing(player))
{
    public override bool HasIntentTip => false;

    // Outgoing includes all modifiers committed when each attack was played.
    // Native SingleAttackIntent would run those hooks again for the preview.
    public override int GetTotalDamage(IEnumerable<Creature> targets, Creature owner)
        => (int)Math.Min(int.MaxValue, Math.Max(0, DuelDamage.Outgoing(player)));
}
