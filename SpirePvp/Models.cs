using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SpirePvp;

// This marker is serialized by the native lobby, so clients never rely on a local menu flag.
public sealed class PvpDuel : ModifierModel
{
    [SavedProperty] public int HealthSteps { get; set; } = 4;
    [SavedProperty] public int BlockSteps { get; set; } = 3;
    [SavedProperty] public int SummonSteps { get; set; } = 3;
    [SavedProperty] public int DoomSteps { get; set; } = 1;
    [SavedProperty] public int PoisonSteps { get; set; } = 2;
    [SavedProperty] public bool Barricade { get; set; } = true;
    [SavedProperty] public bool Development { get; set; }
    [SavedProperty] public bool CooperativeDevelopment { get; set; } = true;
    [SavedProperty] public bool CardLimitEnabled { get; set; }
    [SavedProperty] public bool DeferredDamage { get; set; }
    [SavedProperty] public bool SameSeed { get; set; }
    [SavedProperty] public int CustomCommonCards { get; set; } = 10;
    [SavedProperty] public int CustomUncommonCards { get; set; } = 5;
    [SavedProperty] public int CustomRareCards { get; set; } = 3;
    [SavedProperty] public int MaxCardsPerTurn { get; set; } = 15;
    public override bool ShouldPlay(CardModel card, MegaCrit.Sts2.Core.Entities.Cards.AutoPlayType autoPlayType)
        => !CardLimitEnabled || !PvpSession.IsArena(card.Owner?.Creature.CombatState)
            || DuelCardLimit.CanPlay(card, MaxCardsPerTurn);
    public override LocString Title => new("modifiers", "SPIRE_PVP.title");
    public override LocString Description => new("modifiers", "SPIRE_PVP.description");
    protected override string IconPath => "res://images/packed/modifiers/draft.png";
    public override bool ShouldStopCombatFromEnding() => RunState.CurrentRoom is CombatRoom { Encounter: PvpArena }
        && (DuelDamage.IsSettling || RunState.Players.Count == 2 && RunState.Players.All(p => p.Creature.IsAlive));
}

// Empty enemy side: players remain on the native player side and share its turn.
public sealed class PvpArena : EncounterModel
{
    public override RoomType RoomType => RoomType.Monster;
    public override bool ShouldGiveRewards => false;
    public override IEnumerable<MonsterModel> AllPossibleMonsters => [];
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters() => [];
    public override float CalculateGoldProportion(MegaCrit.Sts2.Core.Combat.CombatState combatState) => 0;
    public override Vector2 GetCameraOffset() => Vector2.Zero;
}
