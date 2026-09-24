using SimLab.Flight.Numerics;

namespace SimLab.Flight.Tests.Numerics;

public class InterpolationTests
{
    static readonly double[] Xs = [0, 1, 3];
    static readonly double[] Ys = [10, 20, 0];

    [Theory]
    [InlineData(-5, 10)]
    [InlineData(0, 10)]
    [InlineData(0.5, 15)]
    [InlineData(2, 10)]
    [InlineData(9, 0)]
    public void Linear_interpolates_and_clamps(double x, double expected)
        => Assert.Equal(expected, Interpolation.Linear(Xs, Ys, x), 12);

    [Fact]
    public void Mismatched_lengths_throw()
        => Assert.Throws<ArgumentException>(() => Interpolation.Linear([0, 1], [1], 0.5));

    [Fact]
    public void Non_increasing_axis_is_rejected()
        => Assert.Throws<ArgumentException>(() => Interpolation.RequireIncreasing([0, 1, 1], "alpha"));
}
