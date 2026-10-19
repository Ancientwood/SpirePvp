using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace SpirePvp;

// Store hits, not a combined HP loss: block, HP-loss hooks and Osty overflow
// still run through the native command once for every hit at settlement.
public static class DuelDamage
{
    private sealed record Hit(Creature Target, decimal Amount, ValueProp Props, Creature? Dealer,
        CardModel? Card, CardPlay? Play)
    {
        public bool BlightDoom { get; set; }
        public bool FinalWrath { get; init; }
        public decimal DamageCap { get; init; } = decimal.MaxValue;
    }
    private sealed class Ledger
    {
        public List<Hit> Hits { get; } = [];
        public HashSet<Creature> Kills { get; } = [];
        public Dictionary<ulong, decimal> NetAttacks { get; } = [];
        public int? ScoredRound { get; set; }
    }
    private static readonly ConditionalWeakTable<ICombatState, Ledger> Ledgers = new();
    private static ICombatState Root(ICombatState state) => state is PvpCombatView view ? view.State : state;
    private static Hit? _replay;
    private static bool _replayConsumed;
    private static readonly AsyncLocal<bool> ReplayHookScope = new();
    private static HashSet<Creature>? _participants;
    private static bool _resolvingDeaths;
    public static bool IsSettling { get; private set; }
    public static bool Enabled(ICombatState? state) => PvpSession.IsArena(state)
        && state!.RunState.Modifiers.OfType<PvpDuel>().FirstOrDefault()?.DeferredDamage == true;
    internal static bool KeepAlive(Creature creature) => IsSettling && !_resolvingDeaths
        && (_participants?.Contains(creature) == true || _replay?.Dealer == creature);

    public static decimal Incoming(Creature target) => target.CombatState == null ? 0
        : Ledgers.GetOrCreateValue(Root(target.CombatState)).Hits.Where(h => h.Target == target).Sum(h => Math.Floor(EffectiveAmount(h)));
    public static decimal Outgoing(Player player) => player.Creature.CombatState == null ? 0
        : Ledgers.GetOrCreateValue(Root(player.Creature.CombatState)).Hits
            .Where(h => h.Dealer != null && PvpTargets.Team(h.Dealer) == player && PvpTargets.Team(h.Target) != player).Sum(h => Math.Floor(EffectiveAmount(h)));
    private static decimal EffectiveAmount(Hit hit) => hit.FinalWrath
        ? Math.Max(0, Math.Min(hit.DamageCap, hit.Amount * WatcherWrathCompatibility.Factor(hit.Target, hit.Dealer, hit.Props, hit.Card, hit.Play)))
        : hit.Amount;
    internal static void Reset(ICombatState state) => Ledgers.Remove(Root(state));

    internal static bool DeferBlightDoom(CardModel card, Creature target)
    {
        if (IsSettling || card is not BlightStrike || !Enabled(target.CombatState)) return false;
        var hit = Ledgers.GetOrCreateValue(Root(target.CombatState!)).Hits.LastOrDefault(h => h.Card == card && h.Target == target && !h.BlightDoom);
        if (hit == null) return false;
        hit.BlightDoom = true;
        return true;
    }

    internal static async Task ApplyBlightDoom(PlayerChoiceContext context, Creature target, CardModel card, IEnumerable<DamageResult> results)
    {
        var damage = results.Sum(r => (decimal)r.UnblockedDamage);
        if (damage > 0 && target.CurrentHp > 0)
            await PowerCmd.Apply<DoomPower>(context, target, damage, card.Owner.Creature, card);
    }

    internal static decimal NetAttack(decimal amount, IEnumerable<DamageResult> results) =>
        Math.Max(0, Math.Floor(amount) - results.Sum(r => (decimal)r.BlockedDamage));

    internal static void RecordNetAttack(ICombatState state, Player owner, decimal amount, IEnumerable<DamageResult> results)
    {
        var ledger = Ledgers.GetOrCreateValue(Root(state));
        ledger.NetAttacks[owner.NetId] = ledger.NetAttacks.GetValueOrDefault(owner.NetId) + NetAttack(amount, results);
    }

    internal static void CompleteSettlement(ICombatState state) => Ledgers.GetOrCreateValue(Root(state)).ScoredRound = state.RoundNumber;

    internal static DuelOutcome SimultaneousResult(ICombatState state)
    {
        var ledger = Ledgers.GetOrCreateValue(Root(state));
        if (!Enabled(state) || ledger.ScoredRound != state.RoundNumber || state.Players.Count != 2) return DuelOutcome.Draw;
        return DuelRules.SimultaneousOutcome(false, false,
            ledger.NetAttacks.GetValueOrDefault(state.Players[0].NetId),
            ledger.NetAttacks.GetValueOrDefault(state.Players[1].NetId));
    }

