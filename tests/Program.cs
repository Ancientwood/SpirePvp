using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using SpirePvp;

var gameDir = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("STS2_GAME_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Slay the Spire 2");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(gameDir, "data_sts2_windows_x86_64", name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
try { RunTests(); }
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }

void RunTests()
{
    int passed = 0;
    void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        Console.WriteLine("PASS " + description);
        passed++;
    }
    LanConnectionTests.Run(Check);
    Check(DuelRules.Outcome(true, true) == DuelOutcome.InProgress, "两人存活时不会因空敌人侧自动胜利");
    Check(DuelRules.Outcome(true, false) == DuelOutcome.FirstWins, "对手死亡判定胜利");
    Check(DuelRules.Outcome(false, true) == DuelOutcome.SecondWins, "本方死亡判定失败");
    Check(DuelRules.Outcome(false, false) == DuelOutcome.Draw, "双方死亡判定平局");
    var ownHost = DuelLayout.Place(101, 101);
    var foeHost = DuelLayout.Place(101, 202);
    var ownClient = DuelLayout.Place(202, 202);
    var foeClient = DuelLayout.Place(202, 101);
    Check(ownHost.X < .5f && ownClient.X < .5f, "房主和客机都把自己显示在左侧");
    Check(foeHost.X > .5f && foeClient.X > .5f, "房主和客机都把对手显示在右侧");
    Check(!ownHost.Mirror && !ownClient.Mirror && foeHost.Mirror && foeClient.Mirror, "双方视角均只镜像右侧对手的角色画面");
    Check(Math.Abs(ownHost.X + foeHost.X - 1) < .00001f && ownHost.Y == foeHost.Y, "对战站位左右对称且处于同一高度");
    var pet0 = DuelLayout.Place(101, 101, 0);
    var pet1 = DuelLayout.Place(101, 101, 1);
    var pet2 = DuelLayout.Place(101, 101, 2);
    var opponentPet = DuelLayout.Place(101, 202, 0);
    Check(pet0.X < .5f && pet1.X < .5f && opponentPet.X > .5f, "召唤物保持在各自主人的半场");
    Check(pet0 != pet1 && pet0 != pet2 && pet1 != pet2, "多个召唤物使用不同站位，避免完全重叠");
    Check(Math.Abs(pet0.X + opponentPet.X - 1) < .00001f, "双方召唤物站位镜像对称");
    var duelMap = new DuelMap();
    Check(!duelMap.GetAllMapPoints().Any() && duelMap.SecondBossMapPoint == null,
        "PvP 地图没有普通房间、分支或第二个 Boss 房");
    Check(duelMap.BossMapPoint.coord.row == 0 && duelMap.BossMapPoint.PointType == MegaCrit.Sts2.Core.Map.MapPointType.Boss,
        "唯一决斗入口位于第一层并使用原生 Boss 节点");
    Check(duelMap.startMapPoints.SetEquals([duelMap.BossMapPoint]), "地图唯一可选起点是决斗房间");
    Check(duelMap.GetPoint(duelMap.BossMapPoint.coord) == duelMap.BossMapPoint
        && !duelMap.HasPoint(new MegaCrit.Sts2.Core.Map.MapCoord(1, 0)), "双方固定房间坐标可解析，普通地图坐标不存在");
    var savedMap = MegaCrit.Sts2.Core.Saves.Runs.SerializableActMap.FromActMap(duelMap);
    var restoredMap = new MegaCrit.Sts2.Core.Map.SavedActMap(savedMap);
    Check(!restoredMap.GetAllMapPoints().Any() && restoredMap.BossMapPoint.coord == duelMap.BossMapPoint.coord
        && restoredMap.startMapPoints.Single().coord == duelMap.BossMapPoint.coord,
        "原生地图序列化与恢复保持唯一决斗入口");
    // Patch real installed game methods; catches missing methods, changed signatures,
    // argument binding mistakes and failures to compile Harmony's generated wrappers.
    var logShim = new Harmony("spirepvp.tests.consolelog");
    logShim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Logging.Logger), "GetIsRunningFromGodotEditor"),
        prefix: new HarmonyMethod(typeof(ConsoleLogShim), nameof(ConsoleLogShim.Prefix)));
    MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[MegaCrit.Sts2.Core.Logging.LogType.Generic] = MegaCrit.Sts2.Core.Logging.LogLevel.Error;
    var harmony = new Harmony("spirepvp.duel");
    ModEntry.Initialize();
    LanConnectionTests.RunGameChecks(Check);
    using (var icon = typeof(ModEntry).Assembly.GetManifestResourceStream("SpirePvp.Assets.Map.DuelIcon.png"))
    {
        var header = new byte[26];
        icon?.ReadExactly(header);
        Check(icon != null && header.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
            && header[25] == 6, "发布 DLL 嵌入真实 RGBA PNG 地图图标，无需外部路径或 PCK");
    }
    Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Runs.RunManager), "EnterNextAct"))?
        .Prefixes.Any(p => p.owner == "spirepvp.duel") != true,
        "普通爬塔进入下一幕没有 PvP 拦截补丁，不增加同步消息或奖励流程");
    Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu), "_Ready"))!
        .Prefixes.Any(p => p.owner == "spirepvp.duel"), "真实主菜单加载时注册 BaseLib 模组配置");
    var configProperties = typeof(PvpModConfig).GetProperties(BindingFlags.Public | BindingFlags.Static);
    Check(configProperties.Length == 15 && configProperties.Count(p => p.PropertyType == typeof(bool)) == 6
        && configProperties.Where(p => p.PropertyType == typeof(double) && p.Name != "每回合最多出牌" && !p.Name.StartsWith("自定义开局")).All(p =>
            p.GetCustomAttribute<BaseLib.Config.ConfigSliderAttribute>() is { Max: 10, Step: .5 }),
        "模组配置提供五种倍率、壁垒、战前发育、出牌限制和回合末伤害结算，不提供 Boss 后 PvP 开关");
    Check(!ModelDb.Contains(typeof(PvpDuel)) && !ModelDb.Contains(typeof(PvpArena)),
        "Mod 初始化不提前构造模型，避免游戏启动时重复注册");
    ModelDb.Init([typeof(PvpDuel), typeof(PvpArena), typeof(PvpFortitudePower), typeof(PvpDraft15), typeof(PvpDraft20), typeof(PvpCustomDraft)]);
    Check(ModelDb.Modifier<PvpDuel>().IsCanonical && ModelDb.Encounter<PvpArena>().IsCanonical,
        "Mod 初始化后原生 ModelDb.Init 可正常注册两个 PvP 模型");
    ModelDb.Init([typeof(MegaCrit.Sts2.Core.Models.Modifiers.Draft), typeof(MegaCrit.Sts2.Core.Models.Modifiers.SealedDeck)]);
    MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
    // Console fixtures aren't loaded by ModManager; provide the native test override.
    MegaCrit.Sts2.Core.Modding.AssemblyInfo.MockTypes = new()
    {
        [typeof(PvpDuel)] = (null, true),
        [typeof(PvpArena)] = (null, true),
        [typeof(PvpFortitudePower)] = (null, true),
        [typeof(PvpDraft15)] = (null, true),
        [typeof(PvpDraft20)] = (null, true),
        [typeof(PvpCustomDraft)] = (null, true),
        [typeof(ExampleCharacterMod.ExampleCard)] = (null, true)
    };
    MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init();
    foreach (bool sealedDeck in new[] { false, true })
    {
        var modifiers = PvpLobbyModifiers.Create(sealedDeck);
        Check(modifiers.All(m => m.IsMutable), $"{(sealedDeck ? "现开套牌" : "选牌")}房间使用可变规则实例");
        var synced = modifiers.Select(m => ModifierModel.FromSerializable(m.ToSerializable())).ToList();
        Check(synced[0] is PvpDuel && synced[1].GetType() == modifiers.Last().GetType(),
            $"{(sealedDeck ? "现开套牌" : "选牌")}房间规则通过原生联机序列化往返");
        Check(!ReferenceEquals(modifiers.First(), PvpLobbyModifiers.Create(sealedDeck).First()),
            $"重新创建{(sealedDeck ? "现开套牌" : "选牌")}房间不会复用上次规则实例");
    }
    Check(Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m)?.Owners.Contains(harmony.Id) == true) >= 18,
        "全部 Harmony 补丁可绑定本机游戏接口");
    Check(AccessTools.Property(typeof(MegaCrit.Sts2.Core.Runs.RunManager), "State") != null, "可读取当前 RunState");
    Check(AccessTools.PropertySetter(typeof(EventModel), nameof(EventModel.Owner)) != null, "原生构筑流程的 Owner 接口存在");
    var p1 = FakePlayer(101);
    var p2 = FakePlayer(202);
    RunVersion020Tests(Check);
    ModelDb.Init([typeof(MegaCrit.Sts2.Core.Models.Powers.UnmovablePower)]);
    var fortitude = (PvpFortitudePower)ModelDb.Power<PvpFortitudePower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(fortitude, [p1.Creature]);
    Check(fortitude.Type == MegaCrit.Sts2.Core.Entities.Powers.PowerType.Buff
        && fortitude.StackType == MegaCrit.Sts2.Core.Entities.Powers.PowerStackType.Single,
        "决斗坚韧是独立的正面状态且不叠加");
    p1.Creature.SetCurrentHpInternal(60);
    fortitude.BeforeApplied(p1.Creature, 1, null, null).GetAwaiter().GetResult();
    Check(p1.Creature.MaxHp == 160 && p1.Creature.CurrentHp == 120, "开战最大生命与当前生命均翻倍，保留受伤比例");
    fortitude.ApplyInternal(p1.Creature, 1, true);
    fortitude.BeforeApplied(p1.Creature, 1, null, null).GetAwaiter().GetResult();
    Check(p1.Creature.MaxHp == 160 && p1.Creature.CurrentHp == 120, "已有状态时不重复翻倍生命");
    Check(MegaCrit.Sts2.Core.Commands.PowerCmd.FindExistingInstanceForStacking(
        ModelDb.Power<PvpFortitudePower>().ToMutable(), p1.Creature, null) == fortitude,
        "原生叠加检查复用新状态，跳过再次增加生命");
    var nativeDouble = (MegaCrit.Sts2.Core.Models.Powers.UnmovablePower)
        ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.UnmovablePower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(nativeDouble, [p1.Creature]);
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(nativeDouble, [1]);
    Check(MegaCrit.Sts2.Core.Commands.PowerCmd.FindExistingInstanceForStacking(nativeDouble, p1.Creature, null) == null,
        "原版翻倍能力与决斗坚韧使用独立模型，不覆盖彼此");
    var blockState = DispatchProxy.Create<ICombatState, BlockCombatFixture>();
    var listeners = (BlockCombatFixture)(object)blockState;
    listeners.Listeners = [fortitude];
    logShim.Patch(AccessTools.PropertyGetter(typeof(CombatManager), nameof(CombatManager.IsOverOrEnding)),
        prefix: new HarmonyMethod(typeof(ConsoleLogShim), nameof(ConsoleLogShim.Prefix)));
    Check(!MegaCrit.Sts2.Core.Hooks.Hook.ShouldClearBlock(blockState, p1.Creature, out var blockPreventer)
        && blockPreventer == fortitude, "决斗坚韧通过原生壁垒钩子阻止持有者格挡清空");
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ShouldClearBlock(blockState, p2.Creature, out _),
        "壁垒效果仅保护自身，不保护未持有状态的玩家");
    var priorBlockScope = p1.Creature.CombatState;
    p1.Creature.CombatState = blockState;
    p1.Creature.GainBlockInternal(10);
    var clearBlock = AccessTools.Method(typeof(Creature), "ClearBlock");
    ((Task)clearBlock.Invoke(p1.Creature, null)).GetAwaiter().GetResult();
    ((Task)clearBlock.Invoke(p1.Creature, null)).GetAwaiter().GetResult();
    Check(p1.Creature.Block == 10, "实际原生回合清空流程连续执行仍保留 10 点格挡");
    p1.Creature.DamageBlockInternal(3, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move);
    Check(p1.Creature.Block == 7, "保留格挡仍正常吸收伤害并扣减");
    p1.Creature.LoseBlockInternal(7);
    Check(p1.Creature.Block == 0, "主动移除格挡的效果仍可清空格挡");
    p1.Creature.CombatState = priorBlockScope;
    foreach (var props in new[] { MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered })
        Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyBlock(blockState, p1.Creature, 5, props, null, null, out _) == 7.5m,
            $"原生格挡钩子对 {props} 起防乘以 1.5，兼容卡牌与药水/被动格挡");
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyBlock(blockState, p2.Creature, 5,
        MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null, out _) == 5, "格挡翻倍只作用于持有者");
    listeners.Listeners = [fortitude, nativeDouble];
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyBlock(blockState, p1.Creature, 5,
        MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null, out var blockModifiers) == 15
        && blockModifiers.Contains(nativeDouble) && blockModifiers.Contains(fortitude),
        "原版翻倍能力与新状态叠加：5 点格挡变为 15，原版层数不消耗");
    Check(nativeDouble.Amount == 1, "新状态不修改原版能力层数");
    listeners.Listeners = [fortitude];
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifySummonAmount(blockState, p1, 4, null) == 6,
        "原生召唤钩子将召唤 4 变为召唤 6");
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifySummonAmount(blockState, p2, 4, null) == 4,
        "召唤倍率不影响另一名玩家，避免两份状态重复放大");
    var scaledHp = new Creature(FakePlayer(303), 80, 80);
    scaledHp.SetMaxHpInternal(MegaCrit.Sts2.Core.Hooks.Hook.ModifySummonAmount(blockState, p1, 3, null));
    Check(scaledHp.MaxHp == 4, "召唤 3 乘以 1.5 后沿用原版生命取整得到 4");
    ModelDb.Init([typeof(MegaCrit.Sts2.Core.Models.Powers.DoomPower), typeof(MegaCrit.Sts2.Core.Models.Powers.PoisonPower)]);
    var doom = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.DoomPower>();
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyPowerAmountReceived(blockState, doom, p1.Creature, 20, p2.Creature, out var doomModifiers) == 10
        && doomModifiers.Contains(fortitude), "原生状态接收钩子将新增 20 层灾厄减为 10 层");
    var oddDoom = MegaCrit.Sts2.Core.Hooks.Hook.ModifyPowerAmountReceived(blockState, doom, p1.Creature, 9, p2.Creature, out _);
    Check(oddDoom == 4.5m && (int)oddDoom == 4, "奇数灾厄减半后沿用原生整数层数，9 层变为 4 层");
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyPowerAmountReceived(blockState, doom, p1.Creature, -9, p2.Creature, out _) == -9,
        "减少灾厄的效果不被减半");
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyPowerAmountReceived(blockState, doom, p1.Creature, 0, null, out _) == 0,
        "零灾厄保持零，不引入额外变化");
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyPowerAmountReceived(blockState, doom, p2.Creature, 20, p1.Creature, out _) == 20,
        "抗性只保护状态持有者，不重复作用于另一名玩家");
    Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyPowerAmountReceived(blockState,
        ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.PoisonPower>(), p1.Creature, 20, p2.Creature, out _) == 20,
        "灾厄抗性不改变中毒等其他负面效果");
    var damageRun = DispatchProxy.Create<IRunState, DamageRunFixture>();
    ((DamageRunFixture)(object)damageRun).Listeners = [fortitude];
    foreach (var props in new[] { MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable })
        Check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(damageRun, null, p1.Creature, p2.Creature, 10,
            props, null, null, MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.None, out _) == 10,
            $"已移除庇护减伤，{props} 的 10 点伤害保持 10 点");
    logShim.Unpatch(AccessTools.PropertyGetter(typeof(CombatManager), nameof(CombatManager.IsOverOrEnding)), HarmonyPatchType.Prefix, logShim.Id);
    ModelDb.Inject(typeof(PvpArena));
    ModelDb.Inject(typeof(StrikeIronclad));
    var state = new CombatState(ModelDb.Encounter<PvpArena>().ToMutable());
    // Tests don't load textures or localization: this minimal player fixture has
    // real creature HP/CombatId and uses the game's normal AddPlayer path.
    state.AddPlayer(p1);
    state.AddPlayer(p2);
    var view1 = new PvpCombatView(state, p1);
    var view2 = new PvpCombatView(state, p2);
    RunNativeEffectTests(state, p1, p2, Check);
    RunCharacterCompatibilityTests(state, p1, p2, Check);
    var renderer = typeof(MintySpire2.MintySpire2Code.combat.SummedIncomingDamageRender);
    Check(CombatDisplayCompatibility.Apply(renderer), "自动识别 MintySpire2 预计承伤显示并安装兼容补丁");
    Check(CombatDisplayCompatibility.Apply(renderer), "重复注册显示兼容不会重复叠加补丁");
    Check(MintySpire2.MintySpire2Code.combat.SummedIncomingDamageRender.CalculateIncomingDamage(p1.Creature) == 0
        && MintySpire2.MintySpire2Code.combat.SummedIncomingDamageRender.Calls == 0,
        "PvP 血条刷新跳过怪物意图预测，避免空引用中断倍率应用与开局");
    Check(MintySpire2.MintySpire2Code.combat.SummedIncomingDamageRender.CalculateIncomingDamage(FakePlayer(919).Creature) == 17,
        "普通战斗与非 PvP 角色仍执行原来的预计承伤计算");
    Check(!CombatDisplayCompatibility.Apply(typeof(Program)), "没有 MintySpire2 时不修改其他类型");
    Check(view1.Enemies.SequenceEqual(new[] { p2.Creature }), "玩家一的群攻只指向玩家二");
    Check(view2.Enemies.SequenceEqual(new[] { p1.Creature }), "玩家二的群攻只指向玩家一");
    Check(view1.GetOpponentsOf(p1.Creature).Single() == p2.Creature, "随机攻击的对手查询正确");
    Check(view1.GetTeammatesOf(p1.Creature).Single() == p1.Creature, "队友查询不包含对手");
    Check(view1.GetCreature(p2.Creature.CombatId) == p2.Creature, "网络 CombatId 保持原生映射");
    view1.RoundNumber = 3;
    Check(view2.RoundNumber == 3 && state.RoundNumber == 3, "双方共享同一个回合状态");
    Check(state.Allies.Count == 2 && state.Enemies.Count == 0, "原版管理器仍可同时为两名玩家恢复能量和抽牌");
    Check(view1.PlayerCreatures.Count == 1, "玩家侧支援牌不会将对手识别为队友");
    var attack = state.CreateCard(ModelDb.Card<StrikeIronclad>(), p1);
    Check(attack.IsValidTarget(p2.Creature), "原版打击可指定对手玩家为目标");
    Check(!attack.IsValidTarget(p1.Creature), "打击无法指定自己为目标");
    Check(!attack.IsValidTarget(null), "单体攻击不能无目标出牌");
    var clone = view1.CloneCard(attack);
    Check(state.ContainsCard(clone) && clone.Owner == p1, "战斗中生成或复制卡牌仍属于同一战斗");
    Check(p1.Creature.CombatState is PvpCombatView, "复制卡牌后保留本方目标视图");
    state.RemoveCreature(p2.Creature, false);
    Check(!state.ContainsCreature(p2.Creature) && p2.Creature.CombatState is PvpCombatView, "暂时移除生物保留 PvP 归属");
    state.AddCreature(p2.Creature);
    Check(view1.Enemies.Single() == p2.Creature, "重新加入生物不会破坏 PvP 对手查询");
    state.RemoveCreature(p2.Creature);
    Check(p2.Creature.CombatState == null, "战斗退出可以彻底解绑生物");
    var combat = (PlayerCombatState)RuntimeHelpers.GetUninitializedObject(typeof(PlayerCombatState));
    AccessTools.Field(typeof(PlayerCombatState), "_player").SetValue(combat, p1);
    p1.MaxEnergy = 3;
    var scope = p1.Creature.CombatState;
    p1.Creature.CombatState = null;
    combat.ResetEnergy();
    combat.LoseEnergy(1);
    Check(combat.Energy == 2, "原生能量消费仍为 3 减 1 等于 2");
    combat.GainEnergy(2);
    Check(combat.Energy == 4, "原生卡牌产能仍可超过回合基础能量");
    combat.ResetEnergy();
    Check(combat.Energy == 3, "下回合按原生最大能量恢复，不累积上回合剩余能量");
    p1.Creature.CombatState = scope;
    RunCardGenerationTests(Check);
    RunMultiplierTests(Check);
    RunDevelopmentTests(Check);
    RunIndependentDevelopmentTests(Check);
    RunEventWhitelistTests(Check);
    RunCardLimitTests(Check);
    RunFinalWrathTests(Check);
    RunDeferredDamageTests(Check);
    RunDeferredBuffTests(Check);
    RunSimultaneousVictoryTests(Check);
    RunCooperativeDevelopmentTests(Check);
    RunNetworkTests(Check).GetAwaiter().GetResult();
    RunDuelResultNetworkTests(Check).GetAwaiter().GetResult();
    RunCounterCleanupTests(Check);
    RunVfxLayoutTests(Check);
    RunSameSeedTests(Check);
    RunDraftCompletionTests(Check).GetAwaiter().GetResult();
    harmony.UnpatchAll(harmony.Id);
    Console.WriteLine($"{passed} checks passed.");
}

void RunCharacterCompatibilityTests(CombatState state, Player first, Player second, Action<bool, string> check)
{
    ModelDb.Init([typeof(ExampleCharacterMod.ExampleCard)]);
    var card = (ExampleCharacterMod.ExampleCard)ModelDb.Card<ExampleCharacterMod.ExampleCard>().ToMutable();
    card.Owner = first;
    check(!card.CheckEnemy(second.Creature), "外部角色测试卡原始阵营逻辑会将对手当队友");
    int count = ModCharacterCompatibility.Apply(typeof(ExampleCharacterMod.ExampleCard).Assembly);
    try
    {
        check(count >= 4, "按类型而非命名空间发现外部 Mod 同步、异步和闭包效果");
        check(card.CheckEnemy(second.Creature) && !card.CheckEnemy(first.Creature), "外部角色效果正确区分 PvP 敌我");
        check(card.ReadSide(second.Creature) == CombatSide.Enemy && card.ReadSide(first.Creature) == CombatSide.Player,
            "外部角色的阵营读取按技能持有者计算");
        check(card.CheckEnemyAsync(second.Creature).GetAwaiter().GetResult(), "外部角色异步效果也识别敌方玩家");
        check(card.Enemies(new[] {first.Creature,second.Creature}).SequenceEqual(new[] {second.Creature}),
            "外部卡牌闭包筛选正确选中敌方玩家");
        check(ModCharacterCompatibility.Apply(typeof(ExampleCharacterMod.ExampleCard).Assembly) == 0,
            "回主菜单重复扫描不重复安装外部效果补丁");
        var saved = second.Creature.CombatState;
        second.Creature.CombatState = null;
        check(!card.CheckEnemy(second.Creature) && card.ReadSide(second.Creature) == CombatSide.Player,
            "外部 Mod 普通爬塔仍保留原阵营规则");
        second.Creature.CombatState = saved;
    }
    finally { new Harmony(ModCharacterCompatibility.HarmonyId).UnpatchAll(ModCharacterCompatibility.HarmonyId); }
}

