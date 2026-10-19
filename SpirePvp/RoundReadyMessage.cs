using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

public sealed class RoundReadyMessage : INetMessage
{
    public ulong Seed;
    public int Floor;
    public int Round;
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public void Serialize(PacketWriter writer) { writer.WriteULong(Seed); writer.WriteInt(Floor); writer.WriteInt(Round); }
    public void Deserialize(PacketReader reader) { Seed = reader.ReadULong(); Floor = reader.ReadInt(); Round = reader.ReadInt(); }
}

// Readiness never reserves a native card/event choice ID. Keep early packets
// from the peer that finishes drawing first until the local setup is complete.
public sealed class RoundReadyChannel : IDisposable
{
    private readonly INetGameService _service;
    private readonly ulong _seed, _remote;
    private readonly Dictionary<(int Floor, int Round), TaskCompletionSource> _signals = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    internal static RoundReadyChannel? Current;
    public RoundReadyChannel(INetGameService service, ulong seed, ulong remote)
    {
        _service = service; _seed = seed; _remote = remote;
        service.RegisterMessageHandler<RoundReadyMessage>(Receive);
    }
    private TaskCompletionSource Signal(int floor, int round)
    {
        if (!_signals.TryGetValue((floor, round), out var signal))
            _signals[(floor, round)] = signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return signal;
    }
    private void Receive(RoundReadyMessage message, ulong sender)
    {
        if (sender != _remote || message.Seed != _seed || message.Floor < 0 || message.Round < 1) return;
        Signal(message.Floor, message.Round).TrySetResult();
    }
    public async Task Wait(int floor, int round, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        var signal = Signal(floor, round);
        Log.Info($"[SpirePvp] 回合准备：楼层 {floor}，回合 {round}，本方抽牌完成。");
        _service.SendMessage(new RoundReadyMessage { Seed = _seed, Floor = floor, Round = round });
        await signal.Task.WaitAsync(linked.Token);
        Log.Info($"[SpirePvp] 回合准备：楼层 {floor}，回合 {round}，双方就绪。");
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _service.UnregisterMessageHandler<RoundReadyMessage>(Receive);
        _lifetime.Dispose();
    }
    internal static void Reset() { Current?.Dispose(); Current = null; }
    internal static void Register(RunManager manager)
    {
        var run = PvpSession.GetState(manager);
        if (!PvpSession.IsPvp(run) || run!.Players.Count != 2) return;
        Reset();
        Current = new(manager.NetService, run.Rng.Seed, run.Players.Single(p => p.NetId != manager.NetService.NetId).NetId);
    }
}

[HarmonyPatch(typeof(RunManager), "InitializeShared")]
internal static class RegisterRoundReadyPatch
{
    static void Postfix(RunManager __instance)
    { RoundReadyChannel.Register(__instance); DuelResultChannel.Register(__instance); }
}