    internal static async Task<IEnumerable<DamageResult>> Queue(PlayerChoiceContext choiceContext, IEnumerable<Creature> targets, decimal amount,
        ValueProp props, Creature? dealer, CardModel? card, CardPlay? play)
    {
        var results = new List<DamageResult>();
        foreach (var target in targets.ToArray())
        {
            if (target.IsDead || dealer?.IsDead == true) { results.Add(new(target, props)); continue; }
            if (PvpSelfDamage.IsCardSelfDamage(target, card, dealer))
            {
                // Pay card self-damage immediately, including in mixed-target
                // commands. Native HP-loss hooks (Rupture etc.) run now as well.
                results.AddRange(await CreatureCmd.Damage(choiceContext, new[] { target }, amount, props, dealer, card, play));
                continue;
            }
            var state = target.CombatState!;
            // Damage is committed at play time. Later powers affect later
            // attacks only; replay bypasses modifier hooks and consumes no buff twice.
            var finalWrath = WatcherWrathCompatibility.Eligible(props);
            var predicted = Math.Max(0, Hook.ModifyDamage(state.RunState, state, target, dealer, amount,
                props, card, play, finalWrath ? ModifyDamageHookType.All & ~ModifyDamageHookType.Cap : ModifyDamageHookType.All,
                CardPreviewMode.None, out var modifiers));
            var locked = predicted;
            var cap = decimal.MaxValue;
            if (finalWrath)
            {
                // Keep uncapped non-Wrath damage and the original cap separately:
                // dividing an already capped hit would lose information.
                locked /= WatcherWrathCompatibility.Factor(target, dealer, props, card, play);
                var capModifiers = new List<AbstractModel>();
                foreach (var listener in state.RunState.IterateHookListeners(state))
                {
                    var candidate = listener.ModifyDamageCap(target, props, dealer, card, play);
                    if (candidate >= cap) continue;
                    cap = candidate;
                    if (predicted > cap) { predicted = cap; capModifiers.Add(listener); }
                }
                modifiers = modifiers.Concat(capModifiers).ToArray();
            }
            var hit = new Hit(target, locked, props, dealer, card, play) { FinalWrath = finalWrath, DamageCap = cap };
            await Hook.AfterModifyingDamageAmount(state.RunState, state, card, modifiers.Distinct());
            Ledgers.GetOrCreateValue(Root(state)).Hits.Add(hit);
            // Dispatch the native outgoing-damage hooks now, using the same
            // play-time forecast as card follow-ups. This includes target-owned
            // powers (Sic 'Em), attacker powers, relics and character-mod hooks.
            await Hook.AfterDamageGiven(choiceContext, state, dealer,
                EnvenomResult(target, predicted, props), props, target, card);
            // Supply a forecast to the card's immediate follow-up (Blight Strike,
            // healing, etc.), without firing actual HP-loss or death hooks now.
            results.Add(EnvenomResult(target, predicted, props));
        }
        RefreshIntents();
        return results;
    }

    internal static DamageResult EnvenomResult(Creature target, decimal amount, ValueProp props)
    {
        var total = (int)Math.Min(int.MaxValue, Math.Max(0, Math.Floor(amount)));
        var blocked = props.HasFlag(ValueProp.Unblockable) ? 0 : Math.Min(total, target.Block);
        return new(target, props) { BlockedDamage = blocked, UnblockedDamage = total - blocked,
            WasFullyBlocked = total > 0 && blocked == total };
    }

    internal static bool FrozenAmount(Creature? target, Creature? dealer, CardModel? card, CardPlay? play, out decimal amount)
    {
        amount = 0;
        if (!IsSettling || _replayConsumed || _replay is not { } hit
            || hit.Target != target || hit.Dealer != dealer || hit.Card != card || hit.Play != play) return false;
        _replayConsumed = true;
        amount = hit.Amount;
        return true;
    }

    internal static bool SkipReplayedOutgoingHooks => ReplayHookScope.Value;

    internal static bool EnterDamageScope(IEnumerable<Creature>? targets, Creature? dealer, CardModel? card, CardPlay? play)
    {
        var previous = ReplayHookScope.Value;
        // Only the outer replay command is marked. A new damage command emitted
        // by a receive/death hook gets its own async scope and fires its outgoing
        // hooks, even if it has the same dealer, target and card as the old hit.
        ReplayHookScope.Value = IsSettling && !_replayConsumed && _replay is { } hit
            && hit.Dealer == dealer && hit.Card == card && hit.Play == play && targets?.Contains(hit.Target) == true;
        return previous;
    }
    internal static void LeaveDamageScope(bool previous) => ReplayHookScope.Value = previous;

