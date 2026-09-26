using SimLab.Flight.Atmosphere;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Tests.Atmosphere;

public class TerrainWindTests
{
    /// <summary>Gaussian ridge running north–south with its 150 m crest on x = 0.</summary>
    sealed class Ridge(double sigma = 200) : ITerrain
    {
        public double Height(double x, double y) => 150 * Math.Exp(-x * x / (2 * sigma * sigma));
        public Vec3 Normal(double x, double y) { double e = 0.5, dx = (Height(x + e, y) - Height(x - e, y)) / (2 * e); return new Vec3(-dx, 0, 1).Normalized(); }
        public double? WaterSurface(double x, double y) => null;
        public ObstacleKind? HitObstacle(Vec3 p) => null;
        public ObstacleKind? HitObstacle(Vec3 a, Vec3 b) => null;
    }

    static WindField West(double speed = 8, ITerrain? terrain = null, double from = 270) =>
        new(new WindSettings(speed, from, 0), 1, new TerrainWind(terrain ?? new Ridge(), -2000, -2000, 2000, 2000, from));

    static Vec3 WindAt(WindField wind, ITerrain terrain, double x, double agl) =>
        wind.At(new Vec3(x, 0, terrain.Height(x, 0) + agl), agl);

    static double Horizontal(Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

    [Fact]
    public void Flat_terrain_matches_the_height_only_wind()
    {
        var wind = new WindField(new WindSettings(8, 270, 1), 1, new TerrainWind(new FlatTerrain(), -2000, -2000, 2000, 2000, 270));
        for (int i = 0; i < 200; i++) wind.Advance(0.002, 20, 12);
        foreach (var (x, y) in new[] { (0.0, 0.0), (-512.3, 77.0), (1500.0, -1800.0), (-1999.0, 1999.0) })
        foreach (var h in new[] { 0.2, 5.0, 20.0, 150.0, 600.0 })
        {
            var expected = wind.At(h);
            var actual = wind.At(new Vec3(x, y, h), h);
            Assert.Equal(expected.X, actual.X);
            Assert.Equal(expected.Y, actual.Y);
            Assert.Equal(expected.Z, actual.Z);
        }
    }

    [Fact]
    public void Windward_face_lifts()
    {
        var ridge = new Ridge();
        Assert.True(WindAt(West(), ridge, -200, 20).Z > 1);
    }

    [Fact]
    public void Lift_is_strongest_near_the_ground()
    {
        var ridge = new Ridge();
        var wind = West();
        double low = WindAt(wind, ridge, -200, 10).Z;
        double mid = WindAt(wind, ridge, -200, 150).Z;
        double high = WindAt(wind, ridge, -200, 600).Z;
        Assert.True(low > mid);
        Assert.True(mid > high);
        Assert.True(high < 0.3);
    }

    [Fact]
    public void Steeper_face_lifts_more()
    {
        var steep = new Ridge(120);
        var gentle = new Ridge(300);
        double steepLift = WindAt(West(terrain: steep), steep, -120, 20).Z;
        double gentleLift = WindAt(West(terrain: gentle), gentle, -300, 20).Z;
        Assert.True(steepLift > gentleLift);
    }

    [Fact]
    public void Lee_side_sinks_and_is_turbulent()
    {
        var ridge = new Ridge();
        var wind = West();
        var lee = WindAt(wind, ridge, 250, 15);
        var flat = West(terrain: new FlatTerrain()).At(15);
        Assert.True(lee.Z < 0);
        Assert.True(Horizontal(lee) < 0.5 * Horizontal(flat));
        Assert.True(wind.Terrain!.Sample(250, 0).Shelter > 0.5);
    }

    [Fact]
    public void Far_upwind_is_unchanged()
    {
        var ridge = new Ridge();
        var actual = WindAt(West(), ridge, -1900, 20);
        var flat = West(terrain: new FlatTerrain()).At(20);
        Assert.Equal(flat.X, actual.X, 1e-9);
        Assert.Equal(flat.Y, actual.Y, 1e-9);
        Assert.Equal(flat.Z, actual.Z, 1e-9);
    }

    [Fact]
    public void Turning_the_wind_swaps_the_faces()
    {
        var ridge = new Ridge();
        var wind = West(from: 90);
        Assert.True(WindAt(wind, ridge, 200, 20).Z > 1);
        Assert.True(WindAt(wind, ridge, -250, 15).Z < 0);
    }
}
