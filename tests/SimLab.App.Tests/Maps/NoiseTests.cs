using SimLab.App.Maps;

namespace SimLab.App.Tests.Maps;

public class NoiseTests
{
    static IEnumerable<(double X, double Y)> Points(int count)
    {
        var rng = new Random(1);
        for (int k = 0; k < count; k++) yield return (rng.NextDouble() * 600 - 300, rng.NextDouble() * 600 - 300);
    }

    [Fact]
    public void Same_seed_gives_the_same_values()
    {
        var a = new Noise2(5);
        var b = new Noise2(5);
        foreach (var (x, y) in Points(200))
        {
            Assert.Equal(a.Value(x, y), b.Value(x, y));
            Assert.Equal(a.Fbm(x, y, 4), b.Fbm(x, y, 4));
            Assert.Equal(a.Ridged(x, y, 4), b.Ridged(x, y, 4));
        }
    }

    [Fact]
    public void Different_seeds_give_different_values()
    {
        var a = new Noise2(5);
        var b = new Noise2(6);
        int differ = Points(200).Count(p => Math.Abs(a.Value(p.X, p.Y) - b.Value(p.X, p.Y)) > 1e-6);
        Assert.True(differ > 180, $"{differ} of 200 differ");
    }

    [Fact]
    public void Value_and_fbm_stay_within_minus_one_and_one_and_use_the_range()
    {
        var n = new Noise2(11);
        double min = 1, max = -1;
        foreach (var (x, y) in Points(10_000))
        {
            double v = n.Value(x, y);
            Assert.InRange(v, -1, 1);
            Assert.InRange(n.Fbm(x, y, 5), -1, 1);
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }
        Assert.True(min < -0.5 && max > 0.5, $"range [{min}, {max}]");
    }

    [Fact]
    public void Ridged_stays_within_zero_and_one()
    {
        var n = new Noise2(11);
        foreach (var (x, y) in Points(10_000)) Assert.InRange(n.Ridged(x, y, 5), 0, 1);
    }

    [Fact]
    public void Value_is_smooth()
    {
        var n = new Noise2(11);
        foreach (var (x, y) in Points(500))
            Assert.True(Math.Abs(n.Value(x + 0.001, y) - n.Value(x, y)) < 0.01);
    }
}
