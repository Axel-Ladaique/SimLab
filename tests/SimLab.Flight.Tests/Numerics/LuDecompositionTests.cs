using SimLab.Flight.Numerics;

namespace SimLab.Flight.Tests.Numerics;

public class LuDecompositionTests
{
    [Fact]
    public void Solves_a_small_system()
    {
        var lu = new LuDecomposition(new double[,] { { 2, 1 }, { 1, 3 } });
        var x = new double[2];
        lu.Solve([3, 5], x);
        Assert.Equal(0.8, x[0], 12);
        Assert.Equal(1.4, x[1], 12);
    }

    [Fact]
    public void Pivots_around_a_zero_diagonal()
    {
        var lu = new LuDecomposition(new double[,] { { 0, 1 }, { 1, 0 } });
        var x = new double[2];
        lu.Solve([2, 3], x);
        Assert.Equal(3, x[0], 12);
        Assert.Equal(2, x[1], 12);
    }

    [Fact]
    public void Solves_many_right_hand_sides_with_one_factorization()
    {
        var a = new double[,] { { 4, -2, 1 }, { -2, 4, -2 }, { 1, -2, 4 } };
        var lu = new LuDecomposition(a);
        var x = new double[3];
        foreach (var expected in new[] { new double[] { 1, 2, 3 }, [-1, 0, 5] })
        {
            var b = new double[3];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) b[i] += a[i, j] * expected[j];
            lu.Solve(b, x);
            for (int i = 0; i < 3; i++) Assert.Equal(expected[i], x[i], 10);
        }
    }

    [Fact]
    public void Rejects_a_singular_matrix()
    {
        Assert.Throws<InvalidOperationException>(() => new LuDecomposition(new double[,] { { 1, 2 }, { 2, 4 } }));
    }
}