    internal static void QueueKill(Creature creature)
    {
        // Damage already set HP to zero; do not turn its delayed death into a
        // forced execution if a later hit hook heals the creature in this batch.
        if (!IsSettling || creature.CurrentHp > 0)
            Ledgers.GetOrCreateValue(Root(creature.CombatState!)).Kills.Add(creature);
        RefreshIntents();
    }

    public static async Task Settle(ICombatState state)
    {
        if (!Enabled(state) || IsSettling) return;
        var ledger = Ledgers.GetOrCreateValue(Root(state));
        if (ledger.Hits.Count == 0 && ledger.Kills.Count == 0) return;
        ledger.NetAttacks.Clear();
        ledger.ScoredRound = null;
        // Keep combat-start participants available until BOTH sides' queued hits
        // have run. A lethal first hit cannot cancel the opponent's pending attack.
        IsSettling = true;
        _participants = state.Creatures.Where(c => c.IsAlive).ToHashSet();
        PvpUi.SetStatus("双方回合结束 · 正在统一结算伤害…");
        try
        {
            // Both players already committed each hit at card-play time.
            // Freeze BOTH final stances before either side's damage/death hooks.
            var hits = ledger.Hits.Select(h => h with { Amount = EffectiveAmount(h), FinalWrath = false }).ToArray();
            ledger.Hits.Clear();
            foreach (var hit in hits)
            {
                _replay = hit;
                _replayConsumed = false;
                var owner = hit.Card?.Owner ?? (hit.Dealer == null ? null : PvpTargets.Team(hit.Dealer)) ?? PvpTargets.Team(hit.Target);
                PlayerChoiceContext context = owner != null && LocalContext.NetId.HasValue
                    ? new HookPlayerChoiceContext(owner, LocalContext.NetId.Value, GameActionType.Combat)
                    : new ThrowingPlayerChoiceContext();
                var task = CreatureCmd.Damage(context, new[] { hit.Target }, hit.Amount,
                    hit.Props, hit.Dealer, hit.Card, hit.Play);
                if (context is HookPlayerChoiceContext choices)
                {
                    await choices.AssignTaskAndWaitForPauseOrCompletion(task);
                    await choices.WaitForCompletion();
                }
                else await task;
                var damageResults = (await task).ToArray();
                var attacker = hit.Dealer == null ? null : PvpTargets.Team(hit.Dealer);
                if (attacker != null && PvpTargets.Team(hit.Target) != attacker)
                    RecordNetAttack(state, attacker, hit.Amount, damageResults);
                if (hit.BlightDoom && hit.Card != null)
                    await ApplyBlightDoom(context, hit.Target, hit.Card, damageResults);
                _replay = null;
            }
            // Damage may bring HP below an already-applied Doom threshold.
            // Check after BOTH players' hits, rather than waiting another round.
            var doomed = DoomPower.GetDoomedCreatures(state.Creatures
                .Where(c => c.CurrentHp > 0 && !ledger.Kills.Contains(c)).ToArray());
            await DoomPower.DoomKill(doomed);
            // Record scores before death callbacks can capture the result.
            CompleteSettlement(state);
            // Direct kills (for example Doom) are also held until the shared
            // settlement. The private native routine retains resurrection and
            // death hooks, without the co-op all-players-dead game-over screen.
            _resolvingDeaths = true;
            var deaths = state.Creatures.Where(c => ledger.Kills.Contains(c)
                || _participants.Contains(c) && c.CurrentHp <= 0).ToArray();
            ledger.Kills.Clear();
            var kill = AccessTools.Method(typeof(CreatureCmd), "KillWithoutCheckingWinCondition");
            foreach (var creature in deaths)
            {
                await (Task)kill.Invoke(null, [creature, false, 0])!;
            }
        }
        finally
        {
            _replay = null;
            _participants = null;
            _resolvingDeaths = false;
            IsSettling = false;
            RefreshIntents();
        }
    }

