using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models;
using System.Reflection;

namespace SpirePvp;

// Player opponents have no monster intention. Only this card gets the PvP
// fallback; other intent checks and all monster targets retain native behavior.
[HarmonyPatch(typeof(GoForTheEyes), "OnPlay")]
internal static class GoForTheEyesPatch
{
    static bool Prefix(GoForTheEyes __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        var target = cardPlay.Target;
        if (target is not { IsPlayer: true } || target.Player == __instance.Owner
            || !PvpSession.IsArena(target.CombatState)) return true;
        __result = Play(__instance, choiceContext, cardPlay);
        return false;
    }

    private static async Task Play(GoForTheEyes card, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue).FromCard(card, cardPlay).Targeting(cardPlay.Target!)
            .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3").Execute(choiceContext);
        // Keep the native weak value: 1 normally, 2 after upgrading. PowerCmd
        // retains Artifact/resistance, death checks, and after-debuff callbacks.
        await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target!, card.DynamicVars.Weak.BaseValue, card.Owner.Creature, card);
    }
}

[HarmonyPatch]
internal static class GoForTheEyesDescriptionPatch
{
    static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(CardModel))
        .Single(m => m.Name == "GetDescriptionForPile" && m.GetParameters().Length == 3);
    static void Postfix(CardModel __instance, ref string __result)
    {
        if (__instance is GoForTheEyes && (PvpSession.IsPvp(__instance.Owner?.RunState)
            || PvpSession.IsArena(__instance.Owner?.Creature.CombatState)))
            __result += "\nPvP 对玩家：无需读取攻击意图即可施加虚弱。";
    }
}