void RunCardLimitTests(Action<bool, string> check)
{
    var settings = new PvpSettings();
    check(!settings.CardLimitEnabled && settings.MaxCardsPerTurn == 15, "出牌限制默认关闭，开启后的默认上限是 15");
    settings.MaxCardsPerTurn = 0;
    check(settings.MaxCardsPerTurn == 15, "出牌上限无效值恢复 15，不造成永久禁出牌");
    settings.MaxCardsPerTurn = 100;
    check(settings.MaxCardsPerTurn == 100, "最大出牌数可设置为 1 至 100");
    var path = Path.Combine(Path.GetTempPath(), "pvp-limit-test-" + Guid.NewGuid() + ".json");
    try
    {
        settings.CardLimitEnabled = true; settings.Development = true; settings.MaxCardsPerTurn = 23;
        settings.Save(path); var loaded = PvpSettings.Load(path);
        check(loaded.CardLimitEnabled && loaded.Development && loaded.MaxCardsPerTurn == 23,
            "发育开关和出牌限制设置可持久化");
    }
    finally { File.Delete(path); }
    var modifiers = PvpLobbyModifiers.Create(0, DuelMultipliers.Default, true, true, 23);
    var copy = modifiers.Select(m => ModifierModel.FromSerializable(m.ToSerializable())).OfType<PvpDuel>().Single();
    check(copy.Development && copy.CardLimitEnabled && copy.MaxCardsPerTurn == 23,
        "房主的发育与出牌限制通过原生规则序列化同步");
    var switched = PvpLobbyModifiers.Create(3, DuelMultipliers.FromModifier(copy), copy.Development, copy.CardLimitEnabled, copy.MaxCardsPerTurn)
        .OfType<PvpDuel>().Single();
    check(switched.CardLimitEnabled && switched.MaxCardsPerTurn == 23, "切换构筑模式保留出牌上限");
    var player = FakePlayer(101); var opponent = FakePlayer(202);
    var card = (CardModel)ModelDb.Card<StrikeIronclad>().ToMutable(); card.Owner = player;
    var state = DispatchProxy.Create<ICombatState, LimitCombatFixture>();
    var fixture = (LimitCombatFixture)(object)state; fixture.Round = 3;
    var history = new MegaCrit.Sts2.Core.Combat.History.CombatHistory();
    MegaCrit.Sts2.Core.Combat.History.Entries.CardPlayStartedEntry Entry(Player owner, int index, int round, bool auto = false) =>
        new(new CardPlay { Card = card, Player = owner, Target = null, ResultPile = PileType.Discard, Resources = default, PlayIndex = index, PlayCount = 2, IsAutoPlay = auto }, round, CombatSide.Player, history, []);
    var entries = new[] { Entry(player,0,3), Entry(opponent,0,3), Entry(player,1,3), Entry(player,0,2), Entry(player,0,3,true) };
    check(DuelCardLimit.Count(entries, player, state) == 2, "每人计数独立、含自动出牌、同一张重复效果只算一次、排除旧回合");
    fixture.Round = 4;
    check(DuelCardLimit.Count(entries, player, state) == 0, "新回合出牌计数自动清零");
    check(typeof(PvpDuel).GetMethod("ShouldPlay")!.DeclaringType == typeof(PvpDuel), "出牌上限接入原生禁止出牌钩子");
}

void RunCooperativeDevelopmentTests(Action<bool, string> check)
{
    var settings = new PvpSettings();
    check(settings.CooperativeDevelopment && !settings.Development, "联机发育偏好默认开启，战前发育总开关仍默认关闭");
    var path = Path.Combine(Path.GetTempPath(), "pvp-coop-test-" + Guid.NewGuid() + ".json");
    try
    {
        settings.CooperativeDevelopment = false; settings.Development = true; settings.Save(path);
        var loaded = PvpSettings.Load(path);
        check(loaded.Development && !loaded.CooperativeDevelopment, "独立发育选择可持久保存并在重启后恢复");
        File.WriteAllText(path, "{\"Development\":true}");
        check(PvpSettings.Load(path).CooperativeDevelopment, "旧设置升级后联机发育默认开启");
    }
    finally { File.Delete(path); }
    var disabled = PvpLobbyModifiers.Create(0, DuelMultipliers.Default, false, cooperativeDevelopment: true).OfType<PvpDuel>().Single();
    check(!disabled.Development && !disabled.CooperativeDevelopment, "未开启战前发育时联机发育不能在房间生效");
    var coop = PvpLobbyModifiers.Create(0, DuelMultipliers.Default, true, true, 23, true).OfType<PvpDuel>().Single();
    check(coop.CooperativeDevelopment, "开启发育的新房间默认使用原生多人发育");
    var copy = (PvpDuel)ModifierModel.FromSerializable(coop.ToSerializable());
    check(copy.CooperativeDevelopment && copy.Development && copy.DeferredDamage && copy.MaxCardsPerTurn == 23,
        "联机发育与伤害、出牌规则同时通过原生联机序列化");
    var solo = PvpLobbyModifiers.Create(1, DuelMultipliers.Default, true, cooperativeDevelopment: false).OfType<PvpDuel>().Single();
    var switched = PvpLobbyModifiers.Create(3, DuelMultipliers.FromModifier(solo), solo.Development,
        solo.CardLimitEnabled, solo.MaxCardsPerTurn, solo.DeferredDamage, solo.CooperativeDevelopment).OfType<PvpDuel>().Single();
    check(!switched.CooperativeDevelopment && switched.Development, "切换选牌模式保留关闭联机的独立发育规则");
    check(typeof(PvpModConfig).GetProperty("联机发育")!.GetCustomAttribute<BaseLib.Config.ConfigVisibleIfAttribute>()?.TargetName == "战前发育",
        "模组配置仅在战前发育开启时显示联机发育选项");
}

void RunSimultaneousVictoryTests(Action<bool, string> check)
{
    check(DuelRules.SimultaneousOutcome(false, false, 40, 30) == DuelOutcome.FirstWins, "双方致死时扣除格挡后的攻击更高者获胜");
    check(DuelRules.SimultaneousOutcome(false, false, 30, 40) == DuelOutcome.SecondWins, "第二位玩家净攻击更高时也正确获胜");
    check(DuelRules.SimultaneousOutcome(false, false, 40, 40) == DuelOutcome.Draw, "双方致死且净攻击相同时平局");
    check(DuelRules.SimultaneousOutcome(true, false, 1, 999) == DuelOutcome.FirstWins
        && DuelRules.SimultaneousOutcome(false, true, 999, 1) == DuelOutcome.SecondWins, "只有一人存活时仍由存活者获胜");
    var first = FakePlayer(601); var second = FakePlayer(602);
    var rules = PvpLobbyModifiers.Create(0, DuelMultipliers.Default, deferredDamage: true).OfType<PvpDuel>().Single();
    var run = DispatchProxy.Create<IRunState, DeferredRunFixture>(); ((DeferredRunFixture)(object)run).Rules = [rules];
    var state = new CombatState(ModelDb.Encounter<PvpArena>().ToMutable(), run); state.AddPlayer(first); state.AddPlayer(second);
    var record = AccessTools.Method(typeof(DuelDamage), "RecordNetAttack");
    var finish = AccessTools.Method(typeof(DuelDamage), "CompleteSettlement");
    var reset = AccessTools.Method(typeof(DuelDamage), "Reset");
    var capture = AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.PvpSession"), "CaptureResult");
    var resultField = AccessTools.Field(typeof(ModEntry).Assembly.GetType("SpirePvp.PvpSession"), "Result");
    var winnerField = AccessTools.Field(typeof(ModEntry).Assembly.GetType("SpirePvp.PvpSession"), "Winner");
    AccessTools.PropertySetter(typeof(Creature), nameof(Creature.CurrentHp)).Invoke(first.Creature, [0]);
    AccessTools.PropertySetter(typeof(Creature), nameof(Creature.CurrentHp)).Invoke(second.Creature, [0]);
    var capped = new[] { new DamageResult(second.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move)
        { BlockedDamage = 10, UnblockedDamage = 20, OverkillDamage = 70 } };
    record.Invoke(null, [state, first, 100m, capped]);
    record.Invoke(null, [state, second, 95m, new[] { new DamageResult(first.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { BlockedDamage = 10 } }]);
    finish.Invoke(null, [state]); capture.Invoke(null, [state]);
    check((DuelOutcome)resultField.GetValue(null)! == DuelOutcome.FirstWins && (ulong?)winnerField.GetValue(null) == 601,
        "原生战斗结果按100减10大于95减10判胜，保留超过生命值的伤害");
    var net = AccessTools.Method(typeof(DuelDamage), "NetAttack");
    check((decimal)net.Invoke(null, [32m, new[] {
        new DamageResult(second.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { BlockedDamage = 2, UnblockedDamage = 1, OverkillDamage = 29 },
        new DamageResult(second.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { BlockedDamage = 3, UnblockedDamage = 27 } }])! == 27,
        "召唤物转移与溢出只计算一次攻击，扣除各段实际吸收的格挡");
    check((decimal)net.Invoke(null, [5m, new[] { new DamageResult(second.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { BlockedDamage = 5 } }])! == 0,
        "完全格挡的攻击贡献零分，不会产生负数");
    var view = new PvpCombatView(state, second);
    var simultaneous = AccessTools.Method(typeof(DuelDamage), "SimultaneousResult");
    check((DuelOutcome)simultaneous.Invoke(null, [view])! == DuelOutcome.FirstWins, "客机角色视角与主视角共用同一结算分数");
    state.RoundNumber++;
    capture.Invoke(null, [state]);
    check((DuelOutcome)resultField.GetValue(null)! == DuelOutcome.Draw && winnerField.GetValue(null) == null,
        "新回合未结算时不会沿用上一回合分数判胜");
    finish.Invoke(null, [state]); rules.DeferredDamage = false; capture.Invoke(null, [state]);
    check((DuelOutcome)resultField.GetValue(null)! == DuelOutcome.Draw, "实时模式保留双方死亡的原有平局判定");
    rules.DeferredDamage = true; reset.Invoke(null, [state]); capture.Invoke(null, [state]);
    check((DuelOutcome)resultField.GetValue(null)! == DuelOutcome.Draw, "重置战斗时清除结算分数，不能污染新对局");
    first.Creature.CombatState = state; second.Creature.CombatState = state;
    var killShim = new Harmony("spirepvp.tests.dualkill");
    killShim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd), "KillWithoutCheckingWinCondition"),
        prefix: new HarmonyMethod(typeof(DuelKillRecorder), nameof(DuelKillRecorder.Kill)) { priority = Priority.First });
    try
    {
        var killPrefix = AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.DuelKillLifecyclePatch"), "Prefix");
        DuelKillRecorder.Calls = 0;
        object?[] killArgs = [new[] { first.Creature, second.Creature }, false, null];
        check(!(bool)killPrefix.Invoke(null, killArgs)!, "双方死亡命令在PvP拦截原生全灭界面分支");
        ((Task)killArgs[2]!).GetAwaiter().GetResult();
        check(DuelKillRecorder.Calls == 2, "两位玩家都保留原生单体死亡与遗物能力处理，不跳过死亡");
        killArgs = [new[] { first.Creature }, true, null];
        check((bool)killPrefix.Invoke(null, killArgs)!, "主动强制放弃仍保留原生流程");
        var ordinary = FakePlayer(603); ordinary.Creature.CombatState = new CombatState();
        killArgs = [new[] { ordinary.Creature }, false, null];
        check((bool)killPrefix.Invoke(null, killArgs)!, "普通战斗的原生全灭与游戏结束流程不受影响");
    }
    finally { killShim.UnpatchAll(killShim.Id); }
    resultField.SetValue(null, DuelOutcome.InProgress); winnerField.SetValue(null, null);
}

void RunDeferredDamageTests(Action<bool, string> check)
{
    var settings = new PvpSettings();
    check(!settings.DeferredDamage, "伤害结算默认保留实时手速模式");
    var path = Path.Combine(Path.GetTempPath(), "pvp-damage-test-" + Guid.NewGuid() + ".json");
    try
    {
        settings.DeferredDamage = true; settings.Save(path);
        check(PvpSettings.Load(path).DeferredDamage, "回合末统一结算开关可持久保存");
        File.WriteAllText(path, "{\"HealthMultiplier\":2}");
        check(!PvpSettings.Load(path).DeferredDamage, "旧设置文件升级后保持实时伤害模式");
    }
    finally { File.Delete(path); }
    var rules = PvpLobbyModifiers.Create(2, DuelMultipliers.Default, true, true, 23, true).OfType<PvpDuel>().Single();
    var saved = ModifierModel.FromSerializable(rules.ToSerializable()) as PvpDuel;
    check(saved?.DeferredDamage == true, "房主的回合末伤害模式通过原生序列化同步给客机");
    var switched = PvpLobbyModifiers.Create(3, DuelMultipliers.FromModifier(saved!), saved!.Development,
        saved.CardLimitEnabled, saved.MaxCardsPerTurn, saved.DeferredDamage).OfType<PvpDuel>().Single();
    check(switched.DeferredDamage && switched.CardLimitEnabled && switched.Development && switched.MaxCardsPerTurn == 23,
        "切换选牌模式保留累计伤害和其它房间规则");
    var run = DispatchProxy.Create<IRunState, DeferredRunFixture>();
    ((DeferredRunFixture)(object)run).Rules = [rules];
    var combat = DispatchProxy.Create<ICombatState, DeferredCombatFixture>();
    var fixture = (DeferredCombatFixture)(object)combat;
    fixture.Run = run; fixture.Encounter = ModelDb.Encounter<PvpArena>().ToMutable();
    var first = FakePlayer(101); var second = FakePlayer(202);
    first.Creature.CombatState = combat; second.Creature.CombatState = combat;
    var source = (CardModel)ModelDb.Card<StrikeIronclad>().ToMutable(); source.Owner = first;
    ((DeferredRunFixture)(object)run).CurrentListeners = () => first.Creature.Powers.Concat(second.Creature.Powers);
    var oldHp = second.Creature.CurrentHp;
    second.Creature.GainBlockInternal(3);
    var result = MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
        second.Creature, 10, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, first.Creature, source, null).GetAwaiter().GetResult();
    check(second.Creature.CurrentHp == oldHp && second.Creature.Block == 3 && result.Single().TotalDamage == 10,
        "累计模式拦截真实伤害命令，不提前扣血或消耗格挡");
    second.Creature.GainBlockInternal(8);
    var attackIntent = new PvpCumulativeAttackIntent(first);
    check(attackIntent is MegaCrit.Sts2.Core.MonsterMoves.Intents.SingleAttackIntent
        && attackIntent.GetTotalDamage([second.Creature], first.Creature) == 10,
        "累计攻击使用原生单次攻击意图，仅显示已锁定的预计攻击总值");
    check(!attackIntent.HasIntentTip && attackIntent.GetTotalDamage([first.Creature], second.Creature) == 10,
        "意图不显示文字提示，不按本地视角重新计算或重复叠加伤害倍率");
    check(second.Creature.Block == 11 && DuelDamage.Incoming(second.Creature) == 10 && DuelDamage.Outgoing(first) == 10,
        "攻击后获得的格挡立即增加，双方累计攻击与待承伤害一致");
    MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
        second.Creature, 4, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,
        null, null).GetAwaiter().GetResult();
    check(DuelDamage.Incoming(second.Creature) == 14 && DuelDamage.Outgoing(first) == 10,
        "无来源的中毒类伤害也累计，且不会冒充玩家攻击意图");
    foreach (var type in new[] { typeof(MegaCrit.Sts2.Core.Models.Powers.WeakPower), typeof(MegaCrit.Sts2.Core.Models.Powers.VulnerablePower) })
        if (!ModelDb.Contains(type)) ModelDb.Init([type]);
    AccessTools.Field(typeof(Player), "_relics").SetValue(first, new List<RelicModel>());
    AccessTools.Field(typeof(Player), "_relics").SetValue(second, new List<RelicModel>());
    var firstPowers = (List<PowerModel>)AccessTools.Field(typeof(Creature), "_powers").GetValue(first.Creature)!;
    var secondPowers = (List<PowerModel>)AccessTools.Field(typeof(Creature), "_powers").GetValue(second.Creature)!;
    var weakLive = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.WeakPower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(weakLive, [first.Creature]);
    firstPowers.Add(weakLive);
    check(DuelDamage.Outgoing(first) == 10, "后来施加虚弱不改变前面已累计的攻击");
    var vulnerableLive = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(vulnerableLive, [second.Creature]);
    secondPowers.Add(vulnerableLive);
    check(DuelDamage.Outgoing(first) == 10, "后来施加易伤不追溯强化先前攻击");
    var predictedResult = MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
        second.Creature, 10, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, first.Creature, source, null).GetAwaiter().GetResult().Single();
    check(DuelDamage.Outgoing(first) == 21 && predictedResult.TotalDamage == 11,
        "后续攻击按出牌时虚弱易伤锁定，预测结果包含被格挡的伤害");
    firstPowers.Remove(weakLive); secondPowers.Remove(vulnerableLive);
    check(DuelDamage.Outgoing(first) == 21, "清除状态也不改写已提交攻击");
    MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
        first.Creature, 8, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, second.Creature).GetAwaiter().GetResult();
    var ownVulnerable = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(ownVulnerable, [first.Creature]);
    firstPowers.Add(ownVulnerable);
    check(DuelDamage.Outgoing(second) == 8, "自身后来受到易伤不改变对手旧攻击");
    firstPowers.Remove(ownVulnerable);
    foreach (var type in new[] { typeof(MegaCrit.Sts2.Core.Models.Powers.ColossusPower), typeof(MegaCrit.Sts2.Core.Models.Powers.EnvenomPower),
        typeof(MegaCrit.Sts2.Core.Models.Powers.PoisonPower), typeof(MegaCrit.Sts2.Core.Models.Powers.DoomPower),
        typeof(MegaCrit.Sts2.Core.Models.Powers.StrengthPower), typeof(MegaCrit.Sts2.Core.Models.Powers.VigorPower),
        typeof(MegaCrit.Sts2.Core.Models.Powers.DoubleDamagePower) })
        if (!ModelDb.Contains(type)) ModelDb.Init([type]);
    var strengthLive = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(strengthLive, [first.Creature]);
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(strengthLive, [3]);
    firstPowers.Add(strengthLive);
    check(DuelDamage.Outgoing(first) == 21, "后来获得力量只影响后续攻击");
    firstPowers.Remove(strengthLive);
    var tickShim = new Harmony("spirepvp.tests.duration");
    tickShim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.PowerCmd), "Decrement"),
        prefix: new HarmonyMethod(typeof(ImmediateStatusRecorder), nameof(ImmediateStatusRecorder.Decrement)));
    try
    {
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(vulnerableLive, [1]);
        vulnerableLive.SkipNextDurationTick = true; secondPowers.Add(vulnerableLive);
        vulnerableLive.AfterSideTurnEnd(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
            MegaCrit.Sts2.Core.Combat.CombatSide.Enemy, new[] { first.Creature, second.Creature }).GetAwaiter().GetResult();
        check(vulnerableLive.Amount == 0 && !vulnerableLive.SkipNextDurationTick, "一层易伤在当前共享回合结束时递减，不多持续一回合");
        secondPowers.Remove(vulnerableLive);
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(weakLive, [2]);
        weakLive.SkipNextDurationTick = true; firstPowers.Add(weakLive);
        weakLive.AfterSideTurnEnd(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
            MegaCrit.Sts2.Core.Combat.CombatSide.Enemy, new[] { first.Creature, second.Creature }).GetAwaiter().GetResult();
        check(weakLive.Amount == 1, "两层虚弱按每个共享回合一层递减"); firstPowers.Remove(weakLive);
        fixture.Encounter = null;
        vulnerableLive.SkipNextDurationTick = true;
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(vulnerableLive, [1]);
        MegaCrit.Sts2.Core.Commands.PowerCmd.TickDownDuration(vulnerableLive).GetAwaiter().GetResult();
        check(vulnerableLive.Amount == 1 && !vulnerableLive.SkipNextDurationTick, "普通战斗仍保留玩家负面状态首次跳过递减的原版规则");
        fixture.Encounter = ModelDb.Encounter<PvpArena>().ToMutable();
    }
    finally { tickShim.UnpatchAll(tickShim.Id); }
    var timingShim = new Harmony("spirepvp.tests.statustiming");
    var applyMethod = typeof(MegaCrit.Sts2.Core.Commands.PowerCmd).GetMethods().Single(m => m.Name == "Apply"
        && !m.IsGenericMethod && m.GetParameters()[1].ParameterType == typeof(PowerModel));
    timingShim.Patch(applyMethod, prefix: new HarmonyMethod(typeof(ImmediateStatusRecorder), nameof(ImmediateStatusRecorder.Apply)));
    var decrementMethod = AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.PowerCmd), "Decrement");
    timingShim.Patch(decrementMethod, prefix: new HarmonyMethod(typeof(ImmediateStatusRecorder), nameof(ImmediateStatusRecorder.Decrement)));
    try
    {
        if (!ModelDb.Contains(typeof(BlightStrike))) ModelDb.Init([typeof(BlightStrike)]);
        AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(first, ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>());
        var blight = (BlightStrike)ModelDb.Card<BlightStrike>().ToMutable(); blight.Owner = first;
        var blightPlay = new CardPlay { Card = blight, Player = first, Target = second.Creature,
            ResultPile = PileType.Discard, Resources = default, PlayIndex = 0, PlayCount = 1, IsAutoPlay = false };
        timingShim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand), "Execute"),
            prefix: new HarmonyMethod(typeof(DeferredBlightRecorder), nameof(DeferredBlightRecorder.Execute)));
        DeferredBlightRecorder.Doom = 0;
        ((Task)AccessTools.Method(typeof(BlightStrike), "OnPlay").Invoke(blight,
            [new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(), blightPlay])!).GetAwaiter().GetResult();
        check(DeferredBlightRecorder.Doom == 0 && second.Creature.CurrentHp == oldHp && second.Creature.Block == 11,
            "真实荒役打击只登记回合末灾厄，不按预测伤害提前施加");
        var applyBlight = AccessTools.Method(typeof(DuelDamage), "ApplyBlightDoom");
        var blightContext = new ThrowingPlayerChoiceContext();
        ((Task)applyBlight.Invoke(null, [blightContext, second.Creature, blight, new[] {
            new DamageResult(second.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { BlockedDamage = 8, UnblockedDamage = 0 } }])!).GetAwaiter().GetResult();
        check(DeferredBlightRecorder.Doom == 0, "荒役打击实际结算被完全格挡时不施加灾厄");
        ((Task)applyBlight.Invoke(null, [blightContext, second.Creature, blight, new[] {
            new DamageResult(second.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { BlockedDamage = 3, UnblockedDamage = 5 } }])!).GetAwaiter().GetResult();
        check(DeferredBlightRecorder.Doom == 5, "荒役打击结算后按实际扣血5点请求灾厄，格挡部分不计入");
        timingShim.Unpatch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand), "Execute"), HarmonyPatchType.Prefix, timingShim.Id);
        if (!ModelDb.Contains(typeof(Outbreak))) ModelDb.Init([typeof(Outbreak)]);
        var outbreak = (Outbreak)ModelDb.Card<Outbreak>().ToMutable(); outbreak.Owner = first;
        var poisonProbe = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.PoisonPower>().ToMutable();
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(poisonProbe, [second.Creature]);
        var outbreakScopeType = typeof(ModEntry).Assembly.GetType("SpirePvp.ImmediateOutbreakScopePatch")!;
        var poisonScopeType = typeof(ModEntry).Assembly.GetType("SpirePvp.ImmediateOutbreakPoisonPatch")!;
        object?[] outbreakScopeArgs = [outbreak, false];
        AccessTools.Method(outbreakScopeType, "Prefix").Invoke(null, outbreakScopeArgs);
        object?[] poisonScopeArgs = [poisonProbe, false];
        AccessTools.Method(poisonScopeType, "Prefix").Invoke(null, poisonScopeArgs);
        var poisonActive = AccessTools.PropertyGetter(poisonScopeType, "Active");
        var asyncPoisonScope = CapturePoisonScope();
        async Task<bool> CapturePoisonScope() { await Task.Yield(); return (bool)poisonActive.Invoke(null, null)!; }
        var limitDamagePrefix = AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.DeferredDamageCommandPatch"), "Prefix");
        var poisonProps = MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered;
        object?[] poisonDamageArgs = [new ThrowingPlayerChoiceContext(), new[] { second.Creature }, 9m, poisonProps, null, null, null, null];
        var beforePoisonLedger = DuelDamage.Incoming(second.Creature);
        check((bool)limitDamagePrefix.Invoke(null, poisonDamageArgs)! && poisonDamageArgs[7] == null
            && DuelDamage.Incoming(second.Creature) == beforePoisonLedger,
            "毒性爆发触发的毒伤放行原生即时扣血，不加入累计伤害");
        var immediateDeathPrefix = AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.DeferredDeathPatch"), "Prefix");
        object?[] immediateDeathArgs = [second.Creature, false, null];
        check((bool)immediateDeathPrefix.Invoke(null, immediateDeathArgs)! && immediateDeathArgs[2] == null,
            "毒性爆发造成致命毒伤时立即走原生死亡流程，不等待回合末");
        AccessTools.Method(poisonScopeType, "Postfix").Invoke(null, [poisonScopeArgs[1]]);
        AccessTools.Method(outbreakScopeType, "Postfix").Invoke(null, [outbreakScopeArgs[1]]);
        check(!(bool)poisonActive.Invoke(null, null)! && asyncPoisonScope.GetAwaiter().GetResult(),
            "毒性爆发的异步毒伤保留即时作用域，调用方恢复后不影响其它卡牌");
        poisonDamageArgs[7] = null;
        check(!(bool)limitDamagePrefix.Invoke(null, poisonDamageArgs)!,
            "普通中毒伤害继续采用原有累计规则");
        ((Task<IEnumerable<DamageResult>>)poisonDamageArgs[7]!).GetAwaiter().GetResult();
        check(DuelDamage.Incoming(second.Creature) == beforePoisonLedger + 9,
            "即时毒伤例外结束后，普通毒伤正常入账");
        var envenom = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.EnvenomPower>().ToMutable();
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(envenom, [first.Creature]);
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(envenom, [3]);
        firstPowers.Add(envenom);
        ImmediateStatusRecorder.Applied = 0;
        var choice = new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext();
        MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choice, second.Creature, 10,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, first.Creature).GetAwaiter().GetResult();
        check(ImmediateStatusRecorder.Applied == 0, "涂毒按出牌时格挡判断：当时完全挡住的攻击不立即上毒");
        second.Creature.LoseBlockInternal(11);
        MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choice, second.Creature, 10,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, first.Creature).GetAwaiter().GetResult();
        var poison = second.Creature.GetPower<MegaCrit.Sts2.Core.Models.Powers.PoisonPower>()!;
        check(ImmediateStatusRecorder.Applied == 1 && poison.Amount == 3 && second.Creature.CurrentHp == oldHp,
            "实际涂毒钩子在攻击入账时立即施加中毒，生命仍保持不变");
        var outgoingLedgerTable = AccessTools.Field(typeof(DuelDamage), "Ledgers").GetValue(null)!;
        var outgoingLedger = outgoingLedgerTable.GetType().GetMethod("GetOrCreateValue")!.Invoke(outgoingLedgerTable, [combat])!;
        var outgoingHits = (System.Collections.IList)outgoingLedger.GetType().GetProperty("Hits")!.GetValue(outgoingLedger)!;
        AccessTools.Field(typeof(DuelDamage), "_replay").SetValue(null, outgoingHits[outgoingHits.Count - 1]);
        AccessTools.Field(typeof(DuelDamage), "_replayConsumed").SetValue(null, true);
        AccessTools.PropertySetter(typeof(DuelDamage), nameof(DuelDamage.IsSettling)).Invoke(null, [true]);
        var replayScope = AccessTools.Field(typeof(DuelDamage), "ReplayHookScope").GetValue(null)!;
        replayScope.GetType().GetProperty("Value")!.SetValue(replayScope, true);
        MegaCrit.Sts2.Core.Hooks.Hook.AfterDamageGiven(choice, combat, first.Creature,
            new DamageResult(second.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { UnblockedDamage = 10 },
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, second.Creature, null).GetAwaiter().GetResult();
        check(ImmediateStatusRecorder.Applied == 1 && poison.Amount == 3, "回合末同一伤害重放跳过攻击回调，涂毒不重复触发");
        ((AsyncLocal<bool>)AccessTools.Field(typeof(DuelDamage), "ReplayHookScope").GetValue(null)!).Value = false;
        AccessTools.PropertySetter(typeof(DuelDamage), nameof(DuelDamage.IsSettling)).Invoke(null, [false]);
        AccessTools.Field(typeof(DuelDamage), "_replay").SetValue(null, null);
        fixture.Creatures = [first.Creature, second.Creature];
        var beforePoison = DuelDamage.Incoming(second.Creature);
        poison.AfterSideTurnStart(CombatSide.Player, fixture.Creatures, combat).GetAwaiter().GetResult();
        check(DuelDamage.Incoming(second.Creature) == beforePoison && poison.Amount == 3,
            "累计模式的回合开始不再重复触发中毒伤害");
        MegaCrit.Sts2.Core.Hooks.Hook.AfterSideTurnEnd(combat, CombatSide.Player, fixture.Creatures).GetAwaiter().GetResult();
        check(DuelDamage.Incoming(second.Creature) == beforePoison + 3 && poison.Amount == 2,
            "本回合新增中毒在本回合末加入结算并正常减一层，不延迟到下回合");
        var doom = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.DoomPower>().ToMutable();
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(doom, [second.Creature]);
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(doom, [10]); secondPowers.Add(doom);
        check(MegaCrit.Sts2.Core.Models.Powers.DoomPower.GetDoomedCreatures(fixture.Creatures).Count == 0,
            "灾厄按当前实际生命判断门槛，不能按预计伤害提前击杀");
        second.Creature.SetCurrentHpInternal(8);
        check(MegaCrit.Sts2.Core.Models.Powers.DoomPower.GetDoomedCreatures(fixture.Creatures).SequenceEqual([second.Creature]),
            "伤害结算降低生命后，原生灾厄检查可在当前回合检出该玩家");
        second.Creature.SetCurrentHpInternal(oldHp);
    }
    finally
    {
        AccessTools.PropertySetter(typeof(DuelDamage), nameof(DuelDamage.IsSettling)).Invoke(null, [false]);
        timingShim.UnpatchAll(timingShim.Id);
    }
    if (!ModelDb.Contains(typeof(MegaCrit.Sts2.Core.Models.Powers.RupturePower)))
        ModelDb.Init([typeof(MegaCrit.Sts2.Core.Models.Powers.RupturePower)]);
    if (!ModelDb.Contains(typeof(MegaCrit.Sts2.Core.Models.Powers.StrengthPower)))
        ModelDb.Init([typeof(MegaCrit.Sts2.Core.Models.Powers.StrengthPower)]);
    var rupture = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.RupturePower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(rupture, [first.Creature]);
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(rupture, [2]);
    var foreignCard = (CardModel)ModelDb.Card<StrikeIronclad>().ToMutable(); foreignCard.Owner = second;
    var ruptureShim = new Harmony("spirepvp.tests.rupture");
    ruptureShim.Patch(typeof(MegaCrit.Sts2.Core.Commands.PowerCmd).GetMethods().Single(m => m.Name == "Apply"
        && !m.IsGenericMethod && m.GetParameters()[1].ParameterType == typeof(PowerModel)),
        prefix: new HarmonyMethod(typeof(RuptureStrengthRecorder), nameof(RuptureStrengthRecorder.Apply)));
    try
    {
        RuptureStrengthRecorder.Strength = 0;
        var choice = new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext();
        var damaged = new DamageResult(first.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered) { UnblockedDamage = 5 };
        rupture.AfterDamageReceived(choice, first.Creature, damaged, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,
            second.Creature, foreignCard).GetAwaiter().GetResult();
        check(RuptureStrengthRecorder.Strength == 0, "PvP 对手卡牌造成真实生命损失不会触发撕裂");
        rupture.AfterDamageReceived(choice, first.Creature, damaged, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,
            second.Creature, source).GetAwaiter().GetResult();
        check(RuptureStrengthRecorder.Strength == 0, "对手的反伤即使携带自己的卡牌来源也不会冒充自伤触发撕裂");
        rupture.AfterDamageReceived(choice, first.Creature, damaged, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,
            null, null).GetAwaiter().GetResult();
        check(RuptureStrengthRecorder.Strength == 0, "PvP 无卡牌来源的中毒、反伤等伤害不会触发撕裂");
        rupture.AfterDamageReceived(choice, first.Creature, damaged, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,
            first.Creature, source).GetAwaiter().GetResult();
        check(RuptureStrengthRecorder.Strength == 2, "自己卡牌造成实际生命损失触发撕裂，保留升级后的力量数量");
        var blocked = new DamageResult(first.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { BlockedDamage = 5 };
        rupture.AfterDamageReceived(choice, first.Creature, blocked, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,
            first.Creature, source).GetAwaiter().GetResult();
        check(RuptureStrengthRecorder.Strength == 2, "完全格挡的自身卡牌伤害不触发撕裂");
        var selfPlay = new CardPlay { Card = source, Player = first, Target = first.Creature,
            ResultPile = PileType.Discard, Resources = default, PlayIndex = 0, PlayCount = 1, IsAutoPlay = false };
        rupture.BeforeCardPlayed(selfPlay).GetAwaiter().GetResult();
        rupture.AfterDamageReceived(choice, first.Creature, damaged, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,
            first.Creature, source).GetAwaiter().GetResult();
        check(RuptureStrengthRecorder.Strength == 2, "正在打出的自伤卡仍沿用原版撕裂的卡牌内计数");
        rupture.AfterCardPlayed(choice, selfPlay).GetAwaiter().GetResult();
        check(RuptureStrengthRecorder.Strength == 4, "自伤卡打完立即获得力量，不推迟到累计结算");
        check(PvpSelfDamage.IsCardSelfDamage(first.Creature, source) && !PvpSelfDamage.IsCardSelfDamage(first.Creature, foreignCard),
            "累计模式只将自身卡牌自伤视为需要立即支付的伤害");
        var damagePrefix = AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.DeferredDamageCommandPatch"), "Prefix");
        object?[] damageArgs = [choice, new[] { first.Creature }, 5m, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,
            first.Creature, source, selfPlay, null];
        check((bool)damagePrefix.Invoke(null, damageArgs)! && damageArgs[7] == null,
            "自伤命令放行原生实际扣血，不进入累计账本，撕裂可当前回合生效");
        rules.DeferredDamage = false;
        rupture.AfterDamageReceived(choice, first.Creature, damaged, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,
            second.Creature, foreignCard).GetAwaiter().GetResult();
        check(RuptureStrengthRecorder.Strength == 4, "实时 PvP 也不能利用对手攻击触发撕裂");
        rules.DeferredDamage = true; fixture.Encounter = null;
        rupture.AfterDamageReceived(choice, first.Creature, damaged, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,
            second.Creature, foreignCard).GetAwaiter().GetResult();
        check(RuptureStrengthRecorder.Strength == 6, "普通战斗保留原生撕裂触发逻辑");
        fixture.Encounter = ModelDb.Encounter<PvpArena>().ToMutable();
    }
    finally { ruptureShim.UnpatchAll(ruptureShim.Id); }
    AccessTools.Method(typeof(DuelDamage), "Reset").Invoke(null, [combat]);
    firstPowers.Clear(); secondPowers.Clear();
    var doubled = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.DoubleDamagePower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(doubled, [first.Creature]);
    firstPowers.Add(doubled);
    MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
        second.Creature, 10, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, first.Creature, source, null).GetAwaiter().GetResult();
    check(DuelDamage.Outgoing(first) == 20, "原生双倍伤害能力实时参与自己的攻击意图");
    MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
        first.Creature, 8, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, second.Creature).GetAwaiter().GetResult();
    firstPowers.Add(ownVulnerable);
    firstPowers.Clear();
    check(DuelDamage.Outgoing(first) == 20 && DuelDamage.Outgoing(second) == 8,
        "每段伤害在产生时锁定，回合末能力到期不会改变显示与结算");
    // Native replay uses these final ledger values without reapplying buffs.
    check(DuelDamage.Incoming(second.Creature) == 20 && DuelDamage.Incoming(first.Creature) == 8,
        "各自上方的攻击数字与对方账本最终伤害完全一致");
    rules.DeferredDamage = false;
    check(!DuelDamage.Enabled(combat), "关闭开关后不拦截实时伤害");
    rules.DeferredDamage = true; fixture.Encounter = null;
    check(!DuelDamage.Enabled(combat), "回合末结算不影响普通发育战斗与正常爬塔");
}

