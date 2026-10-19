using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.ValueProps;
using HarmonyLib;
using System.Reflection;

namespace SpirePvp;

public static class PvpTargets
{
    public static Player? Team(Creature creature) => creature.Player ?? creature.PetOwner;
    // Osty is the player's damage interceptor, not a second independently
    // targetable enemy. Native DieForYou handles death and excess damage.
    public static bool IsEnemyTarget(Creature creature) => creature.Monster is not Osty;
    public static IEnumerable<Creature> NormalizeDamage(IEnumerable<Creature> targets, Creature? dealer,
        CardModel? source, ValueProp props)
    {
        var attacker = dealer == null ? source?.Owner : Team(dealer);
        bool changed = false;
        var normalized = targets.Select(target =>
        {
            if (PvpSession.IsArena(target.CombatState) && target.Monster is Osty
                && target.PetOwner is { } owner && attacker != null && attacker != owner && props.IsPoweredAttack())
            {
                changed = true;
                return owner.Creature;
            }
            return target;
        }).ToList();
        return changed ? normalized.Distinct() : normalized;
    }
}

[HarmonyPatch]
internal static class OstyDamageRoutePatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
        [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
            typeof(Creature), typeof(CardModel), typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay)]);
    static void Prefix(ref IEnumerable<Creature>? targets, Creature? dealer, CardModel? cardSource, ValueProp props)
    {
        if (targets != null) targets = PvpTargets.NormalizeDamage(targets, dealer, cardSource, props);
    }
}
