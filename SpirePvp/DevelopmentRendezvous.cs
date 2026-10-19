using System.Text.Json;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SpirePvp;

// Separate from UI and RunManager so early arrivals and authoritative merging
// can be exercised using the actual native save format.
public sealed class DevelopmentRendezvous
{
    private readonly SerializableRun _baseline;
    private readonly ulong[] _ids;
    private readonly Dictionary<ulong, SerializablePlayer> _ready = new();
    private readonly Dictionary<ulong, SerializableRun> _histories = new();
    private readonly HashSet<ulong> _prepared = new();
    public DevelopmentRendezvous(SerializableRun baseline)
    {
        _baseline = Clone(baseline);
        _ids = _baseline.Players.Select(p => p.NetId).ToArray();
        if (_ids.Length != 2 || _ids.Distinct().Count() != 2) throw new InvalidDataException("独立发育需要两名不同玩家。");
    }
    public bool BothReady => _ids.All(_ready.ContainsKey);
    public bool BothPrepared => BothReady && _ids.All(_prepared.Contains);
    public bool AddReady(ulong sender, SerializablePlayer player, SerializableRun? history = null)
    {
        if (!_ids.Contains(sender) || player.NetId != sender) throw new InvalidDataException("发育数据玩家身份不一致。");
        var original = _baseline.Players.Single(p => p.NetId == sender);
        if (player.CharacterId != original.CharacterId || player.MaxHp <= 0 || player.CurrentHp <= 0
            || player.CurrentHp > player.MaxHp || player.Gold < 0)
            throw new InvalidDataException("无效的发育角色或生命数据。");
        // Readiness is terminal: repeated packets cannot overwrite a locked deck.
        if (_ready.ContainsKey(sender)) return false;
        if (history != null)
        {
            if (history.Players.Count != 1 || history.Players[0].NetId != sender)
                throw new InvalidDataException("发育历史玩家身份不一致。");
            _histories.Add(sender, Clone(history));
        }
        _ready.Add(sender, player);
        return true;
    }
    public void AddPrepared(ulong sender)
    {
        if (!_ids.Contains(sender) || !BothReady) throw new InvalidDataException("尚未完成发育，不能开始决斗。");
        _prepared.Add(sender);
    }
    public SerializableRun Merge(SerializableRun? sharedState = null)
    {
        if (!BothReady) throw new InvalidOperationException("仍有玩家未完成发育。");
        // The host's developed shared state supplies canonical RNG/history;
        // personal inventories always come from their respective owners.
        var result = Clone(sharedState ?? _baseline);
        result.Players = _ids.Select(id => _ready[id]).ToList();
        for (int act = 0; act < result.MapPointHistory.Count; act++)
            for (int floor = 0; floor < result.MapPointHistory[act].Count; floor++)
            {
                var entry = result.MapPointHistory[act][floor];
                entry.PlayerStats = _ids.Select(id =>
                {
                    var source = _histories.GetValueOrDefault(id);
                    var stats = source != null && act < source.MapPointHistory.Count && floor < source.MapPointHistory[act].Count
                        ? source.MapPointHistory[act][floor].PlayerStats.FirstOrDefault(p => p.PlayerId == id) : null;
                    return stats ?? entry.PlayerStats.FirstOrDefault(p => p.PlayerId == id)
                        ?? new MegaCrit.Sts2.Core.Runs.PlayerMapPointHistoryEntry { PlayerId = id,
                            CurrentHp = _ready[id].CurrentHp, MaxHp = _ready[id].MaxHp, CurrentGold = _ready[id].Gold };
                }).ToList();
            }
        result.VisitedMapCoords.Clear();
        result.PreFinishedRoom = null;
        result.MapDrawings = null;
        return Clone(result);
    }
    public SerializableRun Solo(ulong id)
    {
        if (!_ids.Contains(id)) throw new InvalidDataException("本地玩家不在对局中。");
        var result = Clone(_baseline);
        result.Players = [result.Players.Single(p => p.NetId == id)];
        result.VisitedMapCoords.Clear();
        result.PreFinishedRoom = null;
        result.MapDrawings = null;
        return result;
    }
    public static SerializableRun Clone(SerializableRun save) => JsonSerializer.Deserialize<SerializableRun>(
        JsonSerializationUtility.ToJson(save), JsonSerializationUtility.Options) ?? throw new InvalidDataException("无法复制对局数据。");
}
