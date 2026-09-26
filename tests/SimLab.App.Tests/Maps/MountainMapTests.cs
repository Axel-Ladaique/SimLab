using SimLab.App.Maps;
using SimLab.App.Maps.Mountain;

namespace SimLab.App.Tests.Maps;

public class MountainMapTests
{
    static readonly FieldMap Mountain = FieldCatalog.Load("mountain");

    static double H(double x, double y) => Mountain.Terrain.Height(x, y);

    [Fact]
    public void Catalog_lists_the_mountain()
    {
        Assert.Equal("FIELD_MOUNTAIN", FieldCatalog.Find("mountain").NameKey);
        Assert.Equal("mountain", Mountain.Id);
        Assert.Equal(1500, Mountain.DatumElevationM);
        Assert.Equal(2000, Mountain.HalfSize);
        Assert.Equal(4, Mountain.Grid.Step);
    }

    [Fact]
    public void Strip_and_pilot_area_are_flat()
    {
        var layout = MountainMap.Layout;
        for (double x = 20; x <= 170; x += 2)
        for (double y = -20; y <= 20; y += 2)
        {
            if (!layout.OnRunway(x, y)) continue;
            Assert.True(Math.Abs(H(x, y)) < 0.05, $"runway height {H(x, y)} at ({x}, {y})");
            Assert.True(Mountain.Terrain.Normal(x, y).Z > 0.999, $"runway normal at ({x}, {y})");
        }
        var p = layout.PilotPosition;
        for (double x = -25; x <= 25; x += 1)
        for (double y = -25; y <= 25; y += 1)
        {
            if (x * x + y * y > 25 * 25) continue;
            Assert.True(Math.Abs(H(p.X + x, p.Y + y)) < 0.05, $"pilot area height at ({p.X + x}, {p.Y + y})");
            Assert.True(Mountain.Terrain.Normal(p.X + x, p.Y + y).Z > 0.999, $"pilot area normal at ({p.X + x}, {p.Y + y})");
        }
    }

    [Fact]
    public void West_face_drops_into_the_valley()
    {
        foreach (double y in new[] { -600.0, 0, 600 })
        {
            double crest = MountainRelief.CrestX(y);
            Assert.True(H(crest - 900, y) < -300, $"valley at y = {y}: {H(crest - 900, y)}");
            double meanSlope = (H(crest - 30, y) - H(crest - 300, y)) / 270;
            Assert.InRange(meanSlope, 0.45, 0.8);
        }
        bool cliff = false;
        foreach (double y in new[] { 0.0, 400 })
        {
            double crest = MountainRelief.CrestX(y);
            for (double x = crest - 900; x < crest && !cliff; x += 2)
                cliff = (H(x + 8, y) - H(x, y)) / 8 > 1.0;
        }
        Assert.True(cliff, "no cliff band along y = 0 or y = 400");
    }

    [Fact]
    public void East_rises_to_a_snowy_summit()
    {
        var g = Mountain.Grid;
        double max = double.MinValue, mx = 0, my = 0;
        for (int j = 0; j < g.Count; j++)
        for (int i = 0; i < g.Count; i++)
            if (g[i, j] > max) (max, mx, my) = (g[i, j], g.MinX + i * g.Step, g.MinY + j * g.Step);
        Assert.InRange(max, 900, 1000);
        Assert.True(mx > 1000 && my > 1000, $"summit at ({mx}, {my})");
    }

    [Fact]
    public void Lake_holds_water()
    {
        Assert.Equal(MountainMap.LakeLevel, Mountain.Terrain.WaterSurface(-1150, 500));
        Assert.True(H(-1150, 500) <= MountainMap.LakeLevel - 5, $"lakebed {H(-1150, 500)}");
        for (int k = 0; k < 32; k++)
        {
            double t = 2 * Math.PI * k / 32;
            double x = -1150 + 1.1 * 225 * Math.Cos(t), y = 500 + 1.1 * 125 * Math.Sin(t);
            Assert.True(H(x, y) > MountainMap.LakeLevel, $"shore {H(x, y)} at ({x:F0}, {y:F0})");
        }
        Assert.Null(Mountain.Terrain.WaterSurface(0, 0));
    }

    [Fact]
    public void Road_is_drivable()
    {
        var road = new MountainRoad();
        var samples = Along(road.Path, 5).ToList();
        for (int k = 1; k < samples.Count; k++)
        {
            var (x0, y0, _, _) = samples[k - 1];
            var (x1, y1, _, _) = samples[k];
            double run = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            double grade = Math.Abs(H(x1, y1) - H(x0, y0)) / run;
            Assert.True(grade <= 0.12, $"grade {grade:F3} at ({x1:F0}, {y1:F0})");
            Assert.True(road.ProfileHeight(x1, y1) >= road.ProfileHeight(x0, y0) - 1e-6, $"profile falls at ({x1:F0}, {y1:F0})");
        }
        foreach (var (x, y, dx, dy) in samples)
        {
            double cross = Math.Abs(H(x - dy * 2, y + dx * 2) - H(x + dy * 2, y - dx * 2));
            Assert.True(cross < 0.3, $"cross-slope {cross:F2} m at ({x:F0}, {y:F0})");
        }
        var first = road.Path[0];
        var last = road.Path[^1];
        Assert.True(H(first.X, first.Y) < -300, $"road starts at {H(first.X, first.Y)}");
        Assert.True(Math.Abs(H(last.X, last.Y)) < 3, $"road ends at {H(last.X, last.Y)}");
    }

    [Fact]
    public void Layout_takes_off_toward_the_drop()
    {
        var layout = MountainMap.Layout;
        Assert.Equal(270, layout.TakeoffHeading(270));
        var (x, y, heading) = layout.HandLaunchPoint(270);
        Assert.Equal(270, heading);
        double dx = x - layout.PilotPosition.X, dy = y - layout.PilotPosition.Y;
        Assert.True(Math.Sqrt(dx * dx + dy * dy) < 5);
    }

    /// <summary>Points every <paramref name="step"/> metres along the polyline, with the unit direction there.</summary>
    static IEnumerable<(double X, double Y, double Dx, double Dy)> Along(IReadOnlyList<(double X, double Y)> path, double step)
    {
        double carry = 0;
        for (int k = 1; k < path.Count; k++)
        {
            var (ax, ay) = path[k - 1];
            var (bx, by) = path[k];
            double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            if (len <= carry) { carry -= len; continue; }
            double dx = (bx - ax) / len, dy = (by - ay) / len;
            for (double s = carry; s < len; s += step) yield return (ax + dx * s, ay + dy * s, dx, dy);
            carry = step - (len - carry) % step;
            if (carry >= step) carry -= step;
        }
    }
}
