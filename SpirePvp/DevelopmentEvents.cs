using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SpirePvp;

public static class DevelopmentEvents
{
    public static IReadOnlyList<string> Names { get; } = LoadNames();
    private static IReadOnlyList<string> LoadNames()
    {
        using var stream = typeof(DevelopmentEvents).Assembly.GetManifestResourceStream("SpirePvp.EventWhitelist.md")!;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
    public static IReadOnlyList<string> Parse(string table)
    {
        var names = new List<string>();
        foreach (var line in table.Split('\n'))
        {
            var columns = line.Trim().Split('|');
            if (columns.Length < 8 || columns[1].Trim() != "是") continue;
            var name = columns[6].Trim();
            if (name.Length == 0 || name.Any(c => !char.IsAsciiLetterOrDigit(c)))
                throw new InvalidDataException("事件白名单 ID 无效：" + name);
            if (!names.Contains(name)) names.Add(name);
        }
        if (names.Count == 0) throw new InvalidDataException("事件白名单没有保留任何事件。");
        return names.AsReadOnly();
    }
    public static IReadOnlyList<EventModel> Models => Names.Select(name =>
    {
        var type = typeof(EventModel).Assembly.GetType("MegaCrit.Sts2.Core.Models.Events." + name)
            ?? throw new InvalidDataException("未知白名单事件：" + name);
        return ModelDb.GetById<EventModel>(ModelDb.GetId(type));
    }).ToArray();

    public static bool IsAllowed(EventModel model, IRunState run)
    {
        // Native IsAllowed may mix act restrictions with gold/deck/HP checks.
        // Evaluate only this predicate through an act-2 view. Never modify the
        // real run, its players or room/reward scaling.
        var view = DispatchProxy.Create<IRunState, DevelopmentEventConditionView>();
        ((DevelopmentEventConditionView)(object)view).Source = run;
        return model.IsAllowed(view);
    }
    public static bool IsUnlocked(EventModel model, UnlockState unlocks) =>
        (unlocks.IsEpochRevealed<Event1Epoch>() || !Event1Epoch.Events.Any(e => e.Id == model.Id)) &&
        (unlocks.IsEpochRevealed<Event2Epoch>() || !Event2Epoch.Events.Any(e => e.Id == model.Id)) &&
        (unlocks.IsEpochRevealed<Event3Epoch>() || !Event3Epoch.Events.Any(e => e.Id == model.Id));

    public static T Pick<T>(IReadOnlyList<T> eligible, Func<T, bool> visited, Rng rng) where T : class
    {
        if (eligible.Count == 0) throw new InvalidOperationException("没有可用的白名单事件。");
        var fresh = eligible.Where(e => !visited(e)).ToArray();
        return rng.NextItem(fresh.Length == 0 ? eligible : fresh)!;
    }
    public static EventModel Pull(RunState run)
    {
        var eligible = Models.Where(e => IsUnlocked(e, run.UnlockState) && IsAllowed(e, run)).ToArray();
        // The approved pool has unconditional events. Never fall back to an
        // excluded native event if a future whitelist has no eligible entry.
        var rng = SameSeed.Enabled(run) ? SameSeed.EventStream(run) : run.Rng.UpFront;
        var selected = Pick(eligible, e => run.VisitedEventIds.Contains(e.Id), rng);
        run.AddVisitedEvent(selected);
        return selected;
    }
}

public class DevelopmentEventConditionView : DispatchProxy
{
    public IRunState Source = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod!.Name == "get_CurrentActIndex" ? 1 : targetMethod.Invoke(Source, args);
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEvent))]
internal static class DevelopmentWhitelistPatch
{
    [HarmonyPriority(Priority.First)]
    static bool Prefix(RunState runState, ref EventModel __result)
    {
        if (runState.Map is not PvpDevelopmentMap || !PvpSession.IsPvp(runState)) return true;
        __result = DevelopmentEvents.Pull(runState);
        return false;
    }
}
