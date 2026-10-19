namespace SpirePvp;

public enum DuelOutcome { InProgress, FirstWins, SecondWins, Draw }

public static class DuelRules
{
    public static DuelOutcome Outcome(bool firstAlive, bool secondAlive) => (firstAlive, secondAlive) switch
    {
        (true, true) => DuelOutcome.InProgress,
        (true, false) => DuelOutcome.FirstWins,
        (false, true) => DuelOutcome.SecondWins,
        _ => DuelOutcome.Draw
    };

    public static bool Opponents(ulong owner, ulong target) => owner != target;

    public static DuelOutcome SimultaneousOutcome(bool firstAlive, bool secondAlive, decimal firstNetAttack, decimal secondNetAttack)
    {
        if (firstAlive || secondAlive) return Outcome(firstAlive, secondAlive);
        return firstNetAttack > secondNetAttack ? DuelOutcome.FirstWins
            : secondNetAttack > firstNetAttack ? DuelOutcome.SecondWins : DuelOutcome.Draw;
    }
}
