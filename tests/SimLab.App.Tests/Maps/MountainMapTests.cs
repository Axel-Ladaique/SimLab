using SimLab.App.Maps;
using SimLab.App.Maps.Mountain;
using SimLab.Flight.Geometry;

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
        // The pilot's pad stops just short of the crest: check the disc east of the first grid line past its edge.
        var p = layout.PilotPosition;
        for (double x = -25; x <= 25; x += 1)
        for (double y = -25; y <= 25; y += 1)
        {
            if (x * x + y * y > 25 * 25) continue;
            if (p.X + x < MountainRelief.CrestX(p.Y + y) + MountainRelief.PadCrestSetback + MountainMap.GridStep) continue;
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
        foreach (double r in new[] { 1.1, 1.15 })
        for (int k = 0; k < 32; k++)
        {
            double t = 2 * Math.PI * k / 32;
            double x = -1150 + r * 225 * Math.Cos(t), y = 500 + r * 125 * Math.Sin(t);
            Assert.True(H(x, y) > MountainMap.LakeLevel, $"shore {H(x, y)} at ({x:F0}, {y:F0})");
        }
        Assert.Null(Mountain.Terrain.WaterSurface(0, 0));
    }

    /// <summary>The drawn lake is exactly <see cref="WaterBody.Outline"/>, so the crash water and the visible water
    /// agree only if every ground point below the level at the shore lies inside the outline: the terrain's triangles
    /// cross the level up to about 4.5 m outside the dug ellipse (r = 1), which <see cref="MountainRelief.ShoreMargin"/>
    /// covers. Beyond the 35 m blend (r ≈ 1.15 and more) the natural valley floor may dip below the level; it is dry
    /// land, drawn and flown as such.</summary>
    [Fact]
    public void Ground_below_the_lake_level_is_inside_the_water_outline()
    {
        int below = 0;
        for (double r = 0.9; r <= 1.12; r += 0.001)
        for (int k = 0; k < 2000; k++)
        {
            double t = 2 * Math.PI * k / 2000;
            double x = -1150 + r * 225 * Math.Cos(t), y = 500 + r * 125 * Math.Sin(t);
            if (H(x, y) >= MountainMap.LakeLevel) continue;
            below++;
            Assert.True(Mountain.Terrain.WaterSurface(x, y) is not null,
                $"ground {H(x, y) - MountainMap.LakeLevel:F2} m below the level outside the lake at ({x:F1}, {y:F1}), r = {r:F3}");
        }
        Assert.True(below > 1000, $"only {below} samples below the level");
    }

    [Fact]
    public void Road_is_drivable()
    {
        var road = new MountainRoad();
        // The bridge deck carries the road over the re-cut stream channel; its own test checks the deck.
        var samples = Along(road.Path, 5).Where(s => !OnDeck(road, s.X, s.Y)).ToList();
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
    public void Road_banks_are_not_walls()
    {
        // Every grid cell within 16 m of the road on the face: where the natural ground is under 45°, the cut or
        // fill banks stay under 50° (slope 1.2).
        var road = new MountainRoad();
        var g = Mountain.Grid;
        int walls = 0;
        double worst = 0, wx = 0, wy = 0;
        for (double y = -1100; y < -450; y += g.Step)
        for (double x = -1100; x < 0; x += g.Step)
        {
            double cx = x + g.Step / 2, cy = y + g.Step / 2;
            if (road.DistanceTo(cx, cy) > 16) continue;
            if (CellSlope(MountainRelief.Height, x, y, g.Step) >= 1) continue;
            double slope = CellSlope(H, x, y, g.Step);
            if (slope <= 1.2) continue;
            walls++;
            if (slope > worst) (worst, wx, wy) = (slope, x, y);
        }
        Assert.True(walls == 0, $"{walls} cells steeper than 1.2, worst {worst:F2} at ({wx}, {wy})");
    }

    /// <summary>The steeper of the two triangles of the grid cell whose lower-left corner is (x, y).</summary>
    static double CellSlope(Func<double, double, double> h, double x, double y, double step)
    {
        double a = h(x, y), b = h(x + step, y), c = h(x, y + step), d = h(x + step, y + step);
        double lower = Math.Sqrt((b - a) * (b - a) + (d - b) * (d - b)) / step;
        double upper = Math.Sqrt((c - a) * (c - a) + (d - c) * (d - c)) / step;
        return Math.Max(lower, upper);
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
        // The hand launch is from flat ground at the edge, the crest a few metres ahead.
        Assert.True(Math.Abs(H(x, y)) < 0.05, $"launch ground {H(x, y):F2}");
        Assert.True(Mountain.Terrain.Normal(x, y).Z > 0.999);
        Assert.InRange(x - MountainRelief.CrestX(y), 3, 10);
    }

    [Fact]
    public void Pilot_stands_at_the_brow_of_the_face()
    {
        var pilot = MountainMap.Layout.PilotPosition;
        Assert.InRange(pilot.X - MountainRelief.CrestX(pilot.Y), 4, 8);
        // West of the pad the ground only falls: no lip where the pad ends, and the brow is not levelled away.
        for (double y = pilot.Y - 25; y <= pilot.Y + 25; y += 5)
        {
            double crest = MountainRelief.CrestX(y);
            for (double x = pilot.X; x > crest - 100; x -= 1)
                Assert.True(H(x - 1, y) <= H(x, y) + 0.02, $"ground rises west of ({x}, {y}): {H(x - 1, y):F2} vs {H(x, y):F2}");
        }
        // Right in front of the pilot the brow keeps the relief's shape, only lowered to meet the pad.
        for (double y = pilot.Y - 15; y <= pilot.Y + 15; y += 5)
        {
            double crest = MountainRelief.CrestX(y), edge = crest + MountainRelief.PadCrestSetback;
            foreach (double x in new[] { crest, crest - 5, crest - 10 })
                Assert.Equal(MountainRelief.Height(x, y) - MountainRelief.Height(edge, y), H(x, y), 1);
        }
        var windsock = MountainMap.Layout.WindsockPosition;
        Assert.True(windsock.X > pilot.X, "the windsock is not in front of the pilot");
        Assert.True(Math.Abs(H(windsock.X, windsock.Y)) < 0.05);
    }

    static double Distance(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    static double Slope(double x, double y)
    {
        var n = Mountain.Terrain.Normal(x, y);
        return Math.Sqrt(1 - n.Z * n.Z) / n.Z;
    }

    /// <summary>Furniture allowed in the keep-out: the lift and its stations, the bridge and the parked cars.</summary>
    static bool Exempt(Prop p) => p is Cableway or Bridge or Car
        || (p is Building && (Distance(p.Base.X, p.Base.Y, MountainMap.LiftBottom.X, MountainMap.LiftBottom.Y) < 20
                              || Distance(p.Base.X, p.Base.Y, MountainMap.LiftTop.X, MountainMap.LiftTop.Y) < 20));

    static double TopAboveBase(Prop p) => p switch
    {
        ConiferTree t => t.Height,
        Boulder b => 0.75 * b.Height,
        Building b => b.TotalHeight,
        _ => 0,
    };

    [Fact]
    public void Props_are_deterministic()
    {
        var again = MountainMap.Create();
        Assert.Equal(Mountain.Props.Count, again.Props.Count);
        for (int i = 0; i < again.Props.Count; i++)
            Assert.True(Mountain.Props[i].Parts().SequenceEqual(again.Props[i].Parts()), $"prop {i} differs");
    }

    [Fact]
    public void Forest_has_the_right_size_and_stays_off_steep_ground()
    {
        var trees = Mountain.Props.OfType<ConiferTree>().ToList();
        Assert.InRange(trees.Count, 5000, 8000);
        foreach (var t in trees)
        {
            Assert.True(Slope(t.Base.X, t.Base.Y) <= 0.78, $"tree on slope {Slope(t.Base.X, t.Base.Y):F2} at ({t.Base.X:F0}, {t.Base.Y:F0})");
            Assert.InRange(t.Height, 12, 28);
        }
        Assert.InRange(MountainPlanting.ForestDensity(trees[0].Base.X, trees[0].Base.Y, H(trees[0].Base.X, trees[0].Base.Y)), 0, 1);
    }

    [Fact]
    public void Boulders_lie_below_the_cliffs_and_on_the_meadows()
    {
        var boulders = Mountain.Props.OfType<Boulder>().ToList();
        int face = boulders.Count(b => b.Base.X < MountainRelief.CrestX(b.Base.Y));
        Assert.InRange(face, 150, 400);
        Assert.InRange(boulders.Count - face, 20, 40);
        Assert.All(boulders, b => Assert.InRange(b.Radius, 0.8, 4));
    }

    [Fact]
    public void No_prop_in_the_keep_out_the_lake_or_on_the_road()
    {
        var road = new MountainRoad();
        foreach (var p in Mountain.Props)
        {
            var (x, y) = (p.Base.X, p.Base.Y);
            Assert.Null(Mountain.Terrain.WaterSurface(x, y));
            if (Exempt(p)) continue;
            Assert.False(MountainMap.KeepOut(x, y, tall: TopAboveBase(p) > 2), $"{p.GetType().Name} in the keep-out at ({x:F0}, {y:F0})");
            // Crowns and rocks stay off the road too.
            double reach = 8 + p switch { ConiferTree t => t.BaseRadius, Boulder b => b.Radius, _ => 0 };
            Assert.True(road.DistanceTo(x, y) > reach, $"{p.GetType().Name} over the road at ({x:F0}, {y:F0})");
        }
    }

    [Fact]
    public void Every_prop_stands_on_the_ground()
    {
        foreach (var p in Mountain.Props)
        {
            if (p is Bridge) continue;
            if (p is Building b)
            {
                // Set at its lowest corner: the walls reach the ground everywhere.
                foreach (var (cx, cy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                {
                    var (wx, wy) = PlanarYaw.ToWorld(cx * b.Length / 2, cy * b.Width / 2, b.YawDeg);
                    Assert.True(b.Base.Z <= H(b.Base.X + wx, b.Base.Y + wy) + 1e-6, $"building floats at ({b.Base.X:F0}, {b.Base.Y:F0})");
                }
                continue;
            }
            var bases = p is Cableway lift ? lift.Pylons : [p.Base];
            foreach (var q in bases)
                Assert.True(Math.Abs(q.Z - H(q.X, q.Y)) < 0.01, $"{p.GetType().Name} base {q.Z:F2} vs ground {H(q.X, q.Y):F2} at ({q.X:F0}, {q.Y:F0})");
        }
    }

    [Fact]
    public void Chairlift_climbs_to_the_knob_clear_of_the_pilot_and_the_ground()
    {
        var lift = Mountain.Props.OfType<Cableway>().Single();
        Assert.True(lift.Pylons.Count >= 5, $"{lift.Pylons.Count} pylons");
        var pilot = MountainMap.Layout.PilotPosition;
        foreach (var q in lift.Pylons) Assert.True(Distance(q.X, q.Y, pilot.X, pilot.Y) > 350, $"pylon at ({q.X:F0}, {q.Y:F0})");
        for (int span = 0; span + 1 < lift.Pylons.Count; span++)
        foreach (int side in new[] { -1, 1 })
        for (double t = 0; t <= 1; t += 0.01)
        {
            var c = lift.CablePoint(span, side, t);
            Assert.True(c.Z - H(c.X, c.Y) > 3, $"cable {c.Z - H(c.X, c.Y):F1} m above ground at ({c.X:F0}, {c.Y:F0})");
        }
        Assert.Equal(2, Mountain.Props.OfType<Building>().Count(Exempt));
    }

    [Fact]
    public void Hamlet_has_two_chalets_a_hut_and_two_cars()
    {
        var steep = Mountain.Props.OfType<Building>().Where(b => b.SteepRoof).ToList();
        Assert.Equal(2, steep.Count(b => b.Length == 12 && b.Width == 9 && b.WallHeight == 5));
        Assert.Equal(1, steep.Count(b => b.Length == 6 && b.Width == 5 && b.WallHeight == 3));
        Assert.All(steep, b => Assert.InRange(b.Base.X, 190, 310));
        Assert.Equal(2, Mountain.Props.OfType<Car>().Count());
    }

    [Fact]
    public void Bridge_carries_the_road_over_the_stream()
    {
        var road = new MountainRoad();
        var crossing = road.StreamCrossing;
        var bridge = Mountain.Props.OfType<Bridge>().Single();
        Assert.True(Distance(bridge.Base.X, bridge.Base.Y, crossing.X, crossing.Y) < 0.01);
        Assert.Equal(road.ProfileHeight(crossing.X, crossing.Y), bridge.Base.Z, 6);
        // The channel is dug again under the deck.
        Assert.True(H(crossing.X, crossing.Y) < bridge.Base.Z - 1.5, $"channel {H(crossing.X, crossing.Y):F2} under deck {bridge.Base.Z:F2}");
        // The road meets the deck top at both ends, across its whole width, and stays level just beyond them.
        foreach (double along in new[] { -1.0, 1.0 })
        foreach (double beyond in new[] { 0.0, 0.5, 1, 2 })
        for (double across = -bridge.Width / 2; across <= bridge.Width / 2; across += 0.5)
        {
            var (dx, dy) = PlanarYaw.ToWorld(along * (bridge.Length / 2 + beyond), across, bridge.YawDeg);
            double ground = H(bridge.Base.X + dx, bridge.Base.Y + dy);
            Assert.True(Math.Abs(ground - bridge.Base.Z) < 0.05,
                $"road {ground - bridge.Base.Z:F2} m from the deck top {beyond} m beyond its {(along < 0 ? "west" : "east")} end, {across} m across");
        }
    }

    [Fact]
    public void Surface_weights_sum_to_one_over_the_grid_and_the_backdrop()
    {
        var far = Mountain.BackdropSurface!;
        for (double y = -2000; y <= 2000; y += 50)
        for (double x = -2000; x <= 2000; x += 50)
        {
            var w = Mountain.Surface(x, y);
            Assert.True(Math.Abs(w.Sum - 1) < 1e-9, $"grid weights sum {w.Sum} at ({x}, {y})");
        }
        for (double y = -15000; y <= 15000; y += 50)
        for (double x = -15000; x <= 15000; x += 50)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(y)) < 2000 || x * x + y * y > 15000.0 * 15000) continue;
            var w = far(x, y);
            Assert.True(Math.Abs(w.Sum - 1) < 1e-9, $"backdrop weights sum {w.Sum} at ({x}, {y})");
        }
    }

    [Fact]
    public void Strip_is_mowed_and_the_car_park_gravelled()
    {
        var l = MountainMap.Layout;
        for (double x = l.RunwayCentre.X - l.RunwayLength / 2; x <= l.RunwayCentre.X + l.RunwayLength / 2; x += 5)
        for (double y = -l.RunwayWidth / 2; y <= l.RunwayWidth / 2; y += 2)
            Assert.True(Mountain.Surface(x, y).MowedGrass > 0.9, $"strip not mowed at ({x}, {y})");
        Assert.True(Mountain.Surface(210, -70).Gravel > 0.9, "car park");
    }

    [Fact]
    public void Cliffs_are_rock_and_the_summit_snow()
    {
        int cliffs = 0;
        for (double y = -1800; y <= 1800; y += 100)
        for (double x = MountainRelief.CrestX(y) - 900; x < MountainRelief.CrestX(y); x += 2)
        {
            if (Slope(x, y) <= 1.2) continue;
            cliffs++;
            Assert.True(Mountain.Surface(x, y).Rock > 0.6, $"cliff at ({x}, {y}) is {Mountain.Surface(x, y)}");
        }
        Assert.True(cliffs > 10, $"only {cliffs} points steeper than 1.2 on the face");

        var g = Mountain.Grid;
        double max = double.MinValue, mx = 0, my = 0;
        for (int j = 0; j < g.Count; j++)
        for (int i = 0; i < g.Count; i++)
            if (g[i, j] > max) (max, mx, my) = (g[i, j], g.MinX + i * g.Step, g.MinY + j * g.Step);
        Assert.True(Mountain.Surface(mx, my).Snow > 0.7, $"summit ({mx}, {my}) is {Mountain.Surface(mx, my)}");
    }

    [Fact]
    public void Dense_forest_floor_is_needles()
    {
        int checkedPoints = 0;
        for (double y = -1900; y <= 1900; y += 37)
        for (double x = -1900; x <= 1900; x += 37)
        {
            if (MountainPlanting.ForestDensity(x, y, H(x, y)) <= 0.8 || Slope(x, y) > 0.5 || MountainMap.InSoaringBeat(x, y, 40) > 0) continue;
            checkedPoints++;
            Assert.True(Mountain.Surface(x, y).Needles > 0.6, $"forest floor at ({x}, {y}) is {Mountain.Surface(x, y)}");
        }
        Assert.True(checkedPoints > 100, $"only {checkedPoints} dense forest points");
    }

    [Fact]
    public void Soaring_beat_kept_clear_of_trees_is_not_needle_ground()
    {
        int checkedPoints = 0;
        for (double y = -360; y <= 320; y += 10)
        for (double x = -300; x <= 0; x += 10)
        {
            if (MountainMap.InSoaringBeat(x, y, 40) < 1) continue;
            checkedPoints++;
            Assert.True(Mountain.Surface(x, y).Needles < 1e-9, $"needles in the beat at ({x}, {y}): {Mountain.Surface(x, y)}");
        }
        Assert.True(checkedPoints > 100, $"only {checkedPoints} points inside the beat");
    }

    [Fact]
    public void Road_and_stream_are_overlays()
    {
        var road = Assert.Single(Mountain.Overlays, o => o.Kind == SurfaceKind.Dirt);
        Assert.Equal(5, road.Width);
        Assert.Equal(MountainMap.Road.Path, road.Path);
        Assert.False(road.Water);
        var stream = Assert.Single(Mountain.Overlays, o => o.Kind == SurfaceKind.Gravel);
        Assert.Equal(3, stream.Width);
        Assert.Equal(MountainRelief.Stream, stream.Path);
        Assert.True(stream.Water);
    }

    [Fact]
    public void Backdrop_joins_the_grid_edge_and_rises_to_peaks()
    {
        var backdrop = Mountain.Backdrop!;
        var far = Mountain.BackdropSurface!;
        for (double s = -2000; s <= 2000; s += 50)
        foreach (var (x, y) in new[] { (s, -2000.0), (s, 2000.0), (-2000.0, s), (2000.0, s) })
        {
            Assert.True(Math.Abs(backdrop(x, y) - Mountain.Grid.Height(x, y)) <= 0.5,
                $"backdrop {backdrop(x, y):F2} vs grid edge {Mountain.Grid.Height(x, y):F2} at ({x}, {y})");
            var (a, b) = (far(x, y), Mountain.Surface(x, y));
            foreach (var kind in Enum.GetValues<SurfaceKind>())
                Assert.True(Math.Abs(a[kind] - b[kind]) < 1e-6, $"{kind} seam at ({x}, {y}): {a[kind]} vs {b[kind]}");
        }
        double east = double.MinValue;
        for (double y = -6000; y <= 6000; y += 100)
        for (double x = 2500; x <= 14000; x += 100)
            east = Math.Max(east, backdrop(x, y));
        Assert.True(east > 1100, $"highest backdrop to the east {east:F0}");
    }

    /// <summary>On the road over the bridge deck (within its length).</summary>
    static bool OnDeck(MountainRoad road, double x, double y)
    {
        var c = road.StreamCrossing;
        var (along, across) = PlanarYaw.ToLocal(x - c.X, y - c.Y, c.YawDeg);
        return Math.Abs(along) <= MountainRelief.BridgeLength / 2 && Math.Abs(across) <= MountainRelief.BridgeWidth;
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
