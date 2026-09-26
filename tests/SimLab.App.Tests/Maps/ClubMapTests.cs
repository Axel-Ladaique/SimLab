using SimLab.App.Maps;
using SimLab.App.Maps.Club;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Maps;

public class ClubMapTests
{
    static readonly FieldMap Club = FieldCatalog.Load("club");

    static bool IsVegetation(Prop p) => p is BroadleafTree or PoplarTree or ConiferTree or Bush or Hedge;

    [Fact]
    public void Terrain_is_flat_around_the_runway_and_hilly_far_away()
    {
        Assert.Equal(0, ClubMap.Height(0, 0));
        Assert.Equal(0, ClubMap.Height(250, 150));
        double maxFar = 0;
        for (double x = -900; x <= 900; x += 50) maxFar = Math.Max(maxFar, ClubMap.Height(x, -800));
        Assert.InRange(maxFar, 1, ClubMap.HillAmplitude);
    }

    [Fact]
    public void Terrain_height_is_never_negative_and_normals_are_unit_and_upward()
    {
        for (double x = -1000; x <= 1000; x += 97)
        for (double y = -1000; y <= 1000; y += 89)
        {
            Assert.True(Club.Terrain.Height(x, y) >= 0);
            var n = Club.Terrain.Normal(x, y);
            Assert.Equal(1.0, n.Length, 9);
            Assert.True(n.Z > 0.9);
        }
    }

    [Fact]
    public void Layout_keeps_the_club_positions()
    {
        Assert.Equal(new Vec3(0, -25, 0), ClubMap.Layout.PilotPosition);
        Assert.Equal(new Vec3(20, -28, 0), ClubMap.Layout.WindsockPosition);
        Assert.Equal(90, ClubMap.Layout.TakeoffHeading(80));
        Assert.Equal(270, ClubMap.Layout.TakeoffHeading(260));
        var (x, y, _) = ClubMap.Layout.HandLaunchPoint(270);
        Assert.Equal(0, x, 9);
        Assert.Equal(-22, y, 9);
    }

    [Fact]
    public void Map_is_deterministic()
    {
        var a = ClubMap.Create();
        var b = ClubMap.Create();
        Assert.Equal(a.Props.Count, b.Props.Count);
        Assert.Equal(a.Props.Select(p => (p.GetType(), p.Base, p.YawDeg)), b.Props.Select(p => (p.GetType(), p.Base, p.YawDeg)));
    }

    [Fact]
    public void Tree_count_and_species_mix()
    {
        int trees = Club.Props.Count(p => p is BroadleafTree or PoplarTree or ConiferTree);
        Assert.InRange(trees, 1500, 3000);
        Assert.Contains(Club.Props, p => p is ConiferTree);
        Assert.Contains(Club.Props, p => p is PoplarTree);
        Assert.Contains(Club.Props, p => p is Hedge);
        Assert.Contains(Club.Props, p => p is Bush);
        Assert.Single(Club.Props.OfType<PowerLine>());
        Assert.Equal(3, Club.Props.OfType<Car>().Count());
    }

    [Fact]
    public void Vegetation_keeps_out_of_the_runway_and_pilot_area()
    {
        Assert.All(Club.Props.Where(IsVegetation), p => Assert.False(ClubMap.KeepOut(p.Base.X, p.Base.Y), $"{p.GetType().Name} at {p.Base}"));
        Assert.True(ClubMap.KeepOut(0, -25));
        Assert.True(ClubMap.KeepOut(45, 0));
    }

