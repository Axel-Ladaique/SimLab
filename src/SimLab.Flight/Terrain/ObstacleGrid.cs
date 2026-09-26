using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

/// <summary>What an obstacle is, which decides the crash cause shown to the pilot.</summary>
public enum ObstacleKind { Tree, Structure, Wire }

public readonly record struct Obstacle(IObstacleShape Shape, ObstacleKind Kind);

/// <summary>
/// Obstacles bucketed in a uniform horizontal grid, so a query only tests the obstacles near it. Each obstacle is
/// registered in every cell its footprint touches; a query may therefore test one twice, which only costs time.
/// Immutable after construction, so queries are thread-safe.
/// </summary>
public sealed class ObstacleGrid
{
    public const double CellSize = 32;

    readonly Obstacle[] _all;
    readonly Dictionary<long, int[]> _cells;

    public ObstacleGrid(IEnumerable<Obstacle> obstacles)
    {
        _all = obstacles.ToArray();
        var lists = new Dictionary<long, List<int>>();
        for (int i = 0; i < _all.Length; i++)
        {
            var f = _all[i].Shape.Footprint;
            for (int cx = Cell(f.MinX); cx <= Cell(f.MaxX); cx++)
            for (int cy = Cell(f.MinY); cy <= Cell(f.MaxY); cy++)
            {
                long key = Key(cx, cy);
                if (!lists.TryGetValue(key, out var list)) lists[key] = list = [];
                list.Add(i);
            }
        }
        _cells = lists.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    public IReadOnlyList<Obstacle> All => _all;

    /// <summary>Kind of the first obstacle containing the point, or null.</summary>
    public ObstacleKind? Hit(Vec3 p)
    {
        if (!_cells.TryGetValue(Key(Cell(p.X), Cell(p.Y)), out var ids)) return null;
        foreach (int i in ids)
            if (_all[i].Shape.Contains(p)) return _all[i].Kind;
        return null;
    }

    /// <summary>Kind of the first obstacle crossed by the segment a→b, or null.</summary>
    public ObstacleKind? Hit(Vec3 a, Vec3 b)
    {
        for (int cx = Cell(Math.Min(a.X, b.X)); cx <= Cell(Math.Max(a.X, b.X)); cx++)
        for (int cy = Cell(Math.Min(a.Y, b.Y)); cy <= Cell(Math.Max(a.Y, b.Y)); cy++)
        {
            if (!_cells.TryGetValue(Key(cx, cy), out var ids)) continue;
            foreach (int i in ids)
                if (_all[i].Shape.Intersects(a, b)) return _all[i].Kind;
        }
        return null;
    }

    static int Cell(double v) => (int)Math.Floor(v / CellSize);

    static long Key(int cx, int cy) => ((long)cx << 32) | (uint)cy;
}
