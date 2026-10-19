using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

// Each creature sees enemies relative to its owner. The manager keeps the original
// CombatState, preserving native turn order, CombatIds, checksums and action queues.
public sealed class PvpCombatView(CombatState state, Player owner) : ICombatState, ICardScope
{
    public CombatState State => state;
    public Player Owner => owner;
    private static Player? Team(Creature c) => c.Player ?? c.PetOwner;
    public IReadOnlyList<Creature> Allies => state.Creatures.Where(c => Team(c) == owner).ToList();
    public IReadOnlyList<Creature> Enemies => state.Creatures.Where(c => Team(c) != null && Team(c) != owner && PvpTargets.IsEnemyTarget(c)).ToList();
    public IReadOnlyList<Creature> HittableEnemies => Enemies.Where(c => c.IsHittable).ToList();
    public IReadOnlyList<Creature> Creatures => state.Creatures;
    // Ally-only player list prevents multiplayer support cards from helping the opponent.
    public IReadOnlyList<Creature> PlayerCreatures => Allies.Where(c => c.IsPlayer).ToList();
    public IReadOnlyList<Player> Players => state.Players;
    public IRunState RunState => state.RunState;
    public IReadOnlyList<ModifierModel> Modifiers => state.Modifiers;
    public MultiplayerScalingModel? MultiplayerScalingModel => null;
    public int RoundNumber { get => state.RoundNumber; set => state.RoundNumber = value; }
    public CombatSide CurrentSide { get => state.CurrentSide; set => state.CurrentSide = value; }
    public EncounterModel? Encounter => state.Encounter;
    public IReadOnlyList<Creature> EscapedCreatures => state.EscapedCreatures;
    public IReadOnlyList<Creature> CreaturesOnCurrentSide => state.CreaturesOnCurrentSide;
    public event Action<ICombatState>? CreaturesChanged { add => state.CreaturesChanged += value; remove => state.CreaturesChanged -= value; }
    public T CreateCard<T>(Player p) where T : CardModel => state.CreateCard<T>(p);
    public CardModel CreateCard(CardModel c, Player p) => state.CreateCard(c, p);
    public CardModel CloneCard(CardModel c) => state.CloneCard(c);
    public void AddCard(CardModel c, Player p) => state.AddCard(c, p);
    public void RemoveCard(CardModel c) => state.RemoveCard(c);
    public bool ContainsCard(CardModel c) => state.ContainsCard(c);
    public void AddPlayer(Player p) => state.AddPlayer(p);
    public Creature CreateCreature(MonsterModel m, CombatSide side, string? slot) => state.CreateCreature(m, side, slot);
    public void CreatureEscaped(Creature c) => state.CreatureEscaped(c);
    public void RemoveCreature(Creature c, bool unattach = true) => state.RemoveCreature(c, unattach);
    public bool ContainsCreature(Creature c) => state.ContainsCreature(c);
    public bool ContainsMonster<T>() where T : MonsterModel => state.ContainsMonster<T>();
    public Creature? GetCreature(uint? id) => state.GetCreature(id);
    public Task<Creature?> GetCreatureAsync(uint? id, double timeout) => state.GetCreatureAsync(id, timeout);
    public IReadOnlyList<Creature> GetCreaturesOnSide(CombatSide side) => side == CombatSide.Player ? Allies : Enemies;
    public IReadOnlyList<Creature> GetOpponentsOf(Creature c) => state.Creatures.Where(x => Team(x) != null && Team(x) != Team(c) && PvpTargets.IsEnemyTarget(x)).ToList();
    public IReadOnlyList<Creature> GetTeammatesOf(Creature c) => state.Creatures.Where(x => Team(x) == Team(c)).ToList();
    public Player? GetPlayer(ulong id) => state.GetPlayer(id);
    public IEnumerable<AbstractModel> IterateHookListeners() => state.IterateHookListeners();
    public void SortEnemiesBySlotName() => state.SortEnemiesBySlotName();
    public void SetEnemyIndex(Creature c, int i) => state.SetEnemyIndex(c, i);
    public void AddCreature(Creature c) => state.AddCreature(c);
    public bool IsLiveCombat() => state.IsLiveCombat();
}
