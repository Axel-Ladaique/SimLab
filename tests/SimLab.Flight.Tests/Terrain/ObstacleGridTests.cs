using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Tests.Terrain;

public class ObstacleGridTests
{
    static List<Obstacle> RandomObstacles(Random rng)
    {
        double U(double lo, double hi) => lo + rng.NextDouble() * (hi - lo);
        var list = new List<Obstacle>();
        for (int i = 0; i < 300; i++)
        {
            var at = new Vec3(U(-200, 200), U(-200, 200), U(0, 5));
            IObstacleShape shape = (i % 5) switch
            {
                0 => new VerticalCylinder(at, U(0.05, 3), U(2, 20)),
                1 => new VerticalCone(at, U(1, 5), U(5, 20)),
                2 => new Ellipsoid(at + new Vec3(0, 0, 8), U(1, 6), U(1, 6)),
                // Some boxes are 80 m long, so they span several 32 m cells.
                3 => new OrientedBox(at, new Vec3(U(0.5, 40), U(0.5, 3), U(0.5, 5)), U(0, 360)),
                _ => new Capsule(at + new Vec3(0, 0, 7), at + new Vec3(U(-50, 50), U(-50, 50), 7), 0.1),
            };
            list.Add(new Obstacle(shape, (ObstacleKind)(i % 3)));
        }
        return list;
    }

    [Fact]
    public void Grid_agrees_with_a_brute_force_scan()
    {
        var rng = new Random(1);
        var obstacles = RandomObstacles(rng);
        var grid = new ObstacleGrid(obstacles);
        Assert.Equal(obstacles.Count, grid.All.Count);
        double U(double lo, double hi) => lo + rng.NextDouble() * (hi - lo);
        for (int i = 0; i < 5000; i++)
        {
            var p = new Vec3(U(-220, 220), U(-220, 220), U(0, 25));
            Assert.Equal(obstacles.Any(o => o.Shape.Contains(p)), grid.Hit(p) is not null);
        }
        for (int i = 0; i < 2000; i++)
        {
            var a = new Vec3(U(-220, 220), U(-220, 220), U(0, 25));
            // Mostly aircraft-sized segments, some long ones crossing several cells.
            double reach = i % 10 == 0 ? 70 : 3;
            var b = a + new Vec3(U(-reach, reach), U(-reach, reach), U(-2, 2));
            Assert.Equal(obstacles.Any(o => o.Shape.Intersects(a, b)), grid.Hit(a, b) is not null);
        }
    }

    [Fact]
    public void Grid_reports_the_kind_of_the_obstacle_hit()
    {
        var grid = new ObstacleGrid(
        [
            new Obstacle(new VerticalCylinder(new Vec3(0, 0, 0), 1, 10), ObstacleKind.Tree),
            new Obstacle(new Capsule(new Vec3(100, -50, 8), new Vec3(100, 50, 8), 0.1), ObstacleKind.Wire),
        ]);
        Assert.Equal(ObstacleKind.Tree, grid.Hit(new Vec3(0.5, 0, 3)));
        Assert.Equal(ObstacleKind.Wire, grid.Hit(new Vec3(99, 0, 8), new Vec3(101, 0, 8)));
        Assert.Null(grid.Hit(new Vec3(50, 0, 3)));
        Assert.Null(new ObstacleGrid([]).Hit(Vec3.Zero, new Vec3(1, 1, 1)));
    }
}
