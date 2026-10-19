using MegaCrit.Sts2.Core.Map;

namespace SpirePvp;

public sealed class DuelMap : ActMap
{
    // Native map layout divides by (row count - 1). Two empty storage rows keep
    // that math finite; the sole playable room is the boss node at floor one.
    private readonly MapPoint?[,] _grid = new MapPoint?[7, 2];
    protected override MapPoint?[,] Grid => _grid;
    public override MapPoint BossMapPoint { get; }
    // Native UI requires a start marker. It is never playable and is hidden.
    public override MapPoint StartingMapPoint { get; } = new(3, -1) { PointType = MapPointType.Ancient, CanBeModified = false };
    public DuelMap(int floor = 0)
    {
        if (floor < 0) throw new ArgumentOutOfRangeException(nameof(floor));
        BossMapPoint = new(3, floor) { PointType = MapPointType.Boss, CanBeModified = false };
        startMapPoints.Add(BossMapPoint);
    }
}
