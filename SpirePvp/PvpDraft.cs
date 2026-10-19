using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace SpirePvp;

public sealed class PvpDraft15 : Draft
{
    public override LocString Title => new("modifiers", "SPIRE_PVP_DRAFT15.title");
    public override LocString Description => new("modifiers", "SPIRE_PVP_DRAFT15.description");
    protected override string IconPath => "res://images/packed/modifiers/draft.png";
    public override Func<Task> GenerateNeowOption(EventModel e) => () => PvpDraft.Select(e.Owner!, 15);
}

public sealed class PvpDraft20 : Draft
{
    public override LocString Title => new("modifiers", "SPIRE_PVP_DRAFT20.title");
    public override LocString Description => new("modifiers", "SPIRE_PVP_DRAFT20.description");
    protected override string IconPath => "res://images/packed/modifiers/draft.png";
    public override Func<Task> GenerateNeowOption(EventModel e) => () => PvpDraft.Select(e.Owner!, 20);
}

public sealed class PvpCustomDraft : Draft
{
    public override LocString Title => new("modifiers", "SPIRE_PVP_CUSTOM_DRAFT.title");
    public override LocString Description => new("modifiers", "SPIRE_PVP_CUSTOM_DRAFT.description");
    protected override string IconPath => "res://images/packed/modifiers/draft.png";
    public override Func<Task> GenerateNeowOption(EventModel e) => () => Select(e.Owner!);

    public static IEnumerable<CardRarity> Rarities(PvpDuel rules)
    {
        foreach (var (rarity, count) in new[] { (CardRarity.Common, rules.CustomCommonCards),
            (CardRarity.Uncommon, rules.CustomUncommonCards), (CardRarity.Rare, rules.CustomRareCards) })
            for (int i = 0; i < PvpSettings.NormalizeCardCount(count, 0); i++) yield return rarity;
    }

    public static CardCreationOptions Options(Player player, CardRarity rarity) =>
        CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool], c => c.Rarity == rarity)
            .WithFlags(CardCreationFlags.NoUpgradeRoll | CardCreationFlags.NoRarityModification);

    public static List<CardCreationResult> CreateCandidates(Player player, CardRarity rarity, int count)
    {
        var options = Options(player, rarity);
        var available = options.GetPossibleCards(player).Distinct().Count();
        if (available < 3) throw new InvalidOperationException($"角色的 {rarity} 卡池不足三张不同卡牌，无法生成组内不重复的候选。");
        var cards = new List<CardCreationResult>();
        // Each native reward call has its own three-card exclusion list.
        // Reset it for each group; the full batch remains one sealed-deck grid.
        for (int group = 0; group < count; group++)
        {
            var generated = CardFactory.CreateForReward(player, 3, options).Take(3).ToList();
            if (generated.Count != 3 || generated.Select(c => c.Card.Id).Distinct().Count() != 3)
                throw new InvalidOperationException("自定义构筑未能生成三张不同的候选卡牌。");
            cards.AddRange(generated);
        }
        return cards;
    }

    private static async Task Select(Player player)
    {
        // Native sealed-deck selection synchronizes indexes independently for
        // each player. All peers generate the same batches and await each choice.
        foreach (var batch in Rarities(player.RunState.Modifiers.OfType<PvpDuel>().Single()).GroupBy(r => r))
        {
            var count = batch.Count();
            var candidates = CreateCandidates(player, batch.Key, count);
            var prefs = new CardSelectorPrefs(new LocString("modifiers", $"SPIRE_PVP_CUSTOM_SELECT.{batch.Key}.{count}"), count)
                { Cancelable = false, RequireManualConfirmation = true };
            var cards = (await CardSelectCmd.FromSimpleGridForRewards(new BlockingPlayerChoiceContext(), candidates, player, prefs)).ToList();
            CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(cards, PileType.Deck), 1.2f, CardPreviewStyle.GridLayout);
        }
        foreach (var p in player.RunState.Players) p.RelicGrabBag.Remove<PandorasBox>();
        player.RunState.SharedRelicGrabBag.Remove<PandorasBox>();
    }
}

internal static class PvpDraft
{
    // Keep native Draft's rarity, selection synchronization, history and relic exclusions.
    internal static async Task Select(Player player, int count)
    {
        var options = new CardCreationOptions([player.Character.CardPool], CardCreationSource.Other,
            CardRarityOddsType.RegularEncounter).WithFlags(CardCreationFlags.NoUpgradeRoll);
        for (int i = 0; i < count; i++)
        {
            var reward = new CardReward(options, 3, player) { CanSkip = false };
            reward.Populate();
            await reward.SelectUnsynchronized();
        }
        foreach (var p in player.RunState.Players) p.RelicGrabBag.Remove<PandorasBox>();
        player.RunState.SharedRelicGrabBag.Remove<PandorasBox>();
    }
}
