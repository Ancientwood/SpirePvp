using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;

namespace SpirePvp;

public static class DuelCardLimit
{
    public static int Count(IEnumerable<CardPlayStartedEntry> entries, Player player, ICombatState state) =>
        entries.Count(e => e.CardPlay.Player == player && e.CardPlay.PlayIndex == 0 && e.HappenedThisTurn(state));
    public static bool CanPlay(CardModel card, int maximum) =>
        card.Owner?.Creature.CombatState is not { } state ||
        Count(CombatManager.Instance.History.CardPlaysStarted, card.Owner, state) < maximum;
}

// Native dialogue only recognizes card, power and relic preventers, not modifiers.
[HarmonyPatch]
internal static class DuelCardLimitDialoguePatch
{
    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.UnplayableReasonExtensions"),
        "GetPlayerDialogueLine");

    static bool Prefix(UnplayableReason reason, AbstractModel? preventer, ref LocString? __result)
    {
        if (preventer is not PvpDuel || !reason.HasFlag(UnplayableReason.BlockedByHook)
            || reason.HasFlag(UnplayableReason.NoLivingAllies)
            || reason.HasFlag(UnplayableReason.EnergyCostTooHigh)
            || reason.HasFlag(UnplayableReason.StarCostTooHigh)) return true;

        __result = new LocString("combat_messages", "SPIRE_PVP_CARD_LIMIT");
        return false;
    }
}
