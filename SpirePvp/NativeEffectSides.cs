using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace SpirePvp;

// Change relation checks inside native effects only. Creature.Side and the
// combat manager remain untouched so both players keep the shared native turn.
public static class NativeEffectSides
{
    public static Player? Owner(AbstractModel effect) => effect switch
    {
        PowerModel power => power.Owner == null ? null : PvpTargets.Team(power.Owner),
        CardModel card => card.Owner,
        RelicModel relic => relic.Owner,
        PotionModel potion => potion.Owner,
        _ => null
    };
    public static CombatSide Side(Creature target, AbstractModel effect)
    {
        var owner = Owner(effect);
        var team = PvpTargets.Team(target);
        if (!PvpSession.IsArena(target.CombatState) || owner == null || team == null) return target.Side;
        return owner == team ? CombatSide.Player : CombatSide.Enemy;
    }
    public static bool IsEnemy(Creature target, AbstractModel effect) => Side(target, effect) == CombatSide.Enemy;
    public static bool IntendsToAttack(Creature target) => target.Monster?.IntendsToAttack ?? false;
}

[HarmonyPatch]
internal static class NativeEffectRelationPatch
{
    private static readonly MethodInfo GetSide = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.Side));
    private static readonly MethodInfo GetEnemy = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.IsEnemy));
    private static readonly MethodInfo GetMonster = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.Monster));
    private static readonly MethodInfo GetIntent = AccessTools.PropertyGetter(typeof(MonsterModel), nameof(MonsterModel.IntendsToAttack));

    static IEnumerable<MethodBase> TargetMethods()
    {
        var namespaces = new HashSet<string>
        {
            "MegaCrit.Sts2.Core.Models.Cards", "MegaCrit.Sts2.Core.Models.Powers",
            "MegaCrit.Sts2.Core.Models.Relics", "MegaCrit.Sts2.Core.Models.Potions"
        };
        return FindMethods(typeof(CardModel).Assembly.GetTypes().Where(t =>
            !t.IsAbstract && namespaces.Contains(t.Namespace ?? "")));
    }

    internal static bool IsEffectType(Type type)
    {
        try
        {
            for (var root = type; root != null; root = root.DeclaringType)
                if (typeof(CardModel).IsAssignableFrom(root) || typeof(PowerModel).IsAssignableFrom(root)
                    || typeof(RelicModel).IsAssignableFrom(root) || typeof(PotionModel).IsAssignableFrom(root)) return true;
        }
        catch (TypeLoadException) { }
        return false;
    }

    internal static IEnumerable<MethodBase> FindMethods(IEnumerable<Type> types)
    {
        var seen = new HashSet<MethodBase>();
        foreach (var type in types.Where(IsEffectType))
        foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
            var machine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
                ?? method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            var body = machine == null ? method : AccessTools.Method(machine, "MoveNext");
            if (body == null || body.ContainsGenericParameters || Context(body) == null || !seen.Add(body)) continue;
            List<CodeInstruction> code;
            try { code = PatchProcessor.GetOriginalInstructions(body); }
            catch (Exception error)
            {
                MegaCrit.Sts2.Core.Logging.Log.Warn($"[SpirePvp] 无法检查角色效果：{body.DeclaringType?.FullName}.{body.Name}：{error.Message}");
                continue;
            }
            if (code.Any(i => i.Calls(GetSide) || i.Calls(GetEnemy))
                || code.Zip(code.Skip(1)).Any(pair => pair.First.Calls(GetMonster) && pair.Second.Calls(GetIntent)))
                yield return body;
        }
    }

    static (int Argument, FieldInfo? Field)? Context(MethodBase method)
    {
        if (!method.IsStatic && typeof(AbstractModel).IsAssignableFrom(method.DeclaringType)) return (0, null);
        if (!method.IsStatic)
        {
            var field = method.DeclaringType!.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(f => f.Name == "<>4__this" ? 0 : f.Name == "card" ? 1 : 2)
                .FirstOrDefault(f => typeof(AbstractModel).IsAssignableFrom(f.FieldType));
            if (field != null) return (0, field);
        }
        var parameter = method.GetParameters().FirstOrDefault(p => typeof(AbstractModel).IsAssignableFrom(p.ParameterType));
        return parameter == null ? null : (parameter.Position + (method.IsStatic ? 0 : 1), null);
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var context = Context(__originalMethod)!.Value;
        for (int i = 0; i < code.Count; i++)
        {
            var instruction = code[i];
            if (instruction.Calls(GetSide) || instruction.Calls(GetEnemy))
            {
                var helper = instruction.Calls(GetSide) ? nameof(NativeEffectSides.Side) : nameof(NativeEffectSides.IsEnemy);
                var loadOwner = new CodeInstruction(instruction) { opcode = OpCodes.Ldarg, operand = context.Argument };
                yield return loadOwner;
                if (context.Field != null) yield return new CodeInstruction(OpCodes.Ldfld, context.Field);
                yield return CodeInstruction.Call(typeof(NativeEffectSides), helper);
            }
            else if (instruction.Calls(GetMonster) && i + 1 < code.Count && code[i + 1].Calls(GetIntent))
            {
                var replacement = new CodeInstruction(instruction)
                {
                    opcode = OpCodes.Call,
                    operand = AccessTools.Method(typeof(NativeEffectSides), nameof(NativeEffectSides.IntendsToAttack))
                };
                // The second getter normally has no branch targets; retain any
                // end-of-exception markers on the replacement nonetheless.
                replacement.labels.AddRange(code[i + 1].labels);
                replacement.blocks.AddRange(code[i + 1].blocks);
                yield return replacement;
                i++;
            }
            else yield return instruction;
        }
    }
}
