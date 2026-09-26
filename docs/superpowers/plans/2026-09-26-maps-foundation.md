# Maps Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the club-only field with a map model whose render and collision come from one description, fix
the oversized tree hitboxes, catch thin obstacles between hull points, and rebuild the club field with textured
ground, varied vegetation, farmland, a road, a power line and club buildings.

**Architecture:** `SimLab.Flight.Terrain` gets exact obstacle shapes, an `ObstacleGrid` and segment queries;
`GroundContactModel` tests hull points and every pair of them. `SimLab.App.Maps` holds a pure, tested `FieldMap`
(height, surfaces, layout, ambience, props, overlays). Each `Prop` produces both its drawn parts and its collision
from the same dimensions. `ClubMap.Create()` builds the club. The Godot side (`MapBuilder`, `GroundOverlays`,
`PropLayer`, `PropMeshes`, a terrain shader) draws any `FieldMap`.

**Tech Stack:** .NET 8 libraries (SDK 10), C#, xUnit, Godot 4.7.2 .NET.

**Spec:** `docs/superpowers/specs/2026-09-26-maps-foundation-design.md`

## Global Constraints

- Code, comments, docs and commit messages in English; `game/translations/strings.csv` has `keys,fr,en` columns
  (quote fields containing commas).
- Body axes x back, y right, z up; world ENU (x east, y north, z up). Godot world is x east, y up, z south:
  `(x, y, z)ENU → (x, z, −y)`; use `WorldToGodot()` / `GodotBasis`.
- **Yaw convention for props and boxes:** `YawDeg` rotates a horizontal frame clockwise seen from above. At 0° local
  x is east and local y north; at 90° local x is south and local y east (`PlanarYaw`, Task 1).
- `TreatWarningsAsErrors` is on; `dotnet build game/SimLab.Game.csproj` must give 0 warnings.
- Foliage hitbox = **85 %** of the drawn envelope (`Foliage.HitScale`); foliage meshes have a core at **90 %**
  (`Foliage.CoreScale`). Trunks, buildings, cars, tables, fences and poles collide exactly.
- Wires collide with a radius of **0.10 m** (`PowerLine.WireHitRadius`); crash detection runs at 500 Hz.
- Club field: seed 7, relief formula unchanged, runway 100 × 15 m east–west centred on the origin, pilot (0, −25),
  eye 1.7 m, windsock (20, −28), half size 1000 m, vegetation keep-out |x| < 110 and |y| < 60.
- Terrain textures: ≤ 30 MB total, CC0, credited in `game/Textures/CREDITS.md`. **Only the controller downloads,
  after asking the user.**
- If `dotnet` is not found: `export PATH="/usr/local/share/dotnet:$PATH"`. Godot:
  `GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot`.
