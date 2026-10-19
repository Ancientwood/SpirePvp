using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Random;

namespace SpirePvp;

public sealed class PvpDevelopmentMap : ActMap
{
    public const int DevelopmentFloors = 12;
    private readonly MapPoint?[,] _grid = new MapPoint?[7, DevelopmentFloors];
    protected override MapPoint?[,] Grid => _grid;
    public override MapPoint BossMapPoint { get; } = new(3, DevelopmentFloors) { PointType = MapPointType.Boss, CanBeModified = false };
    public override MapPoint StartingMapPoint { get; } = new(3, -1) { PointType = MapPointType.Ancient, CanBeModified = false };

    public PvpDevelopmentMap(ulong seed)
    {
        // A separate named stream gives every peer the same route without
        // consuming room, reward or player-choice random numbers.
        var rng = new Rng(seed, "SpirePvp.DevelopmentMap.v1");
        var previous = StartingMapPoint;
        for (int row = 0; row < DevelopmentFloors; row++)
        {
            var type = row switch
            {
                5 => MapPointType.Treasure,
                10 => MapPointType.Shop,
                11 => MapPointType.RestSite,
                _ => RollPointType(rng.NextInt(10))
            };
            var point = new MapPoint(3, row) { PointType = type, CanBeModified = false };
            _grid[3, row] = point;
            previous.AddChildPoint(point);
            previous = point;
        }
        previous.AddChildPoint(BossMapPoint);
        startMapPoints.Add(_grid[3, 0]!);
    }

    public static MapPointType RollPointType(int roll) => roll switch
    {
        >= 0 and < 4 => MapPointType.Unknown,
        >= 4 and < 7 => MapPointType.Treasure,
        >= 7 and < 10 => MapPointType.RestSite,
        _ => throw new ArgumentOutOfRangeException(nameof(roll))
    };

    public MapPoint? GetNextPoint(IReadOnlyList<MapCoord> visited) => visited.Count == 0
        ? startMapPoints.Single() : GetPoint(visited[^1])?.Children.SingleOrDefault();
}
