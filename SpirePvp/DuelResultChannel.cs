using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

public sealed class DuelResultMessage : INetMessage
{
    public ulong Seed, Winner;
    public int Floor, Round;
    public byte Kind; // 0 ready, 1 authoritative result, 2 acknowledgement
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public void Serialize(PacketWriter writer)
    { writer.WriteULong(Seed); writer.WriteInt(Floor); writer.WriteInt(Round); writer.WriteByte(Kind); writer.WriteULong(Winner); }
    public void Deserialize(PacketReader reader)
    { Seed = reader.ReadULong(); Floor = reader.ReadInt(); Round = reader.ReadInt(); Kind = reader.ReadByte(); Winner = reader.ReadULong(); }
}

// Results use stable player IDs, never a client's player order or a cleared LocalContext.
public sealed class DuelResultChannel : IDisposable
{
    private sealed class Signals
    {
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<ulong> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Ack = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Published;
        public ulong Winner;
    }
    private readonly INetGameService _service;
    private readonly ulong _seed, _remote, _local;
    private readonly Dictionary<(int Floor, int Round), Signals> _signals = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public ulong LocalId => _local;
    internal static DuelResultChannel? Current;
    internal static ulong? FinishedLocalId;
    internal static bool Finished;
    public DuelResultChannel(INetGameService service, ulong seed, ulong remote)
    {
        _service = service; _seed = seed; _remote = remote; _local = service.NetId;
        service.RegisterMessageHandler<DuelResultMessage>(Receive);
        service.Disconnected += Disconnected;
        if (service is INetHostGameService host) host.ClientDisconnected += PeerDisconnected;
    }
    private Signals Signal(int floor, int round)
    {
        if (!_signals.TryGetValue((floor, round), out var signals)) _signals[(floor, round)] = signals = new();
        return signals;
    }
    private void Receive(DuelResultMessage message, ulong sender)
    {
        if (sender != _remote || message.Seed != _seed || message.Floor < 0 || message.Round < 0) return;
        var signal = Signal(message.Floor, message.Round);
        if (message.Kind == 0) signal.Ready.TrySetResult();
        else if (message.Kind == 1 && _service.Type == NetGameType.Client
            && (message.Winner == 0 || message.Winner == LocalId || message.Winner == _remote)) signal.Result.TrySetResult(message.Winner);
        else if (message.Kind == 2 && _service.Type == NetGameType.Host && signal.Published
            && message.Winner == signal.Winner) signal.Ack.TrySetResult();
    }
    private void Send(int floor, int round, byte kind, ulong winner = 0) => _service.SendMessage(
        new DuelResultMessage { Seed = _seed, Floor = floor, Round = round, Kind = kind, Winner = winner });
    public async Task<ulong?> Finish(int floor, int round, ulong? localWinner, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        var signal = Signal(floor, round);
        Send(floor, round, 0);
        if (_service.Type == NetGameType.Host)
        {
            await signal.Ready.Task.WaitAsync(linked.Token);
            if (localWinner != null && localWinner != LocalId && localWinner != _remote)
                throw new InvalidOperationException("决斗胜者不是当前房间的玩家。");
            signal.Published = true;
            signal.Winner = localWinner ?? 0;
            Send(floor, round, 1, localWinner ?? 0);
            await signal.Ack.Task.WaitAsync(linked.Token);
            return localWinner;
        }
        var winner = await signal.Result.Task.WaitAsync(linked.Token);
        Send(floor, round, 2, winner);
        return winner == 0 ? null : winner;
    }
    private void Disconnected(NetErrorInfo _) { if (!_disposed) _lifetime.Cancel(); }
    private void PeerDisconnected(ulong id, NetErrorInfo _) { if (id == _remote) Disconnected(_); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _service.UnregisterMessageHandler<DuelResultMessage>(Receive);
        _service.Disconnected -= Disconnected;
        if (_service is INetHostGameService host) host.ClientDisconnected -= PeerDisconnected;
        _lifetime.Dispose();
    }
    internal static void Reset()
    { Current?.Dispose(); Current = null; Finished = false; FinishedLocalId = null; }
    internal static void Register(RunManager manager)
    {
        var run = PvpSession.GetState(manager);
        if (!PvpSession.IsPvp(run) || run!.Players.Count != 2) return;
        Reset();
        if (manager.NetService.Type is NetGameType.Host or NetGameType.Client)
            Current = new(manager.NetService, run.Rng.Seed, run.Players.Single(p => p.NetId != manager.NetService.NetId).NetId);
    }
}
