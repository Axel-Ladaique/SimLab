using SimLab.App.Maps;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Maps;

public class HeightGridTests
{
    // 3 × 3 grid, step 2, from (−2, −2): heights chosen so the two triangles of each cell differ.
    static readonly HeightGrid Grid = new(-2, -2, 2, 3, [0, 1, 4, 2, 5, 3, 1, 0, 2]);

    [Fact]
    public void Exact_at_vertices()
    {
        for (int j = 0; j < 3; j++)
        for (int i = 0; i < 3; i++)
            Assert.Equal(Grid[i, j], Grid.Height(-2 + 2 * i, -2 + 2 * j), 12);
    }

    [Fact]
    public void Planar_inside_each_triangle()
    {
        // Lower-right triangle a,b,d of cell (0,0): a=(−2,−2) 0, b=(0,−2) 1, d=(0,0) 5.
        Assert.Equal(0 + 0.75 * (1 - 0) + 0.25 * (5 - 1), Grid.Height(-2 + 1.5, -2 + 0.5), 12);
        // Upper-left triangle a,c,d: c=(−2,0) 2.
        Assert.Equal(0 + 0.75 * (2 - 0) + 0.25 * (5 - 2), Grid.Height(-2 + 0.5, -2 + 1.5), 12);
    }

    [Fact]
    public void Continuous_across_the_diagonal_and_cell_edges()
    {
        for (double t = 0; t <= 1; t += 0.1)
        {
            double x = -2 + 2 * t, y = -2 + 2 * t;
            Assert.Equal(Grid.Height(x + 1e-9, y), Grid.Height(x, y + 1e-9), 6);
            Assert.Equal(Grid.Height(0 - 1e-9, -2 + 2 * t), Grid.Height(0 + 1e-9, -2 + 2 * t), 6);
        }
    }

    [Fact]
    public void Clamped_outside()
    {
        Assert.Equal(Grid.Height(-2, -2), Grid.Height(-50, -50), 12);
        Assert.Equal(Grid.Height(2, 0.5), Grid.Height(90, 0.5), 12);
    }

    [Fact]
    public void Normal_is_the_triangle_normal()
    {
        var n = Grid.Normal(-2 + 1.5, -2 + 0.5);                 // triangle a,b,d: dz/dx = 0.5, dz/dy = 2
        var expected = new Vec3(-0.5, -2, 1).Normalized();
        Assert.Equal(expected.X, n.X, 12);
        Assert.Equal(expected.Y, n.Y, 12);
        Assert.Equal(expected.Z, n.Z, 12);
    }

    [Fact]
    public void Sample_covers_the_half_size_with_the_step()
    {
        var g = HeightGrid.Sample(10, 5, (x, y) => x + 2 * y);
        Assert.Equal(5, g.Count);
        Assert.Equal(-10, g.MinX);
        Assert.Equal(10, g.MaxY);
        Assert.Equal(10, g.HalfSize);
        Assert.Equal(3 + 2 * 4, g.Height(3, 4), 9);               // planar function reproduced exactly
    }
}
