using SimLab.Flight.Atmosphere;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Atmosphere;

public class IsaTests
{
    [Fact]
    public void Sea_level_density() => Assert.Equal(1.225, Isa.Density(0), 3);

    [Fact]
    public void Density_at_1000_m() => Assert.Equal(1.112, Isa.Density(1000), 2);

    [Fact]
    public void Warmer_air_is_thinner() => Assert.True(Isa.Density(0, 15) < Isa.Density(0));
}

public class WindFieldTests
{
    [Fact]
    public void Wind_from_west_blows_toward_east()
    {
        var wind = new WindField(new WindSettings(SpeedAt10m: 5, FromDirectionDeg: 270), seed: 1);
        var v = wind.SteadyAt(10);
        Assert.Equal(5, v.X, 9);
        Assert.Equal(0, v.Y, 9);
        Assert.Equal(0, v.Z, 9);
    }

    [Fact]
    public void Wind_from_north_blows_toward_south()
        => Assert.Equal(-4, new WindField(new WindSettings(4, 0), 1).SteadyAt(10).Y, 9);

    [Fact]
    public void Log_profile_is_weaker_near_the_ground()
    {
        var wind = new WindField(new WindSettings(SpeedAt10m: 6), 1);
        Assert.True(wind.SteadyAt(2).Length < wind.SteadyAt(10).Length);
        Assert.True(wind.SteadyAt(50).Length > wind.SteadyAt(10).Length);
    }

    [Fact]
    public void No_turbulence_setting_means_zero_gusts()
    {
        var wind = new WindField(new WindSettings(SpeedAt10m: 6, Turbulence: 0), 1);
        for (int i = 0; i < 1000; i++) wind.Advance(0.002, 30, 15);
        Assert.Equal(Vec3.Zero, wind.Turbulence);
    }

    [Fact]
    public void Dryden_vertical_gust_has_expected_standard_deviation()
    {
        var wind = new WindField(new WindSettings(SpeedAt10m: 6, Turbulence: 1), seed: 42);
        double expectedSigma = 0.1 * wind.SteadyAt(6.1).Length;
        double sum = 0, sumSq = 0;
        int n = 0;
        for (int i = 0; i < 100_000; i++)
        {
            wind.Advance(0.002, 50, 15);
            double w = wind.Turbulence.Z;
            sum += w; sumSq += w * w; n++;
        }
        double mean = sum / n;
        double sigma = Math.Sqrt(sumSq / n - mean * mean);
        Assert.InRange(sigma, 0.75 * expectedSigma, 1.25 * expectedSigma);
        Assert.True(Math.Abs(mean) < 0.4 * expectedSigma, $"mean {mean}");
    }

    [Fact]
    public void Same_seed_gives_same_turbulence()
    {
        var a = new WindField(new WindSettings(6, 0, 1), 7);
        var b = new WindField(new WindSettings(6, 0, 1), 7);
        for (int i = 0; i < 500; i++) { a.Advance(0.002, 20, 12); b.Advance(0.002, 20, 12); }
        Assert.Equal(a.Turbulence, b.Turbulence);
    }

    [Fact]
    public void Reset_restarts_the_turbulence_sequence()
    {
        var wind = new WindField(new WindSettings(6, 0, 1), 7);
        var first = new List<Vec3>();
        for (int i = 0; i < 500; i++) { wind.Advance(0.002, 20, 12); first.Add(wind.Turbulence); }
        wind.Reset();
        Assert.Equal(Vec3.Zero, wind.Turbulence);
        for (int i = 0; i < 500; i++) { wind.Advance(0.002, 20, 12); Assert.Equal(first[i], wind.Turbulence); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    public void Non_positive_roughness_length_is_rejected(double z0)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new WindField(new WindSettings(5, 0, 0, z0), 1));
}
