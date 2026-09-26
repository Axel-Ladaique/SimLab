using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Mountain;

/// <summary>
/// The switchback road (world ENU): from the valley in the south-west, north-east along the valley floor (crossing
/// the stream), then seven hairpins up the south part of the west face, over the crest and across the shoulder to the
/// chalets' car park. Its surface height is a profile along the centre line that follows the natural relief but
/// never falls and never climbs faster than <see cref="MaxGrade"/> (less in bends); <see cref="MountainRelief.Build"/>
/// cuts and fills the ground to it.
/// </summary>
public sealed class MountainRoad
{
    public const double MaxGrade = 0.11;
    const double SampleStep = 2;
    const double CornerReach = 20;
    const double CellSize = 16;

    /// <summary>The road stays level this far beyond each end of the bridge deck.</summary>
    const double BridgeLanding = 4;

    /// <summary>
    /// The authored corners. The eight legs were laid out in face coordinates (distance below the crest, y): each
    /// runs between y = −980 and −530 and, with the hairpin after it, climbs about 8.5 % of its length, so the
    /// profile hugs the ground; each hairpin steps 24 m up the slope.
    /// </summary>
    static readonly (double X, double Y)[] Waypoints =
    [
        (-1600, -1700), (-1470, -1450), (-1330, -1200), (-1180, -1060), (-1060, -980),
        (-672, -530), (-648, -530), (-733, -980), (-709, -980), (-468, -530), (-444, -530), (-564, -980),
        (-540, -980), (-321, -530), (-297, -530), (-423, -980), (-399, -980), (-181, -530), (-157, -530),
        (-270, -980),
        (-120, -760), (-10, -500), (120, -240), (200, -70),
    ];

    readonly (double X, double Y)[] _path;
    readonly double[] _profile;
    readonly int _cols, _rows;
    readonly double _minX, _minY;
    readonly List<int>[] _cells;

    public MountainRoad()
    {
        _path = Resample(Chaikin(WithCornerPoints(Waypoints), 5), SampleStep);
        var (ck, ct, cx, cy, cyaw) = Crossing(_path, MountainRelief.Stream);
        _profile = Profile(_path, Level(_path, ck, ct));
        StreamCrossing = (cx, cy, cyaw, _profile[ck] + ct * (_profile[ck + 1] - _profile[ck]));

        _minX = _path.Min(p => p.X) - CellSize;
        _minY = _path.Min(p => p.Y) - CellSize;
        _cols = (int)((_path.Max(p => p.X) + CellSize - _minX) / CellSize) + 1;
        _rows = (int)((_path.Max(p => p.Y) + CellSize - _minY) / CellSize) + 1;
        _cells = new List<int>[_cols * _rows];
        for (int k = 0; k + 1 < _path.Length; k++)
        {
            var (a, b) = (_path[k], _path[k + 1]);
            int i0 = Col(Math.Min(a.X, b.X)), i1 = Col(Math.Max(a.X, b.X));
            int j0 = Row(Math.Min(a.Y, b.Y)), j1 = Row(Math.Max(a.Y, b.Y));
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
                (_cells[j * _cols + i] ??= []).Add(k);
        }
    }

    /// <summary>Where the road crosses <see cref="MountainRelief.Stream"/>: the point, the road's heading there (a
    /// <see cref="PlanarYaw"/> along the road) and the road surface height.</summary>
    public (double X, double Y, double YawDeg, double Profile) StreamCrossing { get; }

    /// <summary>The centre line, a point every <see cref="SampleStep"/> m from the valley to the car park.</summary>
    public IReadOnlyList<(double X, double Y)> Path => _path;

    public double Width => 5;

    /// <summary>Horizontal distance to the centre line.</summary>
    public double DistanceTo(double x, double y) => Nearest(x, y, double.PositiveInfinity)!.Value.Distance;

    /// <summary>Road surface height at the nearest centre-line point.</summary>
    public double ProfileHeight(double x, double y) => Nearest(x, y, double.PositiveInfinity)!.Value.Profile;

