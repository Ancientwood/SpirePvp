using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
namespace ExampleCharacterMod;
public sealed class ExampleCard : CardModel
{
    public ExampleCard() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }
    public CombatSide ReadSide(Creature creature) => creature.Side;
    public bool CheckEnemy(Creature creature) => creature.IsEnemy;
    public async Task<bool> CheckEnemyAsync(Creature creature)
    {
        await Task.Yield();
        return Owner != null && creature.IsEnemy;
    }
    public IEnumerable<Creature> Enemies(IEnumerable<Creature> creatures) => creatures.Where(c => c.Side != Owner.Creature.Side);
}
