using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Platform;

namespace SpirePvp;

// Native singleplayer synchronizers must not emit room, choice or action
// packets. The original connection is still pumped for rendezvous/heartbeats.
public sealed class DevelopmentNetService(INetGameService connection) : INetGameService
{
    private bool _loading;
    public ulong NetId => connection.NetId;
    public bool IsConnected => connection.IsConnected;
    public bool IsGameLoading => _loading;
    public NetGameType Type => NetGameType.Singleplayer;
    public PlatformType Platform => connection.Platform;
    public PeerVersionInfo LocalVersion => connection.LocalVersion;
    public event Action<NetErrorInfo>? Disconnected { add => connection.Disconnected += value; remove => connection.Disconnected -= value; }
    public void SendMessage<T>(T message, ulong playerId) where T : INetMessage { }
    public void SendMessage<T>(T message) where T : INetMessage { }
    public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage { }
    public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage { }
    public void Update()
    {
        connection.Update();
        IndependentDevelopment.Current?.SendProgress();
    }
    public void Disconnect(NetError reason, bool now = false)
    {
        if (!IndependentDevelopment.Transitioning) connection.Disconnect(reason, now);
    }
    public ConnectionStats? GetStatsForPeer(ulong peerId) => connection.GetStatsForPeer(peerId);
    public void SetGameLoading(bool isLoading) => _loading = isLoading;
    public void SetBufferMessages(bool bufferMessages) { }
    public string? GetRawLobbyIdentifier() => connection.GetRawLobbyIdentifier();
}