void RunDeferredBuffTests(Action<bool, string> check)
{
    foreach (var type in new[] { typeof(MegaCrit.Sts2.Core.Models.Powers.ReaperFormPower), typeof(MegaCrit.Sts2.Core.Models.Powers.SicEmPower),
        typeof(MegaCrit.Sts2.Core.Models.Powers.ConcoctPower), typeof(DeferredAttackProbePower) })
        if (!ModelDb.Contains(type)) ModelDb.Init([type]);
    var run = DispatchProxy.Create<IRunState, DeferredRunFixture>();
    var runFixture = (DeferredRunFixture)(object)run;
    var rules = PvpLobbyModifiers.Create(0, new DuelMultipliers(2, 1.5m), deferredDamage: true).OfType<PvpDuel>().Single();
    runFixture.Rules = [rules];
    var state = DispatchProxy.Create<ICombatState, DeferredCombatFixture>();
    var fixture = (DeferredCombatFixture)(object)state; fixture.Run = run; fixture.Encounter = ModelDb.Encounter<PvpArena>().ToMutable();
    var first = FakePlayer(501); var second = FakePlayer(502);
    first.Creature.CombatState = state; second.Creature.CombatState = state;
    fixture.Creatures = [first.Creature, second.Creature];
    runFixture.CurrentListeners = () => fixture.Creatures.SelectMany(c => c.Powers).ToArray();
    var firstPowers = (List<PowerModel>)AccessTools.Field(typeof(Creature), "_powers").GetValue(first.Creature)!;
    var secondPowers = (List<PowerModel>)AccessTools.Field(typeof(Creature), "_powers").GetValue(second.Creature)!;
    var source = (CardModel)ModelDb.Card<StrikeIronclad>().ToMutable(); source.Owner = first;
    var choice = new ThrowingPlayerChoiceContext();
    var reaper = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.ReaperFormPower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(reaper, [first.Creature]);
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(reaper, [2]); firstPowers.Add(reaper);
    var shim = new Harmony("spirepvp.tests.attackbuffs");
    var apply = typeof(MegaCrit.Sts2.Core.Commands.PowerCmd).GetMethods().Single(m => m.Name == "Apply" && !m.IsGenericMethod && m.GetParameters()[1].ParameterType == typeof(PowerModel));
    shim.Patch(apply, prefix: new HarmonyMethod(typeof(ImmediateStatusRecorder), nameof(ImmediateStatusRecorder.Apply)));
    shim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.OstyCmd), "Summon"),
        prefix: new HarmonyMethod(typeof(DeferredSummonRecorder), nameof(DeferredSummonRecorder.Summon)));
    try
    {
        DeferredBlightRecorder.Doom = 0; second.Creature.GainBlockInternal(20);
        MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choice, second.Creature, 10,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, first.Creature, source, null).GetAwaiter().GetResult();
        check(DeferredBlightRecorder.Doom == 20 && second.Creature.CurrentHp == 80 && second.Creature.Block == 20,
            "死神形态在攻击入账时按伤害立即请求灾厄，包含被格挡攻击且尚未扣血");
        firstPowers.Remove(reaper);
        var osty = new Creature(ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.Osty>().ToMutable(), CombatSide.Player, null) { PetOwner = first };
        osty.SetMaxHpInternal(10); osty.SetCurrentHpInternal(10); osty.CombatState = state;
        fixture.Creatures = [first.Creature, second.Creature, osty];
        var sic = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.SicEmPower>().ToMutable();
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(sic, [second.Creature]);
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Applier)).Invoke(sic, [first.Creature]);
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(sic, [4]); secondPowers.Add(sic);
        DeferredSummonRecorder.Amount = 0;
        MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choice, second.Creature, 6,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, osty, source, null).GetAwaiter().GetResult();
        check(DeferredSummonRecorder.Amount == 4 && DuelDamage.Outgoing(first) == 16,
            "紧追不放在奥斯提攻击时立即召唤，支持目标身上的攻击触发状态");
        MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choice, second.Creature, 6,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, first.Creature, source, null).GetAwaiter().GetResult();
        check(DeferredSummonRecorder.Amount == 4, "紧追不放不因主人的普通攻击错误触发");
        firstPowers.Add(reaper); DeferredBlightRecorder.Doom = 0;
        MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choice, second.Creature, 7,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, osty, source, null).GetAwaiter().GetResult();
        check(DeferredBlightRecorder.Doom == 14 && DeferredSummonRecorder.Amount == 8,
            "死神形态与紧追不放同时响应召唤物攻击，各触发一次");
        var ledgerTable = AccessTools.Field(typeof(DuelDamage), "Ledgers").GetValue(null)!;
        var ledger = ledgerTable.GetType().GetMethod("GetOrCreateValue")!.Invoke(ledgerTable, [state])!;
        var hits = (System.Collections.IList)ledger.GetType().GetProperty("Hits")!.GetValue(ledger)!;
        AccessTools.Field(typeof(DuelDamage), "_replay").SetValue(null, hits[hits.Count - 1]);
        AccessTools.Field(typeof(DuelDamage), "_replayConsumed").SetValue(null, true);
        var sacrificePrefix = AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.DeferredDeathPatch"), "Prefix");
        object?[] sacrificeArgs = [osty, false, null];
        check((bool)sacrificePrefix.Invoke(null, sacrificeArgs)! && sacrificeArgs[2] == null,
            "碎骨主动消灭奥斯提时放行原生死亡，不排到回合末");
        osty.SetCurrentHpInternal(0);
        check(!osty.IsAlive, "奥斯提被主动消灭后在出牌阶段立即显示死亡");
        AccessTools.PropertySetter(typeof(DuelDamage), nameof(DuelDamage.IsSettling)).Invoke(null, [true]);
        check(osty.IsAlive && osty.CurrentHp == 0,
            "结算已登记攻击时保留牺牲召唤物的攻击来源，避免碎骨攻击丢失且不恢复生命");
        osty.SetCurrentHpInternal(10);
        AccessTools.Field(typeof(DuelDamage), "_replayConsumed").SetValue(null, false);
        var originalScope = (bool)AccessTools.Method(typeof(DuelDamage), "EnterDamageScope").Invoke(null, [new[] { second.Creature }, osty, source, null])!;
        async Task<bool> CapturedScope()
        {
            await Task.Yield();
            return ((AsyncLocal<bool>)AccessTools.Field(typeof(DuelDamage), "ReplayHookScope").GetValue(null)!).Value;
        }
        var capturedScope = CapturedScope();
        AccessTools.Method(typeof(DuelDamage), "LeaveDamageScope").Invoke(null, [originalScope]);
        check(capturedScope.GetAwaiter().GetResult() && !((AsyncLocal<bool>)AccessTools.Field(typeof(DuelDamage), "ReplayHookScope").GetValue(null)!).Value,
            "异步重放捕获自身抑制状态，调用者作用域立即恢复并不泄漏");
        AccessTools.Field(typeof(DuelDamage), "_replayConsumed").SetValue(null, true);
        var replayScope = AccessTools.Field(typeof(DuelDamage), "ReplayHookScope").GetValue(null)!;
        replayScope.GetType().GetProperty("Value")!.SetValue(replayScope, true);
        MegaCrit.Sts2.Core.Hooks.Hook.AfterDamageGiven(choice, state, osty,
            new DamageResult(second.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { BlockedDamage = 7 },
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, second.Creature, source).GetAwaiter().GetResult();
        check(DeferredBlightRecorder.Doom == 14 && DeferredSummonRecorder.Amount == 8,
            "回合末重放同时跳过死神形态与紧追不放，避免重复追加灾厄和召唤");
        AccessTools.Method(typeof(DuelDamage), "EnterDamageScope").Invoke(null, [new[] { second.Creature }, osty, source, null]);
        check(!(bool)replayScope.GetType().GetProperty("Value")!.GetValue(replayScope)!,
            "同来源的新伤害命令独立进入异步作用域，不继承旧伤害的回调抑制");
        MegaCrit.Sts2.Core.Hooks.Hook.AfterDamageGiven(choice, state, first.Creature,
            new DamageResult(second.Creature, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) { UnblockedDamage = 3 },
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, second.Creature, source).GetAwaiter().GetResult();
        check(DeferredBlightRecorder.Doom == 20, "结算中新的独立伤害仍保留攻击回调，不全局禁用 Buff");
        ((AsyncLocal<bool>)AccessTools.Field(typeof(DuelDamage), "ReplayHookScope").GetValue(null)!).Value = false;
        AccessTools.PropertySetter(typeof(DuelDamage), nameof(DuelDamage.IsSettling)).Invoke(null, [false]);
        AccessTools.Field(typeof(DuelDamage), "_replay").SetValue(null, null);
        firstPowers.Clear(); secondPowers.Clear(); second.Creature.LoseBlockInternal(20);
        var probe = (DeferredAttackProbePower)ModelDb.Power<DeferredAttackProbePower>().ToMutable();
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(probe, [first.Creature]); firstPowers.Add(probe);
        var previous = DuelDamage.Outgoing(first);
        MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choice, second.Creature, 5,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, first.Creature, source, null).GetAwaiter().GetResult();
        check(probe.Count == 1 && DuelDamage.Outgoing(first) == previous + 7 && second.Creature.CurrentHp == 80,
            "角色Mod同类攻击回调也即时执行，追加伤害继续累计且不会递归重复触发");
        firstPowers.Clear();
        var concoct = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.ConcoctPower>().ToMutable();
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(concoct, [first.Creature]);
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(concoct, [3]); firstPowers.Add(concoct);
        ImmediateStatusRecorder.Applied = 0;
        MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choice, second.Creature, 5,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, first.Creature, source, null).GetAwaiter().GetResult();
        check(ImmediateStatusRecorder.Applied == 1 && second.Creature.GetPower<MegaCrit.Sts2.Core.Models.Powers.PoisonPower>()?.Amount == 3,
            "同类临时上毒 Buff 也通过通用回调立即触发");
    }
    finally
    {
        shim.UnpatchAll(shim.Id);
        ((AsyncLocal<bool>)AccessTools.Field(typeof(DuelDamage), "ReplayHookScope").GetValue(null)!).Value = false;
        AccessTools.PropertySetter(typeof(DuelDamage), nameof(DuelDamage.IsSettling)).Invoke(null, [false]);
        AccessTools.Field(typeof(DuelDamage), "_replay").SetValue(null, null);
    }
}

