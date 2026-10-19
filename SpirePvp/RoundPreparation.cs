using System.Runtime.CompilerServices;

namespace SpirePvp;

internal static class RoundPreparation
{
    private sealed class Pending { public List<Task> Tasks { get; } = []; }
    private static readonly ConditionalWeakTable<object, Pending> Turns = new();
    internal static void Begin(object turn) { Turns.Remove(turn); Turns.Add(turn, new()); }
    internal static void Add(object turn, Task task) => Turns.GetOrCreateValue(turn).Tasks.Add(task);
    internal static Task Complete(object turn) => Turns.TryGetValue(turn, out var pending)
        ? Task.WhenAll(pending.Tasks) : Task.CompletedTask;
}
