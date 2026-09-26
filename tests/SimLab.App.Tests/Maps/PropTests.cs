using SimLab.App.Maps;
using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Tests.Maps;

public class PropTests
{
    static readonly Rgb Green = new(0.2f, 0.3f, 0.1f);

    static MapTerrain Around(Prop prop) => new(HeightGrid.Sample(10, 5, (_, _) => prop.Base.Z), prop.Collision());

    [Fact]
    public void Foliage_hitbox_sits_inside_the_drawn_foliage()
    {
        Assert.True(Foliage.HitScale < Foliage.CoreScale);
        Assert.Equal(0.85, Foliage.HitScale);
    }

    [Fact]
    public void Broadleaf_hitbox_matches_what_is_drawn()
    {
        var tree = new BroadleafTree(new Vec3(5, 5, 2), 0, 15, 4.5, Green);
        var t = Around(tree);
        var c = tree.CrownCentre;
        Assert.Equal(ObstacleKind.Tree, t.HitObstacle(new Vec3(5, 5, 3)));             // trunk
        Assert.Equal(ObstacleKind.Tree, t.HitObstacle(c));                             // heart of the crown
        Assert.Null(t.HitObstacle(new Vec3(6, 5, 4)));                                 // 1 m beside the trunk, below the crown
        Assert.Null(t.HitObstacle(new Vec3(5, 5, 2 + 15 + 0.5)));                      // above the top
        Assert.Null(t.HitObstacle(c + new Vec3(0.9 * tree.CrownRadius, 0, 0)));        // outer 15 % of the foliage
        Assert.Equal(ObstacleKind.Tree, t.HitObstacle(c + new Vec3(0.8 * tree.CrownRadius, 0, 0)));
        // The drawn parts use the same dimensions.
        var parts = tree.Parts().ToList();
        var trunk = parts.Single(p => p.Mesh == PartMesh.Trunk);
        Assert.Equal(2 + tree.TrunkHeight, trunk.Centre.Z + trunk.Size.Z / 2, 9);
        var crown = parts.Single(p => p.Mesh == PartMesh.BroadleafCrown);
        Assert.Equal(c, crown.Centre);
        Assert.Equal(new Vec3(9, 9, 2 * tree.CrownVerticalRadius), crown.Size);
        Assert.Equal(2 + 15, crown.Centre.Z + crown.Size.Z / 2, 9);
    }

    [Fact]
    public void Conifer_hitbox_is_a_cone_inside_the_drawn_crown()
    {
        var tree = new ConiferTree(Vec3.Zero, 0, 16, 4, Green);
        var t = Around(tree);
        double just = tree.CrownBottom + 0.1, envelope = 4 * (1 - 0.1 / tree.CrownHeight);
        Assert.Equal(ObstacleKind.Tree, t.HitObstacle(new Vec3(0, 0, 1)));
        Assert.Null(t.HitObstacle(new Vec3(1, 0, 1)));
        Assert.Null(t.HitObstacle(new Vec3(0, 0, 16.5)));
        Assert.Equal(ObstacleKind.Tree, t.HitObstacle(new Vec3(0.8 * envelope, 0, just)));
        Assert.Null(t.HitObstacle(new Vec3(0.9 * envelope, 0, just)));
        var crown = tree.Parts().Single(p => p.Mesh == PartMesh.ConiferCrown);
        Assert.Equal(16, crown.Centre.Z + crown.Size.Z / 2, 9);
        Assert.Equal(tree.CrownBottom, crown.Centre.Z - crown.Size.Z / 2, 9);
    }

    [Fact]
    public void Poplar_bush_and_hedge_collide_as_foliage()
    {
        var poplar = new PoplarTree(Vec3.Zero, 0, 22, 2.6, Green);
        Assert.Equal(ObstacleKind.Tree, Around(poplar).HitObstacle(poplar.CrownCentre));
        Assert.Null(Around(poplar).HitObstacle(poplar.CrownCentre + new Vec3(0.9 * 2.6, 0, 0)));
        var bush = new Bush(Vec3.Zero, 0, 1.5, 2, Green);
        Assert.Equal(ObstacleKind.Tree, Around(bush).HitObstacle(new Vec3(0, 0, 1)));
        Assert.Null(Around(bush).HitObstacle(new Vec3(1.4, 0, 1)));
        var hedge = new Hedge(Vec3.Zero, 90, 20, 1.5, 2.2, Green);
        Assert.Equal(ObstacleKind.Tree, Around(hedge).HitObstacle(new Vec3(0, -8, 1)));   // yaw 90: length runs south
        Assert.Null(Around(hedge).HitObstacle(new Vec3(-9, 0, 1)));
    }