void RunEventWhitelistTests(Action<bool, string> check)
{
    var names = DevelopmentEvents.Names;
    check(names.Count == 52 && names.Distinct().Count() == 52, "白名单逐行保留 52 个唯一事件");
    check(new[] { "Amalgamator", "TheLanternKey", "FakeMerchant", "TheLegendsWereTrue", "WarHistorianRepy" }
        .All(n => !names.Contains(n)), "五个明确排除的事件不会进入 PvP 问号池");
    check(names.Contains("AromaOfChaos") && names.Contains("Bugslayer") && names.Contains("TinkerTime"),
        "一二三幕保留事件统一混池");
    check(names.Contains("DenseVegetation") && names.Contains("BattlewornDummy"), "白名单保留的事件战斗不会被额外过滤");
    var sample = "| 保留 | 名称 | 来源 | 选项 | 条件 | 事件 ID |\n| 是 | A | B | C | D | AromaOfChaos |\n| 否 | A | B | C | D | FakeMerchant |\n";
    check(DevelopmentEvents.Parse(sample).SequenceEqual(new[] {"AromaOfChaos"}), "白名单只接受保留为是的完整事件行");
    bool failed = false;
    try { DevelopmentEvents.Parse("| 否 | A | B | C | D | FakeMerchant |"); } catch (InvalidDataException) { failed = true; }
    check(failed, "空白名单明确报错，不回退到已排除事件");
    var rng = new MegaCrit.Sts2.Core.Random.Rng(123, "WHITELIST-TEST");
    var next = DevelopmentEvents.Pick(new[] { "A", "B", "C" }, x => x != "B", rng);
    check(next == "B", "事件抽取优先使用尚未访问的合格事件");
    next = DevelopmentEvents.Pick(new[] { "A", "B" }, _ => true, rng);
    check(next is "A" or "B", "合格事件全部访问后才允许重复");
    var nativeTypes = new[] { typeof(MegaCrit.Sts2.Core.Models.Events.CrystalSphere),
        typeof(MegaCrit.Sts2.Core.Models.Events.PotionCourier), typeof(MegaCrit.Sts2.Core.Models.Events.DollRoom) };
    ModelDb.Init(nativeTypes.Where(t => !ModelDb.Contains(t)).ToArray());
    var run = DispatchProxy.Create<IRunState, WhitelistConditionFixture>();
    var fixture = (WhitelistConditionFixture)(object)run;
    var player = FakePlayer(101);
    fixture.Players = [player]; player.Gold = 99;
    check(!DevelopmentEvents.IsAllowed(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>(), run),
        "跨幕水晶球仍要求至少 100 金币");
    player.Gold = 100;
    check(DevelopmentEvents.IsAllowed(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>(), run),
        "发育第一幕满足金币条件后可抽到跨幕水晶球");
    check(DevelopmentEvents.IsAllowed(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.PotionCourier>(), run)
        && DevelopmentEvents.IsAllowed(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.DollRoom>(), run),
        "公共事件的第二幕限制只在入场筛选中解除");
    check(run.CurrentActIndex == 0 && run.Players.Count == 1 && run.Players[0].Gold == 100,
        "跨幕入场检查不改写实际章节、玩家与金币");
    var types = names.Select(n => typeof(EventModel).Assembly.GetType("MegaCrit.Sts2.Core.Models.Events." + n)).ToArray();
    check(types.All(t => t != null && typeof(EventModel).IsAssignableFrom(t)), "全部白名单 ID 都对应当前版本原生普通事件");
}

void RunDevelopmentTests(Action<bool, string> check)
{
    var map = new PvpDevelopmentMap(12345678);
    var rooms = map.GetAllMapPoints().OrderBy(p => p.coord.row).ToArray();
    check(rooms.Length == 12 && map.GetRowCount() == 12 && map.BossMapPoint.coord.row == 12,
        "战前发育包含 12 个房间，决斗位于第 13 层");
    check(rooms[5].PointType == MegaCrit.Sts2.Core.Map.MapPointType.Treasure
        && rooms[10].PointType == MegaCrit.Sts2.Core.Map.MapPointType.Shop
        && rooms[11].PointType == MegaCrit.Sts2.Core.Map.MapPointType.RestSite,
        "第 6 层宝箱，第 11 层商店，第 12 层火堆");
    var outcomes = Enumerable.Range(0, 10).Select(PvpDevelopmentMap.RollPointType).ToArray();
    check(outcomes.Count(t => t == MegaCrit.Sts2.Core.Map.MapPointType.Unknown) == 4
        && outcomes.Count(t => t == MegaCrit.Sts2.Core.Map.MapPointType.Treasure) == 3
        && outcomes.Count(t => t == MegaCrit.Sts2.Core.Map.MapPointType.RestSite) == 3,
        "随机发育房间的问号、宝箱、火堆概率精确为 4∶3∶3");
    check(new PvpDevelopmentMap(12345678).GetAllMapPoints().Select(p => p.PointType)
        .SequenceEqual(map.GetAllMapPoints().Select(p => p.PointType)), "同一种子主客机生成相同发育路线");
    check(Enumerable.Range(0, 30).Select(seed => string.Join(',', new PvpDevelopmentMap((ulong)seed)
        .GetAllMapPoints().Select(p => p.PointType))).Distinct().Count() > 1, "不同种子具有不同的发育路线");
    var visited = new List<MegaCrit.Sts2.Core.Map.MapCoord>();
    foreach (var room in rooms.Append(map.BossMapPoint))
    {
        var next = map.GetNextPoint(visited);
        check(next == room, $"发育地图第 {room.coord.row + 1} 层是唯一下一房间，不能跳层");
        visited.Add(room.coord);
    }
    check(map.GetNextPoint(visited) == null, "决斗结束后没有额外发育房间");
    check(map.GetNextPoint([map.StartingMapPoint.coord]) == rooms[0], "隐藏的起始标记后仍可到达第一层");
    var restored = new MegaCrit.Sts2.Core.Map.SavedActMap(MegaCrit.Sts2.Core.Saves.Runs.SerializableActMap.FromActMap(map));
    check(restored.GetAllMapPoints().Count() == 12 && restored.startMapPoints.Single().coord == rooms[0].coord
        && restored.GetPoint(rooms[^1].coord).Children.Single().coord == restored.BossMapPoint.coord,
        "原生地图序列化保留发育起点和火堆通往决斗的连接");
    foreach (int mode in Enumerable.Range(0, 4))
    {
        var modifiers = PvpLobbyModifiers.Create(mode, new DuelMultipliers(3m, 2m), true);
        var copy = modifiers.Select(m => ModifierModel.FromSerializable(m.ToSerializable())).ToArray();
        var duel = copy.OfType<PvpDuel>().Single();
        check(duel.Development && duel.HealthSteps == 6 && duel.BlockSteps == 4 && PvpLobbyModifiers.GetMode(copy) == mode,
            $"构筑方式 {mode} 原生房间消息同步发育开关和倍率");
        check(PvpLobbyModifiers.Create((mode + 1) % 4, DuelMultipliers.FromModifier(duel), duel.Development)
            .OfType<PvpDuel>().Single().Development, "切换构筑方式保留发育开关");
    }
    check(!PvpLobbyModifiers.Create(0).OfType<PvpDuel>().Single().Development, "默认仍可直接决斗");
    var path = Path.Combine(Path.GetTempPath(), "spirepvp-development-" + Guid.NewGuid() + ".json");
    try
    {
        new PvpSettings { Development = true, LastConstructionMode = 3 }.Save(path);
        var settings = PvpSettings.Load(path);
        check(settings.Development && settings.LastConstructionMode == 3, "发育开关与选牌方式分别保存、重启后保留");
        File.WriteAllText(path, "{}");
        check(!PvpSettings.Load(path).Development, "旧设置升级后发育默认关闭");
    }
    finally { File.Delete(path); File.Delete(path + ".tmp"); }
    var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
    var players = new List<Player> { FakePlayer(711), FakePlayer(712) };
    var currentRooms = new List<MegaCrit.Sts2.Core.Rooms.AbstractRoom>();
    AccessTools.Field(typeof(RunState), "_players").SetValue(run, players);
    AccessTools.Field(typeof(RunState), "_currentRooms").SetValue(run, currentRooms);
    var marker = (PvpDuel)ModelDb.Modifier<PvpDuel>().ToMutable();
    AccessTools.Field(typeof(ModifierModel), "_runState").SetValue(marker, run);
    var scope = DispatchProxy.Create<ICombatState, BlockCombatFixture>();
    ((BlockCombatFixture)(object)scope).Listeners = [marker];
    currentRooms.Add(new MegaCrit.Sts2.Core.Rooms.CombatRoom(ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.CeremonialBeastBoss>().ToMutable(), null));
    check(!MegaCrit.Sts2.Core.Hooks.Hook.ShouldStopCombatFromEnding(scope), "发育事件中的普通战斗不会被 PvP 标记阻止结束");
    currentRooms.Clear();
    currentRooms.Add(new MegaCrit.Sts2.Core.Rooms.CombatRoom(ModelDb.Encounter<PvpArena>().ToMutable(), null));
    check(marker.ShouldStopCombatFromEnding(), "双方存活时决斗仍保持战斗");
    players[1].Creature.SetCurrentHpInternal(0);
    check(!marker.ShouldStopCombatFromEnding(), "决斗有人死亡后允许结束");
}

void RunIndependentDevelopmentTests(Action<bool, string> check)
{
    ModelDb.Init(new[] { typeof(MegaCrit.Sts2.Core.Models.Characters.Ironclad), typeof(MegaCrit.Sts2.Core.Models.Relics.Anchor),
        typeof(MegaCrit.Sts2.Core.Models.Potions.BlockPotion), typeof(MegaCrit.Sts2.Core.Models.Enchantments.Sharp) }
        .Where(t => !ModelDb.Contains(t)).ToArray());
    MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init();
    var rng = new RunRngSet("SOLO-PVP-TEST");
    MegaCrit.Sts2.Core.Saves.Runs.SerializablePlayer Snapshot(ulong id) => new()
    {
        NetId = id, CharacterId = ModelDb.GetId<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(),
        CurrentHp = 71, MaxHp = 83, Gold = 97, MaxEnergy = 3,
        Rng = new MegaCrit.Sts2.Core.Saves.SerializablePlayerRngSet { Seed = id },
        Odds = new(), RelicGrabBag = new RelicGrabBag().ToSerializable(), ExtraFields = new(), UnlockState = new()
    };
    var baseline = new MegaCrit.Sts2.Core.Saves.SerializableRun
    {
        Players = [Snapshot(101), Snapshot(202)], Modifiers = PvpLobbyModifiers.Create(3, new DuelMultipliers(3m, 2m), true)
            .Select(m => m.ToSerializable()).ToList(),
        SerializableRng = rng.ToSerializable(), SerializableOdds = new MegaCrit.Sts2.Core.Odds.RunOddsSet(rng.UnknownMapPoint).ToSerializable(),
        SerializableSharedRelicGrabBag = new RelicGrabBag().ToSerializable(), ExtraFields = new(),
        VisitedMapCoords = [new MegaCrit.Sts2.Core.Map.MapCoord(3, 11)]
    };
    var gate = new DevelopmentRendezvous(baseline);
    var local = gate.Solo(101);
    check(local.Players.Count == 1 && local.Players[0].NetId == 101 && local.VisitedMapCoords.Count == 0,
        "发育状态只包含本方，房间及奖励同步器按单人处理");
    local.Players[0].Gold = 1;
    check(gate.Solo(101).Players[0].Gold == 97 && baseline.Players[0].Gold == 97,
        "个人发育状态复制不会污染对局基线或另一名玩家");
    var client = Snapshot(202);
    client.Gold = 321; client.CurrentHp = 42; client.MaxHp = 109;
    client.Deck = [new() { Id = ModelDb.GetId<StrikeIronclad>(), CurrentUpgradeLevel = 1,
        Enchantment = new() { Id = ModelDb.GetId<MegaCrit.Sts2.Core.Models.Enchantments.Sharp>(), Amount = 4 }, FloorAddedToDeck = 6 }];
    client.Relics = [new() { Id = ModelDb.GetId<MegaCrit.Sts2.Core.Models.Relics.Anchor>(), FloorAddedToDeck = 6 }];
    client.Potions = [new() { Id = ModelDb.GetId<MegaCrit.Sts2.Core.Models.Potions.BlockPotion>(), SlotIndex = 2 }];
    client.ExtraFields.CardShopRemovalsUsed = 2;
    client.ExtraFields.WongoPoints = 11;
    check(gate.AddReady(202, client) && !gate.BothReady && !gate.BothPrepared,
        "客机先完成时锁定个人快照并等待，不提前开战");
    bool rejected = false;
    try { gate.Merge(); } catch (InvalidOperationException) { rejected = true; }
    check(rejected, "双方没有到齐时禁止合并决斗");
    rejected = false;
    try { gate.AddReady(101, client); } catch (InvalidDataException) { rejected = true; }
    check(rejected, "个人快照不能冒用另一名玩家身份");
    rejected = false;
    try { gate.AddPrepared(101); } catch (InvalidDataException) { rejected = true; }
    check(rejected, "尚未收齐快照时不能报告已加载决斗");
    var dead = Snapshot(101); dead.CurrentHp = 0;
    rejected = false;
    try { gate.AddReady(101, dead); } catch (InvalidDataException) { rejected = true; }
    check(rejected, "发育中死亡的角色不能作为存活玩家进入决斗");
    var host = Snapshot(101); host.Gold = 55;
    gate.AddReady(101, host);
    check(gate.BothReady && !gate.BothPrepared, "快照收齐后仍须等待双方重建联机状态");
    check(!gate.AddReady(202, Snapshot(202)), "重复到达消息不会覆盖已确认的牌组和遗物");
    var merged = gate.Merge();
    check(merged.Players.Select(p => p.NetId).SequenceEqual(new ulong[] {101,202})
        && merged.Players[0].Gold == 55 && merged.Players[1].Gold == 321, "合并使用固定玩家顺序并保留各自金币");
    check(merged.Players[1].CurrentHp == 42 && merged.Players[1].MaxHp == 109
        && merged.Players[1].ExtraFields.CardShopRemovalsUsed == 2 && merged.Players[1].ExtraFields.WongoPoints == 11,
        "发育后的生命、最大生命与个人附加计数完整保留");
    check(merged.Players[1].Deck.Single().CurrentUpgradeLevel == 1
        && merged.Players[1].Deck.Single().Enchantment!.Amount == 4
        && merged.Players[1].Deck.Single().FloorAddedToDeck == 6
        && merged.Players[1].Relics.Single().FloorAddedToDeck == 6
        && merged.Players[1].Potions.Single().SlotIndex == 2, "升级、附魔、遗物与药水槽位在保存复制后保留");
    check(merged.VisitedMapCoords.Count == 0 && merged.PreFinishedRoom == null && merged.MapDrawings == null,
        "最终决斗不继承不同步的个人地图位置或房间");
    var finalMap = new PvpDevelopmentMap(rng.Seed);
    var savedFinalMap = new MegaCrit.Sts2.Core.Map.SavedActMap(MegaCrit.Sts2.Core.Saves.Runs.SerializableActMap.FromActMap(finalMap));
    check(finalMap.BossMapPoint.coord.row == 12 && savedFinalMap.GetPoint(finalMap.BossMapPoint.coord) != null,
        "会合保留原发育地图最上方的第 13 层入口，不新建单房地图");
    check(finalMap.GetAllMapPoints().Count() == 12
        && finalMap.GetAllMapPoints().Select(p => p.PointType).SequenceEqual(new PvpDevelopmentMap(rng.Seed).GetAllMapPoints().Select(p => p.PointType)),
        "会合前后同种子的十二个发育节点与类型完全一致");
    var completed = finalMap.GetAllMapPoints().OrderBy(p => p.coord.row).Select(p => p.coord).ToArray();
    check(finalMap.GetNextPoint(completed) == finalMap.BossMapPoint,
        "恢复十二层完成进度后只剩原图的 PvP 房间入口");
    var hostShared = DevelopmentRendezvous.Clone(baseline);
    hostShared.SerializableRng = new RunRngSet("HOST-DEVELOPED-RNG").ToSerializable();
    var hostMerge = gate.Merge(hostShared);
    check(hostMerge.SerializableRng.Seed == hostShared.SerializableRng.Seed
        && hostMerge.Players[1].Gold == 321, "最终共享状态以房主发育后的随机数和历史为准，个人资产仍来自各自快照");
    var historyGate = new DevelopmentRendezvous(baseline);
    var hostHistory = historyGate.Solo(101);
    var clientHistory = historyGate.Solo(202);
    hostHistory.MapPointHistory = [[new() { MapPointType = MegaCrit.Sts2.Core.Map.MapPointType.RestSite,
        PlayerStats = [new() { PlayerId = 101, CurrentGold = 55, HpHealed = 7 }] }]];
    clientHistory.MapPointHistory = [[new() { MapPointType = MegaCrit.Sts2.Core.Map.MapPointType.RestSite,
        PlayerStats = [new() { PlayerId = 202, CurrentGold = 321, HpHealed = 19 }] }]];
    historyGate.AddReady(101, host, hostHistory);
    historyGate.AddReady(202, client, clientHistory);
    var historyMerge = historyGate.Merge(hostHistory);
    check(historyMerge.MapPointHistory[0][0].GetEntry(101).HpHealed == 7
        && historyMerge.MapPointHistory[0][0].GetEntry(202).HpHealed == 19,
        "汇合后每层都有双方各自发育记录，客机历史查询不会缺失");
    historyMerge.MapPointHistory[0][0].GetEntry(202).HpHealed = 0;
    check(historyGate.Merge(hostHistory).MapPointHistory[0][0].GetEntry(202).HpHealed == 19,
        "合并发育历史独立复制，后续战斗不会污染冻结的历史快照");
    gate.AddPrepared(202);
    check(!gate.BothPrepared, "先加载决斗的一方仍等待另一端加载");
    gate.AddPrepared(101);
    check(gate.BothPrepared, "双方重建联机后才能开始决斗");
    DevelopmentMessage RoundTrip(DevelopmentMessage message)
    {
        var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter { WarnOnGrow = false };
        message.Serialize(writer);
        var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
        reader.Reset(writer.Buffer);
        var result = new DevelopmentMessage(); result.Deserialize(reader); return result;
    }
    var ready = RoundTrip(new() { Seed = rng.Seed, Kind = DevelopmentMessageKind.PlayerReady, Player = client });
    check(ready.Player!.NetId == 202 && ready.Player.Deck[0].CurrentUpgradeLevel == 1
        && ready.Player.Deck[0].Enchantment!.Amount == 4 && ready.Player.Potions[0].SlotIndex == 2,
        "原生网络二进制编码完整传输升级、附魔及药水");
    var historyPacket = RoundTrip(new() { Seed = rng.Seed, Kind = DevelopmentMessageKind.PlayerReady, Player = client, Run = clientHistory });
    check(historyPacket.Run!.MapPointHistory[0][0].GetEntry(202).HpHealed == 19,
        "原生网络发育就绪包保留客机的完整逐层历史");
    var progressPacket = RoundTrip(new() { Seed = rng.Seed, Kind = DevelopmentMessageKind.Progress, ProgressRow = 12 });
    check(progressPacket.ProgressRow == 12, "对手位置消息可编码最终决斗入口");
    var offer = RoundTrip(new() { Seed = rng.Seed, Kind = DevelopmentMessageKind.Merge, Run = merged });
    check(offer.Run!.Players[1].Relics[0].Id == client.Relics[0].Id
        && offer.Run.Players[1].CurrentHp == 42 && offer.Run.SerializableRng.Seed == merged.SerializableRng.Seed,
        "房主合并包通过原生网络编码保留共享随机数和两名角色");
    var raw = DispatchProxy.Create<INetGameService, TestTransport>();
    var rawFixture = (TestTransport)(object)raw; rawFixture.Id = 101;
    var single = new DevelopmentNetService(raw);
    single.SendMessage(new DevelopmentMessage());
    single.SendMessage(new DevelopmentMessage(), 202);
    single.RegisterMessageHandler<DevelopmentMessage>((_, _) => { });
    check(single.Type == NetGameType.Singleplayer && single.NetId == 101 && rawFixture.Sent == 0
        && rawFixture.Registered == 0, "单人发育保留真实身份，不发送或订阅原版联机房间动作");
    single.Update();
    check(rawFixture.Updates == 1, "单人发育仍泵送真实连接，维持心跳及最终会合消息");
    single.SetGameLoading(true); single.SetBufferMessages(true);
    check(single.IsGameLoading && rawFixture.BufferCalls == 0, "个人加载和缓冲不阻塞另一端会合消息");
    var coordinatorType = typeof(ModEntry).Assembly.GetType("SpirePvp.IndependentDevelopment")!;
    AccessTools.Field(coordinatorType, "Transitioning").SetValue(null, true);
    try { single.Disconnect(MegaCrit.Sts2.Core.Entities.Multiplayer.NetError.Quit); }
    finally { AccessTools.Field(coordinatorType, "Transitioning").SetValue(null, false); }
    check(rawFixture.Disconnects == 0, "切换个人状态时不关闭多人连接");
    single.Disconnect(MegaCrit.Sts2.Core.Entities.Multiplayer.NetError.Quit);
    check(rawFixture.Disconnects == 1, "正常退出个人发育时会关闭连接，防止对方永久等待");

    // Exercise the actual coordinator callbacks with both arrival orders.
    foreach (bool hostFirst in new[] { false, true })
    {
        var h = DispatchProxy.Create<INetGameService, TestTransport>(); var c = DispatchProxy.Create<INetGameService, TestTransport>();
        var ht = (TestTransport)(object)h; var ct = (TestTransport)(object)c;
        ht.Id = 101; ct.Id = 202; ht.Peer = ct; ct.Peer = ht;
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(RunState), "_players").SetValue(run, new List<Player> { FakePlayer(101), FakePlayer(202) });
        AccessTools.PropertySetter(typeof(RunState), nameof(RunState.Rng)).Invoke(run, [rng]);
        var hs = Activator.CreateInstance(coordinatorType, BindingFlags.Instance | BindingFlags.NonPublic, null, [h, run], null)!;
        var cs = Activator.CreateInstance(coordinatorType, BindingFlags.Instance | BindingFlags.NonPublic, null, [c, run], null)!;
        var hg = new DevelopmentRendezvous(baseline); var cg = new DevelopmentRendezvous(baseline);
        AccessTools.Field(coordinatorType, "_rendezvous").SetValue(hs, hg);
        AccessTools.Field(coordinatorType, "_rendezvous").SetValue(cs, cg);
        AccessTools.Field(coordinatorType, "_arriving").SetValue(hs, true);
        AccessTools.Field(coordinatorType, "_arriving").SetValue(cs, true);
        var hTask = ((TaskCompletionSource<MegaCrit.Sts2.Core.Saves.SerializableRun>)AccessTools.Field(coordinatorType, "_merged").GetValue(hs)!).Task;
        var cTask = ((TaskCompletionSource<MegaCrit.Sts2.Core.Saves.SerializableRun>)AccessTools.Field(coordinatorType, "_merged").GetValue(cs)!).Task;
        try
        {
            void Arrive(INetGameService transport, DevelopmentRendezvous rendezvous, MegaCrit.Sts2.Core.Saves.Runs.SerializablePlayer snapshot, object session)
            {
                rendezvous.AddReady(snapshot.NetId, snapshot);
                transport.SendMessage(RoundTrip(new() { Seed = rng.Seed, Kind = DevelopmentMessageKind.PlayerReady, Player = snapshot }));
                AccessTools.Method(coordinatorType, "TryMerge").Invoke(session, null);
            }
            if (hostFirst) Arrive(h, hg, host, hs); else Arrive(c, cg, client, cs);
            check(!hTask.IsCompleted && !cTask.IsCompleted, "真实会合协调器只收到一人数据时不发布决斗");
            if (hostFirst) Arrive(c, cg, client, cs); else Arrive(h, hg, host, hs);
            check(hTask.IsCompletedSuccessfully && cTask.IsCompletedSuccessfully
                && hTask.Result.Players[1].Gold == 321 && cTask.Result.Players[1].Gold == 321,
                hostFirst ? "主机先到时双方接收相同完整合并数据" : "客机先到时双方接收相同完整合并数据");
            AccessTools.Field(coordinatorType, "<Rejoining>k__BackingField").SetValue(cs, true);
            c.SendMessage(new DevelopmentMessage { Seed = rng.Seed, Kind = DevelopmentMessageKind.Prepared });
            var hGo = ((TaskCompletionSource)AccessTools.Field(coordinatorType, "_go").GetValue(hs)!).Task;
            var cGo = ((TaskCompletionSource)AccessTools.Field(coordinatorType, "_go").GetValue(cs)!).Task;
            check(!hGo.IsCompleted && !cGo.IsCompleted, "客机先加载完成的消息可提前保存，不提前开始或报错");
            hg.AddPrepared(101); AccessTools.Method(coordinatorType, "TryGo").Invoke(hs, null);
            check(hGo.IsCompletedSuccessfully && cGo.IsCompletedSuccessfully, "两端重建完毕后房主统一发出开始信号");
        }
        finally { ((IDisposable)hs).Dispose(); ((IDisposable)cs).Dispose(); }
    }
}

