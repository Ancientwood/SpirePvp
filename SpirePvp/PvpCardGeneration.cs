using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

public static class PvpCardGeneration
{
    // Filter candidates before RNG selection so reward counts, rarities and
    // both peers' deterministic generation stay under the native factory.
    public static IEnumerable<CardModel> Filter(IRunState? state, IEnumerable<CardModel> cards)
        => (PvpSession.IsPvp(state) || BossDuel.Fighting && state?.CurrentRoom is MegaCrit.Sts2.Core.Rooms.CombatRoom room && PvpSession.IsArena(room.CombatState))
            ? cards.Where(card => card.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly)
            : cards;
}

[HarmonyPatch(typeof(CardCreationOptions), nameof(CardCreationOptions.GetPossibleCards))]
internal static class PvpRewardCardPoolPatch
{
    static void Postfix(Player player, ref IEnumerable<CardModel> __result)
        => __result = PvpCardGeneration.Filter(player.RunState, __result);
}

[HarmonyPatch(typeof(CardFactory), "FilterForPlayerCount")]
internal static class PvpCombatCardPoolPatch
{
    static void Postfix(IRunState runState, ref IEnumerable<CardModel> __result)
        => __result = PvpCardGeneration.Filter(runState, __result);
}

// Some effects select straight from unlocked pools instead of CardFactory.
[HarmonyPatch(typeof(CardPoolModel), nameof(CardPoolModel.GetUnlockedCards))]
internal static class PvpUnlockedCardPoolPatch
{
    static void Postfix(ref IEnumerable<CardModel> __result)
        => __result = PvpCardGeneration.Filter(PvpSession.GetState(RunManager.Instance), __result);
}
