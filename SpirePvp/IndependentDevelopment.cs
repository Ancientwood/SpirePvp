using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpirePvp;

internal sealed class IndependentDevelopment : IDisposable
{
    internal static IndependentDevelopment? Current;
    internal static bool Transitioning;
    private static readonly ConditionalWeakTable<NRun, object> RetiringRuns = new();
    private readonly INetGameService _connection;
    private readonly ulong[] _ids;
    private readonly ulong _seed;
    internal CharacterModel OpponentCharacter { get; }
    internal int OpponentRow { get; private set; } = -1;
    private int _lastSentRow = -2;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _hello = NewSignal();
    private readonly TaskCompletionSource<SerializableRun> _merged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _go = NewSignal();
    private DevelopmentRendezvous? _rendezvous;
    private SerializableRun? _sharedAtArrival;
    private DevelopmentMessage? _earlyReady;
    private bool _disposed, _started, _arriving, _sentMerge, _sentGo;
    internal bool Waiting => _arriving && !InDuel;
    internal bool Rejoining { get; private set; }
    internal bool InDuel { get; private set; }
    internal bool Developing => _started && !Rejoining && !InDuel;
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal IndependentDevelopment(INetGameService connection, RunState run)
    {
        _connection = connection;
        _ids = run.Players.Select(p => p.NetId).ToArray();
        _seed = run.Rng.Seed;
        OpponentCharacter = run.Players.Single(p => p.NetId != connection.NetId).Character;
        connection.RegisterMessageHandler<DevelopmentMessage>(Receive);
        connection.Disconnected += Disconnected;
        if (connection is INetHostGameService host) host.ClientDisconnected += PeerDisconnected;
    }
    internal static void Ensure(RunManager manager)
    {
        var run = PvpSession.GetState(manager);
        if (Current == null && run?.Players.Count == 2
            && run.Modifiers.OfType<PvpDuel>().SingleOrDefault() is { Development: true, CooperativeDevelopment: false })
            Current = new IndependentDevelopment(manager.NetService, run);
    }
    internal async Task<RunState> Begin(RunManager manager)
    {
        _rendezvous = new DevelopmentRendezvous(manager.ToSave(null));
        if (_earlyReady != null) _rendezvous.AddReady(_earlyReady.Player!.NetId, _earlyReady.Player, _earlyReady.Run);
        Send(new() { Kind = DevelopmentMessageKind.Hello });
        await _hello.Task.WaitAsync(_lifetime.Token);
        _started = true;
        return await ReplaceRun(manager, _rendezvous.Solo(_connection.NetId), new DevelopmentNetService(_connection));
    }
    private void Send(DevelopmentMessage message)
    {
        message.Seed = _seed;
        _connection.SendMessage(message);
    }
    private void Receive(DevelopmentMessage message, ulong sender)
    {
        if (_disposed || message.Seed != _seed || sender == _connection.NetId || !_ids.Contains(sender)) return;
        try
        {
            switch (message.Kind)
            {
                case DevelopmentMessageKind.Progress:
                    if (message.ProgressRow < -1 || message.ProgressRow > PvpDevelopmentMap.DevelopmentFloors)
                        throw new InvalidDataException("无效的发育地图位置。");
                    OpponentRow = Math.Max(OpponentRow, message.ProgressRow);
                    break;
                case DevelopmentMessageKind.Hello:
                    _hello.TrySetResult();
                    break;
                case DevelopmentMessageKind.PlayerReady:
                    if (message.Player == null || message.Player.NetId != sender) throw new InvalidDataException("对手的发育数据身份不一致。");
                    if (_rendezvous == null) _earlyReady ??= message;
                    else _rendezvous.AddReady(sender, message.Player, message.Run);
                    TryMerge();
                    break;
                case DevelopmentMessageKind.Merge:
                    if (_connection.Type != NetGameType.Client || !_arriving || message.Run == null
                        || !message.Run.Players.Select(p => p.NetId).SequenceEqual(_ids))
                        throw new InvalidDataException("收到不符合当前对局的合并数据。");
                    // Only the connected host sends this packet. Keep its shared
                    // RNG, act data and player ordering on both peers.
                    _merged.TrySetResult(message.Run);
                    break;
                case DevelopmentMessageKind.Prepared:
                    if (_connection.Type != NetGameType.Host || !_sentMerge) throw new InvalidDataException("决斗准备时机不正确。");
                    _rendezvous!.AddPrepared(sender);
                    TryGo();
                    break;
                case DevelopmentMessageKind.Go:
                    if (_connection.Type != NetGameType.Client || !Rejoining) throw new InvalidDataException("决斗尚未重建。");
                    _go.TrySetResult();
                    break;
            }
        }
        catch (Exception error) { Fail("发育数据同步失败：" + error.Message); }
    }
    private void TryMerge()
    {
        if (_connection.Type != NetGameType.Host || _sentMerge || _rendezvous?.BothReady != true) return;
        _sentMerge = true;
        var merged = _rendezvous.Merge(_sharedAtArrival);
        Send(new() { Kind = DevelopmentMessageKind.Merge, Run = merged });
        _merged.TrySetResult(merged);
    }
    private void TryGo()
    {
        if (_sentGo || _rendezvous?.BothPrepared != true) return;
        _sentGo = true;
        Send(new() { Kind = DevelopmentMessageKind.Go });
        _go.TrySetResult();
    }
    internal void Arrive()
    {
        if (_arriving || !Developing) return;
        var run = PvpSession.GetState(RunManager.Instance)!;
        if (run.Map is not PvpDevelopmentMap map || map.GetNextPoint(run.VisitedMapCoords) != map.BossMapPoint) return;
        _arriving = true;
        SendProgress();
        NMapScreen.Instance?.SetTravelEnabled(false);
        if (NMapScreen.Instance is { } screen) DuelMapView.UpdateStatus(screen);
        TaskHelper.RunSafely(Reunite(run));
    }
    private async Task Reunite(RunState solo)
    {
        try
        {
            var player = LocalContext.GetMe(solo)!.ToSerializable();
            _sharedAtArrival = RunManager.Instance.ToSave(null);
            _rendezvous!.AddReady(_connection.NetId, player, _sharedAtArrival);
            Send(new() { Kind = DevelopmentMessageKind.PlayerReady, Player = player, Run = _sharedAtArrival });
            TryMerge();
            var merged = await _merged.Task.WaitAsync(_lifetime.Token);
            Rejoining = true;
            PvpSession.SelectingDecks = true; // Keep the new waiting map locked.
            PvpSession.LocalDeckReady = true;
            var joined = await ReplaceRun(RunManager.Instance, merged, _connection);
            // Restore positions only after native LoadRun has opened the map;
            // setting them earlier would replay the last personal rest site.
            // Both arrivals have completed the same seeded twelve-floor route.
            if (joined.Map is not PvpDevelopmentMap route)
                throw new InvalidDataException("会合后的发育地图缺失。");
            foreach (var point in route.GetAllMapPoints().OrderBy(p => p.coord.row))
                joined.AddVisitedMapCoord(point.coord);
            NMapScreen.Instance?.SetTravelEnabled(false);
            if (_connection.Type == NetGameType.Host)
            {
                _rendezvous.AddPrepared(_connection.NetId);
                TryGo();
            }
            else Send(new() { Kind = DevelopmentMessageKind.Prepared });
            await _go.Task.WaitAsync(_lifetime.Token);
            InDuel = true;
            PvpSession.SelectingDecks = false;
            NMapScreen.Instance?.Close(animateOut: false);
            await RunManager.Instance.FadeOut();
            await RunManager.Instance.EnterMapCoord(PvpSession.GetState(RunManager.Instance)!.Map!.BossMapPoint.coord);
            await RunManager.Instance.FadeIn(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Fail("无法进入决斗：" + error.Message); }
    }
    private async Task<RunState> ReplaceRun(RunManager manager, SerializableRun save, INetGameService service)
    {
        Transitioning = true;
        try
        {
            if (NRun.Instance is { } old) RetiringRuns.GetValue(old, _ => new object());
            // These native services register handlers but native CleanUp does
            // not dispose them. Retire them before creating another run.
            foreach (var name in new[] { "EventSynchronizer", "OneOffSynchronizer", "MapSelectionSynchronizer",
                "ActChangeSynchronizer", "TreasureRoomRelicSynchronizer", "InputSynchronizer", "HoveredModelTracker" })
                if (AccessTools.Property(typeof(RunManager), name)?.GetValue(manager) is IDisposable disposable) disposable.Dispose();
            // Cleanup may release the run, but must preserve the Steam/ENet link.
            AccessTools.PropertySetter(typeof(RunManager), nameof(RunManager.NetService)).Invoke(manager, [new DevelopmentNetService(_connection)]);
            manager.CleanUp();
            AccessTools.PropertySetter(typeof(RunManager), nameof(RunManager.RunLobby)).Invoke(manager, [null]);
            var run = RunState.FromSerializable(save);
            AccessTools.PropertySetter(typeof(RunManager), "State").Invoke(manager, [run]);
            AccessTools.Method(typeof(RunManager), "InitializeShared").Invoke(manager,
                [service, new PeerInputSynchronizer(service), false, null, save.StartTime, save.RunTime, save.WinTime, save.NumReloads]);
            AccessTools.Method(typeof(RunManager), "InitializeRunLobby").Invoke(manager,
                [service, run, run.Players.Select(p => new RunLobbyPlayer { id = p.NetId, isModded = true }).ToArray()]);
            AccessTools.Method(typeof(RunManager), "InitializeSavedRun").Invoke(manager, [save]);
            if (service.Type == NetGameType.Singleplayer)
            {
                // The initial multiplayer act was generated using combined
                // unlocks. Rebuild its room pools for this player's unlocks,
                // without reapplying starting relics or rebuilding the deck.
                var roomSet = AccessTools.Field(typeof(ActModel), "_rooms");
                foreach (var act in run.Acts) roomSet.SetValue(act, Activator.CreateInstance(roomSet.FieldType));
                var relicBag = new RelicGrabBag(refreshAllowed: true);
                relicBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(run.UnlockState), run.Rng.UpFront);
                AccessTools.PropertySetter(typeof(RunState), nameof(RunState.SharedRelicGrabBag)).Invoke(run, [relicBag]);
                AccessTools.Method(typeof(RunManager), "GenerateRooms").Invoke(manager, null);
            }
            await NGame.Instance!.LoadRun(run, null);
            return run;
        }
        finally { Transitioning = false; }
    }
    internal void SendProgress()
    {
        if (!Developing || Transitioning) return;
        var run = PvpSession.GetState(RunManager.Instance);
        if (run?.Map is not PvpDevelopmentMap) return;
        var row = _arriving ? PvpDevelopmentMap.DevelopmentFloors
            : run.VisitedMapCoords.Count == 0 ? -1 : run.VisitedMapCoords.Last().row;
        if (row == _lastSentRow) return;
        _lastSentRow = row;
        Send(new() { Kind = DevelopmentMessageKind.Progress, ProgressRow = row });
    }
    private void Disconnected(NetErrorInfo _) => Fail("对手已离线，无法继续这场决斗。");
    private void PeerDisconnected(ulong id, NetErrorInfo _) { if (_ids.Contains(id)) Disconnected(_); }
    private void Fail(string reason)
    {
        if (_disposed || _lifetime.IsCancellationRequested) return;
        _lifetime.Cancel();
        PvpUi.ShowDevelopmentFailure(reason);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _connection.UnregisterMessageHandler<DevelopmentMessage>(Receive);
        _connection.Disconnected -= Disconnected;
        if (_connection is INetHostGameService host) host.ClientDisconnected -= PeerDisconnected;
    }
    internal static void Reset()
    {
        if (Transitioning) return;
        Current?.Dispose();
        Current = null;
    }
    internal static bool IsRetiring(NRun run, int notification) => notification == 1006 && RetiringRuns.Remove(run);
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
internal static class RegisterDevelopmentConnectionPatch
{
    static void Postfix(RunManager __instance) => IndependentDevelopment.Ensure(__instance);
}

[HarmonyPatch(typeof(NRun), nameof(NRun._Notification))]
internal static class RetiredDevelopmentScenePatch
{
    static bool Prefix(NRun __instance, int what) => !IndependentDevelopment.IsRetiring(__instance, what);
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.OnMapPointSelectedLocally))]
internal static class DevelopmentDuelArrivalPatch
{
    static bool Prefix(NMapPoint point)
    {
        if (IndependentDevelopment.Current?.Developing != true || point.Point.PointType != MapPointType.Boss) return true;
        IndependentDevelopment.Current.Arrive();
        return false;
    }
}
