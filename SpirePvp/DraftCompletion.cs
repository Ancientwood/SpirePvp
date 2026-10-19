namespace SpirePvp;

public static class DraftCompletion
{
    public static async Task WaitForBoth(IReadOnlyList<Task> selections, int localIndex, Action localReady)
    {
        var all = Task.WhenAll(selections);
        await selections[localIndex];
        localReady();
        await all;
    }
}
