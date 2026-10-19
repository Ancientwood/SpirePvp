using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

public static class SameSeed
{
    private sealed class Streams
    {
        public string Location = "";
        public readonly Dictionary<RunRngType, Rng> Values = [];
    }
    private static readonly ConditionalWeakTable<Player, Streams> PlayerStreams = new();
    internal static readonly AsyncLocal<Player?> Actor = new();
    public static bool Enabled(IRunState? run) => run?.Modifiers.OfType<PvpDuel>().FirstOrDefault()?.SameSeed == true;
    public static Rng EventStream(IRunState run) => new(run.Rng.Seed, $"spire_pvp_same_events_{run.CurrentActIndex}_{run.ActFloor}");
    public static Rng PersonalStream(Player player, RunRngType type)
    {
        var run = player.RunState;
        var location = $"{run.CurrentActIndex}:{run.ActFloor}:{run.CurrentMapPointHistoryEntry?.Rooms.Count ?? 0}";
        var streams = PlayerStreams.GetOrCreateValue(player);
        if (streams.Location != location) { streams.Location = location; streams.Values.Clear(); }
        if (!streams.Values.TryGetValue(type, out var rng))
            streams.Values[type] = rng = new Rng(run.Rng.Seed, $"spire_pvp_same_{location}_{type}");
        return rng;
    }
    internal static bool PersonalCombatStream(RunRngType type) => type == RunRngType.Shuffle || type.ToString().StartsWith("Combat");
}

[HarmonyPatch(typeof(Player), nameof(Player.InitializeSeed))]
internal static class SamePlayerSeedPatch
{
    static void Postfix(Player __instance, string seed)
    {
        if (!SameSeed.Enabled(__instance.RunState)) return;
        var rng = new PlayerRngSet(StringHelper.GetDeterministicHashCode(seed));
        AccessTools.PropertySetter(typeof(Player), nameof(Player.PlayerRng)).Invoke(__instance, [rng]);
        AccessTools.PropertySetter(typeof(Player), nameof(Player.PlayerOdds)).Invoke(__instance, [new PlayerOddsSet(rng)]);
    }
}

[HarmonyPatch(typeof(Rng), MethodType.Constructor, new[] { typeof(Player), typeof(ModelId), typeof(ulong) })]
internal static class SameContentSeedPatch
{
    static void Postfix(Rng __instance, Player player, ModelId id, ulong mixin)
    {
        if (SameSeed.Enabled(player.RunState))
            __instance.LoadFromSerializable(new Rng(unchecked(player.RunState.Rng.Seed + StringHelper.GetDeterministicHashCode(id.Entry) + mixin)).ToSerializable());
    }
}

[HarmonyPatch(typeof(EventModel), nameof(EventModel.Rng), MethodType.Setter)]
internal static class SameEventSeedPatch
{
    static void Prefix(EventModel __instance, ref Rng __0)
    {
        var owner = __instance.Owner;
        if (owner != null && SameSeed.Enabled(owner.RunState))
            __0 = new Rng(unchecked(owner.RunState.Rng.Seed + StringHelper.GetDeterministicHashCode(__instance.Id.Entry)));
    }
}

[HarmonyPatch]
internal static class SameSeedActorPatch
{
    static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(CardModel), nameof(CardModel.OnPlayWrapper)),
        AccessTools.Method(typeof(PotionModel), nameof(PotionModel.OnUseWrapper)),
        AccessTools.Method(typeof(Player), nameof(Player.PopulateCombatState)),
        AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Shuffle))
    }.Concat(typeof(CardPileCmd).GetMethods().Where(m => m.Name is "Draw" or "DrawWithoutBlockingOnOtherPlayers"));
    static void Prefix(object? __instance, object[] __args, out Player? __state)
    {
        __state = SameSeed.Actor.Value;
        var player = __instance switch { CardModel card => card.Owner, PotionModel potion => potion.Owner,
            Player owner => owner, _ => __args.OfType<Player>().FirstOrDefault() };
        SameSeed.Actor.Value = player != null && SameSeed.Enabled(player.RunState) ? player : null;
    }
    static void Postfix(Player? __state) => SameSeed.Actor.Value = __state;
    static Exception? Finalizer(Player? __state, Exception? __exception)
    { SameSeed.Actor.Value = __state; return __exception; }
}

[HarmonyPatch(typeof(RunRngSet), nameof(RunRngSet.GetRng))]
internal static class SameCombatRngPatch
{
    static bool Prefix(RunRngSet __instance, RunRngType rngType, ref Rng __result)
    {
        var player = SameSeed.Actor.Value;
        if (player == null || !SameSeed.PersonalCombatStream(rngType) || !SameSeed.Enabled(player.RunState)
            || !ReferenceEquals(__instance, player.RunState.Rng)) return true;
        __result = SameSeed.PersonalStream(player, rngType);
        return false;
    }
}

[HarmonyPatch(typeof(CardPile), nameof(CardPile.RandomizeOrderInternal))]
internal static class SameInitialShufflePatch
{
    static void Prefix(Player player, ref Rng rng)
    { if (SameSeed.Enabled(player.RunState)) rng = SameSeed.PersonalStream(player, RunRngType.Shuffle); }
}