- Commit messages: subject, blank line, then exactly `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
  (use `git commit -F <file>`; check with `git log -1 --format='%(trailers:key=Co-Authored-By,valueonly)'`).
- Branch `feat/maps-foundation`. Never edit the user's settings file
  (`~/Library/Application Support/Godot/app_userdata/SimLab/settings.json`). Scripted runs never save settings.
- The disk is nearly full: if a build fails with ENOSPC, stop and report it.

## File Structure

| File | Responsibility |
|---|---|
| `src/SimLab.Flight/Geometry/PlanarYaw.cs` (new) | Yawed horizontal frames ↔ world |
| `src/SimLab.Flight/Terrain/ObstacleShapes.cs` (new) | `IObstacleShape`, `Footprint`, 5 shapes, shared math |
| `src/SimLab.Flight/Terrain/ObstacleGrid.cs` (new) | `ObstacleKind`, `Obstacle`, spatial grid |
| `src/SimLab.Flight/Terrain/ITerrain.cs` | `HitObstacle(point)` / `HitObstacle(segment)`; `CylinderObstacle` removed in Task 7 |
| `src/SimLab.Flight/Terrain/FlatTerrain.cs` | Obstacles through the grid |
| `src/SimLab.Flight/Ground/GroundContactModel.cs`, `GroundSpec.cs` | Segment checks, new crash causes |
| `src/SimLab.App/Maps/MapLayout.cs`, `SurfaceWeights.cs`, `MapAmbience.cs`, `MapOverlay.cs`, `MapTerrain.cs` (new) | Map model pieces |
| `src/SimLab.App/Maps/Prop.cs`, `Vegetation.cs`, `Structures.cs`, `FieldMap.cs` (new) | Props (parts + collision) and the map |
| `src/SimLab.App/Maps/FieldCatalog.cs` (moved from `Field/`) | Map list, lookup, cached loading |
| `src/SimLab.App/Maps/Club/ClubMap.cs`, `ClubParcels.cs`, `ClubPlanting.cs`, `ClubFurniture.cs` (new) | The club map |
| `src/SimLab.App/Mapping/GodotBasis.cs` | `PartRotation` for prop parts |
| `src/SimLab.App/Field/ClubField.cs`, `ClubFieldTerrain.cs`, `TreePlanter.cs` | Deleted in Task 7 |
| `game/Scripts/World/MapBuilder.cs` (replaces `FieldBuilder.cs`) | Environment, sun, terrain mesh, windsock |
| `game/Scripts/World/GroundOverlays.cs` (new) | Runway stripes, pilot box, track/road ribbons |
| `game/Scripts/World/PropMeshes.cs`, `PropLayer.cs` (new) | Unit meshes; tiled MultiMeshes with LOD |
| `game/Shaders/terrain.gdshader` (new) | Surface blend (colours, then textures) |
| `game/Textures/terrain/*.jpg`, `game/Textures/CREDITS.md` (new) | CC0 textures |

---

### Task 1: Obstacle shapes and `PlanarYaw`

**Files:**
- Create: `src/SimLab.Flight/Geometry/PlanarYaw.cs`
- Create: `src/SimLab.Flight/Terrain/ObstacleShapes.cs`
- Test: `tests/SimLab.Flight.Tests/Terrain/ObstacleShapeTests.cs`

**Interfaces:**
- Produces: `PlanarYaw.ToWorld(double localX, double localY, double yawDeg) → (double X, double Y)`,
  `PlanarYaw.ToLocal(double worldX, double worldY, double yawDeg) → (double X, double Y)`,
  `PlanarYaw.Of(double dx, double dy) → double` (yaw whose local x points along (dx, dy));
  `Footprint(double MinX, double MinY, double MaxX, double MaxY)`;
  `IObstacleShape { bool Contains(Vec3); bool Intersects(Vec3 a, Vec3 b); Footprint Footprint { get; } }`;
  `VerticalCylinder(Vec3 Base, double Radius, double Height)`, `VerticalCone(Vec3 Base, double BaseRadius, double Height)`,
  `Ellipsoid(Vec3 Centre, double HorizontalRadius, double VerticalRadius)`,
  `OrientedBox(Vec3 Centre, Vec3 HalfExtents, double YawDeg)` (half extents along local x, local y, up),
  `Capsule(Vec3 A, Vec3 B, double Radius)` — all `sealed record` classes implementing `IObstacleShape`.

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.Flight.Tests/Terrain/ObstacleShapeTests.cs`:

```csharp
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.Flight.Tests.Terrain;

public class ObstacleShapeTests
{
    [Fact]
    public void Planar_yaw_turns_clockwise_seen_from_above()
    {
        var (x, y) = PlanarYaw.ToWorld(1, 0, 90);
        Assert.Equal(0, x, 9);
        Assert.Equal(-1, y, 9);
        var (ex, ey) = PlanarYaw.ToWorld(0, 1, 90);
        Assert.Equal(1, ex, 9);
        Assert.Equal(0, ey, 9);
        var (lx, ly) = PlanarYaw.ToLocal(PlanarYaw.ToWorld(3, -2, 37).X, PlanarYaw.ToWorld(3, -2, 37).Y, 37);
        Assert.Equal(3, lx, 9);
        Assert.Equal(-2, ly, 9);
        Assert.Equal(0, PlanarYaw.Of(1, 0), 9);
        Assert.Equal(90, PlanarYaw.Of(0, -1), 9);
        var (dx, dy) = PlanarYaw.ToWorld(1, 0, PlanarYaw.Of(3, 4));
        Assert.Equal(0.6, dx, 9);
        Assert.Equal(0.8, dy, 9);
    }

    [Fact]
    public void Vertical_cylinder_contains_points_and_catches_a_crossing_segment()
    {
        var pole = new VerticalCylinder(new Vec3(10, 20, 2), 0.05, 8);
        Assert.True(pole.Contains(new Vec3(10.03, 20, 5)));
        Assert.False(pole.Contains(new Vec3(10.1, 20, 5)));
        Assert.False(pole.Contains(new Vec3(10, 20, 10.5)));
        Assert.False(pole.Contains(new Vec3(10, 20, 1.5)));
        // Both ends are outside, the segment passes through the pole.
        Assert.True(pole.Intersects(new Vec3(9, 20, 5), new Vec3(11, 20, 5)));
        Assert.False(pole.Intersects(new Vec3(9, 20.1, 5), new Vec3(11, 20.1, 5)));
        Assert.False(pole.Intersects(new Vec3(9, 20, 11), new Vec3(11, 20, 11)));
        // Diagonal segment entering through the top cap.
        Assert.True(pole.Intersects(new Vec3(10, 20, 12), new Vec3(10.01, 20, 9)));
        Assert.Equal(new Footprint(9.95, 19.95, 10.05, 20.05), pole.Footprint);
    }

    [Fact]
    public void Vertical_cone_narrows_to_its_tip()
    {
        var cone = new VerticalCone(new Vec3(0, 0, 1), 4, 10);
        Assert.True(cone.Contains(new Vec3(3.9, 0, 1)));
        Assert.False(cone.Contains(new Vec3(3.9, 0, 9)));
        Assert.True(cone.Contains(new Vec3(0.3, 0, 10)));
        Assert.False(cone.Contains(new Vec3(0, 0, 11.1)));
        Assert.True(cone.Intersects(new Vec3(-5, 0, 3), new Vec3(5, 0, 3)));
        Assert.False(cone.Intersects(new Vec3(-5, 3.9, 9), new Vec3(5, 3.9, 9)));
    }

    [Fact]
    public void Ellipsoid_uses_its_horizontal_and_vertical_radii()
    {
        var crown = new Ellipsoid(new Vec3(0, 0, 10), 4, 5);
        Assert.True(crown.Contains(new Vec3(3.6, 0, 10)));
        Assert.False(crown.Contains(new Vec3(4.4, 0, 10)));
        Assert.True(crown.Contains(new Vec3(0, 0, 14.5)));
        Assert.False(crown.Contains(new Vec3(0, 0, 15.5)));
        Assert.True(crown.Intersects(new Vec3(-6, 0, 10), new Vec3(6, 0, 10)));
        Assert.False(crown.Intersects(new Vec3(-6, 4.5, 10), new Vec3(6, 4.5, 10)));
        Assert.Equal(new Footprint(-4, -4, 4, 4), crown.Footprint);
    }

    [Fact]
    public void Oriented_box_follows_its_yaw()
    {
        var box = new OrientedBox(new Vec3(100, 50, 2), new Vec3(6, 1, 2), 30);
        Vec3 Local(double x, double y, double z)
        {
            var (wx, wy) = PlanarYaw.ToWorld(x, y, 30);
            return new Vec3(100 + wx, 50 + wy, 2 + z);
        }
        Assert.True(box.Contains(Local(5.9, 0.9, 1.9)));
        Assert.False(box.Contains(Local(6.1, 0, 0)));
        Assert.False(box.Contains(Local(0, 1.1, 0)));
        Assert.False(box.Contains(Local(0, 0, 2.1)));
        Assert.True(box.Intersects(Local(0, -3, 0), Local(0, 3, 0)));
        Assert.False(box.Intersects(Local(-8, 1.2, 0), Local(8, 1.2, 0)));
        var turned = new OrientedBox(Vec3.Zero, new Vec3(6, 1, 2), 90).Footprint;
        Assert.Equal(-1, turned.MinX, 9);
        Assert.Equal(6, turned.MaxY, 9);
    }

    [Fact]
    public void Capsule_catches_a_segment_passing_within_its_radius()
    {
        var wire = new Capsule(new Vec3(20, -10, 5), new Vec3(20, 10, 5), 0.10);
        Assert.True(wire.Contains(new Vec3(20.05, 3, 5)));
        Assert.False(wire.Contains(new Vec3(20.2, 3, 5)));
        Assert.True(wire.Intersects(new Vec3(19, 0, 5.05), new Vec3(21, 0, 5.05)));
        Assert.False(wire.Intersects(new Vec3(19, 0, 5.15), new Vec3(21, 0, 5.15)));
        Assert.False(wire.Intersects(new Vec3(19, 11, 5), new Vec3(21, 11, 5)));
        Assert.Equal(new Footprint(19.9, -10.1, 20.1, 10.1), wire.Footprint);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.Flight.Tests --filter ObstacleShapeTests`
Expected: FAIL to build (`PlanarYaw`, `VerticalCylinder`… not defined).

- [ ] **Step 3: Implement `PlanarYaw`**

`src/SimLab.Flight/Geometry/PlanarYaw.cs`:

```csharp
namespace SimLab.Flight.Geometry;

/// <summary>
/// Horizontal frames yawed clockwise (seen from above) from the world axes. At 0° local x is east and local y north;
/// at 90° local x is south and local y east. Used for boxes, props and runways.
/// </summary>
public static class PlanarYaw
{
    public static (double X, double Y) ToWorld(double localX, double localY, double yawDeg)
    {
        double a = Angle.Rad(yawDeg), c = Math.Cos(a), s = Math.Sin(a);
        return (localX * c + localY * s, -localX * s + localY * c);
    }

    public static (double X, double Y) ToLocal(double worldX, double worldY, double yawDeg)
    {
        double a = Angle.Rad(yawDeg), c = Math.Cos(a), s = Math.Sin(a);
        return (worldX * c - worldY * s, worldX * s + worldY * c);
    }

    /// <summary>Yaw whose local x axis points along the horizontal direction (dx, dy).</summary>
    public static double Of(double dx, double dy) => Angle.Deg(Math.Atan2(-dy, dx));
}
```

- [ ] **Step 4: Implement the shapes**

`src/SimLab.Flight/Terrain/ObstacleShapes.cs`:

```csharp
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

/// <summary>Horizontal bounding rectangle (world x east, y north) of an obstacle, for the spatial grid.</summary>
public readonly record struct Footprint(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>A solid volume the aircraft must not enter. World ENU axes (x east, y north, z up).</summary>
public interface IObstacleShape
{
    bool Contains(Vec3 p);

    /// <summary>True if any point of the segment a→b is inside the shape.</summary>
    bool Intersects(Vec3 a, Vec3 b);

    Footprint Footprint { get; }
}

static class ShapeMath
{
    /// <summary>Spacing of the points tested along a segment by the shapes without an exact segment test. They are
    /// the large ones (crowns, bushes), many times this size.</summary>
    public const double SampleSpacing = 0.10;

    public static bool SampledIntersects(IObstacleShape shape, Vec3 a, Vec3 b)
    {
        var d = b - a;
        int n = Math.Max(1, (int)Math.Ceiling(d.Length / SampleSpacing));
        for (int i = 0; i <= n; i++)
            if (shape.Contains(a + d * (i / (double)n))) return true;
        return false;
    }

    /// <summary>Narrows [t0, t1] to the parameters where start + t·delta lies in [min, max]; false if that is empty.</summary>
    public static bool ClipSlab(double start, double delta, double min, double max, ref double t0, ref double t1)
    {
        if (Math.Abs(delta) < 1e-12) return start >= min && start <= max;
        double ta = (min - start) / delta, tb = (max - start) / delta;
        if (ta > tb) (ta, tb) = (tb, ta);
        t0 = Math.Max(t0, ta);
        t1 = Math.Min(t1, tb);
        return t0 <= t1;
    }

    /// <summary>Squared distance between the segments p1→q1 and p2→q2 (Ericson, Real-Time Collision Detection, 5.1.9).</summary>
    public static double SegmentDistanceSquared(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2)
    {
        const double eps = 1e-12;
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        double a = Vec3.Dot(d1, d1), e = Vec3.Dot(d2, d2), f = Vec3.Dot(d2, r);
        double s, t;
        if (a <= eps && e <= eps) return r.LengthSquared;
        if (a <= eps)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            double c = Vec3.Dot(d1, r);
            if (e <= eps)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                double b = Vec3.Dot(d1, d2), denom = a * e - b * b;
                s = denom > eps ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }
        return (p1 + d1 * s - (p2 + d2 * t)).LengthSquared;
    }
}

/// <summary>Upright cylinder standing on <see cref="Base"/>: a trunk or a pole. Exact segment test.</summary>
public sealed record VerticalCylinder(Vec3 Base, double Radius, double Height) : IObstacleShape
{
    public bool Contains(Vec3 p)
    {
        if (p.Z < Base.Z || p.Z > Base.Z + Height) return false;
        double dx = p.X - Base.X, dy = p.Y - Base.Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }

    public bool Intersects(Vec3 a, Vec3 b)
    {
        var d = b - a;
        double t0 = 0, t1 = 1;
        if (!ShapeMath.ClipSlab(a.Z, d.Z, Base.Z, Base.Z + Height, ref t0, ref t1)) return false;
        // Closest approach of the horizontal projection to the axis, within the part of the segment at the right height.
        double ax = a.X - Base.X, ay = a.Y - Base.Y, dd = d.X * d.X + d.Y * d.Y;
        double t = dd > 1e-12 ? Math.Clamp(-(ax * d.X + ay * d.Y) / dd, t0, t1) : t0;
        double px = ax + d.X * t, py = ay + d.Y * t;
        return px * px + py * py <= Radius * Radius;
    }

    public Footprint Footprint => new(Base.X - Radius, Base.Y - Radius, Base.X + Radius, Base.Y + Radius);
}

/// <summary>Upright cone with its base disc at <see cref="Base"/> and its tip <see cref="Height"/> above: a conifer
/// crown. Sampled segment test.</summary>
public sealed record VerticalCone(Vec3 Base, double BaseRadius, double Height) : IObstacleShape
{
    public bool Contains(Vec3 p)
    {
        double h = p.Z - Base.Z;
        if (h < 0 || h > Height) return false;
        double r = BaseRadius * (1 - h / Height), dx = p.X - Base.X, dy = p.Y - Base.Y;
        return dx * dx + dy * dy <= r * r;
    }

    public bool Intersects(Vec3 a, Vec3 b) => ShapeMath.SampledIntersects(this, a, b);

    public Footprint Footprint => new(Base.X - BaseRadius, Base.Y - BaseRadius, Base.X + BaseRadius, Base.Y + BaseRadius);
}

/// <summary>Ellipsoid of revolution about the vertical: a broadleaf crown or a bush. Sampled segment test.</summary>
public sealed record Ellipsoid(Vec3 Centre, double HorizontalRadius, double VerticalRadius) : IObstacleShape
{
    public bool Contains(Vec3 p)
    {
        double dx = p.X - Centre.X, dy = p.Y - Centre.Y, dz = p.Z - Centre.Z;
        return (dx * dx + dy * dy) / (HorizontalRadius * HorizontalRadius) + dz * dz / (VerticalRadius * VerticalRadius) <= 1;
    }

    public bool Intersects(Vec3 a, Vec3 b) => ShapeMath.SampledIntersects(this, a, b);

    public Footprint Footprint => new(Centre.X - HorizontalRadius, Centre.Y - HorizontalRadius,
        Centre.X + HorizontalRadius, Centre.Y + HorizontalRadius);
}

/// <summary>Box turned by <see cref="YawDeg"/> (see <see cref="PlanarYaw"/>) about the vertical through its centre;
/// half extents along its local x, local y and up. Exact segment test.</summary>
public sealed record OrientedBox(Vec3 Centre, Vec3 HalfExtents, double YawDeg) : IObstacleShape
{
    public bool Contains(Vec3 p)
    {
        var l = ToLocal(p);
        return Math.Abs(l.X) <= HalfExtents.X && Math.Abs(l.Y) <= HalfExtents.Y && Math.Abs(l.Z) <= HalfExtents.Z;
    }

    public bool Intersects(Vec3 a, Vec3 b)
    {
        var la = ToLocal(a);
        var d = ToLocal(b) - la;
        double t0 = 0, t1 = 1;
        return ShapeMath.ClipSlab(la.X, d.X, -HalfExtents.X, HalfExtents.X, ref t0, ref t1)
            && ShapeMath.ClipSlab(la.Y, d.Y, -HalfExtents.Y, HalfExtents.Y, ref t0, ref t1)
            && ShapeMath.ClipSlab(la.Z, d.Z, -HalfExtents.Z, HalfExtents.Z, ref t0, ref t1);
    }

    public Footprint Footprint
    {
        get
        {
            double a = Angle.Rad(YawDeg), c = Math.Abs(Math.Cos(a)), s = Math.Abs(Math.Sin(a));
            double ex = c * HalfExtents.X + s * HalfExtents.Y, ey = s * HalfExtents.X + c * HalfExtents.Y;
            return new(Centre.X - ex, Centre.Y - ey, Centre.X + ex, Centre.Y + ey);
        }
    }

    Vec3 ToLocal(Vec3 p)
    {
        var (x, y) = PlanarYaw.ToLocal(p.X - Centre.X, p.Y - Centre.Y, YawDeg);
        return new Vec3(x, y, p.Z - Centre.Z);
    }
}

/// <summary>All points within <see cref="Radius"/> of the segment A→B: a wire. Exact segment test.</summary>
public sealed record Capsule(Vec3 A, Vec3 B, double Radius) : IObstacleShape
{
    public bool Contains(Vec3 p) => ShapeMath.SegmentDistanceSquared(p, p, A, B) <= Radius * Radius;

    public bool Intersects(Vec3 a, Vec3 b) => ShapeMath.SegmentDistanceSquared(a, b, A, B) <= Radius * Radius;

    public Footprint Footprint => new(Math.Min(A.X, B.X) - Radius, Math.Min(A.Y, B.Y) - Radius,
        Math.Max(A.X, B.X) + Radius, Math.Max(A.Y, B.Y) + Radius);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/SimLab.Flight.Tests --filter ObstacleShapeTests`
Expected: PASS (6 tests). If a `Footprint` equality fails on rounding (e.g. 9.95 vs 9.950000000000001), compare the
fields with `Assert.Equal(expected, actual, 9)` instead of the record.

- [ ] **Step 6: Commit**

```bash
git add src/SimLab.Flight/Geometry/PlanarYaw.cs src/SimLab.Flight/Terrain/ObstacleShapes.cs tests/SimLab.Flight.Tests/Terrain/ObstacleShapeTests.cs
git commit -F <message file>   # "feat(terrain): exact obstacle shapes and yawed planar frames"
```

---

### Task 2: `ObstacleGrid`

**Files:**
- Create: `src/SimLab.Flight/Terrain/ObstacleGrid.cs`
- Test: `tests/SimLab.Flight.Tests/Terrain/ObstacleGridTests.cs`

**Interfaces:**
- Consumes: Task 1 shapes.
- Produces: `enum ObstacleKind { Tree, Structure, Wire }`; `readonly record struct Obstacle(IObstacleShape Shape, ObstacleKind Kind)`;
  `sealed class ObstacleGrid(IEnumerable<Obstacle>)` with `const double CellSize = 32`, `IReadOnlyList<Obstacle> All`,
  `ObstacleKind? Hit(Vec3 p)`, `ObstacleKind? Hit(Vec3 a, Vec3 b)`.

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.Flight.Tests/Terrain/ObstacleGridTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.Flight.Tests --filter ObstacleGridTests`
Expected: FAIL to build (`ObstacleGrid` not defined).

- [ ] **Step 3: Implement the grid**

`src/SimLab.Flight/Terrain/ObstacleGrid.cs`:

```csharp
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

/// <summary>What an obstacle is, which decides the crash cause shown to the pilot.</summary>
public enum ObstacleKind { Tree, Structure, Wire }

public readonly record struct Obstacle(IObstacleShape Shape, ObstacleKind Kind);

/// <summary>
/// Obstacles bucketed in a uniform horizontal grid, so a query only tests the obstacles near it. Each obstacle is
/// registered in every cell its footprint touches; a query may therefore test one twice, which only costs time.
/// Immutable after construction, so queries are thread-safe.
/// </summary>
public sealed class ObstacleGrid
{
    public const double CellSize = 32;

    readonly Obstacle[] _all;
    readonly Dictionary<long, int[]> _cells;

    public ObstacleGrid(IEnumerable<Obstacle> obstacles)
    {
        _all = obstacles.ToArray();
        var lists = new Dictionary<long, List<int>>();
        for (int i = 0; i < _all.Length; i++)
        {
            var f = _all[i].Shape.Footprint;
            for (int cx = Cell(f.MinX); cx <= Cell(f.MaxX); cx++)
            for (int cy = Cell(f.MinY); cy <= Cell(f.MaxY); cy++)
            {
                long key = Key(cx, cy);
                if (!lists.TryGetValue(key, out var list)) lists[key] = list = [];
                list.Add(i);
            }
        }
        _cells = lists.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    public IReadOnlyList<Obstacle> All => _all;

    /// <summary>Kind of the first obstacle containing the point, or null.</summary>
    public ObstacleKind? Hit(Vec3 p)
    {
        if (!_cells.TryGetValue(Key(Cell(p.X), Cell(p.Y)), out var ids)) return null;
        foreach (int i in ids)
            if (_all[i].Shape.Contains(p)) return _all[i].Kind;
        return null;
    }

    /// <summary>Kind of the first obstacle crossed by the segment a→b, or null.</summary>
    public ObstacleKind? Hit(Vec3 a, Vec3 b)
    {
        for (int cx = Cell(Math.Min(a.X, b.X)); cx <= Cell(Math.Max(a.X, b.X)); cx++)
        for (int cy = Cell(Math.Min(a.Y, b.Y)); cy <= Cell(Math.Max(a.Y, b.Y)); cy++)
        {
            if (!_cells.TryGetValue(Key(cx, cy), out var ids)) continue;
            foreach (int i in ids)
                if (_all[i].Shape.Intersects(a, b)) return _all[i].Kind;
        }
        return null;
    }

    static int Cell(double v) => (int)Math.Floor(v / CellSize);

    static long Key(int cx, int cy) => ((long)cx << 32) | (uint)cy;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SimLab.Flight.Tests --filter ObstacleGridTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit** — `feat(terrain): spatial grid for obstacle queries`

---

### Task 3: Terrain obstacle queries and segment crash detection

**Files:**
- Modify: `src/SimLab.Flight/Terrain/ITerrain.cs` (replace `HitsObstacle`; keep `CylinderObstacle` for now)
- Modify: `src/SimLab.Flight/Terrain/FlatTerrain.cs`
- Modify: `src/SimLab.Flight/Ground/GroundSpec.cs:25` (`CrashCause`)
- Modify: `src/SimLab.Flight/Ground/GroundContactModel.cs:75-100` (`DetectCrash`, `CauseFor`)
- Modify: `src/SimLab.App/Field/ClubFieldTerrain.cs` (temporary adapter until Task 7)
- Modify: `game/translations/strings.csv` (after `CRASH_HULLIMPACT`)
- Test: `tests/SimLab.Flight.Tests/Terrain/TerrainTests.cs`, `tests/SimLab.Flight.Tests/Ground/GroundContactTests.cs`,
  `tests/SimLab.App.Tests/Field/FieldTests.cs`

**Interfaces:**
- Consumes: `Obstacle`, `ObstacleKind`, `ObstacleGrid`, `VerticalCylinder`, `Capsule` (Tasks 1–2).
- Produces: `ITerrain.HitObstacle(Vec3 p) → ObstacleKind?` and `ITerrain.HitObstacle(Vec3 a, Vec3 b) → ObstacleKind?`
  (replacing `bool HitsObstacle(Vec3)`); `FlatTerrain(double elevation = 0, IEnumerable<Obstacle>? obstacles = null)`;
  `CrashCause.StructureStrike`, `CrashCause.WireStrike` (appended to the enum).

- [ ] **Step 1: Update and add the failing tests**

In `tests/SimLab.Flight.Tests/Terrain/TerrainTests.cs`, replace `Point_inside_tree_cylinder_hits_obstacle` with:

```csharp
    [Fact]
    public void Flat_terrain_reports_the_obstacle_kind()
    {
        var t = new FlatTerrain(0, [new Obstacle(new VerticalCylinder(new Vec3(10, 20, 0), 2, 8), ObstacleKind.Tree)]);
        Assert.Equal(ObstacleKind.Tree, t.HitObstacle(new Vec3(11, 20, 5)));
        Assert.Null(t.HitObstacle(new Vec3(11, 20, 9)));
        Assert.Null(t.HitObstacle(new Vec3(13, 20, 5)));
        Assert.Equal(ObstacleKind.Tree, t.HitObstacle(new Vec3(5, 20, 5), new Vec3(15, 20, 5)));
        Assert.Null(new FlatTerrain().HitObstacle(Vec3.Zero, new Vec3(1, 0, 0)));
    }
```

In `tests/SimLab.Flight.Tests/Ground/GroundContactTests.cs`, change the terrain in `Flying_into_a_tree_is_a_crash` to

```csharp
        var terrain = new FlatTerrain(0, [new Obstacle(new VerticalCylinder(new Vec3(10.3, 0, 0), 1, 10), ObstacleKind.Tree)]);
```

and add after it:

```csharp
    /// <summary>Nose and two wingtips: the only probe points; nothing in between.</summary>
    static readonly HullPointSpec[] Outline =
    [
        new("nose", new Vec3(-0.5, 0, 0), "nose"),
        new("wingtipLeft", new Vec3(0.2, -0.75, 0), "wingtip"),
        new("wingtipRight", new Vec3(0.2, 0.75, 0), "wingtip"),
    ];

    [Fact]
    public void A_pole_between_two_hull_points_is_a_crash()
    {
        var model = new GroundContactModel([], Outline, 1.0);
        // Heading east at (10, 0, 5): the wingtips are at x = 9.8, y = ±0.75, the nose at x = 10.5. The pole stands
        // on the wingtip-to-wingtip line, 0.7 m from the nose and 0.75 m from each wingtip.
        var s = new RigidBodyState(new Vec3(10, 0, 5), new Vec3(20, 0, 0), Level, Vec3.Zero);
        var pole = new FlatTerrain(0, [new Obstacle(new VerticalCylinder(new Vec3(9.8, 0, 0), 0.05, 10), ObstacleKind.Structure)]);
        Assert.Equal(CrashCause.StructureStrike, model.DetectCrash(s, pole, Limits));
        var beside = new FlatTerrain(0, [new Obstacle(new VerticalCylinder(new Vec3(9.8, 2, 0), 0.05, 10), ObstacleKind.Structure)]);
        Assert.Equal(CrashCause.None, model.DetectCrash(s, beside, Limits));
    }

    [Fact]
    public void A_wire_is_caught_at_100_m_per_s_without_tunnelling()
    {
        var model = new GroundContactModel([], Outline, 1.0);
        var terrain = new FlatTerrain(0, [new Obstacle(new Capsule(new Vec3(20, -10, 5), new Vec3(20, 10, 5), 0.10), ObstacleKind.Wire)]);
        // One 500 Hz step at 100 m/s moves 0.20 m; the start is offset so no step lands exactly on the wire.
        var cause = CrashCause.None;
        for (double x = 15.037; x < 25 && cause == CrashCause.None; x += 0.20)
            cause = model.DetectCrash(new RigidBodyState(new Vec3(x, 0, 5), new Vec3(100, 0, 0), Level, Vec3.Zero), terrain, Limits);
        Assert.Equal(CrashCause.WireStrike, cause);
    }
```

In `tests/SimLab.App.Tests/Field/FieldTests.cs`, `Terrain_reports_tree_hits` becomes:

```csharp
        Assert.Equal(ObstacleKind.Tree, terrain.HitObstacle(new Vec3(tree.X, tree.Y, tree.BaseZ + 1)));
        Assert.Null(terrain.HitObstacle(new Vec3(0, 0, 5)));
```

(add `using SimLab.Flight.Terrain;`).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test`
Expected: FAIL to build (`HitObstacle`, `StructureStrike`, `WireStrike` not defined).

- [ ] **Step 3: Change `ITerrain` and `FlatTerrain`**

`ITerrain.cs` — replace the `HitsObstacle` member with:

```csharp
    /// <summary>Kind of the obstacle (tree, structure, wire) containing the world point, or null.</summary>
    ObstacleKind? HitObstacle(Vec3 p);

    /// <summary>Kind of the first obstacle crossed by the world segment a→b, or null.</summary>
    ObstacleKind? HitObstacle(Vec3 a, Vec3 b);
```

Leave `CylinderObstacle` in the file (Task 7 deletes it).

`FlatTerrain.cs`:

```csharp
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Terrain;

public sealed class FlatTerrain : ITerrain
{
    readonly double _elevation;
    readonly ObstacleGrid _obstacles;

    public FlatTerrain(double elevation = 0, IEnumerable<Obstacle>? obstacles = null)
    {
        _elevation = elevation;
        _obstacles = new ObstacleGrid(obstacles ?? []);
    }

    public double Height(double x, double y) => _elevation;

    public Vec3 Normal(double x, double y) => Vec3.UnitZ;

    public ObstacleKind? HitObstacle(Vec3 p) => _obstacles.Hit(p);

    public ObstacleKind? HitObstacle(Vec3 a, Vec3 b) => _obstacles.Hit(a, b);
}
```

- [ ] **Step 4: Crash causes and detection**

`GroundSpec.cs:25`:

```csharp
public enum CrashCause { None, HardLanding, TreeStrike, WingtipStrike, NoseOver, HullImpact, StructureStrike, WireStrike }
```

`GroundContactModel.DetectCrash` becomes (wheels loop unchanged):

```csharp
    public CrashCause DetectCrash(in RigidBodyState s, ITerrain terrain, CrashLimits limits)
    {
        foreach (var w in _wheels)
        {
            var p = ProbePoint(w.Position, s, terrain);
            if (p.Depth > 0 && -p.NormalSpeed > limits.MaxGearSinkRate) return CrashCause.HardLanding;
        }
        Span<Vec3> points = stackalloc Vec3[_hull.Length];
        for (int i = 0; i < _hull.Length; i++)
        {
            var h = _hull[i];
            var p = ProbePoint(h.Position, s, terrain);
            points[i] = p.Point;
            if (terrain.HitObstacle(p.Point) is { } kind) return CauseFor(kind);
            if (p.Depth <= 0) continue;
            bool belly = h.Tag == "belly";
            double limit = belly ? limits.MaxBellyImpactSpeed : limits.MaxHullImpactSpeed;
            if (-p.NormalSpeed > limit) return CauseFor(h.Tag);
        }
        // Every pair of hull points spans the aircraft's outline, so a pole or a wire passing between two of them
        // (with no probe point inside it) is caught too.
        for (int i = 0; i < points.Length; i++)
        for (int j = i + 1; j < points.Length; j++)
            if (terrain.HitObstacle(points[i], points[j]) is { } kind) return CauseFor(kind);
        return CrashCause.None;
    }

    static CrashCause CauseFor(ObstacleKind kind) => kind switch
    {
        ObstacleKind.Tree => CrashCause.TreeStrike,
        ObstacleKind.Structure => CrashCause.StructureStrike,
        _ => CrashCause.WireStrike,
    };
```

If the compiler rejects the two `kind` pattern variables, rename the second one `crossed`.

- [ ] **Step 5: Temporary club adapter**

`ClubFieldTerrain.cs`: replace the `_trees` array search with a grid, keeping the old (oversized) cylinders until
Task 7 replaces the club:

```csharp
    readonly CylinderObstacle[] _trees;
    readonly ObstacleGrid _obstacles;

    public ClubFieldTerrain(IEnumerable<CylinderObstacle> trees)
    {
        _trees = trees.ToArray();
        _obstacles = new ObstacleGrid(_trees.Select(t =>
            new Obstacle(new VerticalCylinder(new Vec3(t.X, t.Y, t.BaseZ), t.Radius, t.Height), ObstacleKind.Tree)));
    }
```

and replace `HitsObstacle` with the two `HitObstacle` methods delegating to `_obstacles.Hit`.

- [ ] **Step 6: Translations**

After the `CRASH_HULLIMPACT` line of `game/translations/strings.csv`:

```
CRASH_STRUCTURESTRIKE,Contre un obstacle,Hit a structure
CRASH_WIRESTRIKE,Dans les câbles,Into the wires
```

(`TranslationTests` checks that every `CrashCause` has its key.)

- [ ] **Step 7: Run all tests**

Run: `dotnet test`
Expected: PASS, apart from the known sport roll-rate skip. Then `dotnet build game/SimLab.Game.csproj` → 0 warnings, 0 errors.

- [ ] **Step 8: Commit** — `feat(ground): obstacle kinds and hull-segment crash detection`

---

### Task 4: Map model building blocks

**Files:**
- Create: `src/SimLab.App/Maps/MapLayout.cs`, `src/SimLab.App/Maps/SurfaceWeights.cs`, `src/SimLab.App/Maps/MapAmbience.cs`,
  `src/SimLab.App/Maps/MapOverlay.cs`, `src/SimLab.App/Maps/MapTerrain.cs`
- Modify: `src/SimLab.App/Mapping/GodotBasis.cs` (add `PartRotation`)
- Test: `tests/SimLab.App.Tests/Maps/MapModelTests.cs`, `tests/SimLab.App.Tests/Mapping/MappingTests.cs`

**Interfaces:**
- Consumes: `PlanarYaw`, `ObstacleGrid`, `Obstacle`, `ObstacleKind` (Tasks 1–3); `Rgb` (`SimLab.App.Visual`, `AircraftMeshBuilder.cs`).
- Produces (namespace `SimLab.App.Maps`):
  - `sealed record MapLayout(Vec3 PilotPosition, double EyeHeight, Vec3 WindsockPosition, Vec3 RunwayCentre, double RunwayLength, double RunwayWidth, double RunwayHeadingDeg)`
    with `bool OnRunway(double x, double y)`, `double TakeoffHeading(double windFromDeg)`,
    `(double X, double Y) TakeoffPoint(double headingDeg)`, `(double X, double Y, double HeadingDeg) HandLaunchPoint(double windFromDeg)`.
  - `enum SurfaceKind { Grass, MowedGrass, Dirt, Gravel, Wheat, Ploughed }`;
    `readonly record struct SurfaceWeights(double Grass, double MowedGrass, double Dirt, double Gravel, double Wheat, double Ploughed)`
    with `static Only(SurfaceKind)`, `double Sum`, `SurfaceWeights Toward(SurfaceKind kind, double amount)`.
  - `sealed record MapAmbience(Rgb SkyTop, Rgb SkyHorizon, Rgb GroundHorizon, Rgb GroundBottom, double FogDensity)`.
  - `sealed record MapOverlay(SurfaceKind Kind, IReadOnlyList<(double X, double Y)> Path, double Width)`.
  - `sealed class MapTerrain(Func<double, double, double> height, IEnumerable<Obstacle> obstacles) : ITerrain` with `ObstacleGrid Obstacles`.
  - `GodotBasis.PartRotation(double yawDeg, double pitchDeg) → Quat` (Godot axes).

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.App.Tests/Maps/MapModelTests.cs`:

```csharp
using SimLab.App.Maps;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Tests.Maps;

public class MapModelTests
{
    static readonly MapLayout EastWest = new(new Vec3(0, -25, 0), 1.7, new Vec3(20, -28, 0), Vec3.Zero, 100, 15, 90);

    /// <summary>A runway heading 30°/210°, centred at (500, 200), pilot 20 m to its south-east side.</summary>
    static readonly MapLayout Turned = new(new Vec3(500 + 20 * Math.Cos(Angle.Rad(30)), 200 - 20 * Math.Sin(Angle.Rad(30)), 0),
        1.7, new Vec3(0, 0, 0), new Vec3(500, 200, 0), 200, 20, 30);

    [Theory]
    [InlineData(80, 90)]
    [InlineData(10, 90)]
    [InlineData(260, 270)]
    [InlineData(200, 270)]
    public void Takeoff_is_into_the_wind(double windFrom, double expected) =>
        Assert.Equal(expected, EastWest.TakeoffHeading(windFrom));

    [Fact]
    public void Takeoff_heading_follows_a_turned_runway()
    {
        Assert.Equal(30, Turned.TakeoffHeading(40));
        Assert.Equal(210, Turned.TakeoffHeading(250));
    }

    [Fact]
    public void Takeoff_point_is_inside_the_downwind_threshold()
    {
        var (x, y) = EastWest.TakeoffPoint(90);
        Assert.Equal(-42, x, 9);
        Assert.Equal(0, y, 9);
        Assert.True(EastWest.OnRunway(x, y));
        Assert.True(EastWest.TakeoffPoint(270).X > 0);
        var (tx, ty) = Turned.TakeoffPoint(30);
        Assert.True(Turned.OnRunway(tx, ty));
        // 92 m back from the centre, against the 30° heading.
        Assert.Equal(500 - 92 * Math.Sin(Angle.Rad(30)), tx, 9);
        Assert.Equal(200 - 92 * Math.Cos(Angle.Rad(30)), ty, 9);
    }

    [Fact]
    public void On_runway_uses_the_runway_frame()
    {
        Assert.True(EastWest.OnRunway(49, 7));
        Assert.False(EastWest.OnRunway(51, 0));
        Assert.False(EastWest.OnRunway(0, 8));
        Assert.True(Turned.OnRunway(500 + 99 * Math.Sin(Angle.Rad(30)), 200 + 99 * Math.Cos(Angle.Rad(30))));
        Assert.False(Turned.OnRunway(500 + 101 * Math.Sin(Angle.Rad(30)), 200 + 101 * Math.Cos(Angle.Rad(30))));
    }

    [Fact]
    public void Hand_launch_is_3_m_in_front_of_the_pilot_toward_the_runway()
    {
        var (x, y, heading) = EastWest.HandLaunchPoint(270);
        Assert.Equal(0, x, 9);
        Assert.Equal(-22, y, 9);
        Assert.Equal(270, heading);
        var (tx, ty, _) = Turned.HandLaunchPoint(40);
        double before = Math.Sqrt(Math.Pow(Turned.PilotPosition.X - 500, 2) + Math.Pow(Turned.PilotPosition.Y - 200, 2));
        double after = Math.Sqrt(Math.Pow(tx - 500, 2) + Math.Pow(ty - 200, 2));
        Assert.Equal(before - 3, after, 6);
    }

    [Fact]
    public void Surface_weights_blend_and_stay_normalised()
    {
        var grass = SurfaceWeights.Only(SurfaceKind.Grass);
        var half = grass.Toward(SurfaceKind.Gravel, 0.5);
        Assert.Equal(0.5, half.Grass, 9);
        Assert.Equal(0.5, half.Gravel, 9);
        Assert.Equal(1, half.Toward(SurfaceKind.Wheat, 0.3).Sum, 9);
        Assert.Equal(SurfaceWeights.Only(SurfaceKind.Ploughed), grass.Toward(SurfaceKind.Ploughed, 2));
        Assert.Equal(grass, grass.Toward(SurfaceKind.Dirt, -1));
        foreach (var kind in Enum.GetValues<SurfaceKind>()) Assert.Equal(1, SurfaceWeights.Only(kind).Sum, 9);
    }

    [Fact]
    public void Map_terrain_samples_height_normal_and_obstacles()
    {
        var terrain = new MapTerrain((x, _) => 0.1 * x,
            [new Obstacle(new VerticalCylinder(new Vec3(5, 5, 0.5), 1, 10), ObstacleKind.Structure)]);
        Assert.Equal(2, terrain.Height(20, 7), 9);
        var n = terrain.Normal(3, 4);
        Assert.Equal(1, n.Length, 9);
        Assert.True(n.X < 0 && n.Z > 0.99);
        Assert.Equal(ObstacleKind.Structure, terrain.HitObstacle(new Vec3(5, 5, 3)));
        Assert.Equal(ObstacleKind.Structure, terrain.HitObstacle(new Vec3(0, 5, 3), new Vec3(10, 5, 3)));
        Assert.Null(terrain.HitObstacle(new Vec3(8, 5, 3)));
    }
}
```

Append to `tests/SimLab.App.Tests/Mapping/MappingTests.cs`:

```csharp
    [Fact]
    public void Part_rotation_yaws_clockwise_then_pitches_up()
    {
        // Yaw 90: the part's local x (east at yaw 0) points south, Godot +Z.
        Near(new Vec3(0, 0, 1), GodotBasis.PartRotation(90, 0).Rotate(Vec3.UnitX));
        // Pitched up 30° along that heading.
        Near(new Vec3(0, 0.5, Math.Cos(Angle.Rad(30))), GodotBasis.PartRotation(90, 30).Rotate(Vec3.UnitX));
        // Local y (north at yaw 0, Godot −Z) turns with the yaw.
        var (wx, wy) = PlanarYaw.ToWorld(0, 1, 30);
        Near(GodotBasis.WorldToGodot(new Vec3(wx, wy, 0)), GodotBasis.PartRotation(30, 0).Rotate(new Vec3(0, 0, -1)));
        Near(Vec3.UnitY, GodotBasis.PartRotation(123, 0).Rotate(Vec3.UnitY));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.App.Tests --filter "MapModelTests|MappingTests"`
Expected: FAIL to build.

- [ ] **Step 3: Implement the model pieces**

`src/SimLab.App/Maps/MapLayout.cs`:

```csharp
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps;

/// <summary>
/// Where things are on a map (world ENU). The runway is a rectangle centred on <see cref="RunwayCentre"/>, its long
/// axis along <see cref="RunwayHeadingDeg"/> (compass, either direction); aircraft take off whichever way faces the
/// wind most directly.
/// </summary>
public sealed record MapLayout(
    Vec3 PilotPosition, double EyeHeight, Vec3 WindsockPosition,
    Vec3 RunwayCentre, double RunwayLength, double RunwayWidth, double RunwayHeadingDeg)
{
    const double TakeoffInset = 8;
    const double HandLaunchDistance = 3;

    /// <summary><see cref="PlanarYaw"/> of a frame whose local x runs along the runway toward its heading.</summary>
    double RunwayYaw => RunwayHeadingDeg - 90;

    public bool OnRunway(double x, double y)
    {
        var (along, across) = PlanarYaw.ToLocal(x - RunwayCentre.X, y - RunwayCentre.Y, RunwayYaw);
        return Math.Abs(along) <= RunwayLength / 2 && Math.Abs(across) <= RunwayWidth / 2;
    }

    /// <summary>The runway direction (the heading or its opposite, 0–360) that points most directly into the wind;
    /// the heading itself on a tie.</summary>
    public double TakeoffHeading(double windFromDeg)
    {
        double a = Normalize(RunwayHeadingDeg), b = Normalize(RunwayHeadingDeg + 180);
        return AngleBetween(windFromDeg, a) <= AngleBetween(windFromDeg, b) ? a : b;
    }

    /// <summary>On the centre line, <see cref="TakeoffInset"/> inside the threshold behind an aircraft taking off
    /// on <paramref name="headingDeg"/>.</summary>
    public (double X, double Y) TakeoffPoint(double headingDeg)
    {
        double h = Angle.Rad(headingDeg), back = RunwayLength / 2 - TakeoffInset;
        return (RunwayCentre.X - Math.Sin(h) * back, RunwayCentre.Y - Math.Cos(h) * back);
    }

    /// <summary><see cref="HandLaunchDistance"/> from the pilot toward the runway centre line, with the takeoff heading.</summary>
    public (double X, double Y, double HeadingDeg) HandLaunchPoint(double windFromDeg)
    {
        double h = Angle.Rad(RunwayHeadingDeg);
        double nx = Math.Cos(h), ny = -Math.Sin(h);
        if ((RunwayCentre.X - PilotPosition.X) * nx + (RunwayCentre.Y - PilotPosition.Y) * ny < 0) (nx, ny) = (-nx, -ny);
        return (PilotPosition.X + nx * HandLaunchDistance, PilotPosition.Y + ny * HandLaunchDistance, TakeoffHeading(windFromDeg));
    }

    static double Normalize(double deg) => (deg % 360 + 360) % 360;

    static double AngleBetween(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360));
}
```

`src/SimLab.App/Maps/SurfaceWeights.cs`:

```csharp
namespace SimLab.App.Maps;

public enum SurfaceKind { Grass, MowedGrass, Dirt, Gravel, Wheat, Ploughed }

/// <summary>Mix of ground materials at a point; the weights sum to 1.</summary>
public readonly record struct SurfaceWeights(double Grass, double MowedGrass, double Dirt, double Gravel, double Wheat, double Ploughed)
{
    public static SurfaceWeights Only(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Grass => new(1, 0, 0, 0, 0, 0),
        SurfaceKind.MowedGrass => new(0, 1, 0, 0, 0, 0),
        SurfaceKind.Dirt => new(0, 0, 1, 0, 0, 0),
        SurfaceKind.Gravel => new(0, 0, 0, 1, 0, 0),
        SurfaceKind.Wheat => new(0, 0, 0, 0, 1, 0),
        _ => new(0, 0, 0, 0, 0, 1),
    };

    public double Sum => Grass + MowedGrass + Dirt + Gravel + Wheat + Ploughed;

    /// <summary>This mix moved toward <paramref name="kind"/> by <paramref name="amount"/>, clamped to 0 (unchanged)
    /// … 1 (only that kind).</summary>
    public SurfaceWeights Toward(SurfaceKind kind, double amount)
    {
        double t = Math.Clamp(amount, 0, 1), k = 1 - t;
        var o = Only(kind);
        return new(Grass * k + o.Grass * t, MowedGrass * k + o.MowedGrass * t, Dirt * k + o.Dirt * t,
            Gravel * k + o.Gravel * t, Wheat * k + o.Wheat * t, Ploughed * k + o.Ploughed * t);
    }
}
```

`src/SimLab.App/Maps/MapAmbience.cs`:

```csharp
using SimLab.App.Visual;

namespace SimLab.App.Maps;

/// <summary>Sky gradient, below-horizon colours and fog of a map.</summary>
public sealed record MapAmbience(Rgb SkyTop, Rgb SkyHorizon, Rgb GroundHorizon, Rgb GroundBottom, double FogDensity);
```

`src/SimLab.App/Maps/MapOverlay.cs`:

```csharp
namespace SimLab.App.Maps;

/// <summary>A narrow strip of ground material (track, road) along a polyline in world (x east, y north), drawn as a
/// ribbon draped on the terrain.</summary>
public sealed record MapOverlay(SurfaceKind Kind, IReadOnlyList<(double X, double Y)> Path, double Width);
```

`src/SimLab.App/Maps/MapTerrain.cs`:

```csharp
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>A map's ground: a height function (world x east, y north → z up) and its obstacles.</summary>
public sealed class MapTerrain : ITerrain
{
    const double NormalStep = 0.5;
    readonly Func<double, double, double> _height;

    public MapTerrain(Func<double, double, double> height, IEnumerable<Obstacle> obstacles)
    {
        _height = height;
        Obstacles = new ObstacleGrid(obstacles);
    }

    public ObstacleGrid Obstacles { get; }

    public double Height(double x, double y) => _height(x, y);

    public Vec3 Normal(double x, double y)
    {
        double dx = (_height(x + NormalStep, y) - _height(x - NormalStep, y)) / (2 * NormalStep);
        double dy = (_height(x, y + NormalStep) - _height(x, y - NormalStep)) / (2 * NormalStep);
        return new Vec3(-dx, -dy, 1).Normalized();
    }

    public ObstacleKind? HitObstacle(Vec3 p) => Obstacles.Hit(p);

    public ObstacleKind? HitObstacle(Vec3 a, Vec3 b) => Obstacles.Hit(a, b);
}
```

Add to `GodotBasis`:

```csharp
    /// <summary>
    /// Rotation (Godot axes) of a prop part (<see cref="Maps.PropPart"/>): its local x yawed clockwise seen from above
    /// by <paramref name="yawDeg"/> (<see cref="PlanarYaw"/>), then pitched up by <paramref name="pitchDeg"/>. Part
    /// meshes are built with local x along Godot +X, local y along Godot −Z and up along +Y.
    /// </summary>
    public static Quat PartRotation(double yawDeg, double pitchDeg) =>
        (Quat.FromAxisAngle(Vec3.UnitY, -Angle.Rad(yawDeg)) * Quat.FromAxisAngle(Vec3.UnitZ, Angle.Rad(pitchDeg))).Normalized();
```

(The `<see cref="Maps.PropPart"/>` reference only compiles after Task 5; write `prop part` in plain text here and let
Task 5 leave it as is.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SimLab.App.Tests --filter "MapModelTests|MappingTests"`
Expected: PASS.

- [ ] **Step 5: Commit** — `feat(maps): layout, surfaces, ambience, overlays and map terrain`

---

### Task 5: Props (drawn parts and collision from one description) and `FieldMap`

**Files:**
- Create: `src/SimLab.App/Maps/Prop.cs`, `src/SimLab.App/Maps/Vegetation.cs`, `src/SimLab.App/Maps/Structures.cs`,
  `src/SimLab.App/Maps/FieldMap.cs`
- Test: `tests/SimLab.App.Tests/Maps/PropTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `enum PartMesh { Trunk, BroadleafCrown, ConiferCrown, Box, Roof, Post, Wire }`.
  - `readonly record struct PropPart(PartMesh Mesh, Vec3 Centre, Vec3 Size, double YawDeg, double PitchDeg, Rgb Tint)`.
    `Size` is the extent along the part's local x, local y and up. The game's unit mesh fills a 1 m cube centred on
    the origin, with local x → Godot +X, local y → Godot −Z and up → +Y.
  - `abstract record Prop(Vec3 Base, double YawDeg)` with `abstract IEnumerable<Obstacle> Collision()` and
    `abstract IEnumerable<PropPart> Parts()`.
  - `static class Foliage { const double HitScale = 0.85; const double CoreScale = 0.90; static readonly Rgb Bark; }`.
  - Vegetation: `BroadleafTree(Vec3 Base, double YawDeg, double Height, double CrownRadius, Rgb Tint)`,
    `PoplarTree(… same …)`, `ConiferTree(Vec3 Base, double YawDeg, double Height, double BaseRadius, Rgb Tint)`,
    `Bush(Vec3 Base, double YawDeg, double Radius, double Height, Rgb Tint)`,
    `Hedge(Vec3 Base, double YawDeg, double Length, double Width, double Height, Rgb Tint)`.
  - Structures: `Building(Vec3 Base, double YawDeg, double Length, double Width, double WallHeight, double RoofHeight, bool Open, Rgb Walls, Rgb RoofTint)`,
    `Car(Vec3 Base, double YawDeg, Rgb Paint)`, `Table(Vec3 Base, double YawDeg, Rgb Wood)`,
    `Fence(Vec3 Base, double YawDeg, double Length, Rgb Wood)`, `PowerLine(IReadOnlyList<Vec3> Poles)` with
    `const double WireHitRadius = 0.10` and `Vec3 WirePoint(int span, int side, double t)`.
  - `sealed class FieldMap(string id, string nameKey, double halfSize, Func<double,double,double> height, Func<double,double,SurfaceWeights> surface, MapLayout layout, MapAmbience ambience, IReadOnlyList<Prop> props, IReadOnlyList<MapOverlay> overlays)`
    with `Id`, `NameKey`, `HalfSize`, `Terrain` (`MapTerrain`), `Layout`, `Ambience`, `Props`, `Overlays`,
    `SurfaceWeights Surface(double x, double y)`.

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.App.Tests/Maps/PropTests.cs`:

```csharp
using SimLab.App.Maps;
using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Tests.Maps;

public class PropTests
{
    static readonly Rgb Green = new(0.2f, 0.3f, 0.1f);

    static MapTerrain Around(Prop prop) => new((_, _) => prop.Base.Z, prop.Collision());

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
        var map = new FieldMap("test", "FIELD_TEST", 100, (_, _) => 0, (_, _) => SurfaceWeights.Only(SurfaceKind.Grass),
            new MapLayout(Vec3.Zero, 1.7, Vec3.Zero, Vec3.Zero, 50, 10, 90),
            new MapAmbience(Green, Green, Green, Green, 0), [tree], []);
        Assert.Equal(2, map.Terrain.Obstacles.All.Count);
        Assert.Equal(ObstacleKind.Tree, map.Terrain.HitObstacle(tree.CrownCentre));
        Assert.Equal(1, map.Surface(3, 4).Grass);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.App.Tests --filter PropTests`
Expected: FAIL to build.

- [ ] **Step 3: Implement `Prop.cs`**

```csharp
using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>Unit meshes the game draws prop parts with. Each fills a 1 m cube centred on its origin (local x, local
/// y, up): <see cref="Trunk"/> a tapered upright cylinder, <see cref="Post"/> an upright cylinder, <see cref="Wire"/>
/// a cylinder along local x, <see cref="Box"/> a box, <see cref="Roof"/> a gable prism with its ridge along local x,
/// <see cref="BroadleafCrown"/> a lumpy sphere and <see cref="ConiferCrown"/> a tiered cone (base down).</summary>
public enum PartMesh { Trunk, BroadleafCrown, ConiferCrown, Box, Roof, Post, Wire }

/// <param name="Size">Extent along the part's local x, local y and up (m), before rotation.</param>
/// <param name="YawDeg">See <see cref="PlanarYaw"/>.</param>
/// <param name="PitchDeg">Local x tilted up by this angle after the yaw (wires).</param>
public readonly record struct PropPart(PartMesh Mesh, Vec3 Centre, Vec3 Size, double YawDeg, double PitchDeg, Rgb Tint);

/// <summary>
/// Something standing on a map. Its visible dimensions are defined once; <see cref="Parts"/> (what the game draws)
/// and <see cref="Collision"/> (what the aircraft hits) are both derived from them.
/// </summary>
/// <param name="Base">Ground point the prop stands on (world ENU).</param>
/// <param name="YawDeg">See <see cref="PlanarYaw"/>.</param>
public abstract record Prop(Vec3 Base, double YawDeg)
{
    public abstract IEnumerable<Obstacle> Collision();

    public abstract IEnumerable<PropPart> Parts();

    /// <summary>World point at (x, y) in this prop's yawed frame, z above its base.</summary>
    protected Vec3 At(double localX, double localY, double z)
    {
        var (x, y) = PlanarYaw.ToWorld(localX, localY, YawDeg);
        return new Vec3(Base.X + x, Base.Y + y, Base.Z + z);
    }

    protected PropPart Part(PartMesh mesh, double localX, double localY, double z, double sizeX, double sizeY, double sizeZ, Rgb tint) =>
        new(mesh, At(localX, localY, z), new Vec3(sizeX, sizeY, sizeZ), YawDeg, 0, tint);
}

public static class Foliage
{
    /// <summary>Foliage collides at this fraction of its drawn envelope: brushing the outer leaves survives, the heart
    /// of the crown does not.</summary>
    public const double HitScale = 0.85;

    /// <summary>Foliage meshes have a solid core at this fraction of the envelope, plus lobes or skirts reaching the
    /// envelope, so the hitbox is always inside visible foliage.</summary>
    public const double CoreScale = 0.90;

    public static readonly Rgb Bark = new(0.33f, 0.24f, 0.15f);
}
```

- [ ] **Step 4: Implement `Vegetation.cs`**

```csharp
using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>Deciduous tree (oak): trunk up to 45 % of the height, ellipsoid crown over the top 70 %.</summary>
public sealed record BroadleafTree(Vec3 Base, double YawDeg, double Height, double CrownRadius, Rgb Tint) : Prop(Base, YawDeg)
{
    public double TrunkHeight => 0.45 * Height;
    public double TrunkRadius => Math.Max(0.15, 0.03 * Height);
    public double CrownVerticalRadius => 0.35 * Height;
    public Vec3 CrownCentre => Base + new Vec3(0, 0, Height - CrownVerticalRadius);

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new VerticalCylinder(Base, TrunkRadius, TrunkHeight), ObstacleKind.Tree),
        new(new Ellipsoid(CrownCentre, Foliage.HitScale * CrownRadius, Foliage.HitScale * CrownVerticalRadius), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Trunk, 0, 0, TrunkHeight / 2, 2 * TrunkRadius, 2 * TrunkRadius, TrunkHeight, Foliage.Bark),
        new(PartMesh.BroadleafCrown, CrownCentre, new Vec3(2 * CrownRadius, 2 * CrownRadius, 2 * CrownVerticalRadius), YawDeg, 0, Tint),
    ];
}

/// <summary>Lombardy poplar: tall and narrow, trunk up to 25 % of the height, crown over the top 85 %.</summary>
public sealed record PoplarTree(Vec3 Base, double YawDeg, double Height, double CrownRadius, Rgb Tint) : Prop(Base, YawDeg)
{
    public double TrunkHeight => 0.25 * Height;
    public double TrunkRadius => Math.Max(0.12, 0.025 * Height);
    public double CrownVerticalRadius => 0.425 * Height;
    public Vec3 CrownCentre => Base + new Vec3(0, 0, Height - CrownVerticalRadius);

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new VerticalCylinder(Base, TrunkRadius, TrunkHeight), ObstacleKind.Tree),
        new(new Ellipsoid(CrownCentre, Foliage.HitScale * CrownRadius, Foliage.HitScale * CrownVerticalRadius), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Trunk, 0, 0, TrunkHeight / 2, 2 * TrunkRadius, 2 * TrunkRadius, TrunkHeight, Foliage.Bark),
        new(PartMesh.BroadleafCrown, CrownCentre, new Vec3(2 * CrownRadius, 2 * CrownRadius, 2 * CrownVerticalRadius), YawDeg, 0, Tint),
    ];
}

/// <summary>Fir: trunk up to 25 % of the height, conical crown from 15 % to the tip.</summary>
public sealed record ConiferTree(Vec3 Base, double YawDeg, double Height, double BaseRadius, Rgb Tint) : Prop(Base, YawDeg)
{
    public double TrunkHeight => 0.25 * Height;
    public double TrunkRadius => Math.Max(0.12, 0.025 * Height);
    public double CrownBottom => 0.15 * Height;
    public double CrownHeight => Height - CrownBottom;

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new VerticalCylinder(Base, TrunkRadius, TrunkHeight), ObstacleKind.Tree),
        new(new VerticalCone(Base + new Vec3(0, 0, CrownBottom), Foliage.HitScale * BaseRadius, CrownHeight), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Trunk, 0, 0, TrunkHeight / 2, 2 * TrunkRadius, 2 * TrunkRadius, TrunkHeight, Foliage.Bark),
        Part(PartMesh.ConiferCrown, 0, 0, CrownBottom + CrownHeight / 2, 2 * BaseRadius, 2 * BaseRadius, CrownHeight, Tint),
    ];
}

/// <summary>A round bush standing on the ground.</summary>
public sealed record Bush(Vec3 Base, double YawDeg, double Radius, double Height, Rgb Tint) : Prop(Base, YawDeg)
{
    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new Ellipsoid(Base + new Vec3(0, 0, Height / 2), Foliage.HitScale * Radius, Foliage.HitScale * Height / 2), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.BroadleafCrown, 0, 0, Height / 2, 2 * Radius, 2 * Radius, Height, Tint),
    ];
}

/// <summary>A straight hedge section, its length along the local x axis.</summary>
public sealed record Hedge(Vec3 Base, double YawDeg, double Length, double Width, double Height, Rgb Tint) : Prop(Base, YawDeg)
{
    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, Height / 2),
            new Vec3(Foliage.HitScale * Length / 2, Foliage.HitScale * Width / 2, Foliage.HitScale * Height / 2), YawDeg), ObstacleKind.Tree),
    ];

    public override IEnumerable<PropPart> Parts() => [Part(PartMesh.Box, 0, 0, Height / 2, Length, Width, Height, Tint)];
}
```

- [ ] **Step 5: Implement `Structures.cs`**

```csharp
using SimLab.App.Visual;
using SimLab.Flight.Geometry;
using SimLab.Flight.Terrain;

namespace SimLab.App.Maps;

/// <summary>A hangar (closed walls and a gable roof) or, when <see cref="Open"/>, a shelter (four posts and a flat
/// roof). Its length runs along local x. Collides as the box around it.</summary>
public sealed record Building(Vec3 Base, double YawDeg, double Length, double Width, double WallHeight, double RoofHeight,
    bool Open, Rgb Walls, Rgb RoofTint) : Prop(Base, YawDeg)
{
    const double PostSize = 0.2;

    public double TotalHeight => WallHeight + RoofHeight;

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, TotalHeight / 2), new Vec3(Length / 2, Width / 2, TotalHeight / 2), YawDeg), ObstacleKind.Structure),
    ];

    public override IEnumerable<PropPart> Parts()
    {
        if (!Open)
            return
            [
                Part(PartMesh.Box, 0, 0, WallHeight / 2, Length, Width, WallHeight, Walls),
                Part(PartMesh.Roof, 0, 0, WallHeight + RoofHeight / 2, Length, Width, RoofHeight, RoofTint),
            ];
        double px = Length / 2 - PostSize, py = Width / 2 - PostSize;
        return
        [
            Part(PartMesh.Post, px, py, WallHeight / 2, PostSize, PostSize, WallHeight, Walls),
            Part(PartMesh.Post, px, -py, WallHeight / 2, PostSize, PostSize, WallHeight, Walls),
            Part(PartMesh.Post, -px, py, WallHeight / 2, PostSize, PostSize, WallHeight, Walls),
            Part(PartMesh.Post, -px, -py, WallHeight / 2, PostSize, PostSize, WallHeight, Walls),
            Part(PartMesh.Box, 0, 0, WallHeight + RoofHeight / 2, Length, Width, RoofHeight, RoofTint),
        ];
    }
}

/// <summary>A parked car, 4.2 × 1.8 × 1.5 m, its length along local x.</summary>
public sealed record Car(Vec3 Base, double YawDeg, Rgb Paint) : Prop(Base, YawDeg)
{
    static readonly Rgb Glass = new(0.15f, 0.18f, 0.20f);

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, 0.75), new Vec3(2.1, 0.9, 0.75), YawDeg), ObstacleKind.Structure),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Box, 0, 0, 0.55, 4.2, 1.8, 0.8, Paint),
        Part(PartMesh.Box, -0.2, 0, 1.2, 2.2, 1.6, 0.6, Glass),
    ];
}

/// <summary>A 1.8 × 0.8 m trestle table, 0.775 m high.</summary>
public sealed record Table(Vec3 Base, double YawDeg, Rgb Wood) : Prop(Base, YawDeg)
{
    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, 0.3875), new Vec3(0.9, 0.4, 0.3875), YawDeg), ObstacleKind.Structure),
    ];

    public override IEnumerable<PropPart> Parts() =>
    [
        Part(PartMesh.Box, 0, 0, 0.75, 1.8, 0.8, 0.05, Wood),
        Part(PartMesh.Box, 0.75, 0, 0.36, 0.05, 0.7, 0.72, Wood),
        Part(PartMesh.Box, -0.75, 0, 0.36, 0.05, 0.7, 0.72, Wood),
    ];
}

/// <summary>A 1.1 m post-and-rail fence along local x, a post every 2 m.</summary>
public sealed record Fence(Vec3 Base, double YawDeg, double Length, Rgb Wood) : Prop(Base, YawDeg)
{
    const double Height = 1.1;

    public override IEnumerable<Obstacle> Collision() =>
    [
        new(new OrientedBox(Base + new Vec3(0, 0, Height / 2), new Vec3(Length / 2, 0.05, Height / 2), YawDeg), ObstacleKind.Structure),
    ];

    public override IEnumerable<PropPart> Parts()
    {
        int posts = (int)Math.Floor(Length / 2) + 1;
        for (int i = 0; i < posts; i++)
            yield return Part(PartMesh.Post, -Length / 2 + i * Length / (posts - 1), 0, Height / 2, 0.08, 0.08, Height, Wood);
        yield return Part(PartMesh.Box, 0, 0, 1.0, Length, 0.04, 0.06, Wood);
    }
}

/// <summary>
/// Wooden poles 9 m tall carrying two wires, one each side of a cross-arm, that sag 1.5 m mid-span. Each span's wires
/// are drawn and collide as the same 8 straight segments; wires collide with <see cref="WireHitRadius"/>.
/// </summary>
public sealed record PowerLine(IReadOnlyList<Vec3> Poles) : Prop(Poles[0], 0)
{
    public const double PoleHeight = 9;
    public const double PoleRadius = 0.13;
    public const double ArmHalfLength = 0.8;
    public const double AttachHeight = 8.6;
    public const double Sag = 1.5;

    /// <summary>At least half the distance flown in one 500 Hz step at 100 m/s, so a hull segment cannot jump over a wire.</summary>
    public const double WireHitRadius = 0.10;

    const double WireDrawDiameter = 0.03;
    const int SegmentsPerSpan = 8;
    static readonly Rgb Wood = new(0.40f, 0.30f, 0.20f);
    static readonly Rgb Cable = new(0.08f, 0.08f, 0.08f);

    /// <summary>Yaw of span <paramref name="i"/>, from pole i toward pole i + 1 (the last pole uses the last span).</summary>
    double SpanYaw(int i)
    {
        int a = Math.Min(i, Poles.Count - 2);
        return PlanarYaw.Of(Poles[a + 1].X - Poles[a].X, Poles[a + 1].Y - Poles[a].Y);
    }

    /// <summary>Point at <paramref name="t"/> (0…1) along the wire on <paramref name="side"/> (−1 or 1: the span's
    /// local −y or +y) of span <paramref name="span"/>, following the sag.</summary>
    public Vec3 WirePoint(int span, int side, double t)
    {
        var (ox, oy) = PlanarYaw.ToWorld(0, side * ArmHalfLength, SpanYaw(span));
        var offset = new Vec3(ox, oy, AttachHeight);
        var a = Poles[span] + offset;
        var b = Poles[span + 1] + offset;
        return a + (b - a) * t - new Vec3(0, 0, 4 * Sag * t * (1 - t));
    }

    IEnumerable<(Vec3 A, Vec3 B)> WireSegments()
    {
        for (int span = 0; span < Poles.Count - 1; span++)
        foreach (int side in new[] { -1, 1 })
        for (int k = 0; k < SegmentsPerSpan; k++)
            yield return (WirePoint(span, side, k / (double)SegmentsPerSpan), WirePoint(span, side, (k + 1) / (double)SegmentsPerSpan));
    }

    public override IEnumerable<Obstacle> Collision()
    {
        foreach (var p in Poles) yield return new(new VerticalCylinder(p, PoleRadius, PoleHeight), ObstacleKind.Structure);
        foreach (var (a, b) in WireSegments()) yield return new(new Capsule(a, b, WireHitRadius), ObstacleKind.Wire);
    }

    public override IEnumerable<PropPart> Parts()
    {
        for (int i = 0; i < Poles.Count; i++)
        {
            var p = Poles[i];
            yield return new(PartMesh.Post, p + new Vec3(0, 0, PoleHeight / 2), new Vec3(2 * PoleRadius, 2 * PoleRadius, PoleHeight), 0, 0, Wood);
            yield return new(PartMesh.Box, p + new Vec3(0, 0, AttachHeight + 0.1), new Vec3(0.12, 2 * ArmHalfLength + 0.2, 0.12), SpanYaw(i), 0, Wood);
        }
        foreach (var (a, b) in WireSegments())
        {
            var d = b - a;
            double horizontal = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            yield return new(PartMesh.Wire, (a + b) * 0.5, new Vec3(d.Length, WireDrawDiameter, WireDrawDiameter),
                PlanarYaw.Of(d.X, d.Y), Angle.Deg(Math.Atan2(d.Z, horizontal)), Cable);
        }
    }
}
```

Note the cross-arms are also `PartMesh.Box`, so the test counts `Post` parts (3 poles) and `Wire` parts only.

- [ ] **Step 6: Implement `FieldMap.cs`**

```csharp
namespace SimLab.App.Maps;

/// <summary>
/// A flying site, built deterministically by a factory such as <see cref="Club.ClubMap.Create"/>: relief, ground
/// materials, layout, sky, props and ground overlays. The terrain's obstacles are the props' collisions, so what is
/// drawn and what is hit come from the same description.
/// </summary>
public sealed class FieldMap
{
    readonly Func<double, double, SurfaceWeights> _surface;

    public FieldMap(string id, string nameKey, double halfSize, Func<double, double, double> height,
        Func<double, double, SurfaceWeights> surface, MapLayout layout, MapAmbience ambience,
        IReadOnlyList<Prop> props, IReadOnlyList<MapOverlay> overlays)
    {
        Id = id;
        NameKey = nameKey;
        HalfSize = halfSize;
        _surface = surface;
        Layout = layout;
        Ambience = ambience;
        Props = props;
        Overlays = overlays;
        Terrain = new MapTerrain(height, props.SelectMany(p => p.Collision()));
    }

    public string Id { get; }

    /// <summary>Translation key of the name shown in the menu.</summary>
    public string NameKey { get; }

    /// <summary>The terrain covers [−HalfSize, HalfSize]² (m).</summary>
    public double HalfSize { get; }

    public MapTerrain Terrain { get; }
    public MapLayout Layout { get; }
    public MapAmbience Ambience { get; }
    public IReadOnlyList<Prop> Props { get; }
    public IReadOnlyList<MapOverlay> Overlays { get; }

    public SurfaceWeights Surface(double x, double y) => _surface(x, y);
}
```

(`<see cref="Club.ClubMap.Create"/>` resolves after Task 6; until then write "a map factory" in plain text.)

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/SimLab.App.Tests --filter PropTests`
Expected: PASS. If a hitbox assertion fails, fix the prop's geometry, **never** the test's geometric intent
(trunk and crown heart hit; beside the trunk, above the top and the outer 15 % miss).

- [ ] **Step 8: Commit** — `feat(maps): props whose drawn parts and hitboxes share one description`

---

### Task 6: The club map and the catalog

**Files:**
- Create: `src/SimLab.App/Maps/Club/ClubMap.cs`, `ClubParcels.cs`, `ClubPlanting.cs`, `ClubFurniture.cs`
- Create: `src/SimLab.App/Maps/FieldCatalog.cs`; delete `src/SimLab.App/Field/FieldCatalog.cs`
- Modify: `src/SimLab.App/Settings/AppSettings.cs` (`using SimLab.App.Maps;` instead of `SimLab.App.Field` if nothing else from `Field` is used)
- Modify: `game/Scripts/Menu/MainMenu.cs` (add `using SimLab.App.Maps;`)
- Test: `tests/SimLab.App.Tests/Maps/ClubMapTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces: `ClubMap` (namespace `SimLab.App.Maps.Club`) with `const string Id = "club"`, `const int Seed = 7`,
  `const double HalfSize = 1000`, `HillAmplitude = 8`, `FarmlandRadius = 350`, `RoadY = -220`, `TrackX = -50`,
  `PowerLineY = -229`, `static readonly MapLayout Layout`, `static readonly MapAmbience Ambience`,
  `static readonly IReadOnlyList<MapOverlay> Overlays`, `static double Height(double x, double y)`,
  `static SurfaceWeights Surface(double x, double y)`, `static bool KeepOut(double x, double y)`,
  `static bool InCorridor(double x, double y)`, `static FieldMap Create()`.
  `FieldCatalog` (namespace `SimLab.App.Maps`): `sealed record FieldEntry(string Id, string NameKey, Func<FieldMap> Create)`,
  `IReadOnlyList<FieldEntry> All`, `FieldEntry Find(string id)`, `FieldMap Load(string id)` (built once, cached, thread-safe).

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.App.Tests/Maps/ClubMapTests.cs`:

```csharp
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
    public void Catalog_finds_loads_and_falls_back_to_the_club()
    {
        Assert.Equal("club", FieldCatalog.Find("moon").Id);
        Assert.Same(FieldCatalog.Load("club"), FieldCatalog.Load("club"));
        Assert.Same(FieldCatalog.Load("club"), FieldCatalog.Load("moon"));
        Assert.Equal("FIELD_CLUB", FieldCatalog.Load("club").NameKey);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.App.Tests --filter ClubMapTests`
Expected: FAIL to build.

- [ ] **Step 3: Implement `ClubMap.cs`**

```csharp
using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Club;

/// <summary>
/// The generic club field (world ENU): a grass runway east–west centred on the origin, the pilot box south of it and
/// the pits behind; farmland, groves, a road with a power line beyond. The relief is flat within 300 m and rolls gently
/// further out.
/// </summary>
public static class ClubMap
{
    public const string Id = "club";
    public const int Seed = 7;
    public const double HalfSize = 1000;
    public const double FlatRadius = 300;
    public const double BlendWidth = 300;
    public const double HillAmplitude = 8;
    public const double FarmlandRadius = 350;
    public const double RoadY = -220;
    public const double TrackX = -50;
    public const double PowerLineY = -229;

    public static readonly MapLayout Layout = new(
        PilotPosition: new Vec3(0, -25, 0), EyeHeight: 1.7, WindsockPosition: new Vec3(20, -28, 0),
        RunwayCentre: Vec3.Zero, RunwayLength: 100, RunwayWidth: 15, RunwayHeadingDeg: 90);

    public static readonly MapAmbience Ambience = new(
        SkyTop: new Rgb(0.28f, 0.48f, 0.80f), SkyHorizon: new Rgb(0.66f, 0.76f, 0.88f),
        GroundHorizon: new Rgb(0.35f, 0.38f, 0.30f), GroundBottom: new Rgb(0.12f, 0.15f, 0.10f), FogDensity: 0.00008);

    /// <summary>The dirt track from the pits to the road, and the gravel road.</summary>
    public static readonly IReadOnlyList<MapOverlay> Overlays =
    [
        new(SurfaceKind.Dirt, [(TrackX, -48), (TrackX, RoadY)], 3.5),
        new(SurfaceKind.Gravel, [(-HalfSize, RoadY), (HalfSize, RoadY)], 5),
    ];

    public static FieldMap Create()
    {
        var props = new List<Prop>();
        props.AddRange(ClubPlanting.Plant(Seed));
        props.AddRange(ClubFurniture.Place());
        return new FieldMap(Id, "FIELD_CLUB", HalfSize, Height, Surface, Layout, Ambience, props, Overlays);
    }

    /// <summary>Ground height at (x east, y north). The hill formula was authored with z = south, hence z = −y.</summary>
    public static double Height(double x, double y)
    {
        double z = -y;
        double r = Math.Sqrt(x * x + z * z);
        double blend = SmoothStep((r - FlatRadius) / BlendWidth);
        if (blend <= 0) return 0;
        double h = 0.55 * Math.Sin(x / 137.0 + 0.3) * Math.Cos(z / 191.0 - 1.1)
                 + 0.30 * Math.Sin((x + z) / 83.0 + 2.0)
                 + 0.15 * Math.Cos((x - 2 * z) / 59.0);
        return HillAmplitude * blend * (h + 1.0) * 0.5;
    }

    /// <summary>Tall grass, a mowed apron around the runway, gravel pits behind the pilot, farmland far out.</summary>
    public static SurfaceWeights Surface(double x, double y)
    {
        var w = SurfaceWeights.Only(SurfaceKind.Grass);
        double farm = SmoothStep((Math.Sqrt(x * x + y * y) - FarmlandRadius) / 40);
        if (farm > 0) w = w.Toward(ClubParcels.KindAt(x, y), farm);
        w = w.Toward(SurfaceKind.MowedGrass, RectBlend(x, y, -70, -22, 70, 22, 4));
        w = w.Toward(SurfaceKind.Gravel, RectBlend(x, y, -65, -48, 12, -27, 3));
        return w;
    }

    /// <summary>Kept clear of vegetation: the runway with its safety margins, the pilot box and the pits.</summary>
    public static bool KeepOut(double x, double y) => Math.Abs(x) < 110 && Math.Abs(y) < 60;

    /// <summary>Strips kept clear of trees: the road, the power line and the track.</summary>
    public static bool InCorridor(double x, double y) =>
        Math.Abs(y - RoadY) < 9 || Math.Abs(y - PowerLineY) < 12 || (Math.Abs(x - TrackX) < 6 && y < -40 && y > RoadY);

    /// <summary>1 inside the rectangle, 0 from <paramref name="edge"/> metres outside it, smooth in between.</summary>
    static double RectBlend(double x, double y, double minX, double minY, double maxX, double maxY, double edge)
    {
        double dx = Math.Max(Math.Max(minX - x, x - maxX), 0), dy = Math.Max(Math.Max(minY - y, y - maxY), 0);
        return 1 - SmoothStep(Math.Sqrt(dx * dx + dy * dy) / edge);
    }

    internal static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
```

- [ ] **Step 4: Implement `ClubParcels.cs`**

```csharp
using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Club;

/// <summary>Farmland beyond the club: a grid of 140 m parcels turned 12°, each wheat, ploughed or meadow, some edged
/// with hedges.</summary>
static class ClubParcels
{
    const double Size = 140;
    const double YawDeg = 12;
    const double HedgeSection = 20;
    const int SectionsPerEdge = 6;               // 120 m of hedge, 10 m gaps at the corners
    static readonly Rgb Leaves = new(0.14f, 0.30f, 0.12f);

    public static SurfaceKind KindAt(double x, double y)
    {
        var (lx, ly) = PlanarYaw.ToLocal(x, y, YawDeg);
        return (Hash((int)Math.Floor(lx / Size), (int)Math.Floor(ly / Size)) % 3) switch
        {
            0 => SurfaceKind.Wheat,
            1 => SurfaceKind.Ploughed,
            _ => SurfaceKind.Grass,
        };
    }

    /// <summary>Hedges on the north and east edges of the parcels whose hash picks them, in 20 m sections so they
    /// follow the relief; sections that would reach the club, a corridor or the map edge are left out.</summary>
    public static IEnumerable<Hedge> Hedges(Random rng)
    {
        for (int i = -11; i <= 10; i++)
        for (int j = -11; j <= 10; j++)
        for (int edge = 0; edge < 2; edge++)
        {
            if (((Hash(i, j) >> (edge + 2)) & 1) == 0) continue;
            // Edge centre and direction in the parcel frame: north edges run along local x, east edges along local y.
            double cx = edge == 0 ? (i + 0.5) * Size : (i + 1) * Size;
            double cy = edge == 0 ? (j + 1) * Size : (j + 0.5) * Size;
            double hedgeYaw = edge == 0 ? YawDeg : YawDeg - 90;
            for (int s = 0; s < SectionsPerEdge; s++)
            {
                double along = (s - (SectionsPerEdge - 1) / 2.0) * HedgeSection;
                var (lx, ly) = edge == 0 ? (cx + along, cy) : (cx, cy + along);
                var (x, y) = PlanarYaw.ToWorld(lx, ly, YawDeg);
                if (!Allowed(x, y, hedgeYaw)) continue;
                float k = (float)(0.88 + rng.NextDouble() * 0.24);
                yield return new Hedge(new Vec3(x, y, ClubMap.Height(x, y)), hedgeYaw, HedgeSection, 1.5, 2.2,
                    new Rgb(Leaves.R * k, Leaves.G * k, Leaves.B * k));
            }
        }
    }

    static bool Allowed(double x, double y, double yawDeg)
    {
        for (int e = -1; e <= 1; e++)
        {
            var (dx, dy) = PlanarYaw.ToWorld(e * HedgeSection / 2, 0, yawDeg);
            double px = x + dx, py = y + dy;
            if (Math.Abs(px) > ClubMap.HalfSize - 20 || Math.Abs(py) > ClubMap.HalfSize - 20) return false;
            if (Math.Sqrt(px * px + py * py) < ClubMap.FarmlandRadius + 30) return false;
            if (ClubMap.InCorridor(px, py) || ClubMap.KeepOut(px, py)) return false;
        }
        return true;
    }

    static int Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 73856093u ^ (uint)b * 19349663u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }
}
```

- [ ] **Step 5: Implement `ClubPlanting.cs`**

```csharp
using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Club;

/// <summary>Deterministic vegetation: the old tree lines, poplars along the road, groves, field trees, bushes and
/// hedges. Distance and height cues for the pilot.</summary>
static class ClubPlanting
{
    static readonly Rgb Oak = new(0.16f, 0.33f, 0.14f);
    static readonly Rgb Fir = new(0.10f, 0.24f, 0.12f);
    static readonly Rgb Poplar = new(0.20f, 0.38f, 0.15f);
    static readonly Rgb Shrub = new(0.14f, 0.30f, 0.12f);

    public static IReadOnlyList<Prop> Plant(int seed)
    {
        var rng = new Random(seed);
        var props = new List<Prop>();

        double Jitter(double amplitude) => (rng.NextDouble() * 2 - 1) * amplitude;
        double Between(double lo, double hi) => lo + rng.NextDouble() * (hi - lo);
        Rgb Tint(Rgb c)
        {
            float k = (float)Between(0.88, 1.12);
            return new Rgb(c.R * k, c.G * k, c.B * k);
        }
        bool Free(double x, double y) => !ClubMap.KeepOut(x, y) && !ClubMap.InCorridor(x, y)
            && Math.Abs(x) < ClubMap.HalfSize - 10 && Math.Abs(y) < ClubMap.HalfSize - 10;
        Vec3 Ground(double x, double y) => new(x, y, ClubMap.Height(x, y));

        // Random values are drawn before the Free check, so rejecting one tree never shifts the others.
        void Broadleaf(double x, double y)
        {
            double h = Between(8, 18), crown = Between(0.28, 0.38) * h, yaw = Between(0, 360);
            var tint = Tint(Oak);
            if (Free(x, y)) props.Add(new BroadleafTree(Ground(x, y), yaw, h, crown, tint));
        }
        void Conifer(double x, double y)
        {
            double h = Between(10, 20), radius = Between(0.22, 0.30) * h, yaw = Between(0, 360);
            var tint = Tint(Fir);
            if (Free(x, y)) props.Add(new ConiferTree(Ground(x, y), yaw, h, radius, tint));
        }

        // Tree lines north, east and west of the field, as before the maps rework.
        for (double x = -400; x <= 400; x += 9) Broadleaf(x + Jitter(2), 140 + Jitter(6));
        for (double y = -200; y <= 130; y += 11)
        {
            Broadleaf(260 + Jitter(4), y + Jitter(3));
            Broadleaf(-260 + Jitter(4), y + Jitter(3));
        }

        // Poplars along the north side of the road (placed on purpose inside its corridor), not on the track.
        for (double x = -ClubMap.HalfSize + 20; x <= ClubMap.HalfSize - 20; x += 12)
        {
            double px = x + Jitter(1), h = Between(18, 25), crown = Between(0.11, 0.14) * h;
            var tint = Tint(Poplar);
            if (Math.Abs(px - ClubMap.TrackX) < 6) continue;
            props.Add(new PoplarTree(Ground(px, ClubMap.RoadY + 8), 0, h, crown, tint));
        }

        // Groves: broadleaf, mixed with conifers far away.
        for (int groves = 0; groves < 30;)
        {
            double cx = Between(-900, 900), cy = Between(-900, 900);
            double distance = Math.Sqrt(cx * cx + cy * cy);
            if (distance < 400) continue;
            groves++;
            int count = rng.Next(25, 71);
            double spread = Between(25, 60);
            for (int k = 0; k < count; k++)
            {
                double a = Between(0, 2 * Math.PI), r = spread * Math.Sqrt(rng.NextDouble());
                double x = cx + r * Math.Cos(a), y = cy + r * Math.Sin(a);
                if (distance > 600 && rng.NextDouble() < 0.6) Conifer(x, y);
                else Broadleaf(x, y);
            }
        }

        // Isolated field trees.
        for (int i = 0; i < 300; i++) Broadleaf(Between(-950, 950), Between(-950, 950));

        // Bushes.
        for (int i = 0; i < 120; i++)
        {
            double x = Between(-950, 950), y = Between(-950, 950), radius = Between(0.8, 1.8), h = Between(1, 2.5), yaw = Between(0, 360);
            var tint = Tint(Shrub);
            if (Free(x, y)) props.Add(new Bush(Ground(x, y), yaw, radius, h, tint));
        }

        props.AddRange(ClubParcels.Hedges(rng));
        return props;
    }
}
```

- [ ] **Step 6: Implement `ClubFurniture.cs`**

```csharp
using SimLab.App.Visual;
using SimLab.Flight.Geometry;

namespace SimLab.App.Maps.Club;

/// <summary>
/// Club buildings, tables, cars, the fence and the power line. Everything stands west of the pilot or behind the
/// pilot line, so nothing collidable is between the pilot and the runway (hand launches fly 3 m in front of the pilot)
/// and the menu's live view, east of the pilot, keeps its framing.
/// </summary>
static class ClubFurniture
{
    static readonly Rgb Wood = new(0.45f, 0.33f, 0.20f);
    static readonly Rgb Cladding = new(0.55f, 0.57f, 0.58f);
    static readonly Rgb Tiles = new(0.35f, 0.20f, 0.16f);
    static readonly Rgb SheetRoof = new(0.30f, 0.30f, 0.32f);
    static readonly Rgb[] Paints = [new(0.70f, 0.10f, 0.10f), new(0.85f, 0.85f, 0.88f), new(0.12f, 0.20f, 0.45f)];

    public static IReadOnlyList<Prop> Place()
    {
        static Vec3 Ground(double x, double y) => new(x, y, ClubMap.Height(x, y));
        var props = new List<Prop>
        {
            new Building(Ground(-52, -38), 0, 12, 6, 3.2, 1.2, false, Cladding, Tiles),
            new Building(Ground(-34, -36), 0, 8, 5, 2.4, 0.15, true, Wood, SheetRoof),
            new Table(Ground(-12, -31), 0, Wood),
            new Table(Ground(-8, -31), 0, Wood),
            new Table(Ground(8, -31), 0, Wood),
            new Table(Ground(12, -31), 0, Wood),
            // Behind the pilot line, between the pilots and the pits; stops short of the windsock at x = 20.
            new Fence(Ground(-3, -27.5), 0, 34, Wood),
        };
        // Parked side by side at the west end of the pits, facing north.
        for (int i = 0; i < Paints.Length; i++) props.Add(new Car(Ground(-64 + 2.7 * i, -44), -90, Paints[i]));
        // Along the south side of the road; the track passes between the poles at x = −75 and −25.
        props.Add(new PowerLine(Enumerable.Range(0, 40).Select(i => Ground(-975 + 50 * i, ClubMap.PowerLineY)).ToArray()));
        return props;
    }
}
```

- [ ] **Step 7: Move the catalog**

Delete `src/SimLab.App/Field/FieldCatalog.cs` and create `src/SimLab.App/Maps/FieldCatalog.cs`:

```csharp
using SimLab.App.Maps.Club;

namespace SimLab.App.Maps;

/// <param name="NameKey">Translation key of the name shown in the menu.</param>
public sealed record FieldEntry(string Id, string NameKey, Func<FieldMap> Create);

/// <summary>The flying fields the game offers.</summary>
public static class FieldCatalog
{
    public static IReadOnlyList<FieldEntry> All { get; } = [new(ClubMap.Id, "FIELD_CLUB", ClubMap.Create)];

    static readonly Dictionary<string, Lazy<FieldMap>> Maps = All.ToDictionary(f => f.Id, f => new Lazy<FieldMap>(f.Create));

    /// <summary>The field with this id, or the first one when it is unknown.</summary>
    public static FieldEntry Find(string id) => All.FirstOrDefault(f => f.Id == id) ?? All[0];

    /// <summary>The map of the field with this id (the first one when unknown), built once and then shared.</summary>
    public static FieldMap Load(string id) => Maps[Find(id).Id].Value;
}
```

Fix the `using`s: `AppSettings.cs` (`SimLab.App.Maps`), `game/Scripts/Menu/MainMenu.cs` (add `using SimLab.App.Maps;`;
`field.Id` and `field.NameKey` still work). `grep -rn "FieldCatalog\|FieldEntry" src game/Scripts tests` must
show no reference left to `SimLab.App.Field.FieldCatalog`.

- [ ] **Step 8: Run the tests and build the game**

Run: `dotnet test` → PASS. If `Tree_count_and_species_mix` is out of range, change the number of groves (30) or
isolated trees (300), not the range. `dotnet build game/SimLab.Game.csproj` → 0 warnings.

- [ ] **Step 9: Commit** — `feat(maps): the club map with vegetation, farmland, furniture and a power line`

---

### Task 7: Switch the session and the game to `FieldMap`

**Files:**
- Modify: `src/SimLab.App/Session/FlightSession.cs`
- Delete: `src/SimLab.App/Field/ClubField.cs`, `ClubFieldTerrain.cs`, `TreePlanter.cs`; remove `CylinderObstacle` from
  `src/SimLab.Flight/Terrain/ITerrain.cs`
- Modify tests: `tests/SimLab.App.Tests/Session/FlightSessionTests.cs`, `tests/SimLab.App.Tests/Audio/AircraftSoundTests.cs:13`,
  `tests/SimLab.App.Tests/Audio/OfflineAudioTests.cs:29`, `tests/SimLab.App.Tests/Field/FieldTests.cs` (keep only the
  `SunMath` and `Windsock` tests)
- Delete: `game/Scripts/World/FieldBuilder.cs` (+ `.uid`); create `game/Scripts/World/MapBuilder.cs`,
  `game/Scripts/World/GroundOverlays.cs`, `game/Shaders/terrain.gdshader`
- Modify: `game/Scripts/Flight/FlightScene.cs`, `game/Scripts/Main.cs`, `game/Scripts/Menu/MenuAircraftView.cs`,
  `game/Scripts/Menu/MainMenu.cs`
- Modify: `docs/dev-setup.md` (`--field`)

**Interfaces:**
- Consumes: `FieldMap`, `FieldCatalog.Load`, `MapLayout`, `MapOverlay`, `SurfaceWeights`, `ClubMap` (Tasks 4–6).
- Produces: `FlightSession(AircraftDefinition definition, FlightConditions conditions, FieldMap map)` with
  `FieldMap Map`, `ITerrain Terrain`; `MapBuilder.Build(Node3D root, FieldMap map, FlightConditions conditions) → FieldNodes`,
  `MapBuilder.AimSun(DirectionalLight3D, FlightConditions)`; `GroundOverlays.Add(Node3D root, FieldMap map)`;
  `MenuAircraftView.ShowField(FieldMap map)`. `MapBuilder.Build` calls `PropLayer.Add(root, map.Props)`, which
  Task 8 creates; in this task leave that line out.

- [ ] **Step 1: Session and tests**

`FlightSession`:
- constructor `FlightSession(AircraftDefinition definition, FlightConditions conditions, FieldMap map)`;
  `Map = map;` and `Terrain = map.Terrain;` before building the environments;
- `public FieldMap Map { get; }` and `public ITerrain Terrain { get; }` (type changes from `ClubFieldTerrain`);
- delete `TreeSeed`;
- `StartState()` uses `Map.Layout.TakeoffHeading`, `Map.Layout.TakeoffPoint` and `Map.Layout.HandLaunchPoint`;
- replace `using SimLab.App.Field;` with `using SimLab.App.Maps;`.

Tests: construct sessions with `FieldCatalog.Load("club")` as the third argument, and replace `ClubField.OnRunway` with
`ClubMap.Layout.OnRunway`. In `FieldTests.cs` delete every test that used `ClubField`, `ClubFieldTerrain` or
`TreePlanter`: they now live in `ClubMapTests`, except the normal-direction one. Move
`Normal_tilts_away_from_the_uphill_direction` into `ClubMapTests`, using `Club.Terrain.Normal` and `ClubMap.Height`.

Delete `ClubField.cs`, `ClubFieldTerrain.cs`, `TreePlanter.cs` and `CylinderObstacle`. Run
`grep -rn "ClubField\|TreePlanter\|CylinderObstacle\|TreeSeed" src tests game/Scripts`: only the game files fixed below
may remain.

Run: `dotnet test` → PASS.

- [ ] **Step 2: Terrain shader (colour version)**

`game/Shaders/terrain.gdshader`:

```glsl
shader_type spatial;
render_mode cull_disabled;

// Surface weights: COLOR = (grass, mowed grass, dirt, gravel), CUSTOM0 = (wheat, ploughed, 0, 0).
uniform vec3 grass_color : source_color = vec3(0.14, 0.34, 0.10);
uniform vec3 mowed_color : source_color = vec3(0.20, 0.44, 0.14);
uniform vec3 dirt_color : source_color = vec3(0.40, 0.31, 0.21);
uniform vec3 gravel_color : source_color = vec3(0.55, 0.52, 0.47);
uniform vec3 wheat_color : source_color = vec3(0.62, 0.55, 0.28);
uniform vec3 ploughed_color : source_color = vec3(0.30, 0.22, 0.15);

varying vec4 w0;
varying vec4 w1;
varying vec3 world_pos;

void vertex() {
    w0 = COLOR;
    w1 = CUSTOM0;
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
    vec3 c = grass_color * w0.r + mowed_color * w0.g + dirt_color * w0.b + gravel_color * w0.a
           + wheat_color * w1.r + ploughed_color * w1.g;
    // Large-scale tint variation so the ground never looks uniform.
    float macro = 0.85 + 0.3 * (0.6 * noise(world_pos.xz / 180.0) + 0.4 * noise(world_pos.xz / 55.0));
    ALBEDO = c * macro;
    ROUGHNESS = 1.0;
}
```

- [ ] **Step 3: `MapBuilder`**

Delete `FieldBuilder.cs` (and its `.uid`). Create `game/Scripts/World/MapBuilder.cs`:

```csharp
using Godot;
using SimLab.App.Field;
using SimLab.App.Maps;
using SimLab.App.Settings;
using SimLab.Flight.Geometry;

namespace SimLab.Game.World;

/// <summary>The map's nodes that change after it is built.</summary>
public readonly record struct FieldNodes(WindsockNode Windsock, DirectionalLight3D Sun);

/// <summary>Builds a map's scene: sky, sun, terrain, ground overlays, props and windsock.</summary>
public static class MapBuilder
{
    const float GridStep = 5f;
    const float ShadowDistance = 300f;

    public static FieldNodes Build(Node3D root, FieldMap map, FlightConditions conditions)
    {
        root.AddChild(Environment(map.Ambience));
        var sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.0f, DirectionalShadowMaxDistance = ShadowDistance };
        AimSun(sun, conditions);
        root.AddChild(sun);
        root.AddChild(TerrainMesh(map));
        GroundOverlays.Add(root, map);
        var w = map.Layout.WindsockPosition;
        var sock = new WindsockNode { Position = new Vec3(w.X, w.Y, map.Terrain.Height(w.X, w.Y)).WorldToGodot() };
        root.AddChild(sock);
        return new FieldNodes(sock, sun);
    }

    /// <summary>Points the light from the conditions' sun position.</summary>
    public static void AimSun(DirectionalLight3D sun, FlightConditions conditions)
    {
        var toSun = SunMath.Direction(conditions.SunAzimuthDeg, conditions.SunElevationDeg).WorldToGodot();
        var up = Mathf.Abs(toSun.Y) > 0.99f ? Vector3.Back : Vector3.Up;
        sun.Transform = Transform3D.Identity.LookingAt(-toSun, up);
    }

    static WorldEnvironment Environment(MapAmbience ambience)
    {
        var horizon = ambience.SkyHorizon.ToGodot();
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = ambience.SkyTop.ToGodot(),
                SkyHorizonColor = horizon,
                SkyCurve = 0.30f,
                GroundHorizonColor = ambience.GroundHorizon.ToGodot(),
                GroundBottomColor = ambience.GroundBottom.ToGodot(),
                SunAngleMax = 30f,
            },
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.35f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            FogEnabled = true,
            FogLightColor = horizon,
            FogDensity = (float)ambience.FogDensity,
            // Fog otherwise fully replaces the skybox at long (effectively infinite) view distance,
            // washing the sky out to FogLightColor regardless of density; keep it to hazing the
            // terrain and tree lines only.
            FogSkyAffect = 0f,
        };
        return new WorldEnvironment { Environment = environment };
    }

    /// <summary>A grid over the whole map; each vertex carries the ground normal and the surface weights
    /// (COLOR = grass, mowed, dirt, gravel; CUSTOM0 = wheat, ploughed) that the terrain shader blends.</summary>
    static MeshInstance3D TerrainMesh(FieldMap map)
    {
        int n = (int)(2 * map.HalfSize / GridStep);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);
        for (int j = 0; j <= n; j++)
        for (int i = 0; i <= n; i++)
        {
            double x = -map.HalfSize + i * GridStep, y = -map.HalfSize + j * GridStep;
            var w = map.Surface(x, y);
            st.SetColor(new Color((float)w.Grass, (float)w.MowedGrass, (float)w.Dirt, (float)w.Gravel));
            st.SetCustom(0, new Color((float)w.Wheat, (float)w.Ploughed, 0, 0));
            st.SetNormal(map.Terrain.Normal(x, y).WorldToGodot());
            st.AddVertex(new Vec3(x, y, map.Terrain.Height(x, y)).WorldToGodot());
        }
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
            st.AddIndex(a); st.AddIndex(b); st.AddIndex(d);
            st.AddIndex(a); st.AddIndex(d); st.AddIndex(c);
        }
        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/terrain.gdshader") },
        };
    }
}
```

Time the build of the terrain once (e.g. `System.Diagnostics.Stopwatch` printed with `GD.Print`, removed afterwards).
If it is over 1.5 s, build the mesh with `ArrayMesh.AddSurfaceFromArrays` from preallocated arrays instead, and say
so in the report.

- [ ] **Step 4: `GroundOverlays`**

`game/Scripts/World/GroundOverlays.cs`:

```csharp
using Godot;
using SimLab.App.Maps;
using SimLab.Flight.Geometry;

namespace SimLab.Game.World;

/// <summary>Flat features laid on the terrain: the runway's mowing stripes and thresholds, the pilot box, and the
/// map's tracks and roads as ribbons that follow the relief.</summary>
public static class GroundOverlays
{
    const float Lift = 0.03f;
    const double StripeLength = 5;
    const double ThresholdInset = 5;
    const double ThresholdWidth = 1.5;
    const double RibbonStep = 5;
    static readonly Color StripeLight = new(0.24f, 0.50f, 0.17f);
    static readonly Color StripeDark = new(0.19f, 0.42f, 0.13f);
    static readonly Color Threshold = new(0.32f, 0.58f, 0.24f);
    static readonly Color Gravel = new(0.55f, 0.52f, 0.47f);
    static readonly Color Dirt = new(0.40f, 0.31f, 0.21f);

    public static void Add(Node3D root, FieldMap map)
    {
        AddRunway(root, map);
        var pilot = map.Layout.PilotPosition;
        root.AddChild(Patch(map, pilot.X, pilot.Y, 8, 4, 0, Gravel, 0));
        foreach (var overlay in map.Overlays) root.AddChild(Ribbon(map, overlay));
    }

    static void AddRunway(Node3D root, FieldMap map)
    {
        var l = map.Layout;
        double yaw = l.RunwayHeadingDeg - 90;      // PlanarYaw frame with local x along the runway
        int stripes = (int)Math.Ceiling(l.RunwayLength / StripeLength);
        for (int i = 0; i < stripes; i++)
        {
            double along = -l.RunwayLength / 2 + (i + 0.5) * StripeLength;
            var (dx, dy) = PlanarYaw.ToWorld(along, 0, yaw);
            root.AddChild(Patch(map, l.RunwayCentre.X + dx, l.RunwayCentre.Y + dy, StripeLength, l.RunwayWidth, yaw,
                i % 2 == 0 ? StripeLight : StripeDark, 0));
        }
        foreach (int end in new[] { -1, 1 })
        {
            var (dx, dy) = PlanarYaw.ToWorld(end * (l.RunwayLength / 2 - ThresholdInset), 0, yaw);
            root.AddChild(Patch(map, l.RunwayCentre.X + dx, l.RunwayCentre.Y + dy, ThresholdWidth, l.RunwayWidth, yaw, Threshold, 0.005f));
        }
    }

    /// <summary>A flat rectangle (length along the yawed local x) at the terrain height of its centre.</summary>
    static MeshInstance3D Patch(FieldMap map, double x, double y, double length, double width, double yawDeg, Color color, float extraLift) => new()
    {
        Mesh = new PlaneMesh { Size = new Vector2((float)length, (float)width) },
        Transform = new Transform3D(new Basis(Vector3.Up, -Mathf.DegToRad((float)yawDeg)),
            new Vec3(x, y, map.Terrain.Height(x, y)).WorldToGodot() + new Vector3(0, Lift + extraLift, 0)),
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f },
    };

    static MeshInstance3D Ribbon(FieldMap map, MapOverlay overlay)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        double half = overlay.Width / 2;
        for (int s = 0; s + 1 < overlay.Path.Count; s++)
        {
            var (x0, y0) = overlay.Path[s];
            var (x1, y1) = overlay.Path[s + 1];
            double length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            double ux = (x1 - x0) / length, uy = (y1 - y0) / length;
            int steps = Math.Max(1, (int)Math.Ceiling(length / RibbonStep));
            for (int k = 0; k < steps; k++)
            {
                double t0 = k / (double)steps * length, t1 = (k + 1) / (double)steps * length;
                Vector3 V(double t, double side)
                {
                    double x = x0 + ux * t - uy * side, y = y0 + uy * t + ux * side;
                    return new Vec3(x, y, map.Terrain.Height(x, y)).WorldToGodot() + new Vector3(0, Lift, 0);
                }
                var a = V(t0, -half); var b = V(t0, half); var c = V(t1, half); var d = V(t1, -half);
                st.SetNormal(Vector3.Up);
                st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
                st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
            }
        }
        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = overlay.Kind == SurfaceKind.Gravel ? Gravel : Dirt,
                Roughness = 1f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
    }
}
```

- [ ] **Step 5: Wire the game to the map**

- `FlightScene.Init`: `_session = new FlightSession(definition, services.Settings.Conditions, FieldCatalog.Load(services.Settings.LastField));`
  `_windsock = MapBuilder.Build(this, _session.Map, services.Settings.Conditions).Windsock;` The pilot eye uses
  `_session.Map.Layout.PilotPosition` and `.EyeHeight`. The OSD home point (`FlightScene.cs:176`) uses
  `_session.Map.Layout.PilotPosition`.
- `Main.cs`:
  - At the top of `RunCommandLine`, before any mode:
    ```csharp
        // Map for the scripted modes; kept in memory only, never saved.
        if (ArgValue(args, "--field") is { } field)
            _services.Settings = _services.Settings with { LastField = FieldCatalog.Find(field).Id };
    ```
  - `--render-audio`: pass `FieldCatalog.Load(_services.Settings.LastField)` to `FlightSession`.
  - `PreviewField`: `var map = FieldCatalog.Load(_services.Settings.LastField); MapBuilder.Build(preview, map, …);`.
    Eye = pilot position at terrain height + eye height. Aim as today, using `map.Layout.RunwayLength` and
    `map.Layout.WindsockPosition`.
  - `PreviewAircraft`: `new FlightSession(definition, conditions, FieldCatalog.Load(_services.Settings.LastField))`
    and `MapBuilder.Build(preview, session.Map, …)`.
  - Replace `using SimLab.App.Field;` with `using SimLab.App.Maps;` where `Field` is no longer needed.
- `MenuAircraftView`:
  - keep a `Node3D _sceneryRoot` created in `BuildScenery` and a `FieldMap _map` field, set from a new parameter
    `Init(FlightConditions conditions, FieldMap map)` **before** the base `Init` call, which runs `BuildScenery`;
  - `Origin` becomes a property: the pilot position + (20, −21) at the terrain height + 4 m (the old (20, −46, 4) for
    the club):
    ```csharp
    Vec3 Origin
    {
        get
        {
            var p = _map.Layout.PilotPosition;
            double x = p.X + 20, y = p.Y - 21;
            return new Vec3(x, y, _map.Terrain.Height(x, y) + 4);
        }
    }
    ```
    Update its doc comment accordingly (the static readonly field and its comment go);
  - `public void ShowField(FieldMap map)`: sets `_map`, frees `_sceneryRoot`'s children, rebuilds with
    `MapBuilder.Build(_sceneryRoot, map, _conditions)`, stores the new `FieldNodes` and calls
    `ApplyConditions(_conditions)`. The windsock only takes a pose once it is inside the tree: `ApplyConditions`
    already checks `IsInsideTree()`; call it again with `CallDeferred` so the new windsock gets its pose.
- `MainMenu`: `_view.Init(services.Settings.Conditions, FieldCatalog.Load(services.Settings.LastField));`. In the
  field chip's `Pressed` handler, after saving, call `_view.ShowField(FieldCatalog.Load(field.Id));`.

- [ ] **Step 6: Docs**

`docs/dev-setup.md`, in the command-line table after `--screenshot-field`:

```
| `--field <id>` | Map used by the other modes (e.g. `club`), in memory only; the saved choice is unchanged |
```

and change the `--screenshot-field` description to `Pilot's view of the selected field (no aircraft)`.

- [ ] **Step 7: Build and verify**

```bash
dotnet test
dotnet build game/SimLab.Game.csproj
"$GODOT" --headless --path game -- --smoke-boot
"$GODOT" --headless --path game -- --smoke-flight trainer 10
"$GODOT" --path game -- --field club --screenshot-field /tmp/claude-maps-field.png
```

Expected: tests pass; 0 warnings; `SIMLAB_BOOT_OK`; `SIMLAB_SMOKE_OK`; a screenshot showing the terrain colours,
the striped runway, the pilot box and the windsock. Trees are absent until Task 8. Save screenshots in the session
scratchpad if `/tmp` is not allowed, and report their paths.

- [ ] **Step 8: Commit** — `feat(maps): flight session, menu and scene built from the selected map`

---

### Task 8: Drawing props — `PropMeshes` and `PropLayer`

**Files:**
- Create: `game/Scripts/World/PropMeshes.cs`, `game/Scripts/World/PropLayer.cs`
- Modify: `game/Scripts/World/MapBuilder.cs` (call `PropLayer.Add(root, map.Props)` after `GroundOverlays.Add`)

**Interfaces:**
- Consumes: `Prop.Parts()`, `PropPart`, `PartMesh`, `Foliage.CoreScale` (Task 5); `GodotBasis.PartRotation` (Task 4);
  `GodotConvert.ToGodot(Quat)`, `ToGodot(Rgb)`, `WorldToGodot`.
- Produces: `PropMeshes.Near(PartMesh) → Mesh`, `PropMeshes.Far(PartMesh) → Mesh?`, `PropMeshes.Material`;
  `PropLayer.Add(Node3D root, IReadOnlyList<Prop> props)`.

- [ ] **Step 1: Unit meshes**

`game/Scripts/World/PropMeshes.cs`:

```csharp
using System.Collections.Generic;
using Godot;
using SimLab.App.Maps;

namespace SimLab.Game.World;

/// <summary>
/// The unit meshes prop parts are drawn with (see <see cref="PartMesh"/>): each fills a 1 m cube centred on its
/// origin, local x → +X, local y → −Z, up → +Y. Foliage has a core at <see cref="Foliage.CoreScale"/> of the envelope
/// plus lobes or skirts reaching it, so the 85 % hitbox is always inside visible leaves. Tints come from the
/// MultiMesh instance colours.
/// </summary>
public static class PropMeshes
{
    static readonly Dictionary<PartMesh, Mesh> NearMeshes = new();
    static readonly Dictionary<PartMesh, Mesh?> FarMeshes = new();

    public static readonly StandardMaterial3D Material = new() { VertexColorUseAsAlbedo = true, Roughness = 1f };

    public static Mesh Near(PartMesh mesh)
    {
        if (!NearMeshes.TryGetValue(mesh, out var m)) NearMeshes[mesh] = m = BuildNear(mesh);
        return m;
    }

    /// <summary>A cheaper mesh for distant tiles, or null when the near one is already cheap.</summary>
    public static Mesh? Far(PartMesh mesh)
    {
        if (!FarMeshes.TryGetValue(mesh, out var m)) FarMeshes[mesh] = m = BuildFar(mesh);
        return m;
    }

    static Mesh BuildNear(PartMesh mesh) => mesh switch
    {
        PartMesh.Trunk => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 },
        PartMesh.Post => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1 },
        PartMesh.Box => new BoxMesh { Size = Vector3.One },
        PartMesh.Wire => Merge((new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 4, Rings = 1 },
            new Transform3D(new Basis(Vector3.Back, -Mathf.Pi / 2), Vector3.Zero))),
        // PrismMesh has its ridge along Z; turn it so the ridge runs along local x (+X).
        PartMesh.Roof => Merge((new PrismMesh { Size = Vector3.One }, new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), Vector3.Zero))),
        PartMesh.BroadleafCrown => BroadleafCrown(),
        PartMesh.ConiferCrown => ConiferCrown(),
        _ => new BoxMesh { Size = Vector3.One },
    };

    static Mesh? BuildFar(PartMesh mesh) => mesh switch
    {
        PartMesh.Trunk => new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 4, Rings = 1 },
        PartMesh.BroadleafCrown => new SphereMesh { Radius = 0.48f, Height = 0.96f, RadialSegments = 6, Rings = 3 },
        PartMesh.ConiferCrown => new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 5, Rings = 1 },
        _ => null,
    };

    /// <summary>A core sphere at the core scale plus five lobes, each touching the unit envelope from inside.</summary>
    static Mesh BroadleafCrown()
    {
        float core = 0.5f * (float)Foliage.CoreScale;
        const float lobe = 0.3f, offset = 0.5f - lobe;
        var parts = new List<(Mesh, Transform3D)> { (Sphere(core, 12, 6), Transform3D.Identity) };
        foreach (var dir in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back, Vector3.Up })
            parts.Add((Sphere(lobe, 8, 4), new Transform3D(Basis.Identity, dir * offset)));
        return Merge(parts.ToArray());
    }

    /// <summary>A core cone at the core scale plus three skirts, each steeper than the unit envelope cone (base
    /// radius 0.5 at y = −0.5, tip at y = 0.5) and touching it at its own base.</summary>
    static Mesh ConiferCrown()
    {
        var parts = new List<(Mesh, Transform3D)>
        {
            (new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f * (float)Foliage.CoreScale, Height = 1f, RadialSegments = 10, Rings = 1 }, Transform3D.Identity),
        };
        foreach (var (baseY, height) in new[] { (-0.5f, 0.5f), (-0.22f, 0.42f), (0.04f, 0.46f) })
        {
            float radius = 0.5f * (0.5f - baseY);
            parts.Add((new CylinderMesh { TopRadius = 0f, BottomRadius = radius, Height = height, RadialSegments = 10, Rings = 1 },
                new Transform3D(Basis.Identity, new Vector3(0, baseY + height / 2, 0))));
        }
        return Merge(parts.ToArray());
    }

    static SphereMesh Sphere(float radius, int radial, int rings) =>
        new() { Radius = radius, Height = 2 * radius, RadialSegments = radial, Rings = rings };

    static ArrayMesh Merge(params (Mesh Mesh, Transform3D Transform)[] parts)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (mesh, transform) in parts) st.AppendFrom(mesh, 0, transform);
        return st.Commit();
    }
}
```

`Merge` with a single part is used for the wire and the roof to bake their rotation. If `PrismMesh` turns out
upside down or the wrong way round in the screenshot, fix the transform here.

- [ ] **Step 2: Tiled, level-of-detail MultiMeshes**

`game/Scripts/World/PropLayer.cs`:

```csharp
using System.Collections.Generic;
using Godot;
using SimLab.App.Maps;
using SimLab.App.Mapping;

namespace SimLab.Game.World;

/// <summary>
/// Draws props as MultiMeshes grouped by unit mesh and by 250 m tile. Near tiles use the detailed meshes and cast
/// shadows; beyond <see cref="DetailRange"/> the simplified meshes take over without shadows.
/// </summary>
public static class PropLayer
{
    const float TileSize = 250f;
    const float DetailRange = 450f;
    const float FadeMargin = 30f;

    public static void Add(Node3D root, IReadOnlyList<Prop> props)
    {
        var groups = new Dictionary<(PartMesh Mesh, int TileX, int TileY), List<PropPart>>();
        foreach (var prop in props)
        foreach (var part in prop.Parts())
        {
            var key = (part.Mesh, (int)System.Math.Floor(part.Centre.X / TileSize), (int)System.Math.Floor(part.Centre.Y / TileSize));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<PropPart>();
            list.Add(part);
        }
        foreach (var ((mesh, _, _), parts) in groups)
        {
            var far = PropMeshes.Far(mesh);
            root.AddChild(Instances(PropMeshes.Near(mesh), parts, shadows: true, rangeBegin: 0, rangeEnd: far is null ? 0 : DetailRange));
            if (far is not null) root.AddChild(Instances(far, parts, shadows: false, rangeBegin: DetailRange, rangeEnd: 0));
        }
    }

    static MultiMeshInstance3D Instances(Mesh mesh, List<PropPart> parts, bool shadows, float rangeBegin, float rangeEnd)
    {
        var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh };
        multimesh.InstanceCount = parts.Count;
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            // Unit mesh axes: local x → +X, up → +Y, local y → −Z; so the size maps as (x, up, y).
            var basis = new Basis(GodotBasis.PartRotation(p.YawDeg, p.PitchDeg).ToGodot())
                * Basis.FromScale(new Vector3((float)p.Size.X, (float)p.Size.Z, (float)p.Size.Y));
            multimesh.SetInstanceTransform(i, new Transform3D(basis, p.Centre.WorldToGodot()));
            multimesh.SetInstanceColor(i, p.Tint.ToGodot());
        }
        return new MultiMeshInstance3D
        {
            Multimesh = multimesh,
            MaterialOverride = PropMeshes.Material,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeBegin = rangeBegin,
            VisibilityRangeBeginMargin = rangeBegin > 0 ? FadeMargin : 0,
            VisibilityRangeEnd = rangeEnd,
            VisibilityRangeEndMargin = rangeEnd > 0 ? FadeMargin : 0,
        };
    }
}
```

Add `PropLayer.Add(root, map.Props);` to `MapBuilder.Build` after `GroundOverlays.Add(root, map);`.

- [ ] **Step 3: Build and look**

```bash
dotnet build game/SimLab.Game.csproj
"$GODOT" --headless --path game -- --smoke-flight trainer 10
"$GODOT" --path game -- --screenshot-field <scratchpad>/field.png
"$GODOT" --path game -- --screenshot-flight trainer 8 <scratchpad>/flight-chase.png --view chase
"$GODOT" --path game -- --screenshot-menu <scratchpad>/menu.png
```

Open each PNG with the Read tool and check:
- trees stand on the ground with trunks under the crowns;
- conifers point up, poplars are tall and narrow, hedges follow the parcel edges;
- the hangar roof ridge runs along the hangar's length;
- wires hang between the cross-arms and sag;
- cars, tables and the fence stand behind the pilot line (west or south);
- the menu view still shows the windsock and the runway.

Fix any wrong orientation in `PropMeshes` / `PropLayer`, never in the App geometry, unless a test there is wrong too.

- [ ] **Step 4: Commit** — `feat(maps): draw props with tiled level-of-detail multimeshes`

---

### Task 9: Terrain textures (the controller downloads, the implementer wires them)

**Files:**
- Create: `game/Textures/terrain/{grass,dirt,gravel,soil}_{albedo,normal}.jpg` (+ `.import`), `game/Textures/CREDITS.md`
- Modify: `game/Shaders/terrain.gdshader`, `game/Scripts/World/MapBuilder.cs`, `game/Scripts/World/GroundOverlays.cs`

**Interfaces:**
- Consumes: the colour shader and overlays from Task 7.
- Produces: textured terrain and overlays; `MapBuilder` sets the shader's sampler parameters.

- [ ] **Step 1 (controller only): Download, after asking the user**

Ask the user in chat, naming the four assets, the source (ambientCG, CC0) and the total size (about 10–20 MB), and
wait for a yes. Then, for grass, dirt, gravel and ploughed soil, download the **1K-JPG** package from ambientCG. Keep
only `*_Color.jpg` → `<name>_albedo.jpg` and `*_NormalGL.jpg` → `<name>_normal.jpg` in `game/Textures/terrain/`.
Delete the zips and check the total with `du -sh game/Textures` (≤ 30 MB). Write `game/Textures/CREDITS.md`:

```markdown
# Texture credits

All textures are CC0 (public domain) from ambientCG (https://ambientcg.com), 1K JPG, colour and OpenGL normal maps only.

| File | ambientCG asset |
|---|---|
| terrain/grass_* | <asset id> |
| terrain/dirt_* | <asset id> |
| terrain/gravel_* | <asset id> |
| terrain/soil_* | <asset id> |
```

(with the real asset ids).

- [ ] **Step 2: Import settings**

Run `"$GODOT" --headless --path game --import`. In each generated `.import` file, set `mipmaps/generate=true` and
`compress/mode=2` (VRAM compressed); in the `*_normal.jpg.import` files also set `compress/normal_map=1`. Then run
`--import` again. Commit the `.import` files, not `.godot/`.

- [ ] **Step 3: Textured shader**

Replace the body of `game/Shaders/terrain.gdshader` with:

```glsl
shader_type spatial;
render_mode cull_disabled;

// Surface weights: COLOR = (grass, mowed grass, dirt, gravel), CUSTOM0 = (wheat, ploughed, 0, 0).
uniform sampler2D grass_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tile_metres = 4.0;
// Tints over the grass texture for the surfaces that have no texture of their own.
uniform vec3 grass_tint = vec3(1.0);
uniform vec3 mowed_tint = vec3(1.15, 1.25, 1.05);
uniform vec3 wheat_tint = vec3(1.9, 1.5, 0.55);

varying vec4 w0;
varying vec4 w1;
varying vec3 world_pos;

void vertex() {
    w0 = COLOR;
    w1 = CUSTOM0;
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

// Two scales mixed so the repetition of a 4 m tile is not visible from the air.
vec3 albedo(sampler2D s, vec2 uv) { return mix(texture(s, uv).rgb, texture(s, uv * 0.23).rgb, 0.4); }

void fragment() {
    vec2 uv = world_pos.xz / tile_metres;
    vec3 grass = albedo(grass_albedo, uv);
    vec3 c = grass * grass_tint * w0.r + grass * mowed_tint * w0.g + albedo(dirt_albedo, uv) * w0.b
           + albedo(gravel_albedo, uv) * w0.a + grass * wheat_tint * w1.r + albedo(soil_albedo, uv) * w1.g;
    vec3 n = texture(grass_normal, uv).rgb * (w0.r + w0.g + w1.r) + texture(dirt_normal, uv).rgb * w0.b
           + texture(gravel_normal, uv).rgb * w0.a + texture(soil_normal, uv).rgb * w1.g;
    float macro = 0.85 + 0.3 * (0.6 * noise(world_pos.xz / 180.0) + 0.4 * noise(world_pos.xz / 55.0));
    ALBEDO = c * macro;
    NORMAL_MAP = n;
    ROUGHNESS = 1.0;
}
```

In `MapBuilder.TerrainMesh`, build the material with the textures:

```csharp
    static ShaderMaterial TerrainMaterial()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/terrain.gdshader") };
        foreach (var name in new[] { "grass", "dirt", "gravel", "soil" })
        foreach (var map in new[] { "albedo", "normal" })
            material.SetShaderParameter($"{name}_{map}", GD.Load<Texture2D>($"res://Textures/terrain/{name}_{map}.jpg"));
        return material;
    }
```

- [ ] **Step 4: Textured overlays**

In `GroundOverlays`, give the runway stripes and thresholds the grass texture, and the ribbons and pilot box the dirt
or gravel texture. Use `StandardMaterial3D { AlbedoTexture = …, AlbedoColor = <existing colour as a tint, brightened
so the result matches the colour version>, NormalEnabled = true, NormalTexture = …, Uv1Triplanar = true,
Uv1WorldTriplanar = true, Uv1Scale = Vector3.One / 4f, Roughness = 1f }`. Cache one material per (texture, tint)
pair instead of creating one per stripe.

- [ ] **Step 5: Look and tune**

Take `--screenshot-field`, `--screenshot-flight trainer 8 … --view chase` and `--view fpv`, and `--screenshot-menu`,
and look at them. Tune only the shader tints, `tile_metres` and the overlay tints, until:
- the runway reads as mowed grass, lighter than the field;
- wheat reads golden and ploughed soil brown;
- no tile repetition is obvious from the chase view.

- [ ] **Step 6: Commit** — `feat(maps): CC0 terrain textures blended by surface`

---

### Task 10: Documentation and final verification

**Files:**
- Modify: `docs/realism-backlog.md`, `README.md`, `docs/manual-acceptance.md`

- [ ] **Step 1: Docs**

- `docs/realism-backlog.md`: add row 15. Observation: wheel rolling friction is the same on every ground. Likely
  cause: `WheelSpec.RollingFriction` ignores the surface. How to check: compare real takeoff rolls on grass, gravel
  and asphalt, then scale the friction by `SurfaceKind` (the map knows the surface at each point).
- `README.md` lines 16 and 18: say "flying fields (map model with the club field)" instead of "club field and terrain"
  / "club field".
- `docs/manual-acceptance.md`: add a "Maps" section with:
  - [ ] Flying close past a tree trunk (1–2 m) or over a treetop does not crash; flying into the crown does.
  - [ ] Flying into the hangar, a car or a power-line pole shows « Contre un obstacle »; into the wires, « Dans les câbles ».
  - [ ] The club shows textured grass, a striped runway, farmland, groves, poplars along the road and the power line.
  - [ ] F3 shows ≥ 60 fps in the pilot view and in the chase view over the groves.
  - [ ] The « Terrain » chip on the home screen rebuilds the background view (only the club for now).

- [ ] **Step 2: Full verification**

```bash
dotnet test
dotnet build game/SimLab.Game.csproj
"$GODOT" --headless --path game -- --smoke-boot
for i in 1 2 3; do "$GODOT" --headless --path game -- --smoke-flight trainer 20; done
"$GODOT" --headless --path game -- --smoke-flight wing 10
```

Expected:
- all tests pass (541 + the new ones, 1 known skip);
- 0 warnings;
- `SIMLAB_BOOT_OK`;
- `SIMLAB_SMOKE_OK` for each flight. The known ~1-in-12 teardown abort (exit 134 after `SIMLAB_SMOKE_OK`) is not a
  failure; report it if seen.

Then run the screenshot modes of Task 9 Step 5 again and look at them.

- [ ] **Step 3: Commit** — `docs(maps): acceptance checklist, backlog entry and README`
