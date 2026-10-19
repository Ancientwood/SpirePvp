using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SpirePvp;

public enum DevelopmentMessageKind : byte { Hello, PlayerReady, Merge, Prepared, Go, Progress }

// Discovered by the game's native MessageTypes.Initialize, like other mod messages.
public sealed class DevelopmentMessage : INetMessage
{
    public ulong Seed;
    public DevelopmentMessageKind Kind;
    public SerializablePlayer? Player;
    public SerializableRun? Run;
    public int ProgressRow = -1;
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(Seed);
        writer.WriteByte((byte)Kind);
        if (Kind == DevelopmentMessageKind.PlayerReady) writer.Write(Player ?? throw new InvalidOperationException("缺少个人发育数据。"));
        if (Kind == DevelopmentMessageKind.PlayerReady) { writer.WriteBool(Run != null); if (Run != null) writer.Write(Run); }
        if (Kind == DevelopmentMessageKind.Progress) writer.WriteInt(ProgressRow);
        if (Kind == DevelopmentMessageKind.Merge) writer.Write(Run ?? throw new InvalidOperationException("缺少合并对局数据。"));
    }
    public void Deserialize(PacketReader reader)
    {
        Seed = reader.ReadULong();
        Kind = (DevelopmentMessageKind)reader.ReadByte();
        if (!Enum.IsDefined(Kind)) throw new InvalidDataException("未知发育同步消息。");
        if (Kind == DevelopmentMessageKind.PlayerReady) Player = reader.Read<SerializablePlayer>();
        if (Kind == DevelopmentMessageKind.PlayerReady && reader.ReadBool()) Run = reader.Read<SerializableRun>();
        if (Kind == DevelopmentMessageKind.Progress) ProgressRow = reader.ReadInt();
        if (Kind == DevelopmentMessageKind.Merge) Run = reader.Read<SerializableRun>();
    }
}