void RunMultiplierTests(Action<bool, string> check)
{
    var turnField = AccessTools.Field(typeof(CombatManager), "_turnState");
    var previousTurn = turnField.GetValue(CombatManager.Instance);
    var setupTurn = RuntimeHelpers.GetUninitializedObject(turnField.FieldType);
    AccessTools.Property(turnField.FieldType, "IsStarting").SetValue(setupTurn, true);
    turnField.SetValue(CombatManager.Instance, setupTurn);
    try
    {
    var path = Path.Combine(Path.GetTempPath(), "spirepvp-multipliers-" + Guid.NewGuid() + ".json");
    try
    {
        new PvpSettings { HealthMultiplier = 3.5m, BlockMultiplier = 2.5m, LastConstructionMode = 2, BossDuels = true,
            SummonMultiplier = 2.5m, DoomMultiplier = 0m, PoisonMultiplier = .5m, Barricade = false }.Save(path);
        var settings = PvpSettings.Load(path);
        check(settings.HealthMultiplier == 3.5m && settings.BlockMultiplier == 2.5m
            && settings.LastConstructionMode == 2 && settings.BossDuels, "两种倍率持久保存，不覆盖选牌模式和幕间开关");
        check(settings.SummonMultiplier == 2.5m && settings.DoomMultiplier == 0m
            && settings.PoisonMultiplier == .5m && !settings.Barricade, "新增倍率和关闭的壁垒开关持久保存，零倍率不会回退默认值");
        File.WriteAllText(path, "{\"BossDuels\":true}");
        settings = PvpSettings.Load(path);
        check(settings.HealthMultiplier == 2m && settings.BlockMultiplier == 1.5m, "旧设置文件的生命/格挡倍率默认保持 2/1.5 倍");
        check(settings.SummonMultiplier == 1.5m && settings.DoomMultiplier == .5m
            && settings.PoisonMultiplier == 1m && settings.Barricade, "旧设置升级保留召唤和灾厄原有效果，中毒默认一倍，壁垒默认开启");
        settings.HealthMultiplier = 0; settings.BlockMultiplier = 11;
        check(settings.HealthMultiplier == 2m && settings.BlockMultiplier == 1.5m, "越界倍率安全回退原有默认值");
    }
    finally { File.Delete(path); File.Delete(path + ".tmp"); }
    foreach (var rules in new[] { new DuelMultipliers(.5m,.5m), new DuelMultipliers(3.5m,2.5m), new DuelMultipliers(10m,10m),
        new DuelMultipliers(2m,1.5m,0m,0m,0m,false), new DuelMultipliers(2m,1.5m,2.5m,1.5m,.5m,false),
        new DuelMultipliers(10m,10m,10m,10m,10m,true) })
    {
        check(DuelMultipliers.Decode(rules.Encode()) == rules, $"倍率 {rules.Health}/{rules.Block} 的整数同步编码无精度损失");
        var modifiers = PvpLobbyModifiers.Create(2, rules);
        var restored = modifiers.Select(m => ModifierModel.FromSerializable(m.ToSerializable())).ToArray();
        var duel = (PvpDuel)restored[0];
        check(DuelMultipliers.FromModifier(duel) == rules
            && PvpLobbyModifiers.GetMode(restored) == 2, "原生房间序列化同步全部五种倍率、壁垒及选牌规则");
        check(DuelMultipliers.FromModifier((PvpDuel)PvpLobbyModifiers.Create(3, DuelMultipliers.FromModifier(duel)).First()) == rules,
            "切换选牌模式保留全部房主规则");
        var player = FakePlayer(981);
        var run = DispatchProxy.Create<IRunState, CardGenerationRunFixture>();
        ((CardGenerationRunFixture)(object)run).Rules = restored;
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        player.Creature.SetCurrentHpInternal(3);
        var power = (PvpFortitudePower)ModelDb.Power<PvpFortitudePower>().ToMutable();
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(power, [player.Creature]);
        power.BeforeApplied(player.Creature, 1, null, null).GetAwaiter().GetResult();
        check(player.Creature.CurrentHp == Math.Max(1, decimal.Floor(3 * rules.Health))
            && player.Creature.MaxHp == decimal.Floor(80 * rules.Health), "实际能力应用房间生命倍率，保留受伤状态并按原版取整");
        var scope = DispatchProxy.Create<ICombatState, BlockCombatFixture>();
        var nativeDouble = (MegaCrit.Sts2.Core.Models.Powers.UnmovablePower)ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.UnmovablePower>().ToMutable();
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(nativeDouble, [player.Creature]);
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(nativeDouble, [1]);
        ((BlockCombatFixture)(object)scope).Listeners = [power, nativeDouble];
        var modified = MegaCrit.Sts2.Core.Hooks.Hook.ModifyBlock(scope, player.Creature, 5,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, null, null, out _);
        check(modified == 5 * rules.Block * 2,
            $"实际格挡钩子使用房主倍率，并继续叠加原版翻倍能力：{modified}/{5 * rules.Block * 2}");
        check(MegaCrit.Sts2.Core.Hooks.Hook.ModifySummonAmount(scope, player, 4, null) == 4 * rules.Summon,
            "原生召唤钩子使用房间召唤倍率，包括零倍率");
        check(MegaCrit.Sts2.Core.Hooks.Hook.ShouldClearBlock(scope, player.Creature, out _) == !rules.Barricade,
            "壁垒关闭时允许原版清空格挡，开启时保留格挡");
        foreach (var (canonical, multiplier) in new[] {
            ((PowerModel)ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.DoomPower>(), rules.Doom),
            ((PowerModel)ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.PoisonPower>(), rules.Poison) })
        {
            check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyPowerAmountReceived(scope, canonical, player.Creature, 9, null, out _) == 9 * multiplier,
                "新增负面层数使用房主灾厄或中毒倍率");
            check(MegaCrit.Sts2.Core.Hooks.Hook.ModifyPowerAmountReceived(scope, canonical, player.Creature, -9, null, out _) == -9,
                "调节倍率不会改变负面层数减少或清除效果");
        }
    }
    var low = FakePlayer(982);
    var lowRun = DispatchProxy.Create<IRunState, CardGenerationRunFixture>();
    ((CardGenerationRunFixture)(object)lowRun).Rules = PvpLobbyModifiers.Create(0, 1, 3).ToArray();
    AccessTools.Field(typeof(Player), "_runState").SetValue(low, lowRun);
    low.Creature.SetCurrentHpInternal(1);
    ModelDb.Power<PvpFortitudePower>().ToMutable().BeforeApplied(low.Creature, 1, null, null).GetAwaiter().GetResult();
    check(low.Creature.CurrentHp == 1 && low.Creature.IsAlive, "一血玩家使用 0.5 倍生命时仍至少保留一血，不因取整死亡");
    }
    finally { turnField.SetValue(CombatManager.Instance, previousTurn); }
}

void RunVersion020Tests(Action<bool, string> check)
{
    ModelDb.Init([typeof(MegaCrit.Sts2.Core.Models.Events.Neow), typeof(MegaCrit.Sts2.Core.Models.Characters.Ironclad),
        typeof(MegaCrit.Sts2.Core.Models.CardPools.IroncladCardPool), typeof(MegaCrit.Sts2.Core.Models.Relics.PandorasBox)]);
    foreach (int mode in new[] { 0, 1, 2, 3, 4 })
    {
        var rules = PvpLobbyModifiers.Create(mode);
        var copy = rules.Select(m => ModifierModel.FromSerializable(m.ToSerializable())).ToArray();
        check(PvpLobbyModifiers.GetMode(copy) == mode && copy.All(m => m.IsMutable),
            $"0.2.0 构筑模式 {mode} 经原版序列化后保持相同模式和可变规则");
        check(copy.Last().ClearsPlayerDeck, $"构筑模式 {mode} 清空初始牌组");
    }
    check(ModelDb.Modifier<PvpDraft15>().GenerateNeowOption(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>()) != null
        && ModelDb.Modifier<PvpDraft20>().GenerateNeowOption(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>()) != null,
        "15 张和 20 张构筑提供原生开局构筑回调");
    var customDefault = PvpLobbyModifiers.Create(4).OfType<PvpDuel>().Single();
    check(customDefault.CustomCommonCards == 10 && customDefault.CustomUncommonCards == 5 && customDefault.CustomRareCards == 3,
        "自定义开局默认白卡10、蓝卡5、金卡3次奖励");
    var customRules = PvpLobbyModifiers.Create(4, new DuelMultipliers(2, 1.5m), customCommonCards: 2, customUncommonCards: 1, customRareCards: 4);
    var customCopy = customRules.Select(m => ModifierModel.FromSerializable(m.ToSerializable())).OfType<PvpDuel>().Single();
    check(customCopy.CustomCommonCards == 2 && customCopy.CustomUncommonCards == 1 && customCopy.CustomRareCards == 4,
        "房主自定义数量通过原生房间规则同步给客机");
    check(PvpCustomDraft.Rarities(customCopy).SequenceEqual(new[] { CardRarity.Common, CardRarity.Common,
        CardRarity.Uncommon, CardRarity.Rare, CardRarity.Rare, CardRarity.Rare, CardRarity.Rare }),
        "自定义构筑严格按白、蓝、金配置数量和顺序领取奖励");
    var emptyCustom = PvpLobbyModifiers.Create(4, new DuelMultipliers(2, 1.5m), customCommonCards: 0, customUncommonCards: 0, customRareCards: 0).OfType<PvpDuel>().Single();
    check(PvpCustomDraft.Rarities(emptyCustom).SequenceEqual(new[] { CardRarity.Common }), "全部设为零时保留一次白卡奖励，避免空牌组");
    var draftPlayer = FakePlayer(908);
    var draftRun = DispatchProxy.Create<IRunState, CardGenerationRunFixture>();
    var draftFixture = (CardGenerationRunFixture)(object)draftRun;
    draftFixture.Players = [draftPlayer]; draftFixture.Pvp = true;
    AccessTools.Field(typeof(Player), "_runState").SetValue(draftPlayer, draftRun);
    AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(draftPlayer,
        ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>());
    AccessTools.Field(typeof(Player), "<RelicGrabBag>k__BackingField").SetValue(draftPlayer, new RelicGrabBag());
    var placeholder = ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>().ToMutable();
    AccessTools.PropertySetter(typeof(EventModel), nameof(EventModel.Owner)).Invoke(placeholder, [draftPlayer]);
    var draftShim = new Harmony("spirepvp.tests.draft020");
    draftShim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Rewards.CardReward), "Populate"),
        prefix: new HarmonyMethod(typeof(DraftRewardRecorder), nameof(DraftRewardRecorder.Populate)));
    draftShim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Rewards.Reward), "SelectUnsynchronized"),
        prefix: new HarmonyMethod(typeof(DraftRewardRecorder), nameof(DraftRewardRecorder.Select)));
    draftShim.Patch(AccessTools.Method(typeof(CardCreationOptions), "GetPossibleCards"),
        prefix: new HarmonyMethod(typeof(CustomSealedRecorder), nameof(CustomSealedRecorder.Pool)));
    draftShim.Patch(typeof(MegaCrit.Sts2.Core.Factories.CardFactory).GetMethods().Single(m => m.Name == "CreateForReward" && m.IsPublic),
        prefix: new HarmonyMethod(typeof(CustomSealedRecorder), nameof(CustomSealedRecorder.Generate)));
    draftShim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.CardSelectCmd), "FromSimpleGridForRewards"),
        prefix: new HarmonyMethod(typeof(CustomSealedRecorder), nameof(CustomSealedRecorder.Select)));
    draftShim.Patch(typeof(MegaCrit.Sts2.Core.Commands.CardPileCmd).GetMethods().Single(m => m.Name == "Add"
        && m.GetParameters()[0].ParameterType == typeof(IEnumerable<CardModel>) && m.GetParameters()[1].ParameterType == typeof(PileType)),
        prefix: new HarmonyMethod(typeof(CustomSealedRecorder), nameof(CustomSealedRecorder.Add)));
    draftShim.Patch(typeof(MegaCrit.Sts2.Core.Commands.CardCmd).GetMethods().Single(m => m.Name == "PreviewCardPileAdd"
        && m.GetParameters()[0].ParameterType == typeof(IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult>)),
        prefix: new HarmonyMethod(typeof(CustomSealedRecorder), nameof(CustomSealedRecorder.Preview)));
    try
    {
        foreach (var type in new[] { typeof(ShrugItOff), typeof(BodySlam), typeof(IronWave), typeof(AshenStrike), typeof(FlameBarrier), typeof(Uppercut), typeof(DemonForm), typeof(Offering), typeof(Impervious) })
            if (!ModelDb.Contains(type)) ModelDb.Init([type]);
        CustomSealedRecorder.Reset(); DraftRewardRecorder.Rewards.Clear();
        ModelDb.Modifier<PvpCustomDraft>().GenerateNeowOption(placeholder)!().GetAwaiter().GetResult();
        check(CustomSealedRecorder.Batches.Select(b => (b.Cards.Count, b.Prefs.MinSelect)).SequenceEqual(new[] { (30,10), (15,5), (9,3) }),
            "默认自定义开局按白蓝金依次进行30选10、15选5、9选3");
        check(CustomSealedRecorder.Batches.Select(b => b.Cards[0].Card.Rarity).SequenceEqual(new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare })
            && CustomSealedRecorder.Batches.All(b => b.Cards.All(c => c.Card.Rarity == b.Cards[0].Card.Rarity)),
            "三批现开候选分别限定白蓝金稀有度");
        check(CustomSealedRecorder.Batches.All(b => b.Prefs.MinSelect == b.Prefs.MaxSelect && !b.Prefs.Cancelable && b.Prefs.RequireManualConfirmation),
            "现开界面要求准确选满指定张数并手动确认，不能取消");
        check(CustomSealedRecorder.Added.Count == 18 && DraftRewardRecorder.Rewards.Count == 0,
            "选中的18张卡加入牌库，不再生成逐项卡牌奖励");
        check(CustomSealedRecorder.Batches[0].Prefs.Prompt.GetFormattedText() == "白卡：从 30 张中选择 10 张",
            "构筑提示显示当前稀有度、候选数量和必选数量");
        check(CustomSealedRecorder.GenerationOptions.All(o => o.Flags.HasFlag(CardCreationFlags.NoUpgradeRoll)
            && o.Flags.HasFlag(CardCreationFlags.NoRarityModification)), "分批现开禁止随机升级和稀有度变色");
        check(CustomSealedRecorder.GenerationCounts.Count == 18 && CustomSealedRecorder.GenerationCounts.All(c => c == 3),
            "默认构筑分别刷新10、5、3组三张候选，重置组间排除列表");
        check(CustomSealedRecorder.Batches.All(b => b.Cards.Chunk(3).All(g => g.Select(c => c.Card.Id).Distinct().Count() == 3)),
            "每组三张候选组内不重复");
        check(CustomSealedRecorder.Batches[0].Cards.Select(c => c.Card.Id).Distinct().Count() < 30
            && CustomSealedRecorder.Batches[0].Cards.Select(c => c.Card).Distinct().Count() == 30,
            "组外可以出现相同牌，每个候选仍为独立实例");
        CustomSealedRecorder.Reset(); CustomSealedRecorder.Pending = new TaskCompletionSource<IEnumerable<CardModel>>();
        var pendingCustom = ModelDb.Modifier<PvpCustomDraft>().GenerateNeowOption(placeholder)!();
        check(!pendingCustom.IsCompleted && CustomSealedRecorder.Batches.Count == 1 && CustomSealedRecorder.Added.Count == 0,
            "本批选牌尚未确认时不添加卡牌，不提前开始下一批或进入地图");
        var firstBatch = CustomSealedRecorder.Batches[0]; var pendingChoice = CustomSealedRecorder.Pending;
        CustomSealedRecorder.Pending = null; pendingChoice.SetResult(firstBatch.Cards.Take(firstBatch.Prefs.MinSelect).Select(c => c.Card));
        pendingCustom.GetAwaiter().GetResult();
        check(CustomSealedRecorder.Batches.Count == 3 && CustomSealedRecorder.Added.Count == 18, "确认白卡后继续蓝金两批，全部完成才结束构筑");
        draftFixture.Rules = customRules.ToArray(); CustomSealedRecorder.Reset();
        ModelDb.Modifier<PvpCustomDraft>().GenerateNeowOption(placeholder)!().GetAwaiter().GetResult();
        check(CustomSealedRecorder.Batches.Select(b => (b.Cards.Count, b.Prefs.MinSelect)).SequenceEqual(new[] { (6,2), (3,1), (12,4) }),
            "非默认配置2白1蓝4金对应6选2、3选1、12选4");
        draftFixture.Rules = PvpLobbyModifiers.Create(4, DuelMultipliers.Default, customCommonCards: 0, customUncommonCards: 0, customRareCards: 3).ToArray();
        CustomSealedRecorder.Reset(); ModelDb.Modifier<PvpCustomDraft>().GenerateNeowOption(placeholder)!().GetAwaiter().GetResult();
        check(CustomSealedRecorder.Batches.Count == 1 && CustomSealedRecorder.Batches[0].Cards.Count == 9
            && CustomSealedRecorder.Added.All(c => c.Rarity == CardRarity.Rare), "白蓝数量为零时跳过，只进行金卡9选3");
        draftFixture.Rules = null;
        foreach (var (model, count) in new (ModifierModel, int)[]
            { (ModelDb.Modifier<PvpDraft15>(), 15), (ModelDb.Modifier<PvpDraft20>(), 20) })
        {
            DraftRewardRecorder.Rewards.Clear();
            model.GenerateNeowOption(placeholder)!().GetAwaiter().GetResult();
            check(DraftRewardRecorder.Rewards.Count == count, $"实际构筑回调执行 {count} 次原生选牌，未提前结束");
            check(DraftRewardRecorder.Rewards.All(r => !r.CanSkip
                && (int)AccessTools.Property(r.GetType(), "OptionCount").GetValue(r)! == 3),
                $"{count} 张构筑每轮提供三个候选且不能跳过");
        }
    }
    finally { draftShim.UnpatchAll(draftShim.Id); }
    var bonus = (List<MegaCrit.Sts2.Core.Rewards.Reward>)AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.BossDuel"), "CreateRewards")
        .Invoke(null, [draftPlayer])!;
    check(bonus.Count == 3 && bonus[0] is MegaCrit.Sts2.Core.Rewards.RelicReward
        && bonus[1] is MegaCrit.Sts2.Core.Rewards.PotionReward && bonus[2] is MegaCrit.Sts2.Core.Rewards.CardReward,
        "胜者奖励恰为一个遗物、一瓶药水和一次原生卡牌奖励");
    var eliteReward = (MegaCrit.Sts2.Core.Rewards.CardReward)bonus[2];
    var creation = (CardCreationOptions)AccessTools.Property(eliteReward.GetType(), "Options").GetValue(eliteReward)!;
    check(creation.RarityOdds == CardRarityOddsType.EliteEncounter && creation.Source == CardCreationSource.Encounter
        && (int)AccessTools.Property(eliteReward.GetType(), "OptionCount").GetValue(eliteReward)! == 3,
        "胜者卡牌奖励使用原生精英概率和三选一规则");
    var path = Path.Combine(Path.GetTempPath(), "spirepvp-settings-test-" + Guid.NewGuid() + ".json");
    try
    {
        check(!PvpSettings.Load(path).BossDuels, "幕间 PvP 设置默认关闭");
        new PvpSettings { BossDuels = true }.Save(path);
        check(PvpSettings.Load(path).BossDuels, "幕间 PvP 开关持久保存并重新加载");
        new PvpSettings { BossDuels = false }.Save(path);
        check(!PvpSettings.Load(path).BossDuels, "关闭设置后重启仍保持关闭");
        foreach (int mode in new[] { 0, 1, 2, 3, 4 })
        {
            new PvpSettings { LastConstructionMode = mode, BossDuels = true }.Save(path);
            var remembered = PvpSettings.Load(path);
            check(remembered.LastConstructionMode == mode && remembered.BossDuels
                && PvpLobbyModifiers.GetMode(PvpLobbyModifiers.Create(remembered.LastConstructionMode)) == mode,
                $"开局模式 {mode} 在重启后保留，并创建对应房间规则，不覆盖幕间开关");
        }
        var changed = PvpSettings.Load(path);
        changed.BossDuels = false;
        changed.Save(path);
        check(PvpSettings.Load(path).LastConstructionMode == 4, "修改幕间开关不会清除记忆的开局模式");
        File.WriteAllText(path, "{\"BossDuels\":true}");
        check(PvpSettings.Load(path).LastConstructionMode == 0 && PvpSettings.Load(path).BossDuels,
            "旧设置文件保留原有开关，首次开局默认十选");
        foreach (int invalid in new[] { -1, 999 })
        {
            File.WriteAllText(path, "{\"LastConstructionMode\":" + invalid + "}");
            check(PvpSettings.Load(path).LastConstructionMode == 0, "无效开局模式安全回退十选，避免创建房间报错");
        }
        new PvpSettings { LastConstructionMode = 4, CustomCommonCards = 7, CustomUncommonCards = 2, CustomRareCards = 0 }.Save(path);
        var customSaved = PvpSettings.Load(path);
        check(customSaved.LastConstructionMode == 4 && customSaved.CustomCommonCards == 7 && customSaved.CustomUncommonCards == 2 && customSaved.CustomRareCards == 0,
            "自定义开局模式和三种数量持久保存，允许关闭某一稀有度");
        var invalidCounts = new PvpSettings { CustomCommonCards = -1, CustomUncommonCards = 101, CustomRareCards = 500 };
        check(invalidCounts.CustomCommonCards == 10 && invalidCounts.CustomUncommonCards == 5 && invalidCounts.CustomRareCards == 3,
            "非法自定义数量安全回退10、5、3");
        File.WriteAllText(path, "invalid JSON");
        check(!PvpSettings.Load(path).BossDuels, "损坏的设置文件安全恢复为默认关闭");
    }
    finally { File.Delete(path); File.Delete(path + ".tmp"); }
    var player = FakePlayer(909);
    player.Creature.SetCurrentHpInternal(37);
    var hp = DuelHealthSnapshot.Capture(player);
    player.Creature.SetMaxHpInternal(160);
    player.Creature.SetCurrentHpInternal(0);
    hp.Restore().GetAwaiter().GetResult();
    check(player.Creature.CurrentHp == 37 && player.Creature.MaxHp == 80,
        "幕间战败后恢复原有 37/80 生命，死亡与双倍上限不带入爬塔");
    player.Creature.SetMaxHpInternal(190);
    player.Creature.SetCurrentHpInternal(170);
    hp.Restore().GetAwaiter().GetResult();
    check(player.Creature.CurrentHp == 37 && player.Creature.MaxHp == 80,
        "幕间战斗中的生命和生命上限变化不会永久累计");
    var boss = typeof(ModEntry).Assembly.GetType("SpirePvp.BossDuel")!;
    var run = DispatchProxy.Create<IRunState, CardGenerationRunFixture>();
    var fixture = (CardGenerationRunFixture)(object)run;
    fixture.Players = [FakePlayer(901), FakePlayer(902)];
    ModelDb.Init([typeof(MegaCrit.Sts2.Core.Models.Encounters.CeremonialBeastBoss)]);
    fixture.Room = new MegaCrit.Sts2.Core.Rooms.CombatRoom(ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.CeremonialBeastBoss>().ToMutable(), null);
    fixture.Room.MarkPreFinished();
    var eligible = AccessTools.Method(boss, "Eligible");
    check(!(bool)eligible.Invoke(null, [run])!, "普通双人第一幕 Boss 后 PvP 已停用");
    fixture.ActIndex = 1;
    check(!(bool)eligible.Invoke(null, [run])!, "普通双人第二幕 Boss 后 PvP 已停用");
    fixture.ActIndex = 2;
    check(!(bool)eligible.Invoke(null, [run])!, "第三幕 Boss 后不插入决斗");
    fixture.ActIndex = 0; fixture.Pvp = true;
    check(!(bool)eligible.Invoke(null, [run])!, "独立 PvP 房间不进入普通爬塔幕间流程");
    fixture.Pvp = false; fixture.Players = [FakePlayer(901), FakePlayer(902), FakePlayer(903)];
    check(!(bool)eligible.Invoke(null, [run])!, "三人房间继续使用正常流程");
    fixture.Players = [FakePlayer(901), FakePlayer(902)];
    fixture.Room = new MegaCrit.Sts2.Core.Rooms.CombatRoom(ModelDb.Encounter<PvpArena>().ToMutable(), null);
    fixture.Room.MarkPreFinished();
    check(!(bool)eligible.Invoke(null, [run])!, "非 Boss 房间不提供幕间决斗");
}

