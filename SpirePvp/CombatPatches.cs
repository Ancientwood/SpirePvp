using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
internal static class SetupPatch
{
    static void Postfix(CombatState state)
    {
        if (!PvpSession.IsArena(state)) return;
        DuelDamage.Reset(state);
        foreach (var creature in state.Creatures)
        {
            var owner = creature.Player ?? creature.PetOwner;
            if (owner != null) creature.CombatState = new PvpCombatView(state, owner);
        }
        PvpUi.SetupCombat(state);
    }
}

[HarmonyPatch(typeof(CombatState), nameof(CombatState.AddCreature))]
internal static class PetViewPatch
{
    static void Prefix(CombatState __instance, Creature creature)
    {
        if (creature.CombatState is PvpCombatView view && view.State == __instance) creature.CombatState = __instance;
    }
    static void Postfix(CombatState __instance, Creature creature)
    {
        var owner = creature.Player ?? creature.PetOwner;
        if (PvpSession.IsArena(__instance) && owner != null)
            creature.CombatState = new PvpCombatView(__instance, owner);
    }
}

[HarmonyPatch(typeof(CombatState), nameof(CombatState.RemoveCreature))]
internal static class RemoveViewPatch
{
    static void Prefix(CombatState __instance, Creature creature, out PvpCombatView? __state)
    {
        __state = creature.CombatState as PvpCombatView;
        if (__state?.State == __instance) creature.CombatState = __instance;
    }
    static Exception? Finalizer(Creature creature, PvpCombatView? __state, Exception? __exception)
    {
        if (__state != null && creature.CombatState == __state.State) creature.CombatState = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class AddCardViewPatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(CombatState), "AddCard", [typeof(CardModel)]);
    static void Prefix(CombatState __instance, CardModel card, out PvpCombatView? __state)
    {
        __state = card.Owner?.Creature.CombatState as PvpCombatView;
        if (__state?.State == __instance && card.Owner != null) card.Owner.Creature.CombatState = __instance;
    }
    static Exception? Finalizer(CardModel card, PvpCombatView? __state, Exception? __exception)
    {
        if (__state != null && card.Owner?.Creature.CombatState == __state.State) card.Owner.Creature.CombatState = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.LoseCombat))]
internal static class PvpLossPatch
{
    static bool Prefix() => PvpSession.GetState(RunManager.Instance)?.CurrentRoom is not CombatRoom { Encounter: PvpArena } && !BossDuel.Active;
}

[HarmonyPatch(typeof(AttackCommand), "GetPossibleTargets")]
internal static class PetAttackPatch
{
    static bool Prefix(AttackCommand __instance, ref IReadOnlyList<Creature> __result)
    {
        if (__instance.Attacker?.PetOwner == null || !PvpSession.IsArena(__instance.Attacker.CombatState)) return true;
        var scope = AccessTools.Field(typeof(AttackCommand), "_combatState").GetValue(__instance) as ICombatState;
        if (scope == null) return true;
        __result = scope.GetOpponentsOf(__instance.Attacker);
        return false;
    }
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Saves.SaveManager), "UpdateProgressAfterCombatWon")]
internal static class PvpProgressPatch
{
    static bool Prefix(MegaCrit.Sts2.Core.Rooms.CombatRoom combatRoom) => !PvpSession.IsArena(combatRoom.CombatState);
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsValidTarget))]
internal static class CardTargetPatch
{
    static bool Prefix(CardModel __instance, Creature? target, ref bool __result)
    {
        if (!PvpSession.IsArena(__instance.Owner?.Creature.CombatState)) return true;
        if (__instance.TargetType == TargetType.AnyEnemy)
        {
            var team = target?.Player ?? target?.PetOwner;
            __result = target?.IsAlive == true && team != null && team != __instance.Owner && PvpTargets.IsEnemyTarget(target);
            return false;
        }
        if (__instance.TargetType == TargetType.AnyAlly)
        {
            __result = target?.IsAlive == true && (target.Player ?? target.PetOwner) == __instance.Owner;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(NTargetManager), "AllowedToTargetCreature")]
internal static class UiTargetPatch
{
    static bool Prefix(NTargetManager __instance, Creature creature, ref bool __result)
    {
        if (!PvpSession.IsArena(creature.CombatState)) return true;
        var targetType = (TargetType)AccessTools.Field(typeof(NTargetManager), "_validTargetsType").GetValue(__instance)!;
        if (targetType is not (TargetType.AnyEnemy or TargetType.AnyAlly)) return true;
        var owner = LocalContext.GetMe(creature.CombatState!);
        var team = creature.Player ?? creature.PetOwner;
        __result = creature.IsAlive && team != null && (targetType == TargetType.AnyEnemy ? team != owner && PvpTargets.IsEnemyTarget(creature) : team == owner);
        return false;
    }
}

[HarmonyPatch(typeof(CombatManager), "StartTurn")]
internal static class RoundStartPatch
{
    static bool Prefix(CombatManager __instance, object turnState, ref Task __result)
    {
        var state = PvpSession.TurnState(turnState);
        if (!PvpSession.IsArena(state)) return true;
        // No monster phase, banner or monster hooks. The native end-side routine
        // increments both players' turn numbers and keeps its end-turn bookkeeping.
        if (state.CurrentSide == CombatSide.Enemy)
        {
            __result = PvpSession.CallTask(typeof(CombatManager), "EndEnemyTurn", __instance, turnState);
            return false;
        }
        PvpSession.PreparingRound = true;
        RoundPreparation.Begin(turnState);
        PvpUi.SetStatus("双方正在恢复能量、抽牌…");
        return true;
    }
    static void Postfix(object turnState, ref Task __result)
    {
        if (PvpSession.IsArena(PvpSession.TurnState(turnState)) && PvpSession.PreparingRound)
            __result = AfterSetup(__result, turnState);
    }
    private static async Task AfterSetup(Task original, object turn)
    {
        await original;
        if (PvpSession.TurnState(turn).CurrentSide == CombatSide.Player && PvpSession.TurnLive(turn))
        {
            await RoundPreparation.Complete(turn).WaitAsync(PvpSession.TurnToken(turn));
            await PvpSession.RoundBarrier(turn);
        }
    }
}

[HarmonyPatch(typeof(CombatManager), "RunAutoPrePlayPhase")]
internal static class HoldPlayPhasePatch
{
    static void Postfix(object turnState, Player player, Task __result)
    {
        if (PvpSession.IsArena(player.Creature.CombatState)) RoundPreparation.Add(turnState, __result);
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.PlayerActionsDisabled), MethodType.Getter)]
internal static class InputGatePatch
{
    static void Postfix(ref bool __result)
    {
        if (DuelDamage.IsSettling) __result = true;
        if ((PvpSession.IsPvp(PvpSession.GetState(RunManager.Instance)) || BossDuel.Active) && PvpSession.PreparingRound) __result = true;
    }
}