    [Fact]
    public void Runway_takeoff_and_hand_launch_paths_are_clear()
    {
        for (double x = -50; x <= 50; x += 2)
        for (double y = -7.5; y <= 7.5; y += 2.5)
        for (double z = 0.5; z <= 15; z += 1.5)
            Assert.Null(Club.Terrain.HitObstacle(new Vec3(x, y, z)));
        // Climb-out along the runway axis to the edge of the keep-out, both directions.
        Assert.Null(Club.Terrain.HitObstacle(new Vec3(-109, 0, 2), new Vec3(109, 0, 2)));
        // Hand launch 3 m in front of the pilot, 1.8 m up, along the runway either way.
        Assert.Null(Club.Terrain.HitObstacle(new Vec3(-100, -22, 1.8), new Vec3(100, -22, 1.8)));
        Assert.Null(Club.Terrain.HitObstacle(new Vec3(-100, -22, 0.8), new Vec3(100, -22, 0.8)));
    }

    [Fact]
    public void Every_prop_stands_on_the_ground()
    {
        foreach (var p in Club.Props)
        {
            IReadOnlyList<Vec3> points = p is PowerLine line ? line.Poles : [p.Base];
            foreach (var b in points) Assert.Equal(ClubMap.Height(b.X, b.Y), b.Z, 9);
        }
    }

    [Fact]
    public void Surfaces_sum_to_one_and_match_their_zones()
    {
        for (double x = -1000; x <= 1000; x += 37)
        for (double y = -1000; y <= 1000; y += 41)
            Assert.Equal(1, Club.Surface(x, y).Sum, 9);
        Assert.True(Club.Surface(0, 0).MowedGrass > 0.99);
        Assert.True(Club.Surface(-30, -40).Gravel > 0.99);
        Assert.True(Club.Surface(0, 200).Grass > 0.99);
        bool farmland = false;
        for (double x = -950; x <= 950 && !farmland; x += 20)
        {
            var w = Club.Surface(x, 800);
            farmland = w.Wheat > 0.99 || w.Ploughed > 0.99;
        }
        Assert.True(farmland);
    }

    [Fact]
    public void Normal_tilts_away_from_the_uphill_direction()
    {
        // Scan x at y = 800 for a point where the ground rises with increasing x, and check the
        // normal there leans the opposite way (X < 0, i.e. away from the uphill slope).
        double xUp = FindRising(x => ClubMap.Height(x, 800));
        Assert.True(Club.Terrain.Normal(xUp, 800).X < 0);

        // Same check along y at x = 800: where the ground rises with increasing y, the normal's
        // Y component should be negative.
        double yUp = FindRising(y => ClubMap.Height(800, y));
        Assert.True(Club.Terrain.Normal(800, yUp).Y < 0);
    }

    static double FindRising(Func<double, double> heightAt)
    {
        const double step = 10;
        for (double t = -900; t < 900; t += step)
        {
            if (heightAt(t + step) > heightAt(t))
                return t + step / 2;
        }
        throw new InvalidOperationException("No rising segment found in the scanned range.");
    }

    [Fact]
    public void Backdrop_height_joins_the_grid_edge_and_fades_to_zero_beyond_300m()
    {
        foreach (var (x, y) in new[] { (ClubMap.HalfSize, 0.0), (0.0, ClubMap.HalfSize), (ClubMap.HalfSize, 400.0), (-ClubMap.HalfSize, -300.0) })
            Assert.Equal(Club.Grid.Height(x, y), ClubMap.BackdropHeight(x, y), 0.01);

        Assert.Equal(0, ClubMap.BackdropHeight(ClubMap.HalfSize + 301, 0));
        Assert.Equal(0, ClubMap.BackdropHeight(ClubMap.HalfSize + 2000, ClubMap.HalfSize + 2000));
    }

    [Fact]
    public void Catalog_finds_loads_and_falls_back_to_the_club()
    {
        Assert.Equal("club", FieldCatalog.Find("moon").Id);
        Assert.Same(FieldCatalog.Load("club"), FieldCatalog.Load("club"));
        Assert.Same(FieldCatalog.Load("club"), FieldCatalog.Load("moon"));
        Assert.Equal("FIELD_CLUB", FieldCatalog.Load("club").NameKey);
    }
}