void RunNativeEffectTests(CombatState state, Player p1, Player p2, Action<bool, string> check)
{
    ModelDb.Init([typeof(MegaCrit.Sts2.Core.Models.Monsters.Osty), typeof(MegaCrit.Sts2.Core.Models.Powers.DieForYouPower),
        typeof(MegaCrit.Sts2.Core.Models.Powers.SleightOfFleshPower), typeof(MegaCrit.Sts2.Core.Models.Powers.WeakPower)]);
    var osty = new Creature(ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.Osty>().ToMutable(), CombatSide.Player, null)
        { PetOwner = p2 };
    osty.SetMaxHpInternal(1);
    osty.SetCurrentHpInternal(1);
    osty.CombatState = state;
    state.AddCreature(osty);
    var view = (PvpCombatView)p1.Creature.CombatState!;
    check(!view.Enemies.Contains(osty) && view.Enemies.Contains(p2.Creature), "奥斯提不作为独立敌方目标，群攻不会重复命中本体与宠物");
    var routed = PvpTargets.NormalizeDamage([osty, p2.Creature], p1.Creature, null,
        MegaCrit.Sts2.Core.ValueProps.ValueProp.Move).ToList();
    check(routed.Count == 1 && routed[0] == p2.Creature, "强制攻击奥斯提归入本体承伤流程并去重");
    check(PvpTargets.NormalizeDamage([p1.Creature,p1.Creature], p2.Creature, null,
        MegaCrit.Sts2.Core.ValueProps.ValueProp.Move).Count() == 2, "未重定向的重复目标保持原生次数");
    var die = (MegaCrit.Sts2.Core.Models.Powers.DieForYouPower)ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.DieForYouPower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(die, [osty]);
    var target = die.ModifyUnblockedDamageTarget(routed.Single(), 32, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, p1.Creature);
    check(target == osty, "原生替你赴死将攻击本体的伤害转交存活奥斯提");
    int oldHp = p2.Creature.CurrentHp;
    var absorbed = target.LoseHpInternal(32, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move);
    var spill = routed[0].LoseHpInternal(absorbed.OverkillDamage, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move);
    check(absorbed.UnblockedDamage == 1 && absorbed.OverkillDamage == 31 && spill.UnblockedDamage == 31
        && p2.Creature.CurrentHp == oldHp-31, "奥斯提 1 生命承受 32 伤害：宠物损失 1，本体损失 31");
    check(die.ModifyUnblockedDamageTarget(p2.Creature, 32, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, p1.Creature) == p2.Creature,
        "死亡奥斯提不再替本体承伤");
    p2.Creature.SetCurrentHpInternal(oldHp);
    state.RemoveCreature(osty);
    var flesh = (MegaCrit.Sts2.Core.Models.Powers.SleightOfFleshPower)ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.SleightOfFleshPower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(flesh, [p1.Creature]);
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(flesh, [9]);
    var weak = (MegaCrit.Sts2.Core.Models.Powers.WeakPower)ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.WeakPower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(weak, [p2.Creature]);
    check(NativeEffectSides.IsEnemy(p2.Creature, flesh) && !NativeEffectSides.IsEnemy(p1.Creature, flesh), "效果阵营判断根据能力持有者区分对手和自己");
    ModelDb.Init(new[] { typeof(MegaCrit.Sts2.Core.Models.Potions.WeakPotion) }.Where(t => !ModelDb.Contains(t)).ToArray());
    var weakPotion = (PotionModel)ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.WeakPotion>().ToMutable();
    AccessTools.PropertySetter(typeof(PotionModel), nameof(PotionModel.Owner)).Invoke(weakPotion, [p1]);
    check(weakPotion.IsValidTarget(p2.Creature), "负面药水允许敌方玩家作为目标");
    check(!weakPotion.IsValidTarget(p1.Creature) && !weakPotion.IsValidTarget(osty) && !weakPotion.IsValidTarget(null),
        "负面药水拒绝自己、奥斯提及缺失目标");
    var potionScope = p2.Creature.CombatState;
    p2.Creature.CombatState = null;
    p1.Creature.CombatState = null;
    check(!weakPotion.IsValidTarget(p2.Creature), "普通非 PvP 状态保持原版药水目标规则");
    p2.Creature.CombatState = potionScope;
    p1.Creature.CombatState = new PvpCombatView(state, p1);
    check(p1.Creature.Side == CombatSide.Player && p2.Creature.Side == CombatSide.Player && !p2.Creature.IsEnemy,
        "底层阵营与全局敌人属性保持原生值，不破坏共同回合");
    var shim = new Harmony("spirepvp.tests.effectdamage");
    var damage = AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd), "Damage",
        [typeof(PlayerChoiceContext),typeof(IEnumerable<Creature>),typeof(decimal),typeof(MegaCrit.Sts2.Core.ValueProps.ValueProp),typeof(Creature),typeof(CardModel),typeof(CardPlay)]);
    shim.Patch(damage, prefix: new HarmonyMethod(typeof(EffectDamageRecorder), nameof(EffectDamageRecorder.Prefix)));
    try
    {
        EffectDamageRecorder.Hits.Clear();
        flesh.AfterPowerAmountChanged(new BlockingPlayerChoiceContext(), weak, 1, p1.Creature, null).GetAwaiter().GetResult();
        check(EffectDamageRecorder.Hits.Count == 1 && EffectDamageRecorder.Hits[0] == (p2.Creature,9m),
            "真实血肉戏法回调对敌方玩家施加负面状态后请求 9 点伤害");
        flesh.AfterPowerAmountChanged(new BlockingPlayerChoiceContext(), weak, 0, p1.Creature, null).GetAwaiter().GetResult();
        flesh.AfterPowerAmountChanged(new BlockingPlayerChoiceContext(), weak, 1, p2.Creature, null).GetAwaiter().GetResult();
        var ownWeak = (MegaCrit.Sts2.Core.Models.Powers.WeakPower)ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.WeakPower>().ToMutable();
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(ownWeak, [p1.Creature]);
        flesh.AfterPowerAmountChanged(new BlockingPlayerChoiceContext(), ownWeak, 1, p1.Creature, null).GetAwaiter().GetResult();
        check(EffectDamageRecorder.Hits.Count == 1, "血肉戏法不响应零变化、对手施法或本方负面状态");
        var oldScope = p2.Creature.CombatState;
        p2.Creature.CombatState = null;
        flesh.AfterPowerAmountChanged(new BlockingPlayerChoiceContext(), weak, 1, p1.Creature, null).GetAwaiter().GetResult();
        check(EffectDamageRecorder.Hits.Count == 1, "普通模式继续使用原生阵营判断，不把友军误判成敌人");
        p2.Creature.CombatState = oldScope;
        check(!NativeEffectSides.IntendsToAttack(p2.Creature), "玩家不存在怪物攻击意图时安全返回，眼部攻击不会空引用崩溃");
        RunEyeAttackTests(state, check);
        check(MegaCrit.Sts2.Core.Commands.CreatureCmd.Stun(p2.Creature).IsCompletedSuccessfully,
            "哨子不会调用玩家不存在的怪物 AI 眩晕而抛出异常");
    }
    finally { shim.UnpatchAll(shim.Id); }
    ModelDb.Init([typeof(MegaCrit.Sts2.Core.Models.Relics.UnsettlingLamp)]);
    var lamp = (MegaCrit.Sts2.Core.Models.Relics.UnsettlingLamp)ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.UnsettlingLamp>().ToMutable();
    AccessTools.PropertySetter(typeof(RelicModel), nameof(RelicModel.Owner)).Invoke(lamp, [p1]);
    lamp.BeforeCombatStart().GetAwaiter().GetResult();
    weak.ApplyInternal(p2.Creature, 1, true);
    var card = state.CreateCard(ModelDb.Card<StrikeIronclad>(), p1);
    check(!card.IsValidTarget(osty), "单体攻击不能绕过本体而独立指定奥斯提");
    lamp.BeforePowerAmountChanged(weak, 1, p2.Creature, p1.Creature, card).GetAwaiter().GetResult();
    check(AccessTools.Property(typeof(MegaCrit.Sts2.Core.Models.Relics.UnsettlingLamp), "TriggeringCard").GetValue(lamp) == card,
        "原版不安灯笼识别对敌方玩家施加负面状态的卡牌");
    check(lamp.ModifyPowerAmountGivenMultiplicative(weak, p1.Creature, 1, p2.Creature, card) == 2,
        "原版不安灯笼继续对玩家目标提供负面效果翻倍");
    p2.Creature.RemovePowerInternal(weak);
}
void RunCardGenerationTests(Action<bool, string> check)
{
    foreach (var type in new[] { typeof(Coordinate), typeof(ShrugItOff), typeof(MegaCrit.Sts2.Core.Models.CardPools.MockCardPool) })
        if (!ModelDb.Contains(type)) ModelDb.Init([type]);
    var multi = ModelDb.Card<Coordinate>();
    var normal = ModelDb.Card<ShrugItOff>();
    CardModel[] source = [multi,normal];
    var players = new[] { FakePlayer(701), FakePlayer(702) };
    var run = DispatchProxy.Create<IRunState, CardGenerationRunFixture>();
    var fixture = (CardGenerationRunFixture)(object)run;
    fixture.Players = players;
    foreach (var player in players) AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
    fixture.Pvp = true;
    check(PvpCardGeneration.Filter(run,source).SequenceEqual([normal]), "PvP 根据原生 MultiplayerOnly 标记过滤多人专用牌");
    var filter = AccessTools.Method(typeof(MegaCrit.Sts2.Core.Factories.CardFactory), "FilterForPlayerCount");
    check(((IEnumerable<CardModel>)filter.Invoke(null,[run,source])).SequenceEqual([normal]), "实际原生战斗和变换候选过滤排除多人牌");
    var pool = MegaCrit.Sts2.Core.Models.CardPools.MockCardPool.Create(source);
    var options = new CardCreationOptions([pool],CardCreationSource.Other,CardRarityOddsType.RegularEncounter);
    check(options.GetPossibleCards(players[0]).SequenceEqual([normal]), "实际 Draft 与现开套牌共用的奖励候选过滤排除多人牌");
    foreach (var type in new[] { typeof(AshenStrike), typeof(DemonForm) }) if (!ModelDb.Contains(type)) ModelDb.Init([type]);
    var rarePool = MegaCrit.Sts2.Core.Models.CardPools.MockCardPool.Create(new CardModel[] { normal, multi, ModelDb.Card<AshenStrike>(), ModelDb.Card<DemonForm>() });
    AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(players[0], ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>());
    foreach (var rarity in new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare })
    {
        var fixedOptions = PvpCustomDraft.Options(players[0], rarity);
        var poolList = (List<CardPoolModel>)AccessTools.Field(typeof(CardCreationOptions), "_cardPools").GetValue(fixedOptions)!;
        poolList.Clear(); poolList.Add(rarePool);
        var candidates = fixedOptions.GetPossibleCards(players[0]).ToArray();
        check(candidates.Length == 1 && candidates.All(c => c.Rarity == rarity && c.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly),
            $"原生自定义 {rarity} 奖励候选严格匹配稀有度并排除多人牌");
    }
    fixture.Pvp = false;
    check(((IEnumerable<CardModel>)filter.Invoke(null,[run,source])).Contains(multi), "普通多人模式继续生成多人专用牌");
    check(options.GetPossibleCards(players[0]).Contains(multi), "普通多人奖励候选保持原版多人牌");
    var bossDuel = typeof(ModEntry).Assembly.GetType("SpirePvp.BossDuel")!;
    fixture.Room = new MegaCrit.Sts2.Core.Rooms.CombatRoom(ModelDb.Encounter<PvpArena>().ToMutable(), null);
    AccessTools.Field(bossDuel, "Fighting").SetValue(null, true);
    try
    {
        check(options.GetPossibleCards(players[0]).SequenceEqual([normal]), "普通爬塔的幕间决斗中，生成牌同样排除多人专用牌");
    }
    finally { AccessTools.Field(bossDuel, "Fighting").SetValue(null, false); }
    check(options.GetPossibleCards(players[0]).Contains(multi), "幕间决斗结束后的爬塔奖励恢复原版多人卡池");
    fixture.Room = null;
    fixture.Pvp = true;
    var combat = new CombatState(ModelDb.Encounter<PvpArena>().ToMutable());
    foreach (var player in players) combat.AddPlayer(player);
    var first = MegaCrit.Sts2.Core.Factories.CardFactory.GetForCombat(players[0], source, 20,
        new MegaCrit.Sts2.Core.Random.Rng(123)).ToList();
    check(first.Count == 20 && first.All(card => card.Id == normal.Id), "实际战斗随机生成二十张牌数量不变，均排除多人牌");
    var second = MegaCrit.Sts2.Core.Factories.CardFactory.GetForCombat(players[1], source, 20,
        new MegaCrit.Sts2.Core.Random.Rng(123)).ToList();
    check(first.Select(card => card.Id).SequenceEqual(second.Select(card => card.Id)), "相同种子与卡池生成相同结果，保持两端确定性");
    var distinct = MegaCrit.Sts2.Core.Factories.CardFactory.GetDistinctForCombat(players[0], source, 1,
        new MegaCrit.Sts2.Core.Random.Rng(123)).ToList();
    check(distinct.Count == 1 && distinct[0].Id == normal.Id, "实际不重复战斗生成同样排除多人牌");
}
async Task RunDraftCompletionTests(Action<bool, string> check)
{
    var host = new TaskCompletionSource();
    var client = new TaskCompletionSource();
    int clientMapShown = 0;
    var waitingClient = DraftCompletion.WaitForBoth([host.Task, client.Task], 1, () => clientMapShown++);
    check(clientMapShown == 0 && !waitingClient.IsCompleted, "客机尚未选完时保持选牌界面");
    client.SetResult();
    check(clientMapShown == 1 && !waitingClient.IsCompleted, "客机先选完立即显示等待地图，不再空屏");
    host.SetResult();
    await waitingClient;
    check(clientMapShown == 1 && waitingClient.IsCompletedSuccessfully, "主机完成后等待结束，地图无需重复打开");
    host = new TaskCompletionSource();
    client = new TaskCompletionSource();
    int hostMapShown = 0;
    var waitingHost = DraftCompletion.WaitForBoth([host.Task, client.Task], 0, () => hostMapShown++);
    client.SetResult();
    check(hostMapShown == 0 && !waitingHost.IsCompleted, "对手先完成不打断本方未完成的选牌");
    host.SetResult();
    await waitingHost;
    check(hostMapShown == 1 && waitingHost.IsCompletedSuccessfully, "主机后完成时也正常显示地图");
}
void RunSameSeedTests(Action<bool, string> check)
{
    check(!new PvpSettings().SameSeed && typeof(PvpModConfig).GetProperty("相同种子")?.PropertyType == typeof(bool),
        "相同种子默认关闭，在原生模组配置中提供独立开关");
    var rules = PvpLobbyModifiers.Create(4, DuelMultipliers.Default, sameSeed: true).ToArray();
    var marker = rules.OfType<PvpDuel>().Single();
    check(((PvpDuel)ModifierModel.FromSerializable(marker.ToSerializable())).SameSeed,
        "房主相同种子规则经原生房间序列化同步给客机");
    var fixtureRun = DispatchProxy.Create<IRunState, SameSeedRunFixture>(); var fixture = (SameSeedRunFixture)(object)fixtureRun;
    fixture.Rules = rules; var first = FakePlayer(1101); var second = FakePlayer(1102); fixture.Players = [first, second];
    foreach (var player in fixture.Players)
    {
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, fixtureRun);
        AccessTools.Field(typeof(Player), "_relics").SetValue(player, new List<RelicModel>());
        AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player, ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>());
        player.InitializeSeed("SAME-SEED-PVP");
    }
    check(first.PlayerRng.Seed == second.PlayerRng.Seed && !ReferenceEquals(first.PlayerRng.Rewards, second.PlayerRng.Rewards),
        "移除玩家槽位种子偏移，但双方保留独立奖励随机流");
    var firstRewards = Enumerable.Range(0, 20).Select(_ => first.PlayerRng.Rewards.NextInt()).ToArray();
    var secondRewards = Enumerable.Range(0, 20).Select(_ => second.PlayerRng.Rewards.NextInt()).ToArray();
    check(firstRewards.SequenceEqual(secondRewards), "一方先生成20次奖励不会影响另一方，同种子候选序列一致");
    var pool = MegaCrit.Sts2.Core.Models.CardPools.MockCardPool.Create(new CardModel[] {
        ModelDb.Card<ShrugItOff>(), ModelDb.Card<BodySlam>(), ModelDb.Card<IronWave>(),
        ModelDb.Card<AshenStrike>(), ModelDb.Card<FlameBarrier>(), ModelDb.Card<Uppercut>() });
    var options = CardCreationOptions.ForNonCombatWithUniformOdds([pool]).WithFlags(CardCreationFlags.NoUpgradeRoll);
    string[] Cards(Player player) => Enumerable.Range(0, 10).SelectMany(_ =>
        MegaCrit.Sts2.Core.Factories.CardFactory.CreateForReward(player, 3, options)).Select(c => c.Card.Id.ToString()).ToArray();
    check(Cards(first).SequenceEqual(Cards(second)), "真实卡牌奖励生成连续10组三选一，两名玩家得到完全相同候选");
    var modelId = ModelDb.Card<ShrugItOff>().Id;
    var contentFirst = new MegaCrit.Sts2.Core.Random.Rng(first, modelId, 23);
    var contentSecond = new MegaCrit.Sts2.Core.Random.Rng(second, modelId, 23);
    check(Enumerable.Range(0, 12).All(_ => contentFirst.NextInt() == contentSecond.NextInt()),
        "原生按玩家与内容创建的事件遗物随机数也移除槽位差异");
    var eventFirst = ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>().ToMutable();
    var eventSecond = ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>().ToMutable();
    AccessTools.PropertySetter(typeof(EventModel), nameof(EventModel.Owner)).Invoke(eventFirst, [first]);
    AccessTools.PropertySetter(typeof(EventModel), nameof(EventModel.Owner)).Invoke(eventSecond, [second]);
    AccessTools.PropertySetter(typeof(EventModel), nameof(EventModel.Rng)).Invoke(eventFirst, [new MegaCrit.Sts2.Core.Random.Rng(11)]);
    AccessTools.PropertySetter(typeof(EventModel), nameof(EventModel.Rng)).Invoke(eventSecond, [new MegaCrit.Sts2.Core.Random.Rng(22)]);
    check(Enumerable.Range(0, 12).All(_ => eventFirst.Rng.NextInt() == eventSecond.Rng.NextInt()),
        "相同事件为双方建立相同独立随机序列，不受玩家序号影响");
    var eventPick1 = SameSeed.EventStream(fixtureRun);
    var picked1 = DevelopmentEvents.Pick(DevelopmentEvents.Names, _ => false, eventPick1);
    for (int i = 0; i < 100; i++) fixture.Rng.UpFront.NextInt();
    var picked2 = DevelopmentEvents.Pick(DevelopmentEvents.Names, _ => false, SameSeed.EventStream(fixtureRun));
    check(picked1 == picked2, "道中相同楼层的事件选择不受其他流程额外消耗UpFront随机数影响");
    var state = new CombatState(ModelDb.Encounter<PvpArena>().ToMutable(), fixtureRun);
    state.AddPlayer(first); state.AddPlayer(second);
    CardPile Pile(Player player)
    {
        var pile = new CardPile(PileType.Draw);
        foreach (var card in new CardModel[] { ModelDb.Card<ShrugItOff>(), ModelDb.Card<BodySlam>(), ModelDb.Card<IronWave>(),
            ModelDb.Card<AshenStrike>(), ModelDb.Card<FlameBarrier>(), ModelDb.Card<Uppercut>() })
        { var copy = card.ToMutable(); copy.Owner = player; pile.AddInternal(copy, silent: true); }
        return pile;
    }
    var p1 = Pile(first); var p2 = Pile(second);
    p1.RandomizeOrderInternal(first, fixture.Rng.Shuffle, state);
    fixture.Rng.Shuffle.NextInt(); fixture.Rng.Shuffle.NextInt();
    p2.RandomizeOrderInternal(second, fixture.Rng.Shuffle, state);
    check(p1.Cards.Select(c => c.Id).SequenceEqual(p2.Cards.Select(c => c.Id)),
        "真实开局洗牌相同，期间其他玩家消耗全局洗牌RNG也不影响另一方牌序");
    var actor = (AsyncLocal<Player?>)AccessTools.Field(typeof(SameSeed), "Actor").GetValue(null)!;
    actor.Value = first; var firstShuffle = fixture.Rng.Shuffle;
    var firstDraws = Enumerable.Range(0, 20).Select(_ => firstShuffle.NextInt()).ToArray();
    actor.Value = second; var secondDraws = Enumerable.Range(0, 20).Select(_ => fixture.Rng.Shuffle.NextInt()).ToArray();
    check(firstDraws.SequenceEqual(secondDraws), "后续洗牌流独立推进，相同操作次数得到相同随机序列");
    actor.Value = first; var generated = Enumerable.Range(0, 8).Select(_ => fixture.Rng.CombatCardGeneration.NextInt()).ToArray();
    actor.Value = second;
    check(generated.SequenceEqual(Enumerable.Range(0, 8).Select(_ => fixture.Rng.CombatCardGeneration.NextInt())),
        "战斗内随机生成卡牌使用同种子个人序列，操作先后不交叉消耗");
    actor.Value = null;
    var sourceCard = ModelDb.Card<StrikeIronclad>().ToMutable(); sourceCard.Owner = first;
    var actorPatch = typeof(ModEntry).Assembly.GetType("SpirePvp.SameSeedActorPatch")!;
    object?[] scopeArgs = [sourceCard, Array.Empty<object>(), null];
    AccessTools.Method(actorPatch, "Prefix").Invoke(null, scopeArgs);
    async Task<Player?> CaptureActor() { await Task.Yield(); return actor.Value; }
    var capturedActor = CaptureActor(); AccessTools.Method(actorPatch, "Postfix").Invoke(null, [scopeArgs[2]]);
    check(capturedActor.GetAwaiter().GetResult() == first && actor.Value == null,
        "异步出牌保留自己的随机流上下文，调用方立即恢复，避免两人同时操作串流");
    var restoredRng = MegaCrit.Sts2.Core.Random.PlayerRngSet.FromSerializable(first.PlayerRng.ToSerializable());
    check(restoredRng.Rewards.NextInt() == first.PlayerRng.Rewards.NextInt(), "个人奖励随机进度经原生存档编码恢复，独立发育交接不会重抽");
    fixture.Floor++;
    var nextFloor1 = SameSeed.PersonalStream(first, MegaCrit.Sts2.Core.Entities.Rngs.RunRngType.Shuffle);
    var nextFloor2 = SameSeed.PersonalStream(second, MegaCrit.Sts2.Core.Entities.Rngs.RunRngType.Shuffle);
    check(Enumerable.Range(0, 12).All(_ => nextFloor1.NextInt() == nextFloor2.NextInt()),
        "新房间建立相同新序列，上一房间双方随机消耗差异不污染下一房间");
    actor.Value = null;
    var original = fixture.Rng.Shuffle; actor.Value = first;
    check(!ReferenceEquals(original, fixture.Rng.Shuffle), "只在对应玩家动作上下文中替换战斗随机流");
    marker.SameSeed = false;
    check(ReferenceEquals(original, fixture.Rng.Shuffle), "关闭选项后保留原版共用洗牌RNG");
    actor.Value = null; first.InitializeSeed("SAME-SEED-PVP"); second.InitializeSeed("SAME-SEED-PVP");
    check(first.PlayerRng.Seed != second.PlayerRng.Seed, "关闭选项后玩家种子恢复原版槽位差异");
    fixture.Rules = []; actor.Value = first;
    check(!SameSeed.Enabled(fixtureRun) && ReferenceEquals(original, fixture.Rng.Shuffle), "普通多人不含PvP规则时不替换任何全局随机流");
    actor.Value = null;
    var path = Path.Combine(Path.GetTempPath(), "pvp-sameseed-" + Guid.NewGuid() + ".json");
    try { new PvpSettings { SameSeed = true }.Save(path); check(PvpSettings.Load(path).SameSeed, "相同种子开关可持久记忆"); }
    finally { File.Delete(path); }
}