    internal static void RefreshIntents()
    {
        var room = NCombatRoom.Instance;
        if (room == null) return;
        foreach (var node in room.CreatureNodes)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsNodeReady() || node.Entity.Player is not { } player
                || !Enabled(node.Entity.CombatState)) continue;
            var intent = node.IntentContainer.GetNodeOrNull<MegaCrit.Sts2.Core.Nodes.Combat.NIntent>("SpirePvpDamageIntent");
            if (intent == null)
            {
                intent = MegaCrit.Sts2.Core.Nodes.Combat.NIntent.Create(node.GetHashCode() * .01f);
                intent.Name = "SpirePvpDamageIntent";
                node.IntentContainer.AddChild(intent);
            }
            // Reuse the game's attack animation and numeric label in the native
            // intent container; display only this player's queued outgoing damage.
            if (node.PlayerIntentHandler != null) node.PlayerIntentHandler.Visible = false;
            intent.UpdateIntent(new PvpCumulativeAttackIntent(player),
                node.Entity.CombatState!.Players.Where(p => p != player).Select(p => p.Creature), node.Entity);
            node.IntentContainer.Visible = true;
            node.IntentContainer.Modulate = Colors.White;
            intent.Visible = true;
        }
    }
}

[HarmonyPatch]
internal static class DeferredDamageHookScopePatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
        [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay)]);
    [HarmonyPriority(Priority.First)]
    static void Prefix(IEnumerable<Creature>? targets, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay, out bool __state)
        => __state = DuelDamage.EnterDamageScope(targets, dealer, cardSource, cardPlay);
    static void Postfix(bool __state) => DuelDamage.LeaveDamageScope(__state);
    static Exception? Finalizer(bool __state, Exception? __exception)
    {
        DuelDamage.LeaveDamageScope(__state);
        return __exception;
    }
}

[HarmonyPatch]
internal static class DeferredDamageCommandPatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
        [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay)]);
    [HarmonyPriority(Priority.Last)]
    static bool Prefix(PlayerChoiceContext choiceContext, IEnumerable<Creature>? targets, decimal amount, ValueProp props, Creature? dealer,
        CardModel? cardSource, CardPlay? cardPlay, ref Task<IEnumerable<DamageResult>> __result)
    {
        if (DuelDamage.IsSettling || targets == null
            || (ImmediateOutbreakPoisonPatch.Active && dealer == null && cardSource == null
                && props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered))) return true;
        var list = targets.ToArray();
        if (list.Length == 0 || !list.All(t => DuelDamage.Enabled(t.CombatState))) return true;
        if (list.All(t => PvpSelfDamage.IsCardSelfDamage(t, cardSource, dealer))) return true;
        __result = DuelDamage.Queue(choiceContext, list, amount, props, dealer, cardSource, cardPlay);
        return false;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Powers.DieForYouPower), nameof(MegaCrit.Sts2.Core.Models.Powers.DieForYouPower.ModifyUnblockedDamageTarget))]
internal static class DeferredOstyDeathPatch
{
    static bool Prefix(MegaCrit.Sts2.Core.Models.Powers.DieForYouPower __instance, Creature target, ref Creature __result)
    {
        if (!DuelDamage.IsSettling || __instance.Owner.CurrentHp > 0) return true;
        __result = target;
        return false;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
internal static class DeferredDamageAmountPatch
{
    static bool Prefix(Creature? target, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay,
        ref IEnumerable<AbstractModel> modifiers, ref decimal __result)
    {
        if (!DuelDamage.FrozenAmount(target, dealer, cardSource, cardPlay, out var amount)) return true;
        modifiers = [];
        __result = amount;
        return false;
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.IsAlive), MethodType.Getter)]
internal static class DeferredAlivePatch
{
    static void Postfix(Creature __instance, ref bool __result)
    { if (DuelDamage.KeepAlive(__instance)) __result = true; }
}

[HarmonyPatch]
internal static class DeferredDeathPatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(CreatureCmd), "KillWithoutCheckingWinCondition");
    static bool Prefix(Creature creature, bool force, ref Task __result)
    {
        if (force || (!DuelDamage.IsSettling && creature.Monster is Osty)
            || ImmediateOutbreakPoisonPatch.Active || !DuelDamage.Enabled(creature.CombatState)
            || DuelDamage.IsSettling && !DuelDamage.KeepAlive(creature)) return true;
        DuelDamage.QueueKill(creature);
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch]
internal static class DeferredSettlementPatch
{
    static MethodBase TargetMethod() => typeof(CombatManager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(m => m.Name == "EndPlayerTurnPhaseTwoInternal" && m.GetParameters().Length == 1);
    static void Postfix(object turnState, ref Task __result)
    {
        var state = PvpSession.TurnState(turnState);
        if (DuelDamage.Enabled(state)) __result = Finish(__result, turnState);
    }
    static async Task Finish(Task original, object turn)
    {
        await original;
        if (PvpSession.TurnLive(turn)) await DuelDamage.Settle(PvpSession.TurnState(turn));
    }
}
