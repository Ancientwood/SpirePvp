using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;

namespace SpirePvp;

public static class PvpLobbyModifiers
{
    public static IReadOnlyCollection<ModifierModel> Create(bool sealedDeck) => Create(sealedDeck ? 1 : 0);
    public static int GetMode(IEnumerable<ModifierModel> modifiers) => modifiers.Any(m => m is PvpCustomDraft) ? 4 : modifiers.Any(m => m is PvpDraft20) ? 3
        : modifiers.Any(m => m is PvpDraft15) ? 2 : modifiers.Any(m => m is SealedDeck) ? 1 : 0;
    public static IReadOnlyCollection<ModifierModel> Create(int mode, int healthSteps = 4, int blockSteps = 3)
        => Create(mode, DuelMultipliers.FromSteps(healthSteps, blockSteps));
    public static IReadOnlyCollection<ModifierModel> Create(int mode, DuelMultipliers rules, bool development = false, bool cardLimitEnabled = false, int maxCardsPerTurn = 15, bool deferredDamage = false, bool cooperativeDevelopment = true, int customCommonCards = 10, int customUncommonCards = 5, int customRareCards = 3, bool sameSeed = false)
    {
        DuelMultipliers.FromSteps(rules.HealthSteps, rules.BlockSteps, rules.SummonSteps, rules.DoomSteps, rules.PoisonSteps, rules.Barricade);
        var duel = (PvpDuel)ModelDb.Modifier<PvpDuel>().ToMutable();
        duel.CustomCommonCards = PvpSettings.NormalizeCardCount(customCommonCards, 10);
        duel.CustomUncommonCards = PvpSettings.NormalizeCardCount(customUncommonCards, 5);
        duel.CustomRareCards = PvpSettings.NormalizeCardCount(customRareCards, 3);
        if (mode == 4 && duel.CustomCommonCards + duel.CustomUncommonCards + duel.CustomRareCards == 0)
            duel.CustomCommonCards = 1;
        duel.HealthSteps = rules.HealthSteps;
        duel.BlockSteps = rules.BlockSteps;
        duel.SummonSteps = rules.SummonSteps;
        duel.DoomSteps = rules.DoomSteps;
        duel.PoisonSteps = rules.PoisonSteps;
        duel.Barricade = rules.Barricade;
        duel.Development = development;
        duel.CooperativeDevelopment = development && cooperativeDevelopment;
        duel.CardLimitEnabled = cardLimitEnabled;
        duel.DeferredDamage = deferredDamage;
        duel.SameSeed = sameSeed;
        duel.MaxCardsPerTurn = maxCardsPerTurn is >= 1 and <= 100 ? maxCardsPerTurn : 15;
        return
        [
            duel,
            mode switch
            {
                0 => ModelDb.Modifier<Draft>().ToMutable(),
                1 => ModelDb.Modifier<SealedDeck>().ToMutable(),
                2 => ModelDb.Modifier<PvpDraft15>().ToMutable(),
                3 => ModelDb.Modifier<PvpDraft20>().ToMutable(),
                4 => ModelDb.Modifier<PvpCustomDraft>().ToMutable(),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            }
        ];
    }
}
