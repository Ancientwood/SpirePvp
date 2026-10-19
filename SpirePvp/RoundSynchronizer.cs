using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace SpirePvp;

public static class RoundSynchronizer
{
    public static async Task WaitForBoth(PlayerChoiceSynchronizer sync, IReadOnlyList<Player> players, Player local, int round, CancellationToken cancellation)
    {
        if (players.Count != 2 || !players.Contains(local)) throw new InvalidOperationException("同步回合需要两名玩家和有效的本地玩家。");
        cancellation.ThrowIfCancellationRequested();
        // Reserve in the native player-slot order on both peers. An early message is
        // buffered by the original synchronizer until the other side finishes drawing.
        var reservations = players.Select(p => (Player: p, Id: sync.ReserveChoiceId(p))).ToArray();
        var own = reservations.Single(e => e.Player == local);
        sync.SyncLocalChoice(local, own.Id, PlayerChoiceResult.FromIndex(round));
        var remote = reservations.Single(e => e.Player != local);
        var choice = await sync.WaitForRemoteChoice(remote.Player, remote.Id).WaitAsync(cancellation);
        if (choice.AsIndex() != round) throw new InvalidOperationException("PvP 回合编号不一致，停止对局以防止失步。");
    }
}