void RunVfxLayoutTests(Action<bool, string> check)
{
    var own = DuelLayout.Place(101, 101); var foe = DuelLayout.Place(101, 202);
    check(own.Y == .67f && foe.Y == .67f, "双方角色整体从55%下移到67%屏高，保持同一地面");
    var pet = DuelLayout.Place(101, 101, 0); var foePet = DuelLayout.Place(101, 202, 0);
    check(pet.Y == .69f && foePet.Y == .69f && pet.Y > own.Y, "双方召唤物随主人一起下移并保持前后层次");
    check(DuelLayout.MirrorAnchorX(70, 20, true) == -30 && DuelLayout.MirrorAnchorX(70, 20, false) == 70,
        "模型外的特效和充能球锚点按同一中心镜像，恢复本方时使用原坐标");
    var state = new CombatState(ModelDb.Encounter<PvpArena>().ToMutable());
    var first = FakePlayer(901); var second = FakePlayer(902); state.AddPlayer(first); state.AddPlayer(second);
    check(DuelVfx.SideTargets(state, CombatSide.Enemy, first, 901).SequenceEqual([second.Creature]),
        "群攻特效从原版战斗状态按本方施法者找到对手，不读取空怪物阵营");
    check(DuelVfx.SideTargets(state, CombatSide.Enemy, second, 901).SequenceEqual([first.Creature]),
        "对手释放群攻时特效落在本方位置，不固定显示在画面右侧");
    check(DuelVfx.SideTargets(new PvpCombatView(state, second), CombatSide.Enemy, null, 901).SequenceEqual([first.Creature]),
        "已指定角色视角的特效继续尊重该视角，不被本地玩家覆盖");
    check(DuelVfx.SideTargets(state, CombatSide.Player, second, 901).SequenceEqual([second.Creature]),
        "施法者友方阵营特效仍定位施法者本人");
    check(DuelVfx.SideTargets(state, CombatSide.Enemy, null, 901).SequenceEqual([second.Creature])
        && DuelVfx.SideTargets(state, CombatSide.Enemy, null, null).Count == 0,
        "无施法上下文时安全使用本地视角，身份丢失时不错误选择整队");
    check(DuelVfx.Center([], false) == null && DuelVfx.Center([new Godot.Vector2(float.NaN, 3)], false) == null,
        "无可用特效节点时跳过，不产生除零或NaN位置");
    check(DuelVfx.Center([new Godot.Vector2(100, 200), new Godot.Vector2(300, 400)], false) == new Godot.Vector2(200, 300)
        && DuelVfx.Center([new Godot.Vector2(100, 200), new Godot.Vector2(300, 400)], true) == new Godot.Vector2(200, 400),
        "群体特效按有效节点平均中心定位，地面特效使用最低落脚点");
    var contextPatch = typeof(ModEntry).Assembly.GetType("SpirePvp.DuelVfxCasterPatch")!;
    var context = (AsyncLocal<Player?>)AccessTools.Field(typeof(DuelVfx), "Caster").GetValue(null)!;
    var source = ModelDb.Card<StrikeIronclad>().ToMutable(); source.Owner = second;
    object?[] enter = [source, null]; AccessTools.Method(contextPatch, "Prefix").Invoke(null, enter);
    async Task<Player?> CapturedCaster() { await Task.Yield(); return context.Value; }
    var captured = CapturedCaster(); AccessTools.Method(contextPatch, "Postfix").Invoke(null, [enter[1]]);
    check(captured.GetAwaiter().GetResult() == second && context.Value == null,
        "异步卡牌动画保留正确施法者，调用方不会泄漏到下一名玩家的动画");
    if (!ModelDb.Contains(typeof(Bludgeon))) ModelDb.Init([typeof(Bludgeon)]);
    AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(second,
        ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>());
    var bludgeon = ModelDb.Card<Bludgeon>().ToMutable(); bludgeon.Owner = second;
    var heavyShim = new Harmony("spirepvp.tests.heavyvfx");
    heavyShim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand), "Execute"),
        prefix: new HarmonyMethod(typeof(HeavyVfxRecorder), nameof(HeavyVfxRecorder.Execute)));
    try
    {
        var play = new CardPlay { Card = bludgeon, Player = second, Target = first.Creature, ResultPile = PileType.Discard,
            Resources = default, PlayIndex = 0, PlayCount = 1, IsAutoPlay = false };
        ((Task)AccessTools.Method(typeof(Bludgeon), "OnPlay").Invoke(bludgeon, [new ThrowingPlayerChoiceContext(), play])!).GetAwaiter().GetResult();
        check(HeavyVfxRecorder.Path == "vfx/vfx_heavy_blunt" && HeavyVfxRecorder.Base,
            "真实重锤使用跳起重击与地面砸落路径，修复针对目标脚下而非角色根节点");
        check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.VfxCmd), "PlayOnCreature"))?.Owners.Contains("spirepvp.duel") == true,
            "原生重击的目标脚下特效安装PvP落点修复");
    }
    finally { heavyShim.UnpatchAll(heavyShim.Id); }
}

void RunFinalWrathTests(Action<bool, string> check)
{
    var watcherPath = Environment.GetEnvironmentVariable("STS2_WATCHER_DLL");
    if (string.IsNullOrWhiteSpace(watcherPath) || !File.Exists(watcherPath))
    {
        Console.WriteLine("SKIP Watcher integration checks: set STS2_WATCHER_DLL to the installed Watcher.dll path.");
        return;
    }
    var assembly = Assembly.LoadFrom(watcherPath);
    var wrathType = assembly.GetType(WatcherWrathCompatibility.WrathTypeName)!;
    check(wrathType != null && typeof(PowerModel).IsAssignableFrom(wrathType), "使用本机已安装观者的真实Wrath能力进行兼容检查");
    AccessTools.Method(typeof(WatcherWrathCompatibility), "ApplyLoaded").Invoke(null, null);
    check(new[] { "AfterApplied", "AfterRemoved" }.All(name =>
        Harmony.GetPatchInfo(AccessTools.DeclaredMethod(wrathType, name))?.Owners.Contains("spirepvp.wrath") == true),
        "真实观者进入和退出愤怒时均安装累计攻击意图刷新回调");
    if (!ModelDb.Contains(wrathType)) ModelDb.Init([wrathType]);
    foreach (var type in new[] { typeof(MegaCrit.Sts2.Core.Models.Powers.WeakPower), typeof(MegaCrit.Sts2.Core.Models.Powers.IntangiblePower) })
        if (!ModelDb.Contains(type)) ModelDb.Init([type]);
    var run = DispatchProxy.Create<IRunState, DeferredRunFixture>(); var fixture = (DeferredRunFixture)(object)run;
    fixture.Rules = PvpLobbyModifiers.Create(0, DuelMultipliers.Default, deferredDamage: true).ToArray();
    var first = FakePlayer(801); var second = FakePlayer(802);
    AccessTools.Field(typeof(Player), "_relics").SetValue(first, new List<RelicModel>());
    AccessTools.Field(typeof(Player), "_relics").SetValue(second, new List<RelicModel>());
    var state = new CombatState(ModelDb.Encounter<PvpArena>().ToMutable(), run); state.AddPlayer(first); state.AddPlayer(second);
    var firstPowers = (List<PowerModel>)AccessTools.Field(typeof(Creature), "_powers").GetValue(first.Creature)!;
    var secondPowers = (List<PowerModel>)AccessTools.Field(typeof(Creature), "_powers").GetValue(second.Creature)!;
    fixture.CurrentListeners = () => firstPowers.Concat(secondPowers);
    var source = ModelDb.Card<StrikeIronclad>().ToMutable(); source.Owner = first;
    var canonicalWrath = (PowerModel)typeof(ModelDb).GetMethods().Single(m => m.Name == "Power" && m.IsGenericMethodDefinition)
        .MakeGenericMethod(wrathType).Invoke(null, null)!;
    var wrath = canonicalWrath.ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(wrath, [first.Creature]);
    var targetWrath = canonicalWrath.ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(targetWrath, [second.Creature]);
    var reset = AccessTools.Method(typeof(DuelDamage), "Reset");
    var choice = new ThrowingPlayerChoiceContext();
    void Queue(decimal amount, MegaCrit.Sts2.Core.ValueProps.ValueProp props = MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) =>
        MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choice, second.Creature, amount, props, first.Creature, source, null).GetAwaiter().GetResult();
    firstPowers.Add(wrath); Queue(10);
    check(DuelDamage.Outgoing(first) == 20, "愤怒姿态下出牌时显示20点累计攻击");
    firstPowers.Clear();
    check(DuelDamage.Outgoing(first) == 10 && DuelDamage.Incoming(second.Creature) == 10,
        "愤怒出手后退出愤怒，已经累计的攻击取消双倍，双方意图一致");
    reset.Invoke(null, [state]); Queue(10); firstPowers.Add(wrath);
    check(DuelDamage.Outgoing(first) == 20, "普通姿态出手后进入愤怒，先前累计攻击也按最终愤怒翻倍");
    secondPowers.Add(targetWrath);
    check(DuelDamage.Outgoing(first) == 40, "双方最终都愤怒时同时计算增伤和承伤，总计四倍");
    firstPowers.Clear();
    check(DuelDamage.Outgoing(first) == 20, "攻击方退出愤怒后仍保留防守方最终愤怒的双倍承伤");
    secondPowers.Clear(); check(DuelDamage.Outgoing(first) == 10, "双方最终退出愤怒后恢复原攻击值");
    reset.Invoke(null, [state]);
    var weak = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.WeakPower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(weak, [first.Creature]);
    firstPowers.Add(weak); Queue(12); firstPowers.Clear(); firstPowers.Add(wrath);
    check(DuelDamage.Outgoing(first) == 18, "虚弱仍按出牌时锁定，只重新计算最终愤怒倍率");
    reset.Invoke(null, [state]);
    var intangible = ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.IntangiblePower>().ToMutable();
    AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(intangible, [second.Creature]);
    secondPowers.Add(intangible); Queue(10);
    check(DuelDamage.Outgoing(first) == 1, "愤怒伤害仍受出牌时无实体的一点伤害上限约束");
    secondPowers.Clear(); firstPowers.Clear();
    check(DuelDamage.Outgoing(first) == 1, "出手后退出愤怒且移除无实体，不会把原先上限伤害错误除二或重新计算上限");
    reset.Invoke(null, [state]); Queue(4, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable);
    firstPowers.Add(wrath); secondPowers.Add(targetWrath);
    check(DuelDamage.Outgoing(first) == 4, "中毒类非攻击伤害不受最终愤怒倍率影响");
    firstPowers.Clear(); secondPowers.Clear(); reset.Invoke(null, [state]);
}

void RunCounterCleanupTests(Action<bool, string> check)
{
    var state = new CombatState(ModelDb.Encounter<PvpArena>().ToMutable());
    var player = FakePlayer(701); state.AddPlayer(player);
    var history = new MegaCrit.Sts2.Core.Combat.History.CombatHistory();
    int renders = 0; string text = "";
    using var subscription = new DuelCardLimitSubscription(history, state, 701, 15, value => { renders++; text = value; });
    check(renders == 1 && text == "本回合出牌：0 / 15", "出牌上限计数器仍按当前玩家与规则正常显示");
    history.Clear(); check(renders == 2, "正常战斗历史变化仍刷新计数");
    state.RemoveCreature(player.Creature);
    history.Clear();
    check(state.Players.Count == 0 && renders == 2,
        "准确复现先移除玩家再清空历史：不再抛出Local player not found in combat，不刷新已结束UI");
    state.AddPlayer(player); history.Clear();
    check(renders == 3, "缺少玩家时安全跳过，玩家仍在时正常刷新");
    var ui = typeof(ModEntry).Assembly.GetType("SpirePvp.PvpUi")!;
    AccessTools.Field(ui, "_cardLimitSubscription").SetValue(null, subscription);
    AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.CleanupPatch"), "Prefix").Invoke(null, null);
    history.Clear();
    check(renders == 3, "RunManager清理前先解除计数器订阅，历史清空不再访问旧界面");
    subscription.Dispose(); subscription.Dispose(); history.Clear();
    check(renders == 3, "重复清理计数器安全且不会遗留事件处理器");
    var fresh = new DuelCardLimitSubscription(history, state, 701, 10, _ => renders++);
    AccessTools.Field(ui, "_cardLimitSubscription").SetValue(null, fresh);
    AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.CombatUiCleanupPatch"), "Prefix").Invoke(null, null);
    history.Clear();
    check(renders == 4, "单独重置战斗也提前解除订阅，不依赖场景退出事件");
    using var noLocal = new DuelCardLimitSubscription(history, state, null, 15, _ => renders++);
    history.Clear(); check(renders == 4, "本地身份已清除时不会抛异常或错误刷新");
}

async Task RunDuelResultNetworkTests(Action<bool, string> check)
{
    var host = DispatchProxy.Create<INetGameService, TestTransport>();
    var client = DispatchProxy.Create<INetGameService, TestTransport>();
    var h = (TestTransport)(object)host; var c = (TestTransport)(object)client;
    h.Id = 101; c.Id = 202; h.Peer = c; c.Peer = h;
    using var hostResults = new DuelResultChannel(host, 1337, 202);
    using var clientResults = new DuelResultChannel(client, 1337, 101);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter { WarnOnGrow = false };
    new DuelResultMessage { Seed = 1337, Floor = 13, Round = 4, Kind = 1, Winner = 101 }.Serialize(writer);
    var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader(); reader.Reset(writer.Buffer);
    var decoded = new DuelResultMessage(); decoded.Deserialize(reader);
    check(decoded.Seed == 1337 && decoded.Floor == 13 && decoded.Round == 4 && decoded.Kind == 1 && decoded.Winner == 101,
        "判胜消息可靠传输种子、楼层、回合与胜者ID，不依赖玩家列表顺序");
    var earlyClient = clientResults.Finish(13, 4, 202, timeout.Token);
    check(!earlyClient.IsCompleted, "客机先结算时等待主机，不按本地不同结果提前显示获胜");
    var lateHost = hostResults.Finish(13, 4, 101, timeout.Token);
    await Task.WhenAll(earlyClient, lateHost);
    check(earlyClient.Result == 101 && lateHost.Result == 101, "即使两端本地都认为自己获胜，最终统一采用主机发布的胜者ID");
    check(hostResults.LocalId == 101 && clientResults.LocalId == 202
        && h.Disconnects == 0 && c.Disconnects == 0, "确认结果期间保留本地玩家身份，不主动断开连接");
    h.Id = 0; c.Id = 0;
    check(hostResults.LocalId == 101 && clientResults.LocalId == 202, "网络服务清理身份后，结算仍使用进入房间时冻结的本地ID");
    h.Id = 101; c.Id = 202;
    var earlyHost = hostResults.Finish(13, 5, 202, timeout.Token);
    check(!earlyHost.IsCompleted, "主机先结算时也等待客机完成，不能提前退出结果同步");
    c.HoldResultAck = true;
    var lateClient = clientResults.Finish(13, 5, 101, timeout.Token);
    await lateClient;
    check(lateClient.Result == 202 && !earlyHost.IsCompleted && c.HeldResultAck?.Winner == 202,
        "客机收到胜者后先确认；主机尚未收到确认时保持等待");
    c.DeliverResultAck(); await earlyHost;
    check(earlyHost.Result == 202, "客机确认到达后主机完成，双方只有同一个胜者");
    var drawHost = hostResults.Finish(13, 6, null, timeout.Token);
    var drawClient = clientResults.Finish(13, 6, 202, timeout.Token);
    await Task.WhenAll(drawHost, drawClient);
    check(drawHost.Result == null && drawClient.Result == null, "主机判定平局时双方均为平局，不把空胜者当本地获胜");
    var waitingClient = clientResults.Finish(13, 7, 202, timeout.Token);
    host.SendMessage(new DuelResultMessage { Seed = 1338, Floor = 13, Round = 7, Kind = 1, Winner = 101 });
    host.SendMessage(new DuelResultMessage { Seed = 1337, Floor = 12, Round = 7, Kind = 1, Winner = 101 });
    host.SendMessage(new DuelResultMessage { Seed = 1337, Floor = 13, Round = 6, Kind = 1, Winner = 101 });
    host.SendMessage(new DuelResultMessage { Seed = 1337, Floor = 13, Round = 7, Kind = 1, Winner = 999 });
    h.Id = 303; host.SendMessage(new DuelResultMessage { Seed = 1337, Floor = 13, Round = 7, Kind = 1, Winner = 101 }); h.Id = 101;
    check(!waitingClient.IsCompleted, "错误种子、楼层、回合、非房间玩家胜者和陌生发送者均不能覆盖结果");
    var validHost = hostResults.Finish(13, 7, 101, timeout.Token); await Task.WhenAll(waitingClient, validHost);
    using var canceled = new CancellationTokenSource();
    var waitingHost = hostResults.Finish(13, 8, 101, canceled.Token); canceled.Cancel();
    try { await waitingHost; } catch (OperationCanceledException) { }
    check(waitingHost.IsCanceled, "同步取消时退出等待，界面可显示可返回的失败提示，不永久卡住");
    var gate = AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.DuelFinishedDisconnectPatch"), "Prefix");
    var finishedField = AccessTools.Field(typeof(DuelResultChannel), "Finished");
    finishedField.SetValue(null, true);
    check(!(bool)gate.Invoke(null, null)!, "结算页面已显示时，对手退出不再触发竞争的原生断线错误界面");
    finishedField.SetValue(null, false);
    check((bool)gate.Invoke(null, null)!, "未结束的正常联机继续使用原生断线处理");
}