[HarmonyPatch(typeof(CombatManager), "SwitchSides")]
internal static class SharedExtraTurnPatch
{
    static void Prefix(object turnState)
    {
        var state = PvpSession.TurnState(turnState);
        if (!PvpSession.IsArena(state)) return;
        var extra = (List<Player>)AccessTools.Property(turnState.GetType(), "PlayersTakingExtraTurn").GetValue(turnState)!;
        if (extra.Count == 0) return;
        // A personal extra turn would violate simultaneous rounds. Share the extra
        // round; the card still pays its original cost and runs its normal hooks.
        extra.Clear();
        extra.AddRange(state.Players);
    }
}

[HarmonyPatch]
internal static class CombatEndPatch
{
    private sealed record EndSnapshot(ulong First, ulong Second, ulong? Winner, ulong? Local, int Floor, int Round);
    static MethodBase TargetMethod() => typeof(CombatManager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(m => m.Name == "EndCombatInternal" && m.GetParameters().Length == 1);
    static void Prefix(object turnState, out EndSnapshot? __state)
    {
        __state = null;
        var state = PvpSession.TurnState(turnState);
        if (!PvpSession.IsArena(state)) return;
        PvpSession.CaptureResult(state);
        __state = new(state.Players[0].NetId, state.Players[1].NetId, PvpSession.Winner,
            DuelResultChannel.Current?.LocalId ?? LocalContext.NetId, state.RunState.ActFloor, state.RoundNumber);
    }
    static void Postfix(EndSnapshot? __state, ref Task __result)
    {
        if (__state != null) __result = Show(__result, __state);
    }
    private static async Task Show(Task original, EndSnapshot snapshot)
    {
        string? failure = null;
        try
        {
            await original.WaitAsync(TimeSpan.FromSeconds(20));
            var winner = snapshot.Winner;
            if (!BossDuel.Active && DuelResultChannel.Current is { } channel)
            {
                PvpUi.SetStatus("正在同步决斗结果…");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                winner = await channel.Finish(snapshot.Floor, snapshot.Round, winner, timeout.Token);
            }
            PvpSession.Winner = winner;
            PvpSession.Result = winner == null ? DuelOutcome.Draw
                : winner == snapshot.First ? DuelOutcome.FirstWins : DuelOutcome.SecondWins;
        }
        catch (Exception error)
        {
            if (BossDuel.Active) BossDuel.CombatFailed(error);
            MegaCrit.Sts2.Core.Logging.Log.Error($"[SpirePvp] 决斗结算未完成：{error}");
            failure = "决斗结果未能同步，可返回主菜单";
        }
        PvpSession.PreparingRound = false;
        if (BossDuel.Active) BossDuel.CombatFinished();
        else
        {
            DuelResultChannel.FinishedLocalId = snapshot.Local;
            DuelResultChannel.Finished = true;
            PvpUi.ShowResult(failure);
        }
    }
}

[HarmonyPatch(typeof(PotionModel), nameof(PotionModel.IsValidTarget))]
internal static class PotionTargetPatch
{
    static bool Prefix(PotionModel __instance, Creature? target, ref bool __result)
    {
        if (!PvpSession.IsArena(__instance.Owner?.Creature.CombatState)) return true;
        if (__instance.TargetType is not (TargetType.AnyEnemy or TargetType.AnyAlly)) return true;
        var team = target == null ? null : PvpTargets.Team(target);
        __result = target?.IsAlive == true && team != null && (__instance.TargetType == TargetType.AnyEnemy
            ? team != __instance.Owner && PvpTargets.IsEnemyTarget(target!)
            : team == __instance.Owner && target != __instance.Owner.Creature);
        return false;
    }
}
