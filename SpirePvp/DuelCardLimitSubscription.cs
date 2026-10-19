using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;

namespace SpirePvp;

// CombatManager.Reset removes players before History.Clear raises Changed.
// The counter must tolerate that order and unsubscribe before run cleanup.
public sealed class DuelCardLimitSubscription : IDisposable
{
    private readonly CombatHistory _history;
    private readonly ICombatState _state;
    private readonly ulong? _local;
    private readonly int _maximum;
    private readonly Action<string> _render;
    private bool _disposed;
    public DuelCardLimitSubscription(CombatHistory history, ICombatState state, ulong? local, int maximum, Action<string> render)
    {
        _history = history; _state = state; _local = local; _maximum = maximum; _render = render;
        history.Changed += Refresh;
        Refresh();
    }
    private void Refresh()
    {
        if (_disposed || _local == null) return;
        var me = _state.Players.FirstOrDefault(p => p.NetId == _local);
        if (me == null) return;
        var used = DuelCardLimit.Count(_history.CardPlaysStarted, me, _state);
        _render($"本回合出牌：{used} / {_maximum}"
            + (used >= _maximum ? " · 已达上限，可使用药水或结束回合" : ""));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _history.Changed -= Refresh;
    }
}