    /// <summary>The nearest centre-line point within <paramref name="reach"/>: its distance and the profile height
    /// there; null when the road is further away.</summary>
    internal (double Distance, double Profile)? Nearest(double x, double y, double reach)
    {
        if (double.IsPositiveInfinity(reach)) return NearestAmong(x, y, Enumerable.Range(0, _path.Length - 1), reach);
        int i0 = Math.Max(Col(x - reach), 0), i1 = Math.Min(Col(x + reach), _cols - 1);
        int j0 = Math.Max(Row(y - reach), 0), j1 = Math.Min(Row(y + reach), _rows - 1);
        (double Distance, double Profile)? best = null;
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            var cell = _cells[j * _cols + i];
            if (cell is null) continue;
            var hit = NearestAmong(x, y, cell, best?.Distance ?? reach);
            if (hit is not null) best = hit;
        }
        return best;
    }

    /// <summary>For each centre-line segment passing within <paramref name="reach"/>, the distance to its nearest
    /// point and the profile height there (a segment may come up more than once).</summary>
    internal IEnumerable<(double Distance, double Profile)> Within(double x, double y, double reach)
    {
        int i0 = Math.Max(Col(x - reach), 0), i1 = Math.Min(Col(x + reach), _cols - 1);
        int j0 = Math.Max(Row(y - reach), 0), j1 = Math.Min(Row(y + reach), _rows - 1);
        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            var cell = _cells[j * _cols + i];
            if (cell is null) continue;
            foreach (int k in cell)
            {
                var (a, b) = (_path[k], _path[k + 1]);
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double t = Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / (ex * ex + ey * ey), 0, 1);
                double dx = a.X + t * ex - x, dy = a.Y + t * ey - y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= reach) yield return (d, _profile[k] + t * (_profile[k + 1] - _profile[k]));
            }
        }
    }

    (double Distance, double Profile)? NearestAmong(double x, double y, IEnumerable<int> segments, double reach)
    {
        double bestD2 = reach * reach, bestProfile = 0;
        bool found = false;
        foreach (int k in segments)
        {
            var (a, b) = (_path[k], _path[k + 1]);
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double t = Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / (ex * ex + ey * ey), 0, 1);
            double dx = a.X + t * ex - x, dy = a.Y + t * ey - y, d2 = dx * dx + dy * dy;
            if (d2 > bestD2 || (found && d2 == bestD2)) continue;
            bestD2 = d2;
            bestProfile = _profile[k] + t * (_profile[k + 1] - _profile[k]);
            found = true;
        }
        return found ? (Math.Sqrt(bestD2), bestProfile) : null;
    }

    /// <summary>The first point where the path crosses <paramref name="line"/>: the path segment, the fraction
    /// along it, the point and the path's heading there.</summary>
    static (int K, double T, double X, double Y, double YawDeg) Crossing((double X, double Y)[] path, IReadOnlyList<(double X, double Y)> line)
    {
        for (int k = 0; k + 1 < path.Length; k++)
        for (int m = 0; m + 1 < line.Count; m++)
        {
            var (a, b) = (path[k], path[k + 1]);
            var (c, d) = (line[m], line[m + 1]);
            double ex = b.X - a.X, ey = b.Y - a.Y, fx = d.X - c.X, fy = d.Y - c.Y;
            double den = ex * fy - ey * fx;
            if (Math.Abs(den) < 1e-12) continue;
            double t = ((c.X - a.X) * fy - (c.Y - a.Y) * fx) / den, u = ((c.X - a.X) * ey - (c.Y - a.Y) * ex) / den;
            if (t < 0 || t > 1 || u < 0 || u > 1) continue;
            return (k, t, a.X + t * ex, a.Y + t * ey, PlanarYaw.Of(ex, ey));
        }
        throw new InvalidOperationException("The road does not cross the line.");
    }

    /// <summary>The segments that must stay level: every one overlapping the bridge deck, centred at fraction
    /// <paramref name="t"/> of segment <paramref name="k"/>, and its <see cref="BridgeLanding"/> at each end.</summary>
    static bool[] Level((double X, double Y)[] path, int k, double t)
    {
        var s = new double[path.Length];
        for (int i = 1; i < path.Length; i++) s[i] = s[i - 1] + Distance(path[i - 1], path[i]);
        double centre = s[k] + t * (s[k + 1] - s[k]), half = MountainRelief.BridgeLength / 2 + BridgeLanding;
        var level = new bool[path.Length - 1];
        for (int i = 0; i + 1 < path.Length; i++) level[i] = s[i + 1] > centre - half && s[i] < centre + half;
        return level;
    }

    int Col(double x) => Math.Clamp((int)((x - _minX) / CellSize), 0, _cols - 1);

    int Row(double y) => Math.Clamp((int)((y - _minY) / CellSize), 0, _rows - 1);

    /// <summary>
    /// The natural height along the path, made monotonic and grade-limited (and level where <paramref name="level"/>
    /// says so: over the bridge): the mean of a forward pass (never above
    /// the ground, lagging where it climbs too fast) and a backward pass (never below it, leading), so cuts and fills
    /// are shared around each steep spot.
    /// </summary>
    static double[] Profile((double X, double Y)[] path, bool[] level)
    {
        int n = path.Length;
        var ground = new double[n];
        for (int k = 0; k < n; k++) ground[k] = MountainRelief.Height(path[k].X, path[k].Y);
        var rise = new double[n - 1];
        for (int k = 0; k < n - 1; k++) rise[k] = level[k] ? 0 : GradeLimit(path, k) * Distance(path[k], path[k + 1]);
        var up = new double[n];
        var down = new double[n];
        up[0] = ground[0];
        for (int k = 1; k < n; k++) up[k] = Math.Max(up[k - 1], Math.Min(ground[k], up[k - 1] + rise[k - 1]));
        down[n - 1] = ground[n - 1];
        for (int k = n - 2; k >= 0; k--) down[k] = Math.Min(down[k + 1], Math.Max(ground[k], down[k + 1] - rise[k]));
        var profile = new double[n];
        for (int k = 0; k < n; k++) profile[k] = (up[k] + down[k]) / 2;
        return profile;
    }

    /// <summary>
    /// The steepest grade allowed on segment <paramref name="k"/>: <see cref="MaxGrade"/> on the straight, less in
    /// bends, where the inside edge is shorter and so steeper (a 10 m radius hairpin is held to about 6 %).
    /// </summary>
    static double GradeLimit((double X, double Y)[] path, int k)
    {
        int a = Math.Max(k - 3, 0), b = Math.Min(k + 3, path.Length - 2);
        if (b <= a) return MaxGrade;
        double turn = Math.Abs(Math.IEEERemainder(Heading(path, b) - Heading(path, a), 2 * Math.PI));
        double curvature = turn / ((b - a) * SampleStep);
        return MaxGrade / (1 + 8 * curvature);
    }

    static double Heading((double X, double Y)[] path, int k) => Math.Atan2(path[k + 1].Y - path[k].Y, path[k + 1].X - path[k].X);

    /// <summary>Adds a point <see cref="CornerReach"/> m either side of each corner on the long segments, so the
    /// rounding stays within about half that of the corner; a hairpin's short link rounds over its whole length.</summary>
    static List<(double X, double Y)> WithCornerPoints(IReadOnlyList<(double X, double Y)> corners)
    {
        var points = new List<(double X, double Y)> { corners[0] };
        for (int k = 1; k < corners.Count; k++)
        {
            var (a, b) = (corners[k - 1], corners[k]);
            double len = Distance(a, b);
            if (len > 3 * CornerReach)
            {
                if (k > 1) points.Add(Lerp(a, b, CornerReach / len));
                if (k < corners.Count - 1) points.Add(Lerp(a, b, 1 - CornerReach / len));
            }
            points.Add(b);
        }
        return points;
    }

    /// <summary>Chaikin corner cutting, keeping both ends.</summary>
    static List<(double X, double Y)> Chaikin(List<(double X, double Y)> points, int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            var next = new List<(double X, double Y)>(points.Count * 2) { points[0] };
            for (int k = 1; k < points.Count; k++)
            {
                next.Add(Lerp(points[k - 1], points[k], 0.25));
                next.Add(Lerp(points[k - 1], points[k], 0.75));
            }
            next.Add(points[^1]);
            points = next;
        }
        return points;
    }

    /// <summary>Points every <paramref name="step"/> m of arc length along the polyline, plus its last point.</summary>
    static (double X, double Y)[] Resample(List<(double X, double Y)> points, double step)
    {
        var result = new List<(double X, double Y)> { points[0] };
        double carry = step;
        for (int k = 1; k < points.Count; k++)
        {
            var (a, b) = (points[k - 1], points[k]);
            double len = Distance(a, b), s = carry;
            for (; s < len; s += step) result.Add(Lerp(a, b, s / len));
            carry = s - len;
        }
        if (Distance(result[^1], points[^1]) > step / 4) result.Add(points[^1]);
        else result[^1] = points[^1];
        return result.ToArray();
    }

    static (double X, double Y) Lerp((double X, double Y) a, (double X, double Y) b, double t) =>
        (a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));

    static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
}
