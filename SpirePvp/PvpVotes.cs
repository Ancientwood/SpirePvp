using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace SpirePvp;

public static class PvpVotes
{
    public static async Task<DuelMultipliers> HostMultipliers(PlayerChoiceSynchronizer sync,
        IReadOnlyList<Player> players, Player local, bool isHost, DuelMultipliers localSettings, CancellationToken cancellation)
    {
        if (players.Count != 2 || !players.Contains(local)) throw new InvalidOperationException("倍率同步仅支持双人。");
        cancellation.ThrowIfCancellationRequested();
        var slots = players.Select(p => (Player: p, Id: sync.ReserveChoiceId(p))).ToArray();
        var own = slots.Single(s => s.Player == local);
        var remote = slots.Single(s => s.Player != local);
        int value = isHost ? localSettings.Encode() : 0;
        sync.SyncLocalChoice(local, own.Id, PlayerChoiceResult.FromIndex(value));
        int received = (await sync.WaitForRemoteChoice(remote.Player, remote.Id).WaitAsync(cancellation)).AsIndex();
        if (isHost && received != 0) throw new InvalidOperationException("客机不能覆盖房主的决斗倍率。");
        return DuelMultipliers.Decode(isHost ? value : received);
    }

    public static async Task<bool[]> Exchange(PlayerChoiceSynchronizer sync, IReadOnlyList<Player> players,
        Player local, Func<Task<bool>> choose, CancellationToken cancellation)
    {
        if (players.Count != 2 || !players.Contains(local)) throw new InvalidOperationException("幕间决斗仅支持双人。");
        cancellation.ThrowIfCancellationRequested();
        // Reserve both slots before any asynchronous UI, in the same order on every peer.
        var slots = players.Select(p => (Player: p, Id: sync.ReserveChoiceId(p))).ToArray();
        var tasks = slots.Select(async slot =>
        {
            int value;
            if (slot.Player == local)
            {
                value = await choose().WaitAsync(cancellation) ? 1 : 0;
                sync.SyncLocalChoice(local, slot.Id, PlayerChoiceResult.FromIndex(value));
            }
            else value = (await sync.WaitForRemoteChoice(slot.Player, slot.Id).WaitAsync(cancellation)).AsIndex();
            if (value is not (0 or 1)) throw new InvalidOperationException("幕间决斗投票数据无效。");
            return value == 1;
        }).ToArray();
        return await Task.WhenAll(tasks);
    }
}
