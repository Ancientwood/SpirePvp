using MegaCrit.Sts2.Core.Entities.Creatures;

namespace MintySpire2.MintySpire2Code.combat;

public static class SummedIncomingDamageRender
{
    public static int Calls;
    public static int CalculateIncomingDamage(Creature creature)
    {
        Calls++;
        // Reproduce the incompatible assumption in the supplied client log.
        if (creature.CombatState?.Encounter is SpirePvp.PvpArena)
            throw new NullReferenceException("player opponents have no Monster intent");
        return 17;
    }
}