async Task RunNetworkTests(Action<bool, string> check)
{
    // Transport is in-memory; the message handlers, early-message buffers and
    // choice IDs are the installed game's actual PlayerChoiceSynchronizer.
    var logShim = new Harmony("spirepvp.tests.consolelog");
    logShim.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Logging.Logger), "GetIsRunningFromGodotEditor"),
        prefix: new HarmonyMethod(typeof(ConsoleLogShim), nameof(ConsoleLogShim.Prefix)));
    MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[MegaCrit.Sts2.Core.Logging.LogType.GameSync] = MegaCrit.Sts2.Core.Logging.LogLevel.Error;
    var hostPlayers = new PlayerList([FakePlayer(101), FakePlayer(202)]);
    var clientPlayers = new PlayerList([FakePlayer(101), FakePlayer(202)]);
    var host = DispatchProxy.Create<INetGameService, TestTransport>();
    var client = DispatchProxy.Create<INetGameService, TestTransport>();
    var h = (TestTransport)(object)host;
    var c = (TestTransport)(object)client;
    h.Id = 101; c.Id = 202; h.Peer = c; c.Peer = h;
    using var hs = new PlayerChoiceSynchronizer(host, hostPlayers);
    using var cs = new PlayerChoiceSynchronizer(client, clientPlayers);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    // Reproduce the real failure's ordering: the client finishes all ten draft
    // picks before the host. Each peer must also replay the remote callback.
    for (int i = 0; i < 10; i++)
    {
        var clientChoice = cs.ReserveChoiceId(clientPlayers.Players[1]);
        cs.SyncLocalChoice(clientPlayers.Players[1], clientChoice, PlayerChoiceResult.FromIndex(i % 3));
    }
    check(hs.ChoiceIds.ElementAtOrDefault(1) == 0 && cs.ChoiceIds[1] == 10, "客机提前完成十次选牌，消息先到时仍可缓冲");
    bool consumedPicks = true;
    for (int i = 0; i < 10; i++)
    {
        var remotePickId = hs.ReserveChoiceId(hostPlayers.Players[1]);
        var remotePick = await hs.WaitForRemoteChoice(hostPlayers.Players[1], remotePickId).WaitAsync(timeout.Token);
        consumedPicks &= remotePick.AsIndex() == i % 3;
        var hostPickId = hs.ReserveChoiceId(hostPlayers.Players[0]);
        hs.SyncLocalChoice(hostPlayers.Players[0], hostPickId, PlayerChoiceResult.FromIndex((i + 1) % 3));
        var replayId = cs.ReserveChoiceId(clientPlayers.Players[0]);
        var replay = await cs.WaitForRemoteChoice(clientPlayers.Players[0], replayId).WaitAsync(timeout.Token);
        consumedPicks &= replay.AsIndex() == (i + 1) % 3;
    }
    check(consumedPicks, "双方逐条消费全部选牌结果，不留给回合同步误读");
    check(hs.ChoiceIds.SequenceEqual(cs.ChoiceIds) && hs.ChoiceIds.All(id => id == 10),
        "构筑完成后两端双方选择编号均推进到十，保持一致");
    var clientDeckReady = RoundSynchronizer.WaitForBoth(cs, clientPlayers.Players, clientPlayers.Players[1], 0, timeout.Token);
    check(!clientDeckReady.IsCompleted, "客机先完成构筑时保持等待，不提前开放地图入口");
    var hostDeckReady = RoundSynchronizer.WaitForBoth(hs, hostPlayers.Players, hostPlayers.Players[0], 0, timeout.Token);
    await Task.WhenAll(clientDeckReady, hostDeckReady);
    check(hs.ChoiceIds.SequenceEqual(cs.ChoiceIds) && hs.ChoiceIds.All(id => id == 11),
        "构筑准备信号单独占用一致编号，双方完成后才解锁地图");
    var hostSettings = new DuelMultipliers(3.5m, 2.5m, 2m, 0m, .5m, false);
    var clientSettings = new DuelMultipliers(.5m, 10m, 10m, 10m, 10m, true);
    var clientRules = PvpVotes.HostMultipliers(cs, clientPlayers.Players, clientPlayers.Players[1], false, clientSettings, timeout.Token);
    check(!clientRules.IsCompleted, "客机的倍率设置先到时等待房主，不提前开始战斗");
    var hostRules = PvpVotes.HostMultipliers(hs, hostPlayers.Players, hostPlayers.Players[0], true, hostSettings, timeout.Token);
    await Task.WhenAll(clientRules, hostRules);
    check(hostRules.Result == hostSettings && clientRules.Result == hostSettings, "两端本地倍率不同也共同使用房主的生命与格挡倍率");
    check(hs.ChoiceIds.SequenceEqual(cs.ChoiceIds), "倍率同步之后双方选择编号一致");
    using var hostReady = new RoundReadyChannel(host, 991, 202);
    using var clientReady = new RoundReadyChannel(client, 991, 101);
    hs.ReserveChoiceId(hostPlayers.Players[0]); cs.ReserveChoiceId(clientPlayers.Players[0]);
    check(hs.ChoiceIds[0] != hs.ChoiceIds[1], "回归模拟发育后双方事件选择次数不同的状态");
    var writerReady = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter { WarnOnGrow = false };
    new RoundReadyMessage { Seed = 991, Floor = 14, Round = 2 }.Serialize(writerReady);
    var readerReady = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader(); readerReady.Reset(writerReady.Buffer);
    var decodedReady = new RoundReadyMessage(); decodedReady.Deserialize(readerReady);
    check(decodedReady.Seed == 991 && decodedReady.Floor == 14 && decodedReady.Round == 2,
        "独立回合准备消息通过游戏原生二进制编码保留对局、楼层和回合");
    var originalIds = hs.ChoiceIds.ToArray();
    var remoteIds = cs.ChoiceIds.ToArray();
    var readyFirst = hostReady.Wait(14, 1, timeout.Token);
    check(!readyFirst.IsCompleted, "独立回合准备消息先到时等待对端完成抽牌");
    await Task.WhenAll(readyFirst, clientReady.Wait(14, 1, timeout.Token));
    check(hs.ChoiceIds.SequenceEqual(originalIds) && cs.ChoiceIds.SequenceEqual(remoteIds),
        "回合准备不占用选牌、事件与开局效果的原生选择编号");
    var waitingRound = hostReady.Wait(14, 2, timeout.Token);
    client.SendMessage(new RoundReadyMessage { Seed = 992, Floor = 14, Round = 2 });
    client.SendMessage(new RoundReadyMessage { Seed = 991, Floor = 13, Round = 2 });
    client.SendMessage(new RoundReadyMessage { Seed = 991, Floor = 14, Round = 1 });
    check(!waitingRound.IsCompleted, "其他种子、楼层和旧回合的就绪信号不能解锁当前回合");
    c.Id = 303; client.SendMessage(new RoundReadyMessage { Seed = 991, Floor = 14, Round = 2 }); c.Id = 202;
    check(!waitingRound.IsCompleted, "非对手身份不能发送有效的回合就绪信号");
    await Task.WhenAll(waitingRound, clientReady.Wait(14, 2, timeout.Token));
    var preparation = typeof(ModEntry).Assembly.GetType("SpirePvp.RoundPreparation")!;
    var turn = new object(); var delayedDraw = new TaskCompletionSource();
    AccessTools.Method(preparation, "Begin").Invoke(null, [turn]);
    AccessTools.Method(preparation, "Add").Invoke(null, [turn, delayedDraw.Task]);
    var completeDraw = (Task)AccessTools.Method(preparation, "Complete").Invoke(null, [turn])!;
    check(!completeDraw.IsCompleted, "原生开局暂停等待选牌时，PvP 不提前宣告抽牌完成");
    delayedDraw.SetResult(); await completeDraw;
    check(completeDraw.IsCompletedSuccessfully, "开局选择与抽牌任务真正结束后可以发送就绪信号");
    AccessTools.Method(preparation, "Begin").Invoke(null, [turn]);
    check(((Task)AccessTools.Method(preparation, "Complete").Invoke(null, [turn])!).IsCompletedSuccessfully,
        "下一回合准备任务不继承已结束回合的任务");
    using var cancelled = new CancellationTokenSource();
    var waitingCancelled = hostReady.Wait(14, 3, cancelled.Token); cancelled.Cancel();
    try { await waitingCancelled; check(false, "退出战斗取消就绪等待"); }
    catch (OperationCanceledException) { check(true, "退出战斗取消就绪等待"); }
    var waitingDisposed = hostReady.Wait(14, 4, timeout.Token); hostReady.Dispose();
    try { await waitingDisposed; check(false, "清理对局注销消息并取消未完成等待"); }
    catch (OperationCanceledException) { check(true, "清理对局注销消息并取消未完成等待"); }
    var first = RoundSynchronizer.WaitForBoth(hs, hostPlayers.Players, hostPlayers.Players[0], 1, timeout.Token);
    check(!first.IsCompleted, "先完成抽牌的一方等待对手，不提前开放回合");
    var second = RoundSynchronizer.WaitForBoth(cs, clientPlayers.Players, clientPlayers.Players[1], 1, timeout.Token);
    await Task.WhenAll(first, second);
    check(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully, "双方准备完成后共同开放回合，提前到达的消息可被正确消费");
    var nextClient = RoundSynchronizer.WaitForBoth(cs, clientPlayers.Players, clientPlayers.Players[1], 2, timeout.Token);
    check(!nextClient.IsCompleted, "下回合不复用上一回合准备信号");
    var nextHost = RoundSynchronizer.WaitForBoth(hs, hostPlayers.Players, hostPlayers.Players[0], 2, timeout.Token);
    await Task.WhenAll(nextHost, nextClient);
    check(hs.ChoiceIds.SequenceEqual(cs.ChoiceIds), "连续回合保持双方 choice ID 一致");
    foreach (int count in new[] { 15, 20 })
    {
        bool matches = true;
        for (int i = 0; i < count; i++)
        {
            var clientId = cs.ReserveChoiceId(clientPlayers.Players[1]);
            cs.SyncLocalChoice(clientPlayers.Players[1], clientId, PlayerChoiceResult.FromIndex(i % 3));
        }
        for (int i = 0; i < count; i++)
        {
            var remoteId = hs.ReserveChoiceId(hostPlayers.Players[1]);
            matches &= (await hs.WaitForRemoteChoice(hostPlayers.Players[1], remoteId)).AsIndex() == i % 3;
            var ownId = hs.ReserveChoiceId(hostPlayers.Players[0]);
            hs.SyncLocalChoice(hostPlayers.Players[0], ownId, PlayerChoiceResult.FromIndex(i % 3));
            var received = cs.ReserveChoiceId(clientPlayers.Players[0]);
            matches &= (await cs.WaitForRemoteChoice(clientPlayers.Players[0], received)).AsIndex() == i % 3;
        }
        check(matches && hs.ChoiceIds.SequenceEqual(cs.ChoiceIds), $"{count} 次选牌的提前消息全部消费，两端计数一致");
    }
    foreach (var (hostValue, clientValue) in new[] { (true, false), (true, true), (false, true), (false, false) })
    {
        var earlyClient = PvpVotes.Exchange(cs, clientPlayers.Players, clientPlayers.Players[1],
            () => Task.FromResult(clientValue), timeout.Token);
        check(!earlyClient.IsCompleted, "幕间投票先选择的一方等待另一端");
        var lateHost = PvpVotes.Exchange(hs, hostPlayers.Players, hostPlayers.Players[0],
            () => Task.FromResult(hostValue), timeout.Token);
        await Task.WhenAll(earlyClient, lateHost);
        check(lateHost.Result.SequenceEqual(earlyClient.Result) && lateHost.Result.SequenceEqual([hostValue, clientValue]),
            $"幕间投票 {hostValue}/{clientValue} 两端一致，提前选择不导致单方进入战斗");
        check(lateHost.Result.All(v => v) == (hostValue && clientValue) && hs.ChoiceIds.SequenceEqual(cs.ChoiceIds),
            "只有双方同意才决斗；跳过后仍保持后续选择编号同步");
    }
    var votedHost = new TaskCompletionSource<bool>();
    var pendingVote = PvpVotes.Exchange(hs, hostPlayers.Players, hostPlayers.Players[0],
        () => votedHost.Task, timeout.Token);
    check(!pendingVote.IsCompleted, "尚未点击参加/跳过时不会发送虚假的本地确认");
    var votedClient = PvpVotes.Exchange(cs, clientPlayers.Players, clientPlayers.Players[1],
        () => Task.FromResult(true), timeout.Token);
    check(!votedClient.IsCompleted, "对手先确认不能绕过本方仍在等待的选择界面");
    votedHost.SetResult(false);
    await Task.WhenAll(pendingVote, votedClient);
    check(pendingVote.Result.SequenceEqual([false, true]) && votedClient.Result.SequenceEqual([false, true]),
        "延迟点击跳过后两端收到相同结果，继续正常爬塔");
    var badHost = RoundSynchronizer.WaitForBoth(hs, hostPlayers.Players, hostPlayers.Players[0], 3, timeout.Token);
    var badClient = RoundSynchronizer.WaitForBoth(cs, clientPlayers.Players, clientPlayers.Players[1], 4, timeout.Token);
    try { await Task.WhenAll(badHost, badClient); } catch (InvalidOperationException) { }
    check(badHost.IsFaulted && badClient.IsFaulted, "回合编号失配时双方拒绝继续");
    using var cancellation = new CancellationTokenSource();
    var canceled = RoundSynchronizer.WaitForBoth(hs, hostPlayers.Players, hostPlayers.Players[0], 5, cancellation.Token);
    cancellation.Cancel();
    try { await canceled; } catch (OperationCanceledException) { }
    check(canceled.IsCanceled, "退出对局可取消等待，不永久挂起");
}

void RunEyeAttackTests(CombatState arena, Action<bool, string> check)
{
    ModelDb.Init([typeof(GoForTheEyes)]);
    var player = FakePlayer(881);
    var opponent = FakePlayer(882);
    opponent.Creature.CombatState = arena;
    AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player,
        ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>());
    var shim = new Harmony("spirepvp.tests.eyes");
    var attackType = typeof(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand);
    shim.Patch(AccessTools.Method(attackType, "Execute"), prefix: new HarmonyMethod(typeof(EyeAttackRecorder), nameof(EyeAttackRecorder.Attack)));
    var apply = AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.PowerCmd), "Apply",
        [typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)]);
    shim.Patch(apply, prefix: new HarmonyMethod(typeof(EyeAttackRecorder), nameof(EyeAttackRecorder.Power)));
    try
    {
        foreach (bool upgraded in new[] { false, true })
        {
            var card = (GoForTheEyes)ModelDb.Card<GoForTheEyes>().ToMutable();
            card.Owner = player;
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            var play = (CardPlay)RuntimeHelpers.GetUninitializedObject(typeof(CardPlay));
            AccessTools.Property(typeof(CardPlay), nameof(CardPlay.Target)).SetValue(play, opponent.Creature);
            EyeAttackRecorder.Attacks = 0; EyeAttackRecorder.Powers.Clear();
            ((Task)AccessTools.Method(typeof(GoForTheEyes), "OnPlay").Invoke(card, [new BlockingPlayerChoiceContext(), play])!).GetAwaiter().GetResult();
            check(EyeAttackRecorder.Attacks == 1 && EyeAttackRecorder.Powers.Count == 1
                && EyeAttackRecorder.Powers[0] == (opponent.Creature, upgraded ? 2m : 1m, card),
                upgraded ? "敲过的眼部攻击对 PvP 玩家攻击后请求 2 层虚弱，无需意图" : "眼部攻击对 PvP 玩家攻击后请求 1 层虚弱，无需意图");
            var prefix = AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.GoForTheEyesPatch"), "Prefix");
            opponent.Creature.CombatState = null;
            check((bool)prefix.Invoke(null, [card, new BlockingPlayerChoiceContext(), play, null])!,
                "普通模式不触发眼部攻击 PvP 替代流程");
            opponent.Creature.CombatState = arena;
        }
        var monster = new Creature(ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.Osty>().ToMutable(), CombatSide.Enemy, null);
        monster.CombatState = arena;
        var nativePlay = (CardPlay)RuntimeHelpers.GetUninitializedObject(typeof(CardPlay));
        AccessTools.Property(typeof(CardPlay), nameof(CardPlay.Target)).SetValue(nativePlay, monster);
        var nativeCard = (GoForTheEyes)ModelDb.Card<GoForTheEyes>().ToMutable(); nativeCard.Owner = player;
        check((bool)AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.GoForTheEyesPatch"), "Prefix")
            .Invoke(null, [nativeCard, new BlockingPlayerChoiceContext(), nativePlay, null])!,
            "对怪物的眼部攻击保留原版意图条件");
    }
    finally { shim.UnpatchAll(shim.Id); }
}

static Player FakePlayer(ulong id)
{
    var p = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
    AccessTools.Field(typeof(Player), "<NetId>k__BackingField").SetValue(p, id);
    AccessTools.Field(typeof(Player), "<Creature>k__BackingField").SetValue(p, new Creature(p, 80, 80));
    AccessTools.Field(typeof(Player), "<Deck>k__BackingField").SetValue(p, new CardPile(PileType.Deck));
    return p;
}

sealed class PlayerList(IReadOnlyList<Player> players) : IPlayerCollection
{
    public IReadOnlyList<Player> Players => players;
    public int GetPlayerSlotIndex(Player p) => players.ToList().IndexOf(p);
    public Player? GetPlayer(ulong id) => players.FirstOrDefault(p => p.NetId == id);
}

public static class HeavyVfxRecorder
{
    public static string? Path;
    public static bool Base;
    public static bool Execute(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand __instance,
        ref Task<MegaCrit.Sts2.Core.Commands.Builders.AttackCommand> __result)
    {
        Path = __instance.HitVfx;
        Base = !(bool)AccessTools.Field(__instance.GetType(), "_spawnVfxOnCreatureCenter").GetValue(__instance)!;
        __result = Task.FromResult(__instance); return false;
    }
}

public static class DuelKillRecorder
{
    public static int Calls;
    public static bool Kill(ref Task __result) { Calls++; __result = Task.CompletedTask; return false; }
}

public class SameSeedRunFixture : DispatchProxy
{
    public IReadOnlyList<ModifierModel> Rules = [];
    public IReadOnlyList<Player> Players = [];
    public RunRngSet Rng = new("SAME-SEED-PVP");
    public int Floor = 1;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Modifiers" => Rules, "get_Players" => Players, "get_Rng" => Rng,
        "get_CurrentActIndex" => 0, "get_ActFloor" => Floor,
        "GetPlayerSlotIndex" => Players.ToList().IndexOf((Player)args![0]!),
        "get_CardMultiplayerConstraint" => CardMultiplayerConstraint.MultiplayerOnly,
        "IterateHookListeners" => Array.Empty<AbstractModel>(),
        "CreateCard" => CreateCard((CardModel)args![0]!, (Player)args[1]!),
        _ => null
    };
    private static CardModel CreateCard(CardModel canonical, Player player) { var card = canonical.ToMutable(); card.Owner = player; return card; }
}

public class TestTransport : DispatchProxy
{
    public ulong Id;
    public TestTransport? Peer;
    public int Sent, Registered, Updates, Disconnects, BufferCalls;
    public bool HoldResultAck;
    public DuelResultMessage? HeldResultAck;
    public void DeliverResultAck()
    {
        HoldResultAck = false;
        Peer!._handlers[typeof(DuelResultMessage)].DynamicInvoke(HeldResultAck, Id);
        HeldResultAck = null;
    }
    private readonly Dictionary<Type, Delegate> _handlers = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "get_NetId": return Id;
            case "get_Type": return Id == 101 ? NetGameType.Host : NetGameType.Client;
            case "get_IsConnected": return true;
            case "RegisterMessageHandler": Registered++; _handlers[method.GetGenericArguments()[0]] = (Delegate)args![0]!; return null;
            case "UnregisterMessageHandler": _handlers.Remove(method.GetGenericArguments()[0]); return null;
            case "SendMessage":
                Sent++;
                if (HoldResultAck && args![0] is DuelResultMessage { Kind: 2 } resultAck) { HeldResultAck = resultAck; return null; }
                Peer!._handlers[method.GetGenericArguments()[0]].DynamicInvoke(args![0], Id); return null;
            case "Update": Updates++; return null;
            case "Disconnect": Disconnects++; return null;
            case "SetBufferMessages": BufferCalls++; return null;
            default: return null;
        }
    }
}

public class DamageRunFixture : DispatchProxy
{
    public IEnumerable<AbstractModel> Listeners = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method?.Name == "IterateHookListeners" ? Listeners : null;
}

public static class ConsoleLogShim
{
    public static bool Prefix(ref bool __result) { __result = false; return false; }
}

public class BlockCombatFixture : DispatchProxy
{
    public IEnumerable<AbstractModel> Listeners = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method?.Name == "IterateHookListeners" ? Listeners : null;
}

public static class EffectDamageRecorder
{
    public static readonly List<(Creature,decimal)> Hits = [];
    public static bool Prefix(IEnumerable<Creature> targets, decimal amount, ref Task<IEnumerable<DamageResult>> __result)
    {
        Hits.AddRange(targets.Select(target => (target,amount)));
        __result = Task.FromResult<IEnumerable<DamageResult>>([]);
        return false;
    }
}

public static class EyeAttackRecorder
{
    public static int Attacks;
    public static readonly List<(Creature,decimal,CardModel)> Powers = [];
    public static bool Attack(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand __instance,
        ref Task<MegaCrit.Sts2.Core.Commands.Builders.AttackCommand> __result)
    {
        Attacks++;
        __result = Task.FromResult(__instance);
        return false;
    }
    public static bool Power(PowerModel power, Creature target, decimal amount, CardModel cardSource, ref Task __result)
    {
        if (power is not MegaCrit.Sts2.Core.Models.Powers.WeakPower || cardSource is not GoForTheEyes) return true;
        Powers.Add((target, amount, cardSource)); __result = Task.CompletedTask; return false;
    }
}

public class CardGenerationRunFixture : DispatchProxy
{
    public bool Pvp;
    public IReadOnlyList<Player> Players = [];
    public MegaCrit.Sts2.Core.Rooms.CombatRoom? Room;
    public int ActIndex;
    public RelicGrabBag SharedBag = new();
    public IReadOnlyList<ModifierModel>? Rules;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Players" => Players,
        "get_CurrentRoom" => Room,
        "get_CurrentActIndex" => ActIndex,
        "get_SharedRelicGrabBag" => SharedBag,
        "IterateHookListeners" => Array.Empty<AbstractModel>(),
        "get_Modifiers" => Rules ?? (Pvp ? new List<ModifierModel> { ModelDb.Modifier<PvpDuel>() } : new List<ModifierModel>()),
        "get_CardMultiplayerConstraint" => CardMultiplayerConstraint.MultiplayerOnly,
        _ => null
    };
}

public static class DraftRewardRecorder
{
    public static readonly List<MegaCrit.Sts2.Core.Rewards.CardReward> Rewards = [];
    public static bool Populate() => false;
    public static bool Select(MegaCrit.Sts2.Core.Rewards.Reward __instance, ref Task<bool> __result)
    {
        Rewards.Add((MegaCrit.Sts2.Core.Rewards.CardReward)__instance);
        __result = Task.FromResult(true);
        return false;
    }
}



public class WhitelistConditionFixture : DispatchProxy
{
    public IReadOnlyList<Player> Players = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Players" => Players,
        "get_CurrentActIndex" => 0,
        _ => null
    };
}

public class LimitCombatFixture : DispatchProxy
{
    public int Round;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_RoundNumber" => Round,
        "get_CurrentSide" => CombatSide.Player,
        _ => null
    };
}

public class DeferredRunFixture : DispatchProxy
{
    public IReadOnlyList<ModifierModel> Rules = [];
    public IEnumerable<AbstractModel> Listeners = [];
    public Func<IEnumerable<AbstractModel>>? CurrentListeners;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Modifiers" => Rules,
        "IterateHookListeners" => CurrentListeners?.Invoke() ?? Listeners,
        _ => null
    };
}
public class DeferredCombatFixture : DispatchProxy
{
    public IRunState Run = null!;
    public EncounterModel? Encounter;
    public IReadOnlyList<Creature> Creatures = [];
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_CurrentSide" => CombatSide.Player,
        "get_RunState" => Run,
        "get_Creatures" => Creatures,
        "GetOpponentsOf" => Creatures.Where(c => c != (Creature)args![0]!).ToArray(),
        "get_Encounter" => Encounter,
        "IterateHookListeners" => Run.IterateHookListeners((ICombatState)(object)this).ToArray(),
        _ => null
    };
}

public static class ImmediateStatusRecorder
{
    public static int Applied;
    public static bool Apply(PowerModel power, Creature target, decimal amount, ref Task __result)
    {
        if (power is MegaCrit.Sts2.Core.Models.Powers.DoomPower)
        { DeferredBlightRecorder.Doom += amount; __result = Task.CompletedTask; return false; }
        if (power is not MegaCrit.Sts2.Core.Models.Powers.PoisonPower) return true;
        Applied++;
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Owner)).Invoke(power, [target]);
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(power, [(int)amount]);
        ((List<PowerModel>)AccessTools.Field(typeof(Creature), "_powers").GetValue(target)!).Add(power);
        __result = Task.CompletedTask; return false;
    }
    public static bool Decrement(PowerModel __0, ref Task __result)
    {
        AccessTools.PropertySetter(typeof(PowerModel), nameof(PowerModel.Amount)).Invoke(__0, [__0.Amount - 1]);
        __result = Task.CompletedTask; return false;
    }
}

public static class RuptureStrengthRecorder
{
    public static decimal Strength;
    public static bool Apply(PowerModel power, decimal amount, ref Task __result)
    {
        if (power is not MegaCrit.Sts2.Core.Models.Powers.StrengthPower) return true;
        Strength += amount; __result = Task.CompletedTask; return false;
    }
}

public static class DeferredBlightRecorder
{
    public static decimal Doom;
    public static bool Execute(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand __instance,
        PlayerChoiceContext choiceContext, ref Task<MegaCrit.Sts2.Core.Commands.Builders.AttackCommand> __result)
    {
        var target = (Creature)AccessTools.Field(__instance.GetType(), "_singleTarget").GetValue(__instance)!;
        var amount = (decimal)AccessTools.Field(__instance.GetType(), "_damagePerHit").GetValue(__instance)!;
        var card = (CardModel)__instance.ModelSource!;
        __instance.AddResultsInternal(MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choiceContext, target, amount,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Move, __instance.Attacker, card, null).GetAwaiter().GetResult());
        __result = Task.FromResult(__instance); return false;
    }
}

public static class CustomSealedRecorder
{
    public static readonly List<(List<CardCreationResult> Cards, MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs Prefs)> Batches = [];
    public static readonly List<CardModel> Added = [];
    public static readonly List<int> GenerationCounts = [];
    public static readonly List<CardCreationOptions> GenerationOptions = [];
    public static TaskCompletionSource<IEnumerable<CardModel>>? Pending;
    public static void Reset() { Batches.Clear(); Added.Clear(); GenerationCounts.Clear(); GenerationOptions.Clear(); }
    public static bool Pool(CardCreationOptions __instance, Player player, ref IEnumerable<CardModel> __result)
    {
        if (player.NetId != 908) return true;
        __result = new CardModel[] { ModelDb.Card<ShrugItOff>(), ModelDb.Card<BodySlam>(), ModelDb.Card<IronWave>(),
            ModelDb.Card<AshenStrike>(), ModelDb.Card<FlameBarrier>(), ModelDb.Card<Uppercut>(),
            ModelDb.Card<DemonForm>(), ModelDb.Card<Offering>(), ModelDb.Card<Impervious>() }
            .Where(c => __instance.CardPoolFilter?.Invoke(c) != false).ToArray();
        return false;
    }
    public static bool Generate(Player player, int cardCount, CardCreationOptions options, ref IEnumerable<CardCreationResult> __result)
    {
        if (player.NetId != 908) return true;
        GenerationCounts.Add(cardCount); GenerationOptions.Add(options);
        var canonical = options.GetPossibleCards(player).Take(cardCount).ToArray();
        __result = canonical.Select(model => { var c = model.ToMutable(); c.Owner = player; return new CardCreationResult(c); }).ToList();
        return false;
    }
    public static bool Select(List<CardCreationResult> cards, MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    { Batches.Add((cards, prefs)); __result = Pending?.Task ?? Task.FromResult(cards.Take(prefs.MinSelect).Select(c => c.Card)); return false; }
    public static bool Add(IEnumerable<CardModel> cards, ref Task<IReadOnlyList<CardPileAddResult>> __result)
    { Added.AddRange(cards); __result = Task.FromResult<IReadOnlyList<CardPileAddResult>>([]); return false; }
    public static bool Preview() => false;
}

public static class DeferredSummonRecorder
{
    public static decimal Amount;
    public static bool Summon(decimal amount, ref Task<SummonResult> __result)
    { Amount += amount; __result = Task.FromResult(new SummonResult(null!, amount)); return false; }
}

public sealed class DeferredAttackProbePower : PowerModel
{
    public override MegaCrit.Sts2.Core.Entities.Powers.PowerType Type => MegaCrit.Sts2.Core.Entities.Powers.PowerType.Buff;
    public override MegaCrit.Sts2.Core.Entities.Powers.PowerStackType StackType => MegaCrit.Sts2.Core.Entities.Powers.PowerStackType.Counter;
    public int Count;
    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result,
        MegaCrit.Sts2.Core.ValueProps.ValueProp props, Creature target, CardModel? cardSource)
    {
        if (dealer != Owner || props.HasFlag(MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered)) return;
        Count++;
        await MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choiceContext, target, 2,
            MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, dealer, null, null);
    }
}