    [Fact]
    public void Structures_collide_exactly_with_their_drawn_volume()
    {
        var wood = new Rgb(0.4f, 0.3f, 0.2f);
        var hangar = new Building(new Vec3(0, 0, 1), 0, 12, 6, 3.2, 1.2, false, wood, wood);
        Assert.Equal(ObstacleKind.Structure, Around(hangar).HitObstacle(new Vec3(5.9, 2.9, 1 + 4.3)));
        Assert.Null(Around(hangar).HitObstacle(new Vec3(0, 0, 1 + 4.5)));
        Assert.Contains(hangar.Parts(), p => p.Mesh == PartMesh.Roof);
        var shelter = new Building(Vec3.Zero, 0, 8, 5, 2.4, 0.15, true, wood, wood);
        Assert.Equal(4, shelter.Parts().Count(p => p.Mesh == PartMesh.Post));
        var car = new Car(Vec3.Zero, -90, wood);                                          // facing north
        Assert.Equal(ObstacleKind.Structure, Around(car).HitObstacle(new Vec3(0, 2, 1)));
        Assert.Null(Around(car).HitObstacle(new Vec3(1.2, 0, 1)));
        Assert.Equal(ObstacleKind.Structure, Around(new Table(Vec3.Zero, 0, wood)).HitObstacle(new Vec3(0.8, 0, 0.7)));
        var fence = new Fence(Vec3.Zero, 0, 34, wood);
        Assert.Equal(ObstacleKind.Structure, Around(fence).HitObstacle(new Vec3(16, 0, 1)));
        Assert.Equal(18, fence.Parts().Count(p => p.Mesh == PartMesh.Post));
    }

    [Fact]
    public void Power_line_has_poles_and_sagging_wires()
    {
        var line = new PowerLine([new Vec3(0, 0, 0), new Vec3(50, 0, 0), new Vec3(100, 0, 1)]);
        var t = Around(line);
        Assert.Equal(ObstacleKind.Structure, t.HitObstacle(new Vec3(50, 0, 4)));
        var mid = line.WirePoint(0, 1, 0.5);
        Assert.Equal(8.6 - 1.5, mid.Z, 9);
        Assert.Equal(0.8, mid.Y, 9);                          // span heading east: side +1 is local y, north
        Assert.Equal(ObstacleKind.Wire, t.HitObstacle(mid));
        Assert.Equal(ObstacleKind.Wire, t.HitObstacle(line.WirePoint(1, -1, 0.3)));
        Assert.Null(t.HitObstacle(mid - new Vec3(0, 0, 0.3)));
        // 2 spans × 2 wires × 8 segments, 3 poles, 3 cross-arms.
        Assert.Equal(32, line.Parts().Count(p => p.Mesh == PartMesh.Wire));
        Assert.Equal(3, line.Parts().Count(p => p.Mesh == PartMesh.Post));
    }

    [Fact]
    public void Field_map_collects_prop_collisions()
    {
        var tree = new BroadleafTree(new Vec3(10, 10, 0), 0, 12, 4, Green);
        var map = new FieldMap("test", "FIELD_TEST", HeightGrid.Sample(100, 10, (_, _) => 0), (_, _) => SurfaceWeights.Only(SurfaceKind.Grass),
            new MapLayout(Vec3.Zero, 1.7, Vec3.Zero, Vec3.Zero, 50, 10, 90),
            new MapAmbience(Green, Green, Green, Green, 0), [tree], []);
        Assert.Equal(2, map.Terrain.Obstacles.All.Count);
        Assert.Equal(ObstacleKind.Tree, map.Terrain.HitObstacle(tree.CrownCentre));
        Assert.Equal(1, map.Surface(3, 4).Grass);
    }
}
