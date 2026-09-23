# Symlab Godot Game Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the core libraries into a flyable desktop simulator: Godot 4 (.NET) app with radio (TX16S) setup and calibration, a club field, a line-of-sight camera, the three aircraft rendered with animated control surfaces, HUD, crash screen and French-first menus.

**Architecture:** All game logic that can be tested lives in a new pure .NET library `Symlab.App` (field and terrain, camera rig math, aircraft mesh generation, input routing, flight session, settings, translations, formatting). The Godot project in `game/` is a thin, code-built presentation layer (one `Main.tscn`, everything else constructed in C#) that polls the joypad, calls `Symlab.App`, and draws. Godot scripts are verified by building, a headless smoke run and a windowed screenshot; the final radio check is a manual acceptance run by the user.

**Tech Stack:** .NET 10 SDK; libraries target `net8.0`; Godot 4.7.2 .NET build (`godot-mono` cask) with `Godot.NET.Sdk/4.7.2`; xUnit v2; System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-09-22-symlab-core-design.md` (§2, §3, §6, §7 for this plan). Core-library API as built on branch `feat/core-libraries`.

## Global Constraints

- All code, identifiers, comments, commit messages and docs in **English**. In-game UI text goes through translations: **French is the default locale, English second**.
- `Symlab.Flight`, `Symlab.Input`, `Symlab.App` must **not** reference Godot or third-party runtime packages (BCL only). Only `game/` references Godot.
- World axes: right-handed, y-up, x east, z south (identical in Symlab and Godot). Body axes: x forward, y up, z right. A Godot node's local axes are −Z forward, +Y up, +X right; conversion only through `Symlab.App.Mapping.GodotBasis`.
- Physics fixed step 2 ms via `Simulation.Advance`; rendering interpolates `Simulation.Previous` → `Aircraft.State` with `InterpolationAlpha`.
- Sim-side expo and rates stay **neutral** by default (the pilot trains with the real radio settings).
- Godot binary: `GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"` (verify in Task 9; if different, use the real path and note it in `docs/dev-setup.md`).
- `TreatWarningsAsErrors` stays on for the pure libraries. In `game/` it stays on unless Godot-generated code produces warnings; then disable it **only** in `game/Symlab.Game.csproj` with a comment explaining why.
- Keyboard uses **physical** keys (AZERTY and QWERTY give the same layout).
- Commit after every task, Conventional Commit subject, message ending with the line
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Work on branch `feat/godot-game`, created from `feat/core-libraries`.

## Deviations from the spec (decided while planning)

- `model.glb` per aircraft is **not** required: the aircraft is drawn from a procedural mesh generated from its own aero geometry (surfaces, control surfaces, fuselage box, gear, prop disc), so every definition is visible and animated without art assets. Loading a `.glb` can be added later.
- "HDR sky" is a Godot `ProceduralSkyMaterial` with a configurable sun (azimuth/elevation); an HDR panorama can replace it later.
- The field physics terrain is an analytic height function (flat within 300 m of the runway, gentle hills beyond) shared by physics and rendering.

## File Map

```
src/Symlab.App/
  Symlab.App.csproj
  Mapping/GodotBasis.cs            body↔node axis conversion
  Mapping/StateInterpolation.cs    nlerp / state interpolation for rendering
  Field/ClubField.cs               layout: runway, pilot box, windsock, start points
  Field/ClubFieldTerrain.cs        ITerrain: analytic hills + tree obstacles
  Field/TreePlanter.cs             deterministic tree placement
  Field/SunMath.cs                 sun direction from azimuth/elevation
  Field/Windsock.cs                windsock yaw/droop from wind vector
  Cameras/ICameraRig.cs            CameraPose, CameraContext, ICameraRig
  Cameras/LineOfSightRig.cs        head-tracking camera with limited auto-zoom
  Visual/AircraftMeshBuilder.cs    procedural aircraft mesh parts
  Settings/FlightConditions.cs
  Settings/AppSettings.cs
  Settings/RadioProfileStore.cs
  Localization/TranslationTable.cs
  Ui/CalibrationPrompts.cs
  Ui/FlightDataFormatter.cs
  Session/InputRouter.cs           radio vs keyboard, switch/keyboard actions
  Session/SwitchCapture.cs         "press the switch now" binding capture
  Session/LatencyMeter.cs
  Session/AircraftCatalog.cs
  Session/FlightSession.cs         simulation ownership: start, reset, pause, wind toggle, recorder
tests/Symlab.App.Tests/            mirrors the folders above
game/
  project.godot  Symlab.Game.csproj  Main.tscn
  translations/.gdignore  translations/strings.csv
  Scripts/Main.cs  Scripts/AppPaths.cs  Scripts/GodotConvert.cs  Scripts/Translations.cs  Scripts/Ui.cs
  Scripts/Radio/JoypadReader.cs  Scripts/Radio/RadioScreen.cs
  Scripts/World/FieldBuilder.cs  Scripts/World/WindsockNode.cs
  Scripts/Flight/AircraftVisual.cs  Scripts/Flight/KeyboardInput.cs  Scripts/Flight/FlightScene.cs
  Scripts/Flight/FlightHud.cs  Scripts/Flight/CrashOverlay.cs  Scripts/Flight/DiagnosticsOverlay.cs
  Scripts/Menu/MainMenu.cs  Scripts/Menu/SettingsScreen.cs
docs/dev-setup.md  docs/manual-acceptance.md
```

---

### Task 1: `Symlab.App` library scaffold, axis mapping and state interpolation

**Files:**
- Create: `src/Symlab.App/Symlab.App.csproj`, `src/Symlab.App/Mapping/GodotBasis.cs`, `src/Symlab.App/Mapping/StateInterpolation.cs`
- Create: `tests/Symlab.App.Tests/Symlab.App.Tests.csproj`, `tests/Symlab.App.Tests/TestData.cs`
- Test: `tests/Symlab.App.Tests/Mapping/MappingTests.cs`

**Interfaces:**
- Consumes: `Vec3`, `Quat`, `Attitude`, `RigidBodyState` (Symlab.Flight).
- Produces:
  - `GodotBasis.NodeRotation(Quat bodyOrientation) : Quat` — rotation for a Godot node showing a body with that orientation.
  - `GodotBasis.BodyToNodeLocal(Vec3 body) : Vec3` — body-axis vector in node-local axes (`(b.Z, b.Y, −b.X)`).
  - `StateInterpolation.Nlerp(Quat a, Quat b, double t) : Quat`, `StateInterpolation.Lerp(Vec3 a, Vec3 b, double t) : Vec3`, `StateInterpolation.Interpolate(in RigidBodyState a, in RigidBodyState b, double t) : RigidBodyState`.
  - Test helper `TestData.RepoRoot`, `TestData.Aircraft(string id) : AircraftDefinition`.

- [ ] **Step 1: Check the branch and create the projects**

```bash
cd /Users/axel.ldq/3_SYMLAB
git branch --show-current   # must print feat/godot-game (created by the controller from feat/core-libraries)
```

`src/Symlab.App/Symlab.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Symlab.App</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Symlab.Flight/Symlab.Flight.csproj" />
    <ProjectReference Include="../Symlab.Input/Symlab.Input.csproj" />
  </ItemGroup>
</Project>
```

`tests/Symlab.App.Tests/Symlab.App.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Symlab.App/Symlab.App.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet add tests/Symlab.App.Tests package Microsoft.NET.Test.Sdk
dotnet add tests/Symlab.App.Tests package xunit
dotnet add tests/Symlab.App.Tests package xunit.runner.visualstudio
dotnet sln add src/Symlab.App/Symlab.App.csproj tests/Symlab.App.Tests/Symlab.App.Tests.csproj
```

`tests/Symlab.App.Tests/TestData.cs`:
```csharp
using Symlab.Flight.Airframe;

namespace Symlab.App.Tests;

internal static class TestData
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    public static AircraftDefinition Aircraft(string id) => AircraftLoader.Load(Path.Combine(RepoRoot, "aircraft", id));
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Symlab.App.Tests/Mapping/MappingTests.cs`:
```csharp
using Symlab.App.Mapping;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;

namespace Symlab.App.Tests.Mapping;

public class MappingTests
{
    static void Near(Vec3 e, Vec3 a, int p = 9)
    {
        Assert.Equal(e.X, a.X, p);
        Assert.Equal(e.Y, a.Y, p);
        Assert.Equal(e.Z, a.Z, p);
    }

    static readonly Quat Sample = Attitude.ToOrientation(Angle.Rad(20), Angle.Rad(-10), Angle.Rad(135));

    [Fact]
    public void Node_forward_is_body_forward()
        => Near(Sample.Rotate(Vec3.UnitX), GodotBasis.NodeRotation(Sample).Rotate(new Vec3(0, 0, -1)));

    [Fact]
    public void Node_right_is_body_right_and_up_is_up()
    {
        var node = GodotBasis.NodeRotation(Sample);
        Near(Sample.Rotate(Vec3.UnitZ), node.Rotate(Vec3.UnitX));
        Near(Sample.Rotate(Vec3.UnitY), node.Rotate(Vec3.UnitY));
    }

    [Fact]
    public void Body_points_map_to_the_same_world_points()
    {
        var p = new Vec3(0.4, -0.2, 0.75);
        Near(Sample.Rotate(p), GodotBasis.NodeRotation(Sample).Rotate(GodotBasis.BodyToNodeLocal(p)));
    }

    [Fact]
    public void Nlerp_takes_the_short_way_and_stays_normalized()
    {
        var a = Quat.FromAxisAngle(Vec3.UnitY, 0.1);
        var b = Quat.FromAxisAngle(Vec3.UnitY, 0.3) * -1.0;
        var mid = StateInterpolation.Nlerp(a, b, 0.5);
        Assert.Equal(1.0, mid.Length, 12);
        Near(Quat.FromAxisAngle(Vec3.UnitY, 0.2).Rotate(Vec3.UnitX), mid.Rotate(Vec3.UnitX), 3);
    }

    [Fact]
    public void Interpolate_blends_position_linearly()
    {
        var a = new RigidBodyState(Vec3.Zero, Vec3.Zero, Quat.Identity, Vec3.Zero);
        var b = a with { Position = new Vec3(2, 4, -6) };
        Near(new Vec3(0.5, 1, -1.5), StateInterpolation.Interpolate(a, b, 0.25).Position);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.App.Tests`
Expected: build FAILS (`GodotBasis` not found).

- [ ] **Step 4: Implement**

`src/Symlab.App/Mapping/GodotBasis.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.App.Mapping;

/// <summary>
/// Symlab world axes equal Godot world axes (x east, y up, z south). Symlab body axes (x forward, y up, z right)
/// differ from a Godot node's local axes (−Z forward, +Y up, +X right) by a −90° rotation about Y.
/// </summary>
public static class GodotBasis
{
    static readonly Quat NodeToBody = Quat.FromAxisAngle(Vec3.UnitY, -Math.PI / 2);

    /// <summary>Rotation for a Godot node that displays a body with the given Symlab orientation.</summary>
    public static Quat NodeRotation(Quat bodyOrientation) => (bodyOrientation * NodeToBody).Normalized();

    /// <summary>Converts a body-axis vector to the node's local axes.</summary>
    public static Vec3 BodyToNodeLocal(Vec3 body) => new(body.Z, body.Y, -body.X);
}
```

`src/Symlab.App/Mapping/StateInterpolation.cs`:
```csharp
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;

namespace Symlab.App.Mapping;

public static class StateInterpolation
{
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;

    /// <summary>Normalized linear quaternion interpolation along the shorter arc (accurate for 2 ms steps).</summary>
    public static Quat Nlerp(Quat a, Quat b, double t)
    {
        double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        if (dot < 0) b = b * -1.0;
        return (a * (1 - t) + b * t).Normalized();
    }

    public static RigidBodyState Interpolate(in RigidBodyState a, in RigidBodyState b, double t) => new(
        Lerp(a.Position, b.Position, t),
        Lerp(a.Velocity, b.Velocity, t),
        Nlerp(a.Orientation, b.Orientation, t),
        Lerp(a.AngularVelocity, b.AngularVelocity, t));
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS (existing suites plus the new one).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(app): add Symlab.App library with Godot axis mapping and state interpolation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Club field layout, terrain, trees, sun and windsock math

**Files:**
- Create: `src/Symlab.App/Field/ClubField.cs`, `ClubFieldTerrain.cs`, `TreePlanter.cs`, `SunMath.cs`, `Windsock.cs`
- Test: `tests/Symlab.App.Tests/Field/FieldTests.cs`

**Interfaces:**
- Consumes: `ITerrain`, `CylinderObstacle`, `Vec3`, `Angle`.
- Produces:
  - `ClubField` constants: `RunwayLength = 100`, `RunwayWidth = 15`, `FlatRadius = 300`, `BlendWidth = 300`, `HillAmplitude = 8`, `EyeHeight = 1.7`, `PilotPosition` (0, 0, 25), `WindsockPosition` (20, 0, 28), `TerrainHalfSize = 1000`; methods `OnRunway(x, z)`, `TakeoffHeading(windFromDeg) : double` (90 or 270), `TakeoffPoint(double headingDeg) : (double X, double Z)`, `HandLaunchPoint(double windFromDeg) : (double X, double Z, double HeadingDeg)`.
  - `ClubFieldTerrain(IEnumerable<CylinderObstacle> trees) : ITerrain`, static `GroundHeight(double x, double z)`, `Trees`.
  - `TreePlanter.Plant(int seed) : IReadOnlyList<CylinderObstacle>`, `TreePlanter.Excluded(double x, double z) : bool`.
  - `SunMath.Direction(double azimuthDeg, double elevationDeg) : Vec3` (unit vector toward the sun).
  - `WindsockPose(double HeadingDeg, double DroopDeg)`; `Windsock.Pose(Vec3 wind) : WindsockPose` (heading the sock points to = downwind, clockwise from north; droop 90° = hanging, 0° = horizontal at ≥ 7.7 m/s).

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.App.Tests/Field/FieldTests.cs`:
```csharp
using Symlab.App.Field;
using Symlab.Flight.Geometry;

namespace Symlab.App.Tests.Field;

public class FieldTests
{
    [Fact]
    public void Terrain_is_flat_around_the_runway_and_hilly_far_away()
    {
        Assert.Equal(0, ClubFieldTerrain.GroundHeight(0, 0));
        Assert.Equal(0, ClubFieldTerrain.GroundHeight(250, -150));
        double maxFar = 0;
        for (double x = -900; x <= 900; x += 50)
            maxFar = Math.Max(maxFar, ClubFieldTerrain.GroundHeight(x, 800));
        Assert.InRange(maxFar, 1, ClubField.HillAmplitude);
    }

    [Fact]
    public void Terrain_height_is_never_negative_and_normals_are_unit_and_upward()
    {
        var terrain = new ClubFieldTerrain([]);
        for (double x = -1000; x <= 1000; x += 97)
        for (double z = -1000; z <= 1000; z += 89)
        {
            Assert.True(terrain.Height(x, z) >= 0);
            var n = terrain.Normal(x, z);
            Assert.Equal(1.0, n.Length, 9);
            Assert.True(n.Y > 0.9);
        }
    }

    [Fact]
    public void Trees_are_deterministic_and_keep_clear_of_the_runway_and_pilot_box()
    {
        var a = TreePlanter.Plant(7);
        var b = TreePlanter.Plant(7);
        Assert.Equal(a, b);
        Assert.True(a.Count > 100, $"only {a.Count} trees");
        Assert.All(a, t =>
        {
            Assert.False(TreePlanter.Excluded(t.X, t.Z));
            Assert.InRange(t.Height, 8, 18);
            Assert.Equal(ClubFieldTerrain.GroundHeight(t.X, t.Z), t.BaseY, 9);
        });
        Assert.True(TreePlanter.Excluded(ClubField.PilotPosition.X, ClubField.PilotPosition.Z));
        Assert.True(TreePlanter.Excluded(45, 0));
    }

    [Fact]
    public void Terrain_reports_tree_hits()
    {
        var tree = TreePlanter.Plant(7)[0];
        var terrain = new ClubFieldTerrain([tree]);
        Assert.True(terrain.HitsObstacle(new Vec3(tree.X, tree.BaseY + 1, tree.Z)));
        Assert.False(terrain.HitsObstacle(new Vec3(0, 5, 0)));
    }

    [Theory]
    [InlineData(80, 90)]
    [InlineData(10, 90)]
    [InlineData(260, 270)]
    [InlineData(200, 270)]
    public void Takeoff_is_into_the_wind(double windFrom, double expectedHeading)
        => Assert.Equal(expectedHeading, ClubField.TakeoffHeading(windFrom));

    [Fact]
    public void Takeoff_point_is_at_the_downwind_end_of_the_runway()
    {
        var (x, z) = ClubField.TakeoffPoint(90);
        Assert.True(x < 0 && ClubField.OnRunway(x, z));
        Assert.True(ClubField.TakeoffPoint(270).X > 0);
    }

    [Fact]
    public void Hand_launch_is_in_front_of_the_pilot()
    {
        var (x, z, heading) = ClubField.HandLaunchPoint(270);
        Assert.Equal(ClubField.PilotPosition.X, x, 9);
        Assert.True(z < ClubField.PilotPosition.Z);
        Assert.Equal(270, heading);
    }

    [Fact]
    public void Sun_direction_follows_azimuth_and_elevation()
    {
        var south = SunMath.Direction(180, 0);
        Assert.Equal(0, south.X, 9);
        Assert.Equal(1, south.Z, 9);
        Assert.Equal(1, SunMath.Direction(0, 90).Y, 9);
        Assert.Equal(1, SunMath.Direction(90, 0).X, 9);
    }

    [Fact]
    public void Windsock_points_downwind_and_hangs_in_calm_air()
    {
        var fromWest = Windsock.Pose(new Vec3(8, 0, 0));
        Assert.Equal(90, fromWest.HeadingDeg, 6);
        Assert.Equal(0, fromWest.DroopDeg, 6);
        Assert.Equal(90, Windsock.Pose(Vec3.Zero).DroopDeg, 6);
        Assert.InRange(Windsock.Pose(new Vec3(0, 0, 3)).DroopDeg, 40, 70);
        Assert.Equal(180, Windsock.Pose(new Vec3(0, 0, 3)).HeadingDeg, 6);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.App.Tests --filter FullyQualifiedName~FieldTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.App/Field/ClubField.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.App.Field;

/// <summary>Layout of the generic club field. The runway runs east-west, centered on the origin; the pilot box is south of it.</summary>
public static class ClubField
{
    public const double RunwayLength = 100;
    public const double RunwayWidth = 15;
    public const double FlatRadius = 300;
    public const double BlendWidth = 300;
    public const double HillAmplitude = 8;
    public const double EyeHeight = 1.7;
    public const double TerrainHalfSize = 1000;
    const double TakeoffInset = 8;
    const double HandLaunchDistance = 3;

    public static readonly Vec3 PilotPosition = new(0, 0, 25);
    public static readonly Vec3 WindsockPosition = new(20, 0, 28);

    public static bool OnRunway(double x, double z) => Math.Abs(x) <= RunwayLength / 2 && Math.Abs(z) <= RunwayWidth / 2;

    /// <summary>Runway heading (90 = toward east, 270 = toward west) that points most directly into the wind.</summary>
    public static double TakeoffHeading(double windFromDeg) =>
        AngleBetween(windFromDeg, 90) <= AngleBetween(windFromDeg, 270) ? 90 : 270;

    public static (double X, double Z) TakeoffPoint(double headingDeg) =>
        headingDeg == 90 ? (-RunwayLength / 2 + TakeoffInset, 0) : (RunwayLength / 2 - TakeoffInset, 0);

    public static (double X, double Z, double HeadingDeg) HandLaunchPoint(double windFromDeg) =>
        (PilotPosition.X, PilotPosition.Z - HandLaunchDistance, TakeoffHeading(windFromDeg));

    static double AngleBetween(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360));
}
```

`src/Symlab.App/Field/ClubFieldTerrain.cs`:
```csharp
using Symlab.Flight.Geometry;
using Symlab.Flight.Terrain;

namespace Symlab.App.Field;

/// <summary>Flat within <see cref="ClubField.FlatRadius"/> of the runway, gentle analytic hills beyond; trees as obstacles.</summary>
public sealed class ClubFieldTerrain : ITerrain
{
    const double NormalStep = 0.5;
    readonly CylinderObstacle[] _trees;

    public ClubFieldTerrain(IEnumerable<CylinderObstacle> trees) => _trees = trees.ToArray();

    public IReadOnlyList<CylinderObstacle> Trees => _trees;

    public static double GroundHeight(double x, double z)
    {
        double r = Math.Sqrt(x * x + z * z);
        double blend = SmoothStep((r - ClubField.FlatRadius) / ClubField.BlendWidth);
        if (blend <= 0) return 0;
        double h = 0.55 * Math.Sin(x / 137.0 + 0.3) * Math.Cos(z / 191.0 - 1.1)
                 + 0.30 * Math.Sin((x + z) / 83.0 + 2.0)
                 + 0.15 * Math.Cos((x - 2 * z) / 59.0);
        return ClubField.HillAmplitude * blend * (h + 1.0) * 0.5;
    }

    public double Height(double x, double z) => GroundHeight(x, z);

    public Vec3 Normal(double x, double z)
    {
        double dx = (GroundHeight(x + NormalStep, z) - GroundHeight(x - NormalStep, z)) / (2 * NormalStep);
        double dz = (GroundHeight(x, z + NormalStep) - GroundHeight(x, z - NormalStep)) / (2 * NormalStep);
        return new Vec3(-dx, 1, -dz).Normalized();
    }

    public bool HitsObstacle(Vec3 p)
    {
        foreach (var t in _trees)
            if (t.Contains(p)) return true;
        return false;
    }

    static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
```

`src/Symlab.App/Field/TreePlanter.cs`:
```csharp
using Symlab.Flight.Terrain;

namespace Symlab.App.Field;

/// <summary>Deterministic tree lines, hedgerows and scattered trees used as distance and height cues.</summary>
public static class TreePlanter
{
    public static IReadOnlyList<CylinderObstacle> Plant(int seed)
    {
        var rng = new Random(seed);
        var trees = new List<CylinderObstacle>();

        double Jitter(double amplitude) => (rng.NextDouble() * 2 - 1) * amplitude;

        void Add(double x, double z)
        {
            double height = 8 + rng.NextDouble() * 10;
            if (Excluded(x, z)) return;
            trees.Add(new CylinderObstacle(x, z, 0.25 * height, height, ClubFieldTerrain.GroundHeight(x, z)));
        }

        for (double x = -400; x <= 400; x += 9) Add(x + Jitter(2), -140 + Jitter(6));
        for (double z = -130; z <= 200; z += 11)
        {
            Add(260 + Jitter(4), z + Jitter(3));
            Add(-260 + Jitter(4), z + Jitter(3));
        }
        for (int i = 0; i < 250; i++) Add(rng.NextDouble() * 1800 - 900, rng.NextDouble() * 1800 - 900);
        return trees;
    }

    /// <summary>Keep-out area: runway with safety margins and the pilot box.</summary>
    public static bool Excluded(double x, double z) => Math.Abs(x) < 110 && Math.Abs(z) < 60;
}
```

`src/Symlab.App/Field/SunMath.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.App.Field;

public static class SunMath
{
    /// <summary>Unit vector from the ground toward the sun. Azimuth clockwise from north (−Z), elevation above the horizon.</summary>
    public static Vec3 Direction(double azimuthDeg, double elevationDeg)
    {
        double az = Angle.Rad(azimuthDeg), el = Angle.Rad(elevationDeg);
        return new Vec3(Math.Sin(az) * Math.Cos(el), Math.Sin(el), -Math.Cos(az) * Math.Cos(el));
    }
}
```

`src/Symlab.App/Field/Windsock.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.App.Field;

/// <param name="HeadingDeg">Direction the sock points to (downwind), clockwise from north.</param>
/// <param name="DroopDeg">0 = horizontal, 90 = hanging straight down.</param>
public readonly record struct WindsockPose(double HeadingDeg, double DroopDeg);

public static class Windsock
{
    /// <summary>Wind speed at which a standard sock is fully extended (15 kt).</summary>
    public const double FullExtensionSpeed = 7.7;

    public static WindsockPose Pose(Vec3 wind)
    {
        double horizontal = Math.Sqrt(wind.X * wind.X + wind.Z * wind.Z);
        double heading = horizontal > 1e-6 ? Angle.Deg(Math.Atan2(wind.X, -wind.Z)) : 0;
        if (heading < 0) heading += 360;
        double droop = 90 * (1 - Math.Clamp(horizontal / FullExtensionSpeed, 0, 1));
        return new WindsockPose(heading, droop);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Symlab.App.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(app): add club field layout, terrain, trees, sun and windsock math

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Camera rig interface and line-of-sight camera

**Files:**
- Create: `src/Symlab.App/Cameras/ICameraRig.cs`, `src/Symlab.App/Cameras/LineOfSightRig.cs`
- Test: `tests/Symlab.App.Tests/Cameras/LineOfSightRigTests.cs`

**Interfaces:**
- Consumes: `Vec3`, `Quat`, `Angle`.
- Produces:
  - `CameraPose(Vec3 Position, Vec3 LookAt, double VerticalFovDeg)`.
  - `CameraContext(Vec3 AircraftPosition, Quat AircraftOrientation, double AircraftSpan)`.
  - `interface ICameraRig { CameraPose Update(double dt, in CameraContext ctx); void Reset(in CameraContext ctx); }`
  - `LineOfSightRig(Vec3 eye, double baseFovDeg, bool autoZoom)` with `Eye`, `BaseFovDeg` (settable), `AutoZoom` (settable), `ZoomedFov(double distance, double span) : double`, constants `HeadTimeConstant = 0.06`, `TargetScreenFraction = 0.08`, `MaxZoomFactor = 3.0`; static `FovForScreen(double screenHeightCm, double viewingDistanceCm) : double`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.App.Tests/Cameras/LineOfSightRigTests.cs`:
```csharp
using Symlab.App.Cameras;
using Symlab.Flight.Geometry;

namespace Symlab.App.Tests.Cameras;

public class LineOfSightRigTests
{
    static readonly Vec3 Eye = new(0, 1.7, 25);

    static CameraContext At(Vec3 p) => new(p, Quat.Identity, 1.5);

    static Vec3 LookDirection(CameraPose pose) => (pose.LookAt - pose.Position).Normalized();

    [Fact]
    public void Camera_sits_at_the_eye_and_converges_on_the_aircraft()
    {
        var rig = new LineOfSightRig(Eye, 50, autoZoom: false);
        var target = new Vec3(40, 20, -30);
        rig.Reset(At(target));
        CameraPose pose = default;
        for (int i = 0; i < 60; i++) pose = rig.Update(1.0 / 60, At(target));
        Assert.Equal(Eye, pose.Position);
        var expected = (target - Eye).Normalized();
        Assert.True(Vec3.Dot(LookDirection(pose), expected) > 0.9999);
        Assert.Equal(50, pose.VerticalFovDeg);
    }

    [Fact]
    public void Head_lags_behind_a_sudden_jump()
    {
        var rig = new LineOfSightRig(Eye, 50, autoZoom: false);
        rig.Reset(At(new Vec3(0, 20, -50)));
        var pose = rig.Update(0.016, At(new Vec3(80, 20, 25)));
        var toNew = (new Vec3(80, 20, 25) - Eye).Normalized();
        double dot = Vec3.Dot(LookDirection(pose), toNew);
        Assert.True(dot < 0.99, $"no lag: {dot}");
    }

    [Fact]
    public void Auto_zoom_narrows_the_view_for_far_aircraft_but_only_three_times()
    {
        var rig = new LineOfSightRig(Eye, 60, autoZoom: true);
        Assert.Equal(60, rig.ZoomedFov(10, 1.5), 9);
        Assert.Equal(20, rig.ZoomedFov(400, 1.5), 9);
        double mid = rig.ZoomedFov(60, 1.5);
        Assert.InRange(mid, 20, 60);
    }

    [Fact]
    public void Auto_zoom_off_keeps_the_true_apparent_size()
    {
        var rig = new LineOfSightRig(Eye, 45, autoZoom: false);
        rig.Reset(At(new Vec3(0, 50, -300)));
        Assert.Equal(45, rig.Update(0.016, At(new Vec3(0, 50, -300))).VerticalFovDeg);
    }

    [Fact]
    public void Fov_matches_screen_geometry()
        => Assert.Equal(28.07, LineOfSightRig.FovForScreen(30, 60), 2);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.App.Tests --filter FullyQualifiedName~LineOfSightRigTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.App/Cameras/ICameraRig.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.App.Cameras;

public readonly record struct CameraPose(Vec3 Position, Vec3 LookAt, double VerticalFovDeg);

/// <param name="AircraftPosition">Interpolated aircraft CG position, world axes.</param>
/// <param name="AircraftSpan">Largest dimension of the aircraft (wingspan), m.</param>
public readonly record struct CameraContext(Vec3 AircraftPosition, Quat AircraftOrientation, double AircraftSpan);

/// <summary>A camera behaviour. Line-of-sight now; chase and FPV rigs come in sub-project 3.</summary>
public interface ICameraRig
{
    CameraPose Update(double dt, in CameraContext ctx);
    void Reset(in CameraContext ctx);
}
```

`src/Symlab.App/Cameras/LineOfSightRig.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.App.Cameras;

/// <summary>The pilot's eyes in the pilot box: follows the aircraft like a head, with optional limited auto-zoom.</summary>
public sealed class LineOfSightRig : ICameraRig
{
    public const double HeadTimeConstant = 0.06;
    public const double TargetScreenFraction = 0.08;
    public const double MaxZoomFactor = 3.0;

    Vec3 _look = new(0, 0, -1);
    bool _initialized;

    public LineOfSightRig(Vec3 eye, double baseFovDeg, bool autoZoom)
    {
        Eye = eye;
        BaseFovDeg = baseFovDeg;
        AutoZoom = autoZoom;
    }

    public Vec3 Eye { get; }
    public double BaseFovDeg { get; set; }
    public bool AutoZoom { get; set; }

    public void Reset(in CameraContext ctx)
    {
        var to = ctx.AircraftPosition - Eye;
        if (to.Length > 1e-6) _look = to.Normalized();
        _initialized = true;
    }

    public CameraPose Update(double dt, in CameraContext ctx)
    {
        if (!_initialized) Reset(ctx);
        var to = ctx.AircraftPosition - Eye;
        double distance = to.Length;
        var direction = distance > 1e-6 ? to / distance : _look;
        double k = 1 - Math.Exp(-Math.Max(dt, 0) / HeadTimeConstant);
        _look = (_look + (direction - _look) * k).Normalized();
        double fov = AutoZoom ? ZoomedFov(distance, ctx.AircraftSpan) : BaseFovDeg;
        return new CameraPose(Eye, Eye + _look * Math.Max(distance, 1.0), fov);
    }

    /// <summary>FOV that shows the span at <see cref="TargetScreenFraction"/> of the screen, never narrower than base/<see cref="MaxZoomFactor"/>.</summary>
    public double ZoomedFov(double distance, double span)
    {
        double apparent = Angle.Deg(2 * Math.Atan(span / (2 * Math.Max(distance, 0.1))));
        return Math.Clamp(apparent / TargetScreenFraction, BaseFovDeg / MaxZoomFactor, BaseFovDeg);
    }

    /// <summary>Vertical FOV giving true apparent size for a screen of that height seen from that distance.</summary>
    public static double FovForScreen(double screenHeightCm, double viewingDistanceCm) =>
        Angle.Deg(2 * Math.Atan(screenHeightCm / (2 * viewingDistanceCm)));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Symlab.App.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(app): add camera rig interface and line-of-sight camera

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Procedural aircraft mesh

**Files:**
- Create: `src/Symlab.App/Visual/AircraftMeshBuilder.cs`
- Test: `tests/Symlab.App.Tests/Visual/AircraftMeshBuilderTests.cs`

**Interfaces:**
- Consumes: `AircraftDefinition`, `SurfaceSegment` (with `Position`, `ChordAxis`, `NormalAxis`, `FlowChordAxis`, `FlowNormalAxis`, `Chord`, `Area`, `ControlIndex`, `ControlChordFraction`), `Aircraft.Aero.Segments`, `Quat`, `Vec3`.
- Produces:
  - `Rgb(float R, float G, float B)`.
  - `MeshPart(string Name, int ControlIndex, Vec3 HingePoint, Vec3 HingeAxis, IReadOnlyList<Vec3> Triangles, Rgb Color)` — vertices in body axes, three per triangle; `ControlIndex` −1 for fixed parts; for control parts, rotating the part's vertices about `HingeAxis` (through `HingePoint`) by the deflection angle (radians, positive = trailing edge down) moves the trailing edge toward the surface's negative normal.
  - `AircraftMeshBuilder.Build(AircraftDefinition definition, IReadOnlyList<SurfaceSegment> segments) : IReadOnlyList<MeshPart>` — part names: `airframe`, each control's name, `fuselage`, `gear` (if wheels), `propeller` (if power).

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.App.Tests/Visual/AircraftMeshBuilderTests.cs`:
```csharp
using Symlab.App.Visual;
using Symlab.Flight.Airframe;
using Symlab.Flight.Geometry;

namespace Symlab.App.Tests.Visual;

public class AircraftMeshBuilderTests
{
    static IReadOnlyList<MeshPart> Parts(string id)
    {
        var def = TestData.Aircraft(id);
        return AircraftMeshBuilder.Build(def, new Aircraft(def).Aero.Segments);
    }

    static Vec3 Centroid(IReadOnlyList<Vec3> v) => v.Aggregate(Vec3.Zero, (a, b) => a + b) / v.Count;

    static Vec3 RotateAboutHinge(MeshPart part, Vec3 p, double angle) =>
        part.HingePoint + Quat.FromAxisAngle(part.HingeAxis, angle).Rotate(p - part.HingePoint);

    [Theory]
    [InlineData("trainer", new[] { "airframe", "aileronRight", "aileronLeft", "elevator", "rudder", "fuselage", "gear", "propeller" })]
    [InlineData("wing", new[] { "airframe", "elevonRight", "elevonLeft", "fuselage", "propeller" })]
    public void Builds_one_part_per_control_plus_fixed_parts(string id, string[] names)
    {
        var parts = Parts(id);
        Assert.Equal(names.OrderBy(n => n), parts.Select(p => p.Name).OrderBy(n => n));
        Assert.All(parts, p =>
        {
            Assert.True(p.Triangles.Count > 0 && p.Triangles.Count % 3 == 0, p.Name);
            Assert.Equal(1.0, p.HingeAxis.Length, 9);
        });
    }

    [Fact]
    public void Trailing_edge_down_deflection_lowers_the_right_aileron()
    {
        var aileron = Parts("trainer").Single(p => p.Name == "aileronRight");
        var c = Centroid(aileron.Triangles);
        Assert.True(RotateAboutHinge(aileron, c, 0.3).Y < c.Y);
    }

    [Fact]
    public void Positive_rudder_deflection_moves_its_trailing_edge_right()
    {
        var rudder = Parts("trainer").Single(p => p.Name == "rudder");
        var c = Centroid(rudder.Triangles);
        Assert.True(RotateAboutHinge(rudder, c, 0.3).Z > c.Z);
    }

    [Fact]
    public void Control_parts_sit_aft_of_their_hinge()
    {
        var elevator = Parts("trainer").Single(p => p.Name == "elevator");
        Assert.True(Centroid(elevator.Triangles).X < elevator.HingePoint.X);
    }

    [Fact]
    public void Fuselage_spans_the_hull_points()
    {
        var def = TestData.Aircraft("trainer");
        var fuselage = Parts("trainer").Single(p => p.Name == "fuselage");
        Assert.Equal(def.Hull.Max(h => h.Position.X), fuselage.Triangles.Max(v => v.X), 6);
        Assert.Equal(def.Hull.Min(h => h.Position.X), fuselage.Triangles.Min(v => v.X), 6);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.App.Tests --filter FullyQualifiedName~AircraftMeshBuilderTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.App/Visual/AircraftMeshBuilder.cs`:
```csharp
using Symlab.Flight.Aero;
using Symlab.Flight.Airframe;
using Symlab.Flight.Geometry;

namespace Symlab.App.Visual;

public readonly record struct Rgb(float R, float G, float B);

/// <summary>
/// A renderable piece of the aircraft. Vertices are in body axes, three per triangle. For control parts, rotating the
/// vertices about <see cref="HingeAxis"/> through <see cref="HingePoint"/> by the deflection (rad, positive = trailing
/// edge down) reproduces the physical deflection.
/// </summary>
public sealed record MeshPart(string Name, int ControlIndex, Vec3 HingePoint, Vec3 HingeAxis, IReadOnlyList<Vec3> Triangles, Rgb Color);

/// <summary>Builds a simple flat-panel model of an aircraft directly from its aerodynamic geometry.</summary>
public static class AircraftMeshBuilder
{
    static readonly Rgb SurfaceColor = new(0.93f, 0.91f, 0.86f);
    static readonly Rgb ControlColor = new(0.96f, 0.47f, 0.10f);
    static readonly Rgb FuselageColor = new(0.78f, 0.16f, 0.12f);
    static readonly Rgb DarkColor = new(0.12f, 0.12f, 0.12f);
    const double FuselageWidth = 0.09;
    const double FuselageHeight = 0.11;
    const double GearSize = 0.05;
    const int PropSides = 16;

    public static IReadOnlyList<MeshPart> Build(AircraftDefinition definition, IReadOnlyList<SurfaceSegment> segments)
    {
        var parts = new List<MeshPart>();
        var fixedTriangles = new List<Vec3>();
        var controlTriangles = new SortedDictionary<int, List<Vec3>>();
        var hinges = new Dictionary<int, (Vec3 Point, Vec3 Axis)>();

        foreach (var s in segments)
        {
            var span = Vec3.Cross(s.FlowChordAxis, s.FlowNormalAxis).Normalized();
            var half = span * (s.Area / s.Chord / 2);
            var leading = s.Position + s.ChordAxis * (0.25 * s.Chord);
            var trailing = s.Position - s.ChordAxis * (0.75 * s.Chord);
            if (s.ControlIndex < 0)
            {
                AddQuad(fixedTriangles, leading - half, leading + half, trailing + half, trailing - half);
                continue;
            }

            var hinge = trailing + s.ChordAxis * (s.ControlChordFraction * s.Chord);
            AddQuad(fixedTriangles, leading - half, leading + half, hinge + half, hinge - half);
            if (!controlTriangles.TryGetValue(s.ControlIndex, out var list)) controlTriangles[s.ControlIndex] = list = [];
            AddQuad(list, hinge - half, hinge + half, trailing + half, trailing - half);
            if (!hinges.ContainsKey(s.ControlIndex))
            {
                var toTrailing = trailing - hinge;
                double sign = Vec3.Dot(Vec3.Cross(span, toTrailing), s.NormalAxis * -1) >= 0 ? 1 : -1;
                hinges[s.ControlIndex] = (hinge, span * sign);
            }
        }

        parts.Add(new MeshPart("airframe", -1, Vec3.Zero, Vec3.UnitZ, fixedTriangles, SurfaceColor));
        foreach (var (index, triangles) in controlTriangles)
            parts.Add(new MeshPart(definition.Controls[index].Name, index, hinges[index].Point, hinges[index].Axis, triangles, ControlColor));

        parts.Add(new MeshPart("fuselage", -1, Vec3.Zero, Vec3.UnitZ, Fuselage(definition), FuselageColor));

        if (definition.Wheels.Count > 0)
        {
            var gear = new List<Vec3>();
            foreach (var w in definition.Wheels) AddBox(gear, w.Position, new Vec3(GearSize, GearSize, GearSize / 2));
            parts.Add(new MeshPart("gear", -1, Vec3.Zero, Vec3.UnitZ, gear, DarkColor));
        }

        if (definition.Power is { } power)
            parts.Add(new MeshPart("propeller", -1, power.Position, power.ThrustAxis, Disc(power.Position, power.ThrustAxis, power.Propeller.DiameterM / 2), DarkColor));

        return parts;
    }

    static List<Vec3> Fuselage(AircraftDefinition definition)
    {
        double front = definition.Hull.Count > 0 ? definition.Hull.Max(h => h.Position.X) : 0.5;
        double back = definition.Hull.Count > 0 ? definition.Hull.Min(h => h.Position.X) : -0.5;
        var triangles = new List<Vec3>();
        AddBox(triangles, new Vec3((front + back) / 2, 0, 0), new Vec3((front - back) / 2, FuselageHeight / 2, FuselageWidth / 2));
        return triangles;
    }

    static List<Vec3> Disc(Vec3 center, Vec3 axis, double radius)
    {
        var u = Vec3.Cross(axis, Math.Abs(axis.Y) < 0.9 ? Vec3.UnitY : Vec3.UnitZ).Normalized();
        var v = Vec3.Cross(axis, u);
        var triangles = new List<Vec3>();
        for (int i = 0; i < PropSides; i++)
        {
            double a0 = 2 * Math.PI * i / PropSides, a1 = 2 * Math.PI * (i + 1) / PropSides;
            triangles.Add(center);
            triangles.Add(center + (u * Math.Cos(a0) + v * Math.Sin(a0)) * radius);
            triangles.Add(center + (u * Math.Cos(a1) + v * Math.Sin(a1)) * radius);
        }
        return triangles;
    }

    static void AddQuad(List<Vec3> t, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        t.Add(a); t.Add(b); t.Add(c);
        t.Add(a); t.Add(c); t.Add(d);
    }

    static void AddBox(List<Vec3> t, Vec3 center, Vec3 half)
    {
        Vec3 P(double sx, double sy, double sz) => center + new Vec3(sx * half.X, sy * half.Y, sz * half.Z);
        AddQuad(t, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        AddQuad(t, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
        AddQuad(t, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));
        AddQuad(t, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        AddQuad(t, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));
        AddQuad(t, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Symlab.App.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(app): build procedural aircraft meshes with hinged control surfaces

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Settings, flight conditions and radio profile store

**Files:**
- Create: `src/Symlab.App/Settings/FlightConditions.cs`, `AppSettings.cs`, `RadioProfileStore.cs`
- Test: `tests/Symlab.App.Tests/Settings/SettingsTests.cs`

**Interfaces:**
- Consumes: `WindSettings`, `RadioProfile` (`ToJson`, `FromJson` throwing `InvalidDataException`), `AxisCalibration`, `ChannelSettings`, `StickFunction`.
- Produces:
  - `FlightConditions(double WindSpeed = 0, double WindFromDeg = 270, double Turbulence = 0, double SunAzimuthDeg = 200, double SunElevationDeg = 40, int Seed = 1)` with `ToWindSettings()`.
  - `enum StickMode { Mode1 = 1, Mode2 = 2 }`.
  - `AppSettings` record (init properties, defaults): `FovDeg = 50`, `AutoZoom = true`, `ShowFlightData = false`, `RecordFlights = true`, `VSync = true`, `Language = "fr"`, `StickMode = Mode2`, `LastAircraft = "trainer"`, `Conditions = new()`; `static Load(string path)` (defaults on missing/invalid file), `Save(string path)`, `Sanitized()` (FOV clamped 10..100, language fr|en).
  - `RadioProfileStore(string directory)` with `PathFor(string guid)`, `Load(string guid, out string? error) : RadioProfile?`, `Save(RadioProfile profile)`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.App.Tests/Settings/SettingsTests.cs`:
```csharp
using Symlab.App.Settings;
using Symlab.Input;

namespace Symlab.App.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("symlab-settings-").FullName;

    [Fact]
    public void Missing_or_corrupt_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.Equal(new AppSettings(), AppSettings.Load(path));
        File.WriteAllText(path, "{ not json");
        Assert.Equal("fr", AppSettings.Load(path).Language);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var path = Path.Combine(_dir, "nested", "settings.json");
        var s = new AppSettings { FovDeg = 35, AutoZoom = false, Language = "en", StickMode = StickMode.Mode1, LastAircraft = "wing",
            Conditions = new FlightConditions(WindSpeed: 5, WindFromDeg: 90, Turbulence: 0.5) };
        s.Save(path);
        var back = AppSettings.Load(path);
        Assert.Equal(s, back);
        Assert.Equal(5, back.Conditions.ToWindSettings().SpeedAt10m);
    }

    [Fact]
    public void Sanitize_clamps_fov_and_unknown_language()
    {
        var s = new AppSettings { FovDeg = 170, Language = "de" }.Sanitized();
        Assert.Equal(100, s.FovDeg);
        Assert.Equal("fr", s.Language);
    }

    [Fact]
    public void Radio_profiles_are_stored_per_guid()
    {
        var store = new RadioProfileStore(_dir);
        var profile = new RadioProfile
        {
            DeviceGuid = "0300/abc:def",
            DeviceName = "TX16S",
            Channels = { [StickFunction.Aileron] = new ChannelSettings(3, false, AxisCalibration.Identity) },
        };
        store.Save(profile);
        Assert.DoesNotContain(':', Path.GetFileName(store.PathFor(profile.DeviceGuid)));
        var back = store.Load(profile.DeviceGuid, out var error);
        Assert.Null(error);
        Assert.Equal(3, back!.Channels[StickFunction.Aileron].AxisIndex);
        Assert.Null(store.Load("unknown", out _));
    }

    [Fact]
    public void Corrupt_profile_reports_an_error_instead_of_throwing()
    {
        var store = new RadioProfileStore(_dir);
        File.WriteAllText(store.PathFor("bad"), "{ \"Channels\": { \"Aileron\": { \"AxisIndex\": -2 } } }");
        Assert.Null(store.Load("bad", out var error));
        Assert.NotNull(error);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.App.Tests --filter FullyQualifiedName~SettingsTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.App/Settings/FlightConditions.cs`:
```csharp
using Symlab.Flight.Atmosphere;

namespace Symlab.App.Settings;

/// <param name="WindSpeed">Mean wind at 10 m, m/s.</param>
/// <param name="WindFromDeg">Direction the wind blows from, clockwise from north.</param>
/// <param name="Turbulence">Dryden intensity scale (0 none, 1 moderate).</param>
/// <param name="SunAzimuthDeg">Sun azimuth, clockwise from north.</param>
/// <param name="SunElevationDeg">Sun elevation above the horizon.</param>
public sealed record FlightConditions(
    double WindSpeed = 0,
    double WindFromDeg = 270,
    double Turbulence = 0,
    double SunAzimuthDeg = 200,
    double SunElevationDeg = 40,
    int Seed = 1)
{
    public WindSettings ToWindSettings() => new(WindSpeed, WindFromDeg, Turbulence);
}
```

`src/Symlab.App/Settings/AppSettings.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Symlab.App.Settings;

public enum StickMode { Mode1 = 1, Mode2 = 2 }

public sealed record AppSettings
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public double FovDeg { get; init; } = 50;
    public bool AutoZoom { get; init; } = true;
    public bool ShowFlightData { get; init; }
    public bool RecordFlights { get; init; } = true;
    public bool VSync { get; init; } = true;
    public string Language { get; init; } = "fr";
    public StickMode StickMode { get; init; } = StickMode.Mode2;
    public string LastAircraft { get; init; } = "trainer";
    public FlightConditions Conditions { get; init; } = new();

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings()).Sanitized();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public AppSettings Sanitized() => this with
    {
        FovDeg = Math.Clamp(FovDeg, 10, 100),
        Language = Language is "fr" or "en" ? Language : "fr",
        Conditions = Conditions ?? new FlightConditions(),
    };
}
```

`src/Symlab.App/Settings/RadioProfileStore.cs`:
```csharp
using System.Text.Json;
using Symlab.Input;

namespace Symlab.App.Settings;

/// <summary>One JSON profile per radio, named after its device GUID.</summary>
public sealed class RadioProfileStore
{
    readonly string _directory;

    public RadioProfileStore(string directory) => _directory = directory;

    public string PathFor(string guid)
    {
        var safe = new string(guid.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
        return Path.Combine(_directory, (safe.Length == 0 ? "unknown" : safe) + ".json");
    }

    public RadioProfile? Load(string guid, out string? error)
    {
        error = null;
        var path = PathFor(guid);
        if (!File.Exists(path)) return null;
        try
        {
            return RadioProfile.FromJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            error = $"{path}: {ex.Message}";
            return null;
        }
    }

    public void Save(RadioProfile profile)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(profile.DeviceGuid), profile.ToJson());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Symlab.App.Tests`
Expected: all PASS. (If `RadioProfile.FromJson` accepts the corrupt fixture, check how it validates — the final-review fix added validation of `AxisIndex ≥ 0`; do not weaken the test.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(app): add settings, flight conditions and radio profile store

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Translations, calibration prompts and flight-data formatting

**Files:**
- Create: `src/Symlab.App/Localization/TranslationTable.cs`, `src/Symlab.App/Ui/CalibrationPrompts.cs`, `src/Symlab.App/Ui/FlightDataFormatter.cs`
- Create: `game/translations/strings.csv`, `game/translations/.gdignore` (empty file — keeps Godot's CSV importer away; the game loads the CSV itself)
- Test: `tests/Symlab.App.Tests/Localization/TranslationTests.cs`, `tests/Symlab.App.Tests/Ui/UiTextTests.cs`

**Interfaces:**
- Consumes: `CalibrationWizard.Stage`, `StickFunction`, `StickMode`, `Aircraft` (`AirData`, `Power.Telemetry.BatteryVoltage`), `CrashCause`.
- Produces:
  - `TranslationTable.Parse(string csv) : TranslationTable` with `Languages`, `Keys`, `Get(string language, string key) : string` (returns the key when missing), `MissingEntries() : IReadOnlyList<string>` ("lang:key" for empty cells). CSV header `keys,<lang>,<lang>...`; fields may be double-quoted (commas inside, `""` escape); the two-character sequence `\n` becomes a newline.
  - `CalibrationPrompts.StageKey(CalibrationWizard.Stage stage, StickFunction? function) : string`, `CalibrationPrompts.StickSideKey(StickFunction function, StickMode mode) : string`.
  - `FlightDataLine(string Key, string Value)`; `FlightDataFormatter.Format(Aircraft aircraft, double heightAgl, double flightTimeSeconds, double throttle) : IReadOnlyList<FlightDataLine>` (keys `HUD_AIRSPEED` km/h, `HUD_ALTITUDE` m, `HUD_THROTTLE` %, `HUD_BATTERY` V or "—", `HUD_TIMER` mm:ss).
  - `FlightDataFormatter.CrashKey(CrashCause cause) : string` = `"CRASH_" + cause.ToString().ToUpperInvariant()`.
  - `game/translations/strings.csv` with every key listed below in `fr` and `en`.

- [ ] **Step 1: Write the translations file**

`game/translations/.gdignore`: empty file.

`game/translations/strings.csv`:
```csv
keys,fr,en
APP_TITLE,Symlab — simulateur de vol RC,Symlab — RC flight simulator
MENU_FLY,Voler,Fly
MENU_RADIO,Radio,Radio
MENU_SETTINGS,Réglages,Settings
MENU_QUIT,Quitter,Quit
MENU_AIRCRAFT,Avion,Aircraft
MENU_CONDITIONS,Conditions sur le terrain,Field conditions
COND_WIND_SPEED,Vent (m/s),Wind (m/s)
COND_WIND_DIR,Vent venant de (°),Wind from (°)
COND_TURBULENCE,Turbulence,Turbulence
COND_SUN_AZIMUTH,Soleil : azimut (°),Sun azimuth (°)
COND_SUN_ELEVATION,Soleil : hauteur (°),Sun elevation (°)
BACK,Retour,Back
RADIO_TITLE,Radio,Radio
RADIO_NO_DEVICE,"Aucune radio détectée. Branchez la radio en USB et choisissez « USB Joystick (HID) » sur la radio.","No radio detected. Plug the radio in over USB and choose ""USB Joystick (HID)"" on the radio."
RADIO_DEVICE_READY,Calibrée — prête à voler,Calibrated — ready to fly
RADIO_DEVICE_UNCALIBRATED,Non calibrée,Not calibrated
RADIO_CALIBRATE,Calibrer,Calibrate
RADIO_NEXT,Suivant,Next
RADIO_CANCEL,Annuler,Cancel
RADIO_SAVED,Calibration enregistrée.,Calibration saved.
RADIO_MODE,Mode des manches,Stick mode
RADIO_BIND_RESET,Interrupteur « reset »,Reset switch
RADIO_BIND_PAUSE,Interrupteur « pause »,Pause switch
RADIO_BIND_WIND,Interrupteur « vent »,Wind switch
RADIO_BIND_WAIT,Basculez l'interrupteur maintenant…,Flip the switch now…
RADIO_BIND_DONE,Interrupteur assigné.,Switch assigned.
RADIO_AXES,Voies brutes,Raw axes
RADIO_HELP,"EdgeTX : Réglages radio → USB → mode Joystick. Créez un modèle « Simulateur » sans mixage (voies 1 à 4 = manches). Si la radio n'apparaît pas, débranchez-la et rebranchez-la puis choisissez « USB Joystick (HID) ».","EdgeTX: Radio settings → USB → Joystick mode. Create a ""Simulator"" model with no mixes (channels 1–4 = sticks). If the radio does not show up, unplug and replug it, then choose ""USB Joystick (HID)""."
CAL_CENTER,"Lâchez les manches (gaz où vous voulez), puis Suivant.","Release the sticks (throttle anywhere), then Next."
CAL_EXTREMES,"Faites de grands cercles avec les deux manches jusqu'aux butées, puis Suivant.","Move both sticks in full circles to their limits, then Next."
CAL_ID_THROTTLE,"Mettez les gaz à fond ({0}), puis Suivant.","Push the throttle to full ({0}), then Next."
CAL_ID_AILERON,"Ailerons à droite à fond ({0}), puis Suivant.","Full right aileron ({0}), then Next."
CAL_ID_ELEVATOR,"Tirez la profondeur à fond, vers vous ({0}), puis Suivant.","Pull full up elevator, toward you ({0}), then Next."
CAL_ID_RUDDER,"Dérive à droite à fond ({0}), puis Suivant.","Full right rudder ({0}), then Next."
CAL_DONE,Calibration terminée.,Calibration complete.
CAL_FAILED,"Aucun manche n'a assez bougé. Recommencez ce geste plus franchement.","No stick moved enough. Repeat the move more firmly."
STICK_LEFT,manche gauche,left stick
STICK_RIGHT,manche droit,right stick
SET_TITLE,Réglages,Settings
SET_FOV,Champ de vision vertical (°),Vertical field of view (°)
SET_SCREEN_HEIGHT,Hauteur de l'écran (cm),Screen height (cm)
SET_VIEW_DISTANCE,Distance aux yeux (cm),Viewing distance (cm)
SET_FOV_FROM_SCREEN,Calculer depuis l'écran,Compute from screen
SET_AUTOZOOM,Zoom automatique limité,Limited auto-zoom
SET_FLIGHT_DATA,Afficher les données de vol,Show flight data
SET_RECORD,Enregistrer les vols (CSV),Record flights (CSV)
SET_LANGUAGE,Langue,Language
SET_VSYNC,Synchronisation verticale,Vertical sync
HUD_AIRSPEED,Vitesse,Airspeed
HUD_ALTITUDE,Hauteur,Height
HUD_THROTTLE,Gaz,Throttle
HUD_BATTERY,Batterie,Battery
HUD_TIMER,Chrono,Timer
HUD_PAUSED,PAUSE,PAUSED
HUD_KEYBOARD,"Clavier — Z/S gaz, flèches ailerons/profondeur, Q/D dérive, R reset, P pause, V vent","Keyboard — W/S throttle, arrows aileron/elevator, A/D rudder, R reset, P pause, V wind"
HUD_LATENCY,Latence estimée,Estimated latency
HUD_FPS,Images/s,FPS
HUD_INPUT,Commande,Input
WIND_ON,Vent activé,Wind on
WIND_OFF,Vent coupé,Wind off
CRASH_TITLE,Crash !,Crash!
CRASH_HINT,Appuyez sur R (ou l'interrupteur reset) pour recommencer.,Press R (or the reset switch) to try again.
CRASH_NONE,—,—
CRASH_HARDLANDING,Atterrissage trop dur,Landing too hard
CRASH_TREESTRIKE,Dans les arbres,Into the trees
CRASH_WINGTIPSTRIKE,Saumon d'aile au sol,Wingtip strike
CRASH_NOSEOVER,Passage sur le nez,Nose-over
CRASH_HULLIMPACT,Choc de la cellule,Airframe impact
```

- [ ] **Step 2: Write the failing tests**

`tests/Symlab.App.Tests/Localization/TranslationTests.cs`:
```csharp
using Symlab.App.Localization;
using Symlab.App.Ui;
using Symlab.Flight.Ground;

namespace Symlab.App.Tests.Localization;

public class TranslationTests
{
    static TranslationTable Shipped() =>
        TranslationTable.Parse(File.ReadAllText(Path.Combine(TestData.RepoRoot, "game", "translations", "strings.csv")));

    [Fact]
    public void Parses_quoted_fields_escaped_quotes_and_newlines()
    {
        var t = TranslationTable.Parse("keys,fr,en\nA,\"un, deux\",\"say \"\"hi\"\"\"\nB,ligne\\nsuivante,next\n");
        Assert.Equal(new[] { "fr", "en" }, t.Languages);
        Assert.Equal("un, deux", t.Get("fr", "A"));
        Assert.Equal("say \"hi\"", t.Get("en", "A"));
        Assert.Equal("ligne\nsuivante", t.Get("fr", "B"));
        Assert.Equal("MISSING", t.Get("fr", "MISSING"));
    }

    [Fact]
    public void Shipped_file_has_french_first_and_no_missing_entries()
    {
        var t = Shipped();
        Assert.Equal("fr", t.Languages[0]);
        Assert.Empty(t.MissingEntries());
    }

    [Fact]
    public void Every_key_used_by_the_app_exists()
    {
        var t = Shipped();
        var keys = t.Keys.ToHashSet();
        foreach (CrashCause cause in Enum.GetValues<CrashCause>()) Assert.Contains(FlightDataFormatter.CrashKey(cause), keys);
        foreach (var k in new[] { "HUD_AIRSPEED", "HUD_ALTITUDE", "HUD_THROTTLE", "HUD_BATTERY", "HUD_TIMER",
                     "CAL_CENTER", "CAL_EXTREMES", "CAL_ID_THROTTLE", "CAL_ID_AILERON", "CAL_ID_ELEVATOR", "CAL_ID_RUDDER",
                     "CAL_DONE", "STICK_LEFT", "STICK_RIGHT" })
            Assert.Contains(k, keys);
    }
}
```

`tests/Symlab.App.Tests/Ui/UiTextTests.cs`:
```csharp
using Symlab.App.Settings;
using Symlab.App.Ui;
using Symlab.Flight.Airframe;
using Symlab.Input;

namespace Symlab.App.Tests.Ui;

public class UiTextTests
{
    [Theory]
    [InlineData(StickFunction.Throttle, StickMode.Mode2, "STICK_LEFT")]
    [InlineData(StickFunction.Throttle, StickMode.Mode1, "STICK_RIGHT")]
    [InlineData(StickFunction.Elevator, StickMode.Mode2, "STICK_RIGHT")]
    [InlineData(StickFunction.Elevator, StickMode.Mode1, "STICK_LEFT")]
    [InlineData(StickFunction.Aileron, StickMode.Mode1, "STICK_RIGHT")]
    [InlineData(StickFunction.Rudder, StickMode.Mode2, "STICK_LEFT")]
    public void Stick_side_depends_on_the_mode(StickFunction f, StickMode mode, string expected)
        => Assert.Equal(expected, CalibrationPrompts.StickSideKey(f, mode));

    [Fact]
    public void Stage_keys_cover_every_stage()
    {
        Assert.Equal("CAL_CENTER", CalibrationPrompts.StageKey(CalibrationWizard.Stage.Center, null));
        Assert.Equal("CAL_ID_RUDDER", CalibrationPrompts.StageKey(CalibrationWizard.Stage.Identify, StickFunction.Rudder));
        Assert.Equal("CAL_DONE", CalibrationPrompts.StageKey(CalibrationWizard.Stage.Done, null));
    }

    [Fact]
    public void Flight_data_is_formatted_in_pilot_units()
    {
        var aircraft = new Aircraft(TestData.Aircraft("trainer"));
        var lines = FlightDataFormatter.Format(aircraft, heightAgl: 23.4, flightTimeSeconds: 201, throttle: 0.654);
        Assert.Equal(new[] { "HUD_AIRSPEED", "HUD_ALTITUDE", "HUD_THROTTLE", "HUD_BATTERY", "HUD_TIMER" }, lines.Select(l => l.Key));
        Assert.Equal("0 km/h", lines[0].Value);
        Assert.Equal("23 m", lines[1].Value);
        Assert.Equal("65 %", lines[2].Value);
        Assert.EndsWith(" V", lines[3].Value);
        Assert.Equal("03:21", lines[4].Value);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.App.Tests`
Expected: build FAILS.

- [ ] **Step 4: Implement**

`src/Symlab.App/Localization/TranslationTable.cs`:
```csharp
using System.Text;

namespace Symlab.App.Localization;

/// <summary>Translation CSV: header <c>keys,&lt;lang&gt;,...</c>, one key per line; RFC 4180 quoting; <c>\n</c> means newline.</summary>
public sealed class TranslationTable
{
    readonly Dictionary<string, Dictionary<string, string>> _byLanguage;
    readonly List<string> _keys;

    TranslationTable(List<string> languages, List<string> keys, Dictionary<string, Dictionary<string, string>> byLanguage)
    {
        Languages = languages;
        _keys = keys;
        _byLanguage = byLanguage;
    }

    public IReadOnlyList<string> Languages { get; }
    public IReadOnlyList<string> Keys => _keys;

    public string Get(string language, string key) =>
        _byLanguage.TryGetValue(language, out var map) && map.TryGetValue(key, out var text) && text.Length > 0 ? text : key;

    public IReadOnlyList<string> MissingEntries() =>
        Languages.SelectMany(lang => _keys.Where(k => _byLanguage[lang].GetValueOrDefault(k, "").Length == 0).Select(k => $"{lang}:{k}")).ToList();

    public static TranslationTable Parse(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) throw new InvalidDataException("Translation file is empty.");
        var header = SplitLine(lines[0]);
        if (header.Count < 2 || header[0] != "keys") throw new InvalidDataException("Translation header must start with 'keys'.");
        var languages = header.Skip(1).ToList();
        var byLanguage = languages.ToDictionary(l => l, _ => new Dictionary<string, string>());
        var keys = new List<string>();
        foreach (var line in lines.Skip(1))
        {
            var fields = SplitLine(line);
            var key = fields[0];
            keys.Add(key);
            for (int i = 0; i < languages.Count; i++)
                byLanguage[languages[i]][key] = i + 1 < fields.Count ? fields[i + 1].Replace("\\n", "\n") : "";
        }
        return new TranslationTable(languages, keys, byLanguage);
    }

    static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
```

`src/Symlab.App/Ui/CalibrationPrompts.cs`:
```csharp
using Symlab.App.Settings;
using Symlab.Input;

namespace Symlab.App.Ui;

public static class CalibrationPrompts
{
    public static string StageKey(CalibrationWizard.Stage stage, StickFunction? function) => stage switch
    {
        CalibrationWizard.Stage.Center => "CAL_CENTER",
        CalibrationWizard.Stage.Extremes => "CAL_EXTREMES",
        CalibrationWizard.Stage.Identify => function switch
        {
            StickFunction.Throttle => "CAL_ID_THROTTLE",
            StickFunction.Aileron => "CAL_ID_AILERON",
            StickFunction.Elevator => "CAL_ID_ELEVATOR",
            _ => "CAL_ID_RUDDER",
        },
        _ => "CAL_DONE",
    };

    /// <summary>Mode 2: throttle/rudder on the left stick. Mode 1: throttle on the right, elevator on the left.</summary>
    public static string StickSideKey(StickFunction function, StickMode mode) => function switch
    {
        StickFunction.Aileron => "STICK_RIGHT",
        StickFunction.Rudder => "STICK_LEFT",
        StickFunction.Throttle => mode == StickMode.Mode2 ? "STICK_LEFT" : "STICK_RIGHT",
        _ => mode == StickMode.Mode2 ? "STICK_RIGHT" : "STICK_LEFT",
    };
}
```

`src/Symlab.App/Ui/FlightDataFormatter.cs`:
```csharp
using System.Globalization;
using Symlab.Flight.Airframe;
using Symlab.Flight.Ground;

namespace Symlab.App.Ui;

public readonly record struct FlightDataLine(string Key, string Value);

public static class FlightDataFormatter
{
    public static IReadOnlyList<FlightDataLine> Format(Aircraft aircraft, double heightAgl, double flightTimeSeconds, double throttle)
    {
        var inv = CultureInfo.InvariantCulture;
        var seconds = (int)Math.Max(0, flightTimeSeconds);
        string battery = aircraft.Power is null ? "—" : aircraft.Power.Telemetry.BatteryVoltage.ToString("0.0", inv) + " V";
        if (aircraft.Power is not null && aircraft.Power.Telemetry.BatteryVoltage <= 0)
            battery = aircraft.Power.Spec.Battery.OpenCircuitVoltage(aircraft.Power.StateOfCharge).ToString("0.0", inv) + " V";
        return
        [
            new("HUD_AIRSPEED", (aircraft.AirData.Airspeed * 3.6).ToString("0", inv) + " km/h"),
            new("HUD_ALTITUDE", Math.Max(0, heightAgl).ToString("0", inv) + " m"),
            new("HUD_THROTTLE", (Math.Clamp(throttle, 0, 1) * 100).ToString("0", inv) + " %"),
            new("HUD_BATTERY", battery),
            new("HUD_TIMER", $"{seconds / 60:00}:{seconds % 60:00}"),
        ];
    }

    public static string CrashKey(CrashCause cause) => "CRASH_" + cause.ToString().ToUpperInvariant();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Symlab.App.Tests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(app): add translation table, calibration prompts and flight-data formatting

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Input routing, switch capture and latency estimate

**Files:**
- Create: `src/Symlab.App/Session/InputRouter.cs`, `SwitchCapture.cs`, `LatencyMeter.cs`
- Test: `tests/Symlab.App.Tests/Session/InputTests.cs`

**Interfaces:**
- Consumes: `RadioProfile` (`Read`, `Switches`), `SwitchTracker`, `SwitchBinding`, `SwitchAction`, `KeyboardStick`, `KeyboardKeys`, `RawInputFrame`, `StickState`, `ControlInputs`, `Simulation.FixedStep`.
- Produces:
  - `enum InputSource { Keyboard, Radio }`.
  - `JoypadSnapshot(string Guid, string Name, RawInputFrame Frame)`.
  - `KeyboardCommands(bool Reset, bool Pause, bool ToggleWind)`.
  - `RouterOutput(ControlInputs Controls, IReadOnlyList<SwitchAction> Actions, InputSource Source, string DeviceName)`.
  - `InputRouter(Func<string, RadioProfile?> loadProfile)` with `Update(double dt, IReadOnlyList<JoypadSnapshot> pads, KeyboardKeys keys, KeyboardCommands commands) : RouterOutput` and `InvalidateProfiles()`. The first pad with a stored profile drives the aircraft; otherwise the keyboard does. Profiles are cached per GUID until `InvalidateProfiles()`. Keyboard commands fire on rising edges; radio switch actions come from a `SwitchTracker` built from the active profile.
  - `SwitchCapture` with `Update(SwitchAction action, RawInputFrame frame) : SwitchBinding?` (first frame is the baseline; a newly pressed button, or an axis rising by more than 1.0, becomes the binding with threshold at the midpoint; an axis moving down only updates the baseline) and `Cancel()`.
  - `LatencyMeter` with `Add(double inputToPhysicsSeconds, double frameSeconds, double interpolationAlpha)` and `AverageMs` (rolling 60 samples of `inputToPhysics + frame + (1 − alpha)·FixedStep`).

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.App.Tests/Session/InputTests.cs`:
```csharp
using Symlab.App.Session;
using Symlab.Flight.Sim;
using Symlab.Input;

namespace Symlab.App.Tests.Session;

public class InputTests
{
    static RadioProfile Profile(params SwitchBinding[] switches)
    {
        var p = new RadioProfile { DeviceGuid = "radio-1", DeviceName = "TX16S" };
        p.Channels[StickFunction.Throttle] = new ChannelSettings(2, false, AxisCalibration.Identity);
        p.Channels[StickFunction.Aileron] = new ChannelSettings(0, false, AxisCalibration.Identity);
        p.Channels[StickFunction.Elevator] = new ChannelSettings(1, false, AxisCalibration.Identity);
        p.Channels[StickFunction.Rudder] = new ChannelSettings(3, false, AxisCalibration.Identity);
        p.Switches.AddRange(switches);
        return p;
    }

    static JoypadSnapshot Pad(string guid, double[] axes, bool[]? buttons = null) =>
        new(guid, "TX16S", new RawInputFrame(axes, buttons ?? new bool[4]));

    [Fact]
    public void Calibrated_radio_drives_the_aircraft()
    {
        var router = new InputRouter(guid => guid == "radio-1" ? Profile() : null);
        var output = router.Update(0.016, [Pad("radio-1", [0.5, -0.25, 1.0, 0])], default, default);
        Assert.Equal(InputSource.Radio, output.Source);
        Assert.Equal("TX16S", output.DeviceName);
        Assert.Equal(0.5, output.Controls.Aileron, 9);
        Assert.Equal(-0.25, output.Controls.Elevator, 9);
        Assert.Equal(1.0, output.Controls.Throttle, 9);
    }

    [Fact]
    public void Uncalibrated_pad_falls_back_to_the_keyboard()
    {
        var router = new InputRouter(_ => null);
        var output = router.Update(0.5, [Pad("other", [1, 1, 1, 1])], default(KeyboardKeys) with { RollRight = true }, default);
        Assert.Equal(InputSource.Keyboard, output.Source);
        Assert.True(output.Controls.Aileron > 0);
    }

    [Fact]
    public void Profiles_are_loaded_once_until_invalidated()
    {
        int loads = 0;
        var router = new InputRouter(_ => { loads++; return Profile(); });
        for (int i = 0; i < 5; i++) router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0])], default, default);
        Assert.Equal(1, loads);
        router.InvalidateProfiles();
        router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0])], default, default);
        Assert.Equal(2, loads);
    }

    [Fact]
    public void Radio_switches_and_keyboard_commands_fire_once_per_press()
    {
        var router = new InputRouter(_ => Profile(new SwitchBinding(SwitchAction.Reset, ButtonIndex: 1)));
        bool[] off = [false, false, false, false], on = [false, true, false, false];
        router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0], off)], default, default);
        Assert.Equal(SwitchAction.Reset, Assert.Single(router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0], on)], default, default).Actions));
        Assert.Empty(router.Update(0.016, [Pad("radio-1", [0, 0, 0, 0], on)], default, default).Actions);

        var pause = new KeyboardCommands(false, true, false);
        Assert.Equal(SwitchAction.Pause, Assert.Single(router.Update(0.016, [], default, pause).Actions));
        Assert.Empty(router.Update(0.016, [], default, pause).Actions);
    }

    [Fact]
    public void Switch_capture_binds_a_new_button_or_a_rising_axis()
    {
        var capture = new SwitchCapture();
        Assert.Null(capture.Update(SwitchAction.Reset, new RawInputFrame([0, -1], [false, false])));
        Assert.Null(capture.Update(SwitchAction.Reset, new RawInputFrame([0.2, -1], [false, false])));
        var byButton = capture.Update(SwitchAction.Reset, new RawInputFrame([0.2, -1], [false, true]));
        Assert.Equal(new SwitchBinding(SwitchAction.Reset, ButtonIndex: 1), byButton);

        var axisCapture = new SwitchCapture();
        axisCapture.Update(SwitchAction.ToggleWind, new RawInputFrame([0, -1], [false]));
        var byAxis = axisCapture.Update(SwitchAction.ToggleWind, new RawInputFrame([0, 1], [false]));
        Assert.Equal(new SwitchBinding(SwitchAction.ToggleWind, AxisIndex: 1, Threshold: 0), byAxis);
    }

    [Fact]
    public void Switch_capture_ignores_an_axis_moving_down()
    {
        var capture = new SwitchCapture();
        capture.Update(SwitchAction.Pause, new RawInputFrame([1], []));
        Assert.Null(capture.Update(SwitchAction.Pause, new RawInputFrame([-1], [])));
        Assert.Equal(new SwitchBinding(SwitchAction.Pause, AxisIndex: 0, Threshold: 0), capture.Update(SwitchAction.Pause, new RawInputFrame([1], [])));
    }

    [Fact]
    public void Latency_estimate_adds_processing_frame_and_interpolation_delay()
    {
        var meter = new LatencyMeter();
        meter.Add(0.001, 0.008, 0.5);
        Assert.Equal(1 + 8 + 0.5 * Simulation.FixedStep * 1000, meter.AverageMs, 9);
        Assert.Equal(0, new LatencyMeter().AverageMs);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.App.Tests --filter FullyQualifiedName~InputTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.App/Session/InputRouter.cs`:
```csharp
using Symlab.Flight.Controls;
using Symlab.Input;

namespace Symlab.App.Session;

public enum InputSource { Keyboard, Radio }

public readonly record struct JoypadSnapshot(string Guid, string Name, RawInputFrame Frame);

public readonly record struct KeyboardCommands(bool Reset, bool Pause, bool ToggleWind);

public readonly record struct RouterOutput(ControlInputs Controls, IReadOnlyList<SwitchAction> Actions, InputSource Source, string DeviceName);

/// <summary>Chooses the calibrated radio when one is connected, else the keyboard, and turns switches and keys into actions.</summary>
public sealed class InputRouter
{
    readonly Func<string, RadioProfile?> _loadProfile;
    readonly Dictionary<string, RadioProfile?> _profiles = new();
    readonly KeyboardStick _keyboard = new();
    SwitchTracker? _switches;
    string? _activeGuid;
    KeyboardCommands _previousCommands;

    public InputRouter(Func<string, RadioProfile?> loadProfile) => _loadProfile = loadProfile;

    public void InvalidateProfiles()
    {
        _profiles.Clear();
        _activeGuid = null;
        _switches = null;
    }

    public RouterOutput Update(double dt, IReadOnlyList<JoypadSnapshot> pads, KeyboardKeys keys, KeyboardCommands commands)
    {
        var actions = new List<SwitchAction>();
        if (commands.Reset && !_previousCommands.Reset) actions.Add(SwitchAction.Reset);
        if (commands.Pause && !_previousCommands.Pause) actions.Add(SwitchAction.Pause);
        if (commands.ToggleWind && !_previousCommands.ToggleWind) actions.Add(SwitchAction.ToggleWind);
        _previousCommands = commands;

        var keyboardSticks = _keyboard.Update(dt, keys);

        foreach (var pad in pads)
        {
            if (!_profiles.TryGetValue(pad.Guid, out var profile)) _profiles[pad.Guid] = profile = _loadProfile(pad.Guid);
            if (profile is null) continue;
            if (_activeGuid != pad.Guid)
            {
                _activeGuid = pad.Guid;
                _switches = new SwitchTracker(profile.Switches);
            }
            actions.AddRange(_switches!.Update(pad.Frame));
            return new RouterOutput(ToControls(profile.Read(pad.Frame)), actions, InputSource.Radio, pad.Name);
        }

        _activeGuid = null;
        _switches = null;
        return new RouterOutput(ToControls(keyboardSticks), actions, InputSource.Keyboard, "");
    }

    static ControlInputs ToControls(StickState s) => new(s.Throttle, s.Aileron, s.Elevator, s.Rudder);
}
```

`src/Symlab.App/Session/SwitchCapture.cs`:
```csharp
using Symlab.Input;

namespace Symlab.App.Session;

/// <summary>"Flip the switch now": binds the first button pressed, or the first axis moved up by more than half its travel.</summary>
public sealed class SwitchCapture
{
    const double AxisJump = 1.0;
    RawInputFrame? _baseline;

    public SwitchBinding? Update(SwitchAction action, RawInputFrame frame)
    {
        if (_baseline is not { } baseline)
        {
            _baseline = Copy(frame);
            return null;
        }

        for (int i = 0; i < Math.Min(frame.Buttons.Length, baseline.Buttons.Length); i++)
            if (frame.Buttons[i] && !baseline.Buttons[i]) return Done(new SwitchBinding(action, ButtonIndex: i));

        for (int i = 0; i < Math.Min(frame.Axes.Length, baseline.Axes.Length); i++)
        {
            double delta = frame.Axes[i] - baseline.Axes[i];
            if (delta > AxisJump) return Done(new SwitchBinding(action, AxisIndex: i, Threshold: (frame.Axes[i] + baseline.Axes[i]) / 2));
            if (delta < -AxisJump) baseline.Axes[i] = frame.Axes[i];
        }
        return null;
    }

    public void Cancel() => _baseline = null;

    SwitchBinding Done(SwitchBinding binding)
    {
        _baseline = null;
        return binding;
    }

    static RawInputFrame Copy(RawInputFrame f) => new((double[])f.Axes.Clone(), (bool[])f.Buttons.Clone());
}
```

`src/Symlab.App/Session/LatencyMeter.cs`:
```csharp
using Symlab.Flight.Sim;

namespace Symlab.App.Session;

/// <summary>
/// Estimated stick-to-screen latency inside the app: input processing, one frame until presentation, and the
/// interpolation delay of the rendered physics state. USB polling and display lag are not included.
/// </summary>
public sealed class LatencyMeter
{
    readonly double[] _samples = new double[60];
    int _count;
    int _next;

    public void Add(double inputToPhysicsSeconds, double frameSeconds, double interpolationAlpha)
    {
        _samples[_next] = inputToPhysicsSeconds + frameSeconds + (1 - interpolationAlpha) * Simulation.FixedStep;
        _next = (_next + 1) % _samples.Length;
        _count = Math.Min(_count + 1, _samples.Length);
    }

    public double AverageMs => _count == 0 ? 0 : _samples.Take(_count).Average() * 1000;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Symlab.App.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(app): add input routing, switch capture and latency estimate

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Aircraft catalog and flight session

**Files:**
- Create: `src/Symlab.App/Session/AircraftCatalog.cs`, `src/Symlab.App/Session/FlightSession.cs`
- Test: `tests/Symlab.App.Tests/Session/FlightSessionTests.cs`

**Interfaces:**
- Consumes: `AircraftLoader`, `AircraftDefinition`, `Aircraft`, `Simulation`, `FlightEnvironment`, `WindField`, `WindSettings`, `InitialConditions`, `FlightRecorder`, `ClubField`, `ClubFieldTerrain`, `TreePlanter`, `FlightConditions`, `StateInterpolation`, `SwitchAction`, `CrashCause`.
- Produces:
  - `AircraftEntry(string Id, string Name, string Description)`; `AircraftCatalog.List(string aircraftRoot, out IReadOnlyList<string> errors) : IReadOnlyList<AircraftEntry>` (every sub-folder with an `aircraft.json` that loads, sorted by id; failures go to `errors`, the `airfoils` folder is skipped).
  - `FlightSession(AircraftDefinition definition, FlightConditions conditions)` implementing `IDisposable`, with `TreeSeed = 7`, `Definition`, `Conditions`, `Terrain`, `Aircraft`, `Simulation` (replaced when wind is toggled), `Paused`, `WindEnabled`, `FlightTime`, `Recorder`, `StartState() : RigidBodyState`, `Reset()`, `Handle(SwitchAction)`, `Tick(double frameDt, in ControlInputs input) : int`, `DisplayState : RigidBodyState`, `HeightAgl : double`, `AttachRecorder(FlightRecorder recorder)`, `Span : double` (largest wing span, for the camera).

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.App.Tests/Session/FlightSessionTests.cs`:
```csharp
using Symlab.App.Field;
using Symlab.App.Session;
using Symlab.App.Settings;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Input;

namespace Symlab.App.Tests.Session;

public class FlightSessionTests
{
    static FlightSession Session(string id, FlightConditions? conditions = null) =>
        new(TestData.Aircraft(id), conditions ?? new FlightConditions(WindSpeed: 4, WindFromDeg: 80));

    [Fact]
    public void Catalog_lists_the_shipped_aircraft()
    {
        var list = AircraftCatalog.List(Path.Combine(TestData.RepoRoot, "aircraft"), out var errors);
        Assert.Empty(errors);
        Assert.Equal(new[] { "sport", "trainer", "wing" }, list.Select(a => a.Id));
        Assert.All(list, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public void Aircraft_with_wheels_start_on_the_runway_facing_into_the_wind()
    {
        using var session = Session("trainer");
        var s = session.Aircraft.State;
        Assert.True(ClubField.OnRunway(s.Position.X, s.Position.Z));
        Assert.True(s.Position.X < 0);
        Assert.Equal(90, Angle.Deg(Attitude.FromOrientation(s.Orientation).Heading), 6);
        Assert.Equal(0, s.Velocity.Length);
    }

    [Fact]
    public void Flying_wing_starts_as_a_hand_launch()
    {
        using var session = Session("wing");
        var s = session.Aircraft.State;
        Assert.InRange(s.Position.Y, 1.5, 2.5);
        Assert.Equal(10, s.Velocity.Length, 6);
    }

    [Fact]
    public void Tick_advances_time_and_pause_stops_it()
    {
        using var session = Session("trainer");
        Assert.Equal(5, session.Tick(0.01, ControlInputs.Neutral));
        Assert.Equal(0.01, session.FlightTime, 9);
        session.Handle(SwitchAction.Pause);
        Assert.True(session.Paused);
        Assert.Equal(0, session.Tick(0.01, ControlInputs.Neutral));
        session.Handle(SwitchAction.Pause);
        Assert.Equal(5, session.Tick(0.01, ControlInputs.Neutral));
    }

    [Fact]
    public void Reset_returns_to_the_start()
    {
        using var session = Session("trainer");
        for (int i = 0; i < 200; i++) session.Tick(0.01, new ControlInputs(1, 0, 0, 0));
        Assert.True(session.Aircraft.State.Velocity.Length > 1);
        session.Handle(SwitchAction.Reset);
        Assert.Equal(session.StartState(), session.Aircraft.State);
        Assert.Equal(0, session.FlightTime);
    }

    [Fact]
    public void Wind_toggle_swaps_between_calm_and_the_chosen_wind_keeping_the_aircraft_state()
    {
        using var session = Session("trainer");
        session.Tick(0.1, ControlInputs.Neutral);
        var before = session.Aircraft.State;
        Assert.Equal(4, session.Simulation.Environment.Wind.Settings.SpeedAt10m);
        session.Handle(SwitchAction.ToggleWind);
        Assert.False(session.WindEnabled);
        Assert.Equal(0, session.Simulation.Environment.Wind.Settings.SpeedAt10m);
        Assert.Equal(before, session.Aircraft.State);
        session.Handle(SwitchAction.ToggleWind);
        Assert.True(session.WindEnabled);
    }

    [Fact]
    public void Flight_timer_stops_after_a_crash()
    {
        using var session = Session("trainer");
        session.Aircraft.OverrideState(session.Aircraft.State with { Position = new Vec3(0, 3, 0), Velocity = new Vec3(0, -15, 0) });
        for (int i = 0; i < 100 && session.Aircraft.Crash == CrashCause.None; i++) session.Tick(0.01, ControlInputs.Neutral);
        Assert.NotEqual(CrashCause.None, session.Aircraft.Crash);
        double t = session.FlightTime;
        session.Tick(0.1, ControlInputs.Neutral);
        Assert.Equal(t, session.FlightTime);
    }

    [Fact]
    public void Display_state_lies_between_the_last_two_physics_states()
    {
        using var session = Session("trainer");
        for (int i = 0; i < 300; i++) session.Tick(0.01, new ControlInputs(1, 0, 0, 0));
        session.Tick(0.003, new ControlInputs(1, 0, 0, 0));
        double x0 = session.Simulation.Previous.Position.X, x1 = session.Aircraft.State.Position.X, xd = session.DisplayState.Position.X;
        Assert.InRange(xd, Math.Min(x0, x1), Math.Max(x0, x1));
    }

    [Fact]
    public void Session_uses_the_club_field_with_trees()
    {
        using var session = Session("sport");
        Assert.True(session.Terrain.Trees.Count > 100);
        Assert.InRange(session.Span, 1.1, 1.3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.App.Tests --filter FullyQualifiedName~FlightSessionTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.App/Session/AircraftCatalog.cs`:
```csharp
using Symlab.Flight.Airframe;

namespace Symlab.App.Session;

public readonly record struct AircraftEntry(string Id, string Name, string Description);

public static class AircraftCatalog
{
    public static IReadOnlyList<AircraftEntry> List(string aircraftRoot, out IReadOnlyList<string> errors)
    {
        var found = new List<AircraftEntry>();
        var problems = new List<string>();
        if (Directory.Exists(aircraftRoot))
        {
            foreach (var folder in Directory.GetDirectories(aircraftRoot).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (!File.Exists(Path.Combine(folder, "aircraft.json"))) continue;
                try
                {
                    var def = AircraftLoader.Load(folder);
                    found.Add(new AircraftEntry(Path.GetFileName(folder), def.Name, def.Description));
                }
                catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or ArgumentException)
                {
                    problems.Add(ex.Message);
                }
            }
        }
        errors = problems;
        return found;
    }
}
```

`src/Symlab.App/Session/FlightSession.cs`:
```csharp
using Symlab.App.Field;
using Symlab.App.Mapping;
using Symlab.App.Settings;
using Symlab.Flight.Airframe;
using Symlab.Flight.Atmosphere;
using Symlab.Flight.Controls;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Recording;
using Symlab.Flight.Sim;
using Symlab.Input;

namespace Symlab.App.Session;

/// <summary>One flight at the club field: owns the simulation and applies pilot actions (reset, pause, wind).</summary>
public sealed class FlightSession : IDisposable
{
    public const int TreeSeed = 7;
    const double HandLaunchHeight = 1.8;
    const double HandLaunchSpeed = 10;
    const double HandLaunchPitchDeg = 10;

    readonly FlightEnvironment _windy;
    readonly FlightEnvironment _calm;

    public FlightSession(AircraftDefinition definition, FlightConditions conditions)
    {
        Definition = definition;
        Conditions = conditions;
        Terrain = new ClubFieldTerrain(TreePlanter.Plant(TreeSeed));
        _windy = new FlightEnvironment(Terrain, new WindField(conditions.ToWindSettings(), conditions.Seed));
        _calm = new FlightEnvironment(Terrain, new WindField(new WindSettings(), conditions.Seed));
        Aircraft = new Aircraft(definition);
        Simulation = new Simulation(Aircraft, _windy);
        Span = definition.Surfaces.Where(s => s.Role == Symlab.Flight.Aero.SurfaceRole.Wing).Select(s => s.TotalSpan).DefaultIfEmpty(1.0).Max();
        Reset();
    }

    public AircraftDefinition Definition { get; }
    public FlightConditions Conditions { get; }
    public ClubFieldTerrain Terrain { get; }
    public Aircraft Aircraft { get; }
    public Simulation Simulation { get; private set; }
    public bool Paused { get; private set; }
    public bool WindEnabled { get; private set; } = true;
    public double FlightTime { get; private set; }
    public double Span { get; }
    public FlightRecorder? Recorder { get; private set; }

    public RigidBodyState StartState()
    {
        if (Definition.Wheels.Count > 0)
        {
            double heading = ClubField.TakeoffHeading(Conditions.WindFromDeg);
            var (x, z) = ClubField.TakeoffPoint(heading);
            return InitialConditions.OnGround(Definition, Terrain, x, z, heading);
        }
        var (lx, lz, launchHeading) = ClubField.HandLaunchPoint(Conditions.WindFromDeg);
        return InitialConditions.InFlight(new Vec3(lx, Terrain.Height(lx, lz) + HandLaunchHeight, lz), launchHeading, HandLaunchSpeed, HandLaunchPitchDeg);
    }

    public void Reset()
    {
        Simulation.Reset(StartState());
        FlightTime = 0;
        Paused = false;
    }

    public void Handle(SwitchAction action)
    {
        switch (action)
        {
            case SwitchAction.Reset: Reset(); break;
            case SwitchAction.Pause: Paused = !Paused; break;
            case SwitchAction.ToggleWind: SetWind(!WindEnabled); break;
            case SwitchAction.NextCamera: break;
        }
    }

    public int Tick(double frameDt, in ControlInputs input)
    {
        if (Paused) return 0;
        bool flying = Aircraft.Crash == CrashCause.None;
        int steps = Simulation.Advance(frameDt, input);
        if (flying && Aircraft.Crash == CrashCause.None) FlightTime += steps * Simulation.FixedStep;
        return steps;
    }

    public RigidBodyState DisplayState => StateInterpolation.Interpolate(Simulation.Previous, Aircraft.State, Simulation.InterpolationAlpha);

    public double HeightAgl
    {
        get
        {
            var p = Aircraft.State.Position;
            return p.Y - Terrain.Height(p.X, p.Z);
        }
    }

    public void AttachRecorder(FlightRecorder recorder)
    {
        Recorder?.Dispose();
        Recorder = recorder;
        Simulation.Recorder = recorder;
    }

    public void Dispose() => Recorder?.Dispose();

    void SetWind(bool on)
    {
        if (on == WindEnabled) return;
        WindEnabled = on;
        Simulation = new Simulation(Aircraft, on ? _windy : _calm) { Recorder = Recorder };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all PASS (all four test projects).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(app): add aircraft catalog and flight session

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 9: Godot toolchain, project scaffold and boot smoke test

**Files:**
- Create: `game/project.godot`, `game/Symlab.Game.csproj`, `game/Main.tscn`
- Create: `game/Scripts/Main.cs`, `game/Scripts/AppPaths.cs`, `game/Scripts/GodotConvert.cs`, `game/Scripts/Translations.cs`
- Create: `docs/dev-setup.md`
- Modify: `.gitignore` (Godot build output)

**Interfaces:**
- Consumes: `AppSettings`, `TranslationTable`, `GodotBasis`, `Rgb`, `Vec3`, `Quat`.
- Produces (Godot side, namespace `Symlab.Game`):
  - `AppPaths.GameRoot`, `RepoRoot`, `AircraftRoot` (env `SYMLAB_AIRCRAFT_DIR` overrides), `TranslationsCsv`, `UserDir`, `SettingsFile`, `RadioDir`, `RecordingsDir`.
  - `GodotConvert.ToGodot(this Vec3) : Vector3`, `ToGodot(this Quat) : Quaternion`, `ToGodot(this Rgb, float alpha = 1) : Color`, `BodyTransform(Vec3 position, Quat orientation) : Transform3D`.
  - `Translations.Register(string csvPath, string language)`.
  - Command-line (after `--`): `--smoke-boot` prints `SYMLAB_BOOT_OK locale=<locale> title=<APP_TITLE>` and exits 0.

- [ ] **Step 1: Install Godot .NET and check the binary**

```bash
df -h /System/Volumes/Data | tail -1
brew install --cask godot-mono
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
"$GODOT" --version
```
Expected: at least ~1 GB free before installing; version `4.7.2.stable.mono...`. If the cask installs a different 4.x version, use that exact version in the csproj `Sdk` attribute and in `config/features`, and record it in `docs/dev-setup.md`. If the install asks for a password, stop and report NEEDS_CONTEXT (the user must run it).

- [ ] **Step 2: Create the Godot project files**

`game/project.godot`:
```ini
; Engine configuration file.
config_version=5

[application]
config/name="Symlab"
run/main_scene="res://Main.tscn"
config/features=PackedStringArray("4.7", "C#", "Forward Plus")

[display]
window/size/viewport_width=1600
window/size/viewport_height=900
window/stretch/mode="canvas_items"
window/vsync/vsync_mode=1

[dotnet]
project/assembly_name="Symlab.Game"

[rendering]
anti_aliasing/quality/msaa_3d=2
```

`game/Symlab.Game.csproj`:
```xml
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>Symlab.Game</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../src/Symlab.App/Symlab.App.csproj" />
  </ItemGroup>
</Project>
```

`game/Main.tscn`:
```ini
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://Scripts/Main.cs" id="1_main"]

[node name="Main" type="Node"]
script = ExtResource("1_main")
```

Append to `.gitignore`:
```
game/.godot/
game/export/
```

- [ ] **Step 3: Write the scripts**

`game/Scripts/AppPaths.cs`:
```csharp
using Godot;

namespace Symlab.Game;

public static class AppPaths
{
    public static string GameRoot => ProjectSettings.GlobalizePath("res://");
    public static string RepoRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(GameRoot, ".."));
    public static string AircraftRoot => System.Environment.GetEnvironmentVariable("SYMLAB_AIRCRAFT_DIR") ?? System.IO.Path.Combine(RepoRoot, "aircraft");
    public static string TranslationsCsv => System.IO.Path.Combine(GameRoot, "translations", "strings.csv");
    public static string UserDir => OS.GetUserDataDir();
    public static string SettingsFile => System.IO.Path.Combine(UserDir, "settings.json");
    public static string RadioDir => System.IO.Path.Combine(UserDir, "radios");
    public static string RecordingsDir => System.IO.Path.Combine(UserDir, "recordings");
}
```

`game/Scripts/GodotConvert.cs`:
```csharp
using Godot;
using Symlab.App.Mapping;
using Symlab.App.Visual;
using Symlab.Flight.Geometry;

namespace Symlab.Game;

public static class GodotConvert
{
    public static Vector3 ToGodot(this Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static Quaternion ToGodot(this Quat q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    public static Color ToGodot(this Rgb c, float alpha = 1f) => new(c.R, c.G, c.B, alpha);

    /// <summary>Transform of a node that displays a body at this position and Symlab orientation.</summary>
    public static Transform3D BodyTransform(Vec3 position, Quat orientation) =>
        new(new Basis(GodotBasis.NodeRotation(orientation).ToGodot()), position.ToGodot());
}
```

`game/Scripts/Translations.cs`:
```csharp
using Godot;
using Symlab.App.Localization;

namespace Symlab.Game;

/// <summary>Loads the translation CSV (ignored by Godot's importer via .gdignore) into Godot's TranslationServer.</summary>
public static class Translations
{
    public static void Register(string csvPath, string language)
    {
        var table = TranslationTable.Parse(System.IO.File.ReadAllText(csvPath));
        foreach (var lang in table.Languages)
        {
            var translation = new Translation { Locale = lang };
            foreach (var key in table.Keys) translation.AddMessage(key, table.Get(lang, key));
            TranslationServer.AddTranslation(translation);
        }
        TranslationServer.SetLocale(language);
    }
}
```

`game/Scripts/Main.cs`:
```csharp
using Godot;
using Symlab.App.Settings;

namespace Symlab.Game;

public partial class Main : Node
{
    public override void _Ready()
    {
        var settings = AppSettings.Load(AppPaths.SettingsFile);
        Translations.Register(AppPaths.TranslationsCsv, settings.Language);
        var args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "--smoke-boot") >= 0)
        {
            GD.Print($"SYMLAB_BOOT_OK locale={TranslationServer.GetLocale()} title={Tr("APP_TITLE")}");
            GetTree().Quit(0);
        }
    }
}
```

`docs/dev-setup.md`:
````markdown
# Developer setup

- .NET 10 SDK: `brew install --cask dotnet-sdk`
- Godot 4.7.2 .NET: `brew install --cask godot-mono`
- Godot binary: `/Applications/Godot_mono.app/Contents/MacOS/Godot` (below: `$GODOT`)

## Libraries and tests

```bash
dotnet test
```

## Game

```bash
dotnet build game/Symlab.Game.csproj
"$GODOT" --headless --path game --import      # first time only (creates game/.godot)
"$GODOT" --path game                          # run the simulator
```

Smoke checks (after `--`, arguments go to the game):

```bash
"$GODOT" --headless --path game -- --smoke-boot
```

User data (settings, radio profiles, flight recordings): `~/Library/Application Support/Godot/app_userdata/Symlab/`.
````

- [ ] **Step 4: Build and run the boot smoke test**

```bash
cd /Users/axel.ldq/3_SYMLAB
dotnet build game/Symlab.Game.csproj
"$GODOT" --headless --path game --import
"$GODOT" --headless --path game -- --smoke-boot; echo "exit=$?"
```
Expected: build succeeds with 0 warnings (see Global Constraints if Godot-generated code warns); output contains `SYMLAB_BOOT_OK locale=fr title=Symlab — simulateur de vol RC` and `exit=0`. Also run `dotnet test` (all green).

- [ ] **Step 5: Commit**

Commit the project files and any `*.uid` files Godot generated next to scripts; never `game/.godot/`.
```bash
git add -A
git commit -m "feat(game): scaffold Godot .NET project with translations and boot smoke test

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Joypad reading, shared UI helpers and the radio screen

**Files:**
- Create: `game/Scripts/Services.cs`, `game/Scripts/Ui.cs`, `game/Scripts/Radio/JoypadReader.cs`, `game/Scripts/Radio/RadioScreen.cs`
- Modify: `game/Scripts/Main.cs` (full replacement below)

**Interfaces:**
- Consumes: `AppSettings`, `StickMode`, `RadioProfileStore`, `InputRouter`, `JoypadSnapshot`, `CalibrationWizard`, `CalibrationPrompts`, `SwitchCapture`, `SwitchAction`, `RawInputFrame`.
- Produces:
  - `Services` (`Settings` settable, `Radios`, `Router`, `SaveSettings()`).
  - `Ui.T(key)`, `Ui.Screen(Control owner, string title) : VBoxContainer`, `Ui.Text(string, int size = 18) : Label`, `Ui.Button(string, Action) : Button`, `Ui.Slider(string label, double min, double max, double step, double value, Action<double> changed, string format) : HBoxContainer`, `Ui.Check(string, bool, Action<bool>) : CheckBox`, `Ui.Row(params Control[]) : HBoxContainer`.
  - `JoypadReader.MaxAxes` (= `(int)JoyAxis.Max`), `JoypadReader.MaxButtons = 32`, `JoypadReader.Poll() : IReadOnlyList<JoypadSnapshot>`.
  - `RadioScreen : Control` with `Init(Services services, Action back)`.
  - `Main.ShowRadio()`; command line `--screen radio` opens the radio screen; `--smoke-radio` prints `SYMLAB_RADIO_OK joypads=<n>` then one line per pad `JOYPAD guid=<guid> name=<name> axes=<a0;a1;...>` and exits 0.

- [ ] **Step 1: Write the scripts**

`game/Scripts/Services.cs`:
```csharp
using Symlab.App.Session;
using Symlab.App.Settings;

namespace Symlab.Game;

/// <summary>Application-wide objects shared by the screens.</summary>
public sealed class Services
{
    public required AppSettings Settings { get; set; }
    public required RadioProfileStore Radios { get; init; }
    public required InputRouter Router { get; init; }

    public void SaveSettings() => Settings.Save(AppPaths.SettingsFile);
}
```

`game/Scripts/Ui.cs`:
```csharp
using System.Globalization;
using Godot;

namespace Symlab.Game;

/// <summary>Small factory for the code-built UI, so every screen looks the same.</summary>
public static class Ui
{
    public static string T(string key) => TranslationServer.Translate(key).ToString();

    public static VBoxContainer Screen(Control owner, string title)
    {
        owner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0.10f, 0.12f, 0.15f) };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        owner.AddChild(background);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        owner.AddChild(scroll);

        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 48);
        scroll.AddChild(margin);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);
        column.AddChild(Text(title, 34));
        return column;
    }

    public static Label Text(string text, int size = 18)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    public static Button Button(string text, System.Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(240, 44) };
        button.Pressed += pressed;
        return button;
    }

    public static HBoxContainer Slider(string label, double min, double max, double step, double value, System.Action<double> changed, string format)
    {
        var name = Text(label);
        name.CustomMinimumSize = new Vector2(320, 0);
        var valueLabel = Text(value.ToString(format, CultureInfo.InvariantCulture));
        valueLabel.CustomMinimumSize = new Vector2(80, 0);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(360, 24),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        slider.ValueChanged += v =>
        {
            valueLabel.Text = v.ToString(format, CultureInfo.InvariantCulture);
            changed(v);
        };
        return Row(name, slider, valueLabel);
    }

    public static CheckBox Check(string text, bool on, System.Action<bool> toggled)
    {
        var check = new CheckBox { Text = text, ButtonPressed = on };
        check.Toggled += v => toggled(v);
        return check;
    }

    public static HBoxContainer Row(params Control[] children)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        foreach (var child in children) row.AddChild(child);
        return row;
    }
}
```

`game/Scripts/Radio/JoypadReader.cs`:
```csharp
using Godot;
using Symlab.App.Session;
using Symlab.Input;

namespace Symlab.Game.Radio;

/// <summary>Reads every connected joypad (the radio in USB-joystick mode) through Godot's SDL input layer.</summary>
public static class JoypadReader
{
    public const int MaxAxes = (int)JoyAxis.Max;
    public const int MaxButtons = 32;

    public static IReadOnlyList<JoypadSnapshot> Poll()
    {
        var pads = new List<JoypadSnapshot>();
        foreach (int device in Input.GetConnectedJoypads())
        {
            var axes = new double[MaxAxes];
            for (int i = 0; i < MaxAxes; i++) axes[i] = Input.GetJoyAxis(device, (JoyAxis)i);
            var buttons = new bool[MaxButtons];
            for (int i = 0; i < MaxButtons; i++) buttons[i] = Input.IsJoyButtonPressed(device, (JoyButton)i);
            pads.Add(new JoypadSnapshot(Input.GetJoyGuid(device), Input.GetJoyName(device), new RawInputFrame(axes, buttons)));
        }
        return pads;
    }
}
```

`game/Scripts/Radio/RadioScreen.cs`:
```csharp
using Godot;
using Symlab.App.Session;
using Symlab.App.Settings;
using Symlab.App.Ui;
using Symlab.Input;

namespace Symlab.Game.Radio;

/// <summary>Radio setup: detected devices, live raw axes, calibration wizard, stick mode and switch assignment.</summary>
public partial class RadioScreen : Control
{
    static readonly Color Good = new(0.35f, 0.85f, 0.45f);
    static readonly Color Bad = new(0.95f, 0.40f, 0.35f);

    Services _services = null!;
    ItemList _devices = null!;
    Label _status = null!;
    Label _prompt = null!;
    Button _next = null!;
    Button _cancel = null!;
    readonly List<ProgressBar> _bars = [];
    IReadOnlyList<JoypadSnapshot> _pads = [];
    string? _selectedGuid;
    CalibrationWizard? _wizard;
    SwitchCapture? _capture;
    SwitchAction _captureAction;

    public void Init(Services services, System.Action back)
    {
        _services = services;
        var column = Ui.Screen(this, Ui.T("RADIO_TITLE"));

        _devices = new ItemList { CustomMinimumSize = new Vector2(600, 90) };
        _devices.ItemSelected += index => _selectedGuid = index < _pads.Count ? _pads[(int)index].Guid : null;
        column.AddChild(_devices);

        _status = Ui.Text("", 20);
        column.AddChild(_status);

        var mode = new OptionButton();
        mode.AddItem("Mode 1", 1);
        mode.AddItem("Mode 2", 2);
        mode.Selected = services.Settings.StickMode == StickMode.Mode1 ? 0 : 1;
        mode.ItemSelected += index =>
        {
            _services.Settings = _services.Settings with { StickMode = index == 0 ? StickMode.Mode1 : StickMode.Mode2 };
            _services.SaveSettings();
        };
        column.AddChild(Ui.Row(Ui.Text(Ui.T("RADIO_MODE")), mode));

        column.AddChild(Ui.Text(Ui.T("RADIO_AXES"), 22));
        for (int i = 0; i < JoypadReader.MaxAxes; i++)
        {
            var bar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(420, 16) };
            _bars.Add(bar);
            column.AddChild(Ui.Row(Ui.Text($"{i + 1}", 16), bar));
        }

        _prompt = Ui.Text("", 22);
        column.AddChild(_prompt);
        _next = Ui.Button(Ui.T("RADIO_NEXT"), Next);
        _cancel = Ui.Button(Ui.T("RADIO_CANCEL"), Cancel);
        column.AddChild(Ui.Row(Ui.Button(Ui.T("RADIO_CALIBRATE"), StartCalibration), _next, _cancel));
        column.AddChild(Ui.Row(
            Ui.Button(Ui.T("RADIO_BIND_RESET"), () => StartCapture(SwitchAction.Reset)),
            Ui.Button(Ui.T("RADIO_BIND_PAUSE"), () => StartCapture(SwitchAction.Pause)),
            Ui.Button(Ui.T("RADIO_BIND_WIND"), () => StartCapture(SwitchAction.ToggleWind))));
        column.AddChild(Ui.Text(Ui.T("RADIO_HELP"), 16));
        column.AddChild(Ui.Button(Ui.T("BACK"), back));
        SetBusy(false);
    }

    JoypadSnapshot? Selected
    {
        get
        {
            foreach (var pad in _pads)
                if (pad.Guid == _selectedGuid) return pad;
            return _pads.Count > 0 ? _pads[0] : null;
        }
    }

    public override void _Process(double delta)
    {
        _pads = JoypadReader.Poll();
        RefreshDevices();
        if (Selected is not { } pad)
        {
            _status.Text = Ui.T("RADIO_NO_DEVICE");
            _status.Modulate = Bad;
            foreach (var bar in _bars) bar.Value = 50;
            return;
        }

        for (int i = 0; i < _bars.Count && i < pad.Frame.Axes.Length; i++) _bars[i].Value = (pad.Frame.Axes[i] + 1) * 50;
        bool calibrated = _services.Radios.Load(pad.Guid, out _) is not null;
        _status.Text = $"{pad.Name} — {Ui.T(calibrated ? "RADIO_DEVICE_READY" : "RADIO_DEVICE_UNCALIBRATED")}";
        _status.Modulate = calibrated ? Good : Bad;

        if (_wizard is not null)
        {
            _wizard.Feed(pad.Frame);
            _prompt.Text = PromptText(_wizard);
        }
        if (_capture is not null && _capture.Update(_captureAction, pad.Frame) is { } binding)
        {
            SaveBinding(pad, binding);
            _capture = null;
            _prompt.Text = Ui.T("RADIO_BIND_DONE");
            SetBusy(false);
        }
    }

    void RefreshDevices()
    {
        if (_devices.ItemCount == _pads.Count) return;
        _devices.Clear();
        foreach (var pad in _pads) _devices.AddItem(pad.Name);
    }

    string PromptText(CalibrationWizard wizard)
    {
        string text = Ui.T(CalibrationPrompts.StageKey(wizard.Current, wizard.FunctionToIdentify));
        if (wizard.FunctionToIdentify is { } function)
            text = string.Format(text, Ui.T(CalibrationPrompts.StickSideKey(function, _services.Settings.StickMode)));
        return text;
    }

    void StartCalibration()
    {
        if (Selected is not { } pad) return;
        _capture = null;
        _wizard = new CalibrationWizard(pad.Frame.Axes.Length);
        SetBusy(true);
    }

    void Next()
    {
        if (_wizard is null || Selected is not { } pad) return;
        try
        {
            _wizard.Next();
        }
        catch (System.InvalidOperationException)
        {
            _prompt.Text = Ui.T("CAL_FAILED");
            return;
        }
        if (_wizard.Current != CalibrationWizard.Stage.Done) return;

        var profile = _wizard.BuildProfile(pad.Guid, pad.Name);
        var previous = _services.Radios.Load(pad.Guid, out _);
        if (previous is not null) profile.Switches.AddRange(previous.Switches);
        _services.Radios.Save(profile);
        _services.Router.InvalidateProfiles();
        _wizard = null;
        _prompt.Text = Ui.T("RADIO_SAVED");
        SetBusy(false);
    }

    void Cancel()
    {
        _wizard = null;
        _capture = null;
        _prompt.Text = "";
        SetBusy(false);
    }

    void StartCapture(SwitchAction action)
    {
        if (Selected is not { } pad || _services.Radios.Load(pad.Guid, out _) is null)
        {
            _prompt.Text = Ui.T("RADIO_DEVICE_UNCALIBRATED");
            return;
        }
        _wizard = null;
        _capture = new SwitchCapture();
        _captureAction = action;
        _prompt.Text = Ui.T("RADIO_BIND_WAIT");
        SetBusy(true);
    }

    void SaveBinding(JoypadSnapshot pad, SwitchBinding binding)
    {
        var profile = _services.Radios.Load(pad.Guid, out _);
        if (profile is null) return;
        profile.Switches.RemoveAll(s => s.Action == binding.Action);
        profile.Switches.Add(binding);
        _services.Radios.Save(profile);
        _services.Router.InvalidateProfiles();
    }

    void SetBusy(bool busy)
    {
        _next.Disabled = !busy || _capture is not null;
        _cancel.Disabled = !busy;
    }
}
```

`game/Scripts/Main.cs` (replace the whole file):
```csharp
using Godot;
using Symlab.App.Session;
using Symlab.App.Settings;
using Symlab.Game.Radio;

namespace Symlab.Game;

public partial class Main : Node
{
    Services _services = null!;
    Node? _current;

    public override void _Ready()
    {
        var settings = AppSettings.Load(AppPaths.SettingsFile);
        Translations.Register(AppPaths.TranslationsCsv, settings.Language);
        var store = new RadioProfileStore(AppPaths.RadioDir);
        _services = new Services { Settings = settings, Radios = store, Router = new InputRouter(guid => store.Load(guid, out _)) };

        var args = OS.GetCmdlineUserArgs();
        if (Has(args, "--smoke-boot"))
        {
            GD.Print($"SYMLAB_BOOT_OK locale={TranslationServer.GetLocale()} title={Tr("APP_TITLE")}");
            GetTree().Quit(0);
            return;
        }
        if (Has(args, "--smoke-radio"))
        {
            var pads = JoypadReader.Poll();
            GD.Print($"SYMLAB_RADIO_OK joypads={pads.Count}");
            foreach (var pad in pads)
                GD.Print($"JOYPAD guid={pad.Guid} name={pad.Name} axes={string.Join(";", pad.Frame.Axes.Select(a => a.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}");
            GetTree().Quit(0);
            return;
        }
        ShowRadio();
    }

    public void ShowRadio()
    {
        var screen = new RadioScreen();
        screen.Init(_services, ShowRadio);
        Switch(screen);
    }

    void Switch(Node next)
    {
        _current?.QueueFree();
        _current = next;
        AddChild(next);
    }

    static bool Has(string[] args, string flag) => System.Array.IndexOf(args, flag) >= 0;
}
```

- [ ] **Step 2: Build and run the smoke checks**

```bash
dotnet build game/Symlab.Game.csproj
"$GODOT" --headless --path game -- --smoke-boot; echo "exit=$?"
"$GODOT" --headless --path game -- --smoke-radio; echo "exit=$?"
```
Expected: both exit 0; `SYMLAB_RADIO_OK joypads=0` (or more if a joypad is plugged in).

- [ ] **Step 3: Windowed screenshot of the radio screen**

Run the app windowed for a visual check and stop it after a few seconds:
```bash
"$GODOT" --path game -- --screen radio & sleep 6; kill %1
```
(`--screen radio` falls through to `ShowRadio()`, which is the default for now.) Confirm visually in your report that the radio screen opened without errors in the Godot output.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(game): add joypad reading, UI helpers and the radio setup screen

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Controller checkpoint — real radio**

The controller asks the user to plug in the TX16S (USB Joystick mode) and run:
```bash
"$GODOT" --headless --path game -- --smoke-radio
```
Expected: `joypads=1` and axes that change when the sticks move (run twice with sticks in different positions). If the radio is not listed, stop and investigate before Task 13 (this is the risk the whole project hinges on).

---

### Task 11: The club field

**Files:**
- Create: `game/Scripts/World/FieldBuilder.cs`, `game/Scripts/World/WindsockNode.cs`
- Modify: `game/Scripts/Main.cs` (add the `--screenshot-field <path>` mode)

**Interfaces:**
- Consumes: `ClubField`, `ClubFieldTerrain` (`GroundHeight`, `Normal`, `Trees`), `FlightConditions`, `SunMath`, `WindsockPose`, `GodotConvert`.
- Produces:
  - `FieldBuilder.Build(Node3D root, ClubFieldTerrain terrain, FlightConditions conditions) : WindsockNode` — adds sky/environment, sun, terrain mesh, runway, pilot box with fence, trees (MultiMesh) and the windsock.
  - `WindsockNode : Node3D` with `Apply(WindsockPose pose)`.
  - Command line `--screenshot-field <path>`: builds the field with a camera at the pilot's eyes looking along the runway, waits 20 frames, saves a PNG to `<path>`, exits 0.

- [ ] **Step 1: Write the scripts**

`game/Scripts/World/WindsockNode.cs`:
```csharp
using Godot;
using Symlab.App.Field;

namespace Symlab.Game.World;

/// <summary>6 m pole with an orange sock that points downwind and droops in light air.</summary>
public partial class WindsockNode : Node3D
{
    const float PoleHeight = 6f;
    Node3D _sock = null!;

    public override void _Ready()
    {
        var pole = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = PoleHeight },
            Position = new Vector3(0, PoleHeight / 2, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.85f, 0.85f) },
        };
        AddChild(pole);

        _sock = new Node3D { Position = new Vector3(0, PoleHeight, 0) };
        var cone = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.30f, BottomRadius = 0.12f, Height = 1.6f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.45f, 0.05f) },
            Position = new Vector3(0, 0, -0.8f),
            RotationDegrees = new Vector3(90, 0, 0),
        };
        _sock.AddChild(cone);
        AddChild(_sock);
    }

    public void Apply(WindsockPose pose) =>
        _sock.Rotation = new Vector3(-Mathf.DegToRad((float)pose.DroopDeg), -Mathf.DegToRad((float)pose.HeadingDeg), 0);
}
```

`game/Scripts/World/FieldBuilder.cs`:
```csharp
using Godot;
using Symlab.App.Field;
using Symlab.App.Settings;
using Symlab.Flight.Terrain;

namespace Symlab.Game.World;

/// <summary>Builds the generic club field: sky, sun, rolling terrain, grass runway, pilot box, trees and windsock.</summary>
public static class FieldBuilder
{
    const float GridStep = 10f;
    static readonly Color Grass = new(0.30f, 0.47f, 0.19f);
    static readonly Color Mowed = new(0.40f, 0.60f, 0.26f);
    static readonly Color Gravel = new(0.55f, 0.52f, 0.47f);

    public static WindsockNode Build(Node3D root, ClubFieldTerrain terrain, FlightConditions conditions)
    {
        root.AddChild(Environment());
        root.AddChild(Sun(conditions));
        root.AddChild(TerrainMesh(terrain));
        root.AddChild(FlatPatch(ClubField.RunwayLength, ClubField.RunwayWidth, new Vector3(0, 0.03f, 0), Mowed));
        var pilot = ClubField.PilotPosition.ToGodot();
        root.AddChild(FlatPatch(8, 4, pilot + new Vector3(0, 0.03f, 0), Gravel));
        root.AddChild(Fence(pilot.Z - 4f));
        AddTrees(root, terrain.Trees);
        var sock = new WindsockNode { Position = ClubField.WindsockPosition.ToGodot() };
        root.AddChild(sock);
        return sock;
    }

    static WorldEnvironment Environment()
    {
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.30f, 0.50f, 0.85f),
                SkyHorizonColor = new Color(0.72f, 0.80f, 0.90f),
                GroundHorizonColor = new Color(0.60f, 0.62f, 0.55f),
                GroundBottomColor = new Color(0.25f, 0.30f, 0.20f),
                SunAngleMax = 30f,
            },
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.75f, 0.80f, 0.88f),
            FogDensity = 0.0004f,
        };
        return new WorldEnvironment { Environment = environment };
    }

    static DirectionalLight3D Sun(FlightConditions conditions)
    {
        var toSun = SunMath.Direction(conditions.SunAzimuthDeg, conditions.SunElevationDeg).ToGodot();
        var up = Mathf.Abs(toSun.Y) > 0.99f ? Vector3.Back : Vector3.Up;
        return new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightEnergy = 1.2f,
            Transform = Transform3D.Identity.LookingAt(-toSun, up),
        };
    }

    static MeshInstance3D TerrainMesh(ClubFieldTerrain terrain)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float half = (float)ClubField.TerrainHalfSize;
        int cells = (int)(2 * half / GridStep);

        void Vertex(int i, int j)
        {
            double x = -half + i * GridStep, z = -half + j * GridStep;
            st.SetNormal(terrain.Normal(x, z).ToGodot());
            st.AddVertex(new Vector3((float)x, (float)ClubFieldTerrain.GroundHeight(x, z), (float)z));
        }

        for (int i = 0; i < cells; i++)
        for (int j = 0; j < cells; j++)
        {
            float shade = 0.9f + 0.1f * Mathf.Sin(i * 12.9898f + j * 78.233f);
            st.SetColor(Grass * shade);
            Vertex(i, j); Vertex(i + 1, j); Vertex(i + 1, j + 1);
            Vertex(i, j); Vertex(i + 1, j + 1); Vertex(i, j + 1);
        }

        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Roughness = 1f,
            },
        };
    }

    static MeshInstance3D FlatPatch(double length, double width, Vector3 center, Color color) => new()
    {
        Mesh = new PlaneMesh { Size = new Vector2((float)length, (float)width) },
        Position = center,
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f },
    };

    static Node3D Fence(float z)
    {
        var fence = new Node3D();
        var material = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.33f, 0.20f) };
        for (float x = -10; x <= 10; x += 2)
            fence.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 1.1f, 0.08f) }, Position = new Vector3(x, 0.55f, z), MaterialOverride = material });
        fence.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(20f, 0.06f, 0.04f) }, Position = new Vector3(0, 1.0f, z), MaterialOverride = material });
        return fence;
    }

    static void AddTrees(Node3D root, IReadOnlyList<CylinderObstacle> trees)
    {
        var trunks = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.25f, Height = 1f } };
        var crowns = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 1f, Height = 1f } };
        trunks.InstanceCount = trees.Count;
        crowns.InstanceCount = trees.Count;
        for (int i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            float h = (float)t.Height, r = (float)t.Radius, y = (float)t.BaseY;
            var basePoint = new Vector3((float)t.X, y, (float)t.Z);
            trunks.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(1, 0.35f * h, 1)), basePoint + new Vector3(0, 0.175f * h, 0)));
            crowns.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(r, 0.75f * h, r)), basePoint + new Vector3(0, 0.625f * h, 0)));
        }
        root.AddChild(new MultiMeshInstance3D { Multimesh = trunks, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.33f, 0.24f, 0.15f) } });
        root.AddChild(new MultiMeshInstance3D { Multimesh = crowns, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.33f, 0.14f) } });
    }
}
```

Add to `Main.cs`: a field-preview mode. Insert before `ShowRadio();` at the end of `_Ready`:
```csharp
        if (ArgValue(args, "--screenshot-field") is { } fieldShot)
        {
            var preview = new Node3D();
            AddChild(preview);
            var terrain = new Symlab.App.Field.ClubFieldTerrain(Symlab.App.Field.TreePlanter.Plant(FlightSession.TreeSeed));
            World.FieldBuilder.Build(preview, terrain, _services.Settings.Conditions);
            var eye = Symlab.App.Field.ClubField.PilotPosition.ToGodot() + new Vector3(0, (float)Symlab.App.Field.ClubField.EyeHeight, 0);
            var camera = new Camera3D { Current = true, Fov = (float)_services.Settings.FovDeg, Far = 4000f };
            preview.AddChild(camera);
            camera.LookAtFromPosition(eye, new Vector3(-30, 8, 0), Vector3.Up);
            CaptureAfterFrames(20, fieldShot);
            return;
        }
```
and add these members to `Main`:
```csharp
    static string? ArgValue(string[] args, string flag)
    {
        int i = System.Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    async void CaptureAfterFrames(int frames, string path)
    {
        for (int i = 0; i < frames; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"SYMLAB_SCREENSHOT path={path} error={error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
```

- [ ] **Step 2: Build, smoke and screenshot**

```bash
dotnet build game/Symlab.Game.csproj
"$GODOT" --headless --path game -- --smoke-boot; echo "exit=$?"
"$GODOT" --path game -- --screenshot-field /tmp/symlab-field.png; echo "exit=$?"
```
Expected: exit 0 both; `SYMLAB_SCREENSHOT ... error=Ok`. Open the PNG (Read tool) and confirm in your report: sky with horizon, green ground, lighter runway strip ahead, tree lines in the distance, windsock visible to the right. If the screenshot is black or empty, fix before committing.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(game): build the club field with terrain, runway, trees, sky, sun and windsock

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Aircraft visual with animated control surfaces

**Files:**
- Create: `game/Scripts/Flight/AircraftVisual.cs`
- Modify: `game/Scripts/Main.cs` (add `--screenshot-aircraft <id> <path>`)

**Interfaces:**
- Consumes: `MeshPart`, `AircraftMeshBuilder`, `GodotBasis.BodyToNodeLocal`, `GodotConvert`, `Aircraft` (`Deflections`, `Power.Telemetry.Rpm`), `RigidBodyState`, `FlightSession`, `AircraftLoader`.
- Produces: `AircraftVisual : Node3D` with `Build(IReadOnlyList<MeshPart> parts)`, `UpdateFrom(Aircraft aircraft, RigidBodyState display)` and `SetAllDeflections(double radians)` (preview helper); command line `--screenshot-aircraft <id> <path>`: field + aircraft at its start state with control deflections forced to +0.35 rad, camera 4 m to the aircraft's left-front, PNG after 20 frames, exit 0.

- [ ] **Step 1: Write the script**

`game/Scripts/Flight/AircraftVisual.cs`:
```csharp
using Godot;
using Symlab.App.Mapping;
using Symlab.App.Visual;
using Symlab.Flight.Airframe;
using Symlab.Flight.Dynamics;

namespace Symlab.Game.Flight;

/// <summary>Draws the procedural aircraft; control surfaces rotate about their hinges with the servo deflections.</summary>
public partial class AircraftVisual : Node3D
{
    readonly List<(Node3D Pivot, Vector3 Axis, int ControlIndex)> _controls = [];
    Node3D? _propeller;

    public void Build(IReadOnlyList<MeshPart> parts)
    {
        foreach (var part in parts)
        {
            if (part.ControlIndex < 0)
            {
                var mesh = MeshFor(part.Triangles, part.Color, Vector3.Zero, part.Name == "propeller" ? 0.35f : 1f);
                AddChild(mesh);
                if (part.Name == "propeller") _propeller = mesh;
                continue;
            }
            var hinge = GodotBasis.BodyToNodeLocal(part.HingePoint).ToGodot();
            var axis = GodotBasis.BodyToNodeLocal(part.HingeAxis).ToGodot().Normalized();
            var pivot = new Node3D { Position = hinge };
            pivot.AddChild(MeshFor(part.Triangles, part.Color, hinge, 1f));
            AddChild(pivot);
            _controls.Add((pivot, axis, part.ControlIndex));
        }
    }

    public void UpdateFrom(Aircraft aircraft, RigidBodyState display)
    {
        Transform = GodotConvert.BodyTransform(display.Position, display.Orientation);
        foreach (var (pivot, axis, index) in _controls)
            pivot.Basis = new Basis(axis, (float)aircraft.Deflections[index]);
        if (_propeller is not null) _propeller.Visible = (aircraft.Power?.Telemetry.Rpm ?? 0) > 200;
    }

    /// <summary>Preview helper: shows every control surface at the same deflection (rad, positive = trailing edge down).</summary>
    public void SetAllDeflections(double radians)
    {
        foreach (var (pivot, axis, _) in _controls) pivot.Basis = new Basis(axis, (float)radians);
    }

    static MeshInstance3D MeshFor(IReadOnlyList<Symlab.Flight.Geometry.Vec3> triangles, Rgb color, Vector3 origin, float alpha)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(color.ToGodot(alpha));
        foreach (var v in triangles) st.AddVertex(GodotBasis.BodyToNodeLocal(v).ToGodot() - origin);
        st.GenerateNormals();
        return new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            },
        };
    }
}
```

Add to `Main.cs` `_Ready`, before the final `ShowRadio();`:
```csharp
        if (ArgValue(args, "--screenshot-aircraft") is { } aircraftId && args.Length > System.Array.IndexOf(args, "--screenshot-aircraft") + 2)
        {
            string shotPath = args[System.Array.IndexOf(args, "--screenshot-aircraft") + 2];
            var def = Symlab.Flight.Airframe.AircraftLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId));
            var session = new FlightSession(def, _services.Settings.Conditions);
            var preview = new Node3D();
            AddChild(preview);
            World.FieldBuilder.Build(preview, session.Terrain, _services.Settings.Conditions);
            var visual = new Flight.AircraftVisual();
            preview.AddChild(visual);
            visual.Build(Symlab.App.Visual.AircraftMeshBuilder.Build(def, session.Aircraft.Aero.Segments));
            var start = session.StartState();
            visual.UpdateFrom(session.Aircraft, start);
            visual.SetAllDeflections(0.35);
            var p = start.Position.ToGodot();
            var forward = start.Orientation.Rotate(Symlab.Flight.Geometry.Vec3.UnitX).ToGodot();
            var left = -start.Orientation.Rotate(Symlab.Flight.Geometry.Vec3.UnitZ).ToGodot();
            var camera = new Camera3D { Current = true, Fov = 50f, Near = 0.05f, Far = 4000f };
            preview.AddChild(camera);
            camera.LookAtFromPosition(p + forward * 2.5f + left * 3f + Vector3.Up * 1.2f, p, Vector3.Up);
            CaptureAfterFrames(20, shotPath);
            return;
        }
```

- [ ] **Step 2: Build and screenshot each aircraft**

```bash
dotnet build game/Symlab.Game.csproj
for id in trainer sport wing; do "$GODOT" --path game -- --screenshot-aircraft $id /tmp/symlab-$id.png; echo "$id exit=$?"; done
```
Expected: exit 0 for all three. Open the three PNGs (Read tool) and confirm in the report: aircraft right way up, nose toward the camera side it should be (camera is front-left), wings level, orange control surfaces at the trailing edges, fin on top (trainer/sport), winglets (wing), prop disc not visible at rest.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(game): draw aircraft from their aero geometry with hinged control surfaces

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 13: Flight scene — input, physics, camera, HUD and crash screen

**Files:**
- Create: `game/Scripts/Flight/KeyboardInput.cs`, `game/Scripts/Flight/FlightHud.cs`, `game/Scripts/Flight/CrashOverlay.cs`, `game/Scripts/Flight/FlightScene.cs`
- Modify: `game/Scripts/Main.cs` (add `StartFlight`, `--smoke-flight`, `--screenshot-flight`)

**Interfaces:**
- Consumes: `Services`, `FlightSession`, `AircraftLoader`, `AircraftMeshBuilder`, `AircraftVisual`, `FieldBuilder`, `WindsockNode`, `Windsock`, `LineOfSightRig`, `CameraContext`, `ClubField`, `InputRouter`, `RouterOutput`, `InputSource`, `JoypadReader`, `LatencyMeter`, `FlightDataFormatter`, `FlightRecorder`, `KeyboardKeys`, `KeyboardCommands`, `ControlInputs`, `CrashCause`.
- Produces:
  - `KeyboardInput.Keys() : KeyboardKeys`, `KeyboardInput.Commands() : KeyboardCommands` — physical keys: W/S throttle (Z/S on AZERTY), arrows = aileron and elevator (↓ = pull = pitch up), A/D rudder (Q/D on AZERTY), R reset, P pause, V wind.
  - `FlightHud : CanvasLayer` with `UpdateHud(FlightSession session, RouterOutput input, bool showData)`.
  - `CrashOverlay : CanvasLayer` with `UpdateCrash(CrashCause cause)`.
  - `FlightScene : Node3D` with `Init(Services services, string aircraftId, Action exit, Func<double, ControlInputs>? script = null)`, `Session`, `Latency`, `LastInput`, `LastSteps`. Esc (`ui_cancel`) calls `exit`. Flights are recorded to `AppPaths.RecordingsDir` when `Settings.RecordFlights` (not for scripted runs).
  - `Main.StartFlight(string aircraftId, Func<double, ControlInputs>? script = null)`.
  - Command line `--smoke-flight <id> <seconds>` (headless): scripted throttle 0.7, prints `SYMLAB_SMOKE_OK aircraft=<id> t=<s> x=<x> y=<y> z=<z> crash=<cause>` when sim time ≥ seconds (or on crash), exits 0.
  - Command line `--screenshot-flight <id> <seconds> <path>` (windowed): scripted takeoff (throttle 1; elevator 0.25 from 3.5 s to 5 s, then 0.05), saves the pilot's view to `<path>` at that time, exits 0.

- [ ] **Step 1: Write the scripts**

`game/Scripts/Flight/KeyboardInput.cs`:
```csharp
using Godot;
using Symlab.App.Session;
using Symlab.Input;

namespace Symlab.Game.Flight;

/// <summary>Physical-key mapping, identical on AZERTY and QWERTY keyboards.</summary>
public static class KeyboardInput
{
    static bool Down(Key key) => Input.IsPhysicalKeyPressed(key);

    public static KeyboardKeys Keys() => new(
        ThrottleUp: Down(Key.W), ThrottleDown: Down(Key.S),
        RollLeft: Down(Key.Left), RollRight: Down(Key.Right),
        PitchUp: Down(Key.Down), PitchDown: Down(Key.Up),
        YawLeft: Down(Key.A), YawRight: Down(Key.D));

    public static KeyboardCommands Commands() => new(Down(Key.R), Down(Key.P), Down(Key.V));
}
```

`game/Scripts/Flight/FlightHud.cs`:
```csharp
using Godot;
using Symlab.App.Session;
using Symlab.App.Ui;

namespace Symlab.Game.Flight;

/// <summary>Minimal by default: input source line; optional flight data; PAUSE banner.</summary>
public partial class FlightHud : CanvasLayer
{
    Label _data = null!;
    Label _input = null!;
    Label _banner = null!;

    public override void _Ready()
    {
        _data = Ui.Text("", 18);
        _data.Position = new Vector2(24, 20);
        AddChild(_data);
        _input = Ui.Text("", 14);
        _input.Position = new Vector2(24, 862);
        AddChild(_input);
        _banner = Ui.Text("", 44);
        _banner.Position = new Vector2(700, 380);
        AddChild(_banner);
    }

    public void UpdateHud(FlightSession session, RouterOutput input, bool showData)
    {
        _data.Visible = showData;
        if (showData)
            _data.Text = string.Join("\n", FlightDataFormatter.Format(session.Aircraft, session.HeightAgl, session.FlightTime, input.Controls.Throttle)
                .Select(line => $"{Ui.T(line.Key)} : {line.Value}"));
        string source = input.Source == InputSource.Radio ? $"{Ui.T("HUD_INPUT")} : {input.DeviceName}" : Ui.T("HUD_KEYBOARD");
        _input.Text = session.WindEnabled ? source : $"{source}   —   {Ui.T("WIND_OFF")}";
        _banner.Text = session.Paused ? Ui.T("HUD_PAUSED") : "";
    }
}
```

`game/Scripts/Flight/CrashOverlay.cs`:
```csharp
using Godot;
using Symlab.App.Ui;
using Symlab.Flight.Ground;

namespace Symlab.Game.Flight;

public partial class CrashOverlay : CanvasLayer
{
    PanelContainer _panel = null!;
    Label _cause = null!;

    public override void _Ready()
    {
        _panel = new PanelContainer { Position = new Vector2(550, 330), CustomMinimumSize = new Vector2(500, 200) };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        column.AddChild(Ui.Text(Ui.T("CRASH_TITLE"), 40));
        _cause = Ui.Text("", 24);
        column.AddChild(_cause);
        column.AddChild(Ui.Text(Ui.T("CRASH_HINT"), 18));
        _panel.AddChild(column);
        _panel.Visible = false;
        AddChild(_panel);
    }

    public void UpdateCrash(CrashCause cause)
    {
        _panel.Visible = cause != CrashCause.None;
        if (_panel.Visible) _cause.Text = Ui.T(FlightDataFormatter.CrashKey(cause));
    }
}
```

`game/Scripts/Flight/FlightScene.cs`:
```csharp
using Godot;
using Symlab.App.Cameras;
using Symlab.App.Field;
using Symlab.App.Session;
using Symlab.App.Visual;
using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Recording;
using Symlab.Game.Radio;
using Symlab.Game.World;

namespace Symlab.Game.Flight;

/// <summary>One flight: polls input, advances the session, draws the aircraft from the pilot's eyes.</summary>
public partial class FlightScene : Node3D
{
    Services _services = null!;
    FlightSession _session = null!;
    AircraftVisual _visual = null!;
    Camera3D _camera = null!;
    LineOfSightRig _rig = null!;
    WindsockNode _windsock = null!;
    FlightHud _hud = null!;
    CrashOverlay _crash = null!;
    System.Action _exit = null!;
    System.Func<double, ControlInputs>? _script;
    readonly LatencyMeter _latency = new();

    public FlightSession Session => _session;
    public LatencyMeter Latency => _latency;
    public RouterOutput LastInput { get; private set; }
    public int LastSteps { get; private set; }

    public void Init(Services services, string aircraftId, System.Action exit, System.Func<double, ControlInputs>? script = null)
    {
        _services = services;
        _exit = exit;
        _script = script;
        var definition = AircraftLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId));
        _session = new FlightSession(definition, services.Settings.Conditions);
        if (services.Settings.RecordFlights && script is null) StartRecording(aircraftId, definition);

        _windsock = FieldBuilder.Build(this, _session.Terrain, services.Settings.Conditions);
        _visual = new AircraftVisual();
        AddChild(_visual);
        _visual.Build(AircraftMeshBuilder.Build(definition, _session.Aircraft.Aero.Segments));

        var pilot = ClubField.PilotPosition;
        var eye = new Vec3(pilot.X, _session.Terrain.Height(pilot.X, pilot.Z) + ClubField.EyeHeight, pilot.Z);
        _rig = new LineOfSightRig(eye, services.Settings.FovDeg, services.Settings.AutoZoom);
        _rig.Reset(Context());
        _camera = new Camera3D { Current = true, Near = 0.1f, Far = 4000f, Fov = (float)services.Settings.FovDeg };
        AddChild(_camera);
        _hud = new FlightHud();
        AddChild(_hud);
        _crash = new CrashOverlay();
        AddChild(_crash);
    }

    public override void _Process(double delta)
    {
        ulong start = Time.GetTicksUsec();
        LastInput = _script is null
            ? _services.Router.Update(delta, JoypadReader.Poll(), KeyboardInput.Keys(), KeyboardInput.Commands())
            : new RouterOutput(_script(_session.Simulation.Time), [], InputSource.Keyboard, "script");
        foreach (var action in LastInput.Actions) _session.Handle(action);
        LastSteps = _session.Tick(delta, LastInput.Controls);
        _latency.Add((Time.GetTicksUsec() - start) / 1e6, delta, _session.Simulation.InterpolationAlpha);

        _visual.UpdateFrom(_session.Aircraft, _session.DisplayState);
        var pose = _rig.Update(delta, Context());
        var from = pose.Position.ToGodot();
        var to = pose.LookAt.ToGodot();
        var up = Mathf.Abs((to - from).Normalized().Y) > 0.999f ? Vector3.Back : Vector3.Up;
        _camera.Fov = (float)pose.VerticalFovDeg;
        _camera.LookAtFromPosition(from, to, up);
        _windsock.Apply(Windsock.Pose(_session.Simulation.Environment.Wind.At(6)));
        _hud.UpdateHud(_session, LastInput, _services.Settings.ShowFlightData);
        _crash.UpdateCrash(_session.Aircraft.Crash);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) _exit();
    }

    public override void _ExitTree() => _session.Dispose();

    CameraContext Context()
    {
        var display = _session.DisplayState;
        return new CameraContext(display.Position, display.Orientation, _session.Span);
    }

    void StartRecording(string aircraftId, AircraftDefinition definition)
    {
        System.IO.Directory.CreateDirectory(AppPaths.RecordingsDir);
        var path = System.IO.Path.Combine(AppPaths.RecordingsDir, $"{System.DateTime.Now:yyyyMMdd-HHmmss}-{aircraftId}.csv");
        _session.AttachRecorder(new FlightRecorder(new System.IO.StreamWriter(path), definition));
    }
}
```

Modify `game/Scripts/Main.cs`: add the fields and methods below, and in `_Ready`, before the final `ShowRadio();`, add the two command-line modes.

Fields and methods:
```csharp
    double _smokeSeconds = -1;
    string? _smokeScreenshot;
    string _smokeAircraft = "";

    public void StartFlight(string aircraftId, System.Func<double, Symlab.Flight.Controls.ControlInputs>? script = null)
    {
        var scene = new Flight.FlightScene();
        scene.Init(_services, aircraftId, ShowRadio, script);
        Switch(scene);
    }

    public override void _Process(double delta)
    {
        if (_smokeSeconds < 0 || _current is not Flight.FlightScene flight) return;
        var session = flight.Session;
        bool crashed = session.Aircraft.Crash != Symlab.Flight.Ground.CrashCause.None;
        if (session.Simulation.Time < _smokeSeconds && !crashed) return;
        _smokeSeconds = -1;
        if (_smokeScreenshot is { } path)
        {
            CaptureAfterFrames(2, path);
            return;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var p = session.Aircraft.State.Position;
        GD.Print($"SYMLAB_SMOKE_OK aircraft={_smokeAircraft} t={session.Simulation.Time.ToString("0.00", inv)} x={p.X.ToString("0.0", inv)} y={p.Y.ToString("0.00", inv)} z={p.Z.ToString("0.0", inv)} crash={session.Aircraft.Crash}");
        GetTree().Quit(0);
    }
```

Command-line modes (in `_Ready`):
```csharp
        int smokeIndex = System.Array.IndexOf(args, "--smoke-flight");
        if (smokeIndex >= 0 && smokeIndex + 2 < args.Length)
        {
            _smokeAircraft = args[smokeIndex + 1];
            _smokeSeconds = double.Parse(args[smokeIndex + 2], System.Globalization.CultureInfo.InvariantCulture);
            StartFlight(_smokeAircraft, _ => new Symlab.Flight.Controls.ControlInputs(0.7, 0, 0, 0));
            return;
        }
        int shotIndex = System.Array.IndexOf(args, "--screenshot-flight");
        if (shotIndex >= 0 && shotIndex + 3 < args.Length)
        {
            _smokeAircraft = args[shotIndex + 1];
            _smokeSeconds = double.Parse(args[shotIndex + 2], System.Globalization.CultureInfo.InvariantCulture);
            _smokeScreenshot = args[shotIndex + 3];
            StartFlight(_smokeAircraft, t => new Symlab.Flight.Controls.ControlInputs(1, 0, t > 3.5 && t < 5 ? 0.25 : 0.05, 0));
            return;
        }
```

- [ ] **Step 2: Build and run the smoke flights**

```bash
dotnet build game/Symlab.Game.csproj
for id in trainer sport wing; do "$GODOT" --headless --path game -- --smoke-flight $id 5; echo "$id exit=$?"; done
```
Expected: three `SYMLAB_SMOKE_OK` lines, exit 0. Trainer and sport: `t=5.00`, moved along the runway (x changed by more than 5 m), `crash=None`. Wing: hand launch with throttle 0.7, `crash=None` or a belly landing without crash.

- [ ] **Step 3: Windowed screenshots from the pilot's eyes**

```bash
"$GODOT" --path game -- --screenshot-flight trainer 9 /tmp/symlab-flight-trainer.png; echo "exit=$?"
"$GODOT" --path game -- --screenshot-flight wing 5 /tmp/symlab-flight-wing.png; echo "exit=$?"
```
Expected: exit 0. Open both PNGs and confirm in the report: the view is from the pilot box, the aircraft is visible (small, in the sky, near the image center thanks to head tracking), the field and sky look as in Task 11, the keyboard help line is at the bottom.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(game): add the flight scene with radio/keyboard input, line-of-sight camera, HUD and crash screen

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Main menu, settings screen and final navigation

**Files:**
- Create: `game/Scripts/Menu/MainMenu.cs`, `game/Scripts/Menu/SettingsScreen.cs`, `game/Scripts/DisplaySettings.cs`
- Modify: `game/Scripts/Main.cs` (full replacement below — consolidates every mode from Tasks 9–13)

**Interfaces:**
- Consumes: `AircraftCatalog`, `AircraftEntry`, `FlightConditions`, `AppSettings`, `LineOfSightRig.FovForScreen`, `Ui`, `Services`, `RadioScreen`, `FlightScene`, `FieldBuilder`, `AircraftVisual`, `FlightSession`.
- Produces:
  - `MainMenu : Control` with `Init(Services services, Action<string> fly, Action radio, Action settings, Action quit)` — aircraft picker (remembers `LastAircraft`), description, field-condition sliders (wind 0–12 m/s, direction 0–355°, turbulence 0–1.5, sun azimuth 0–355°, sun elevation 5–85°), catalog errors, buttons Fly / Radio / Settings / Quit. Every change is saved.
  - `SettingsScreen : Control` with `Init(Services services, Action back, Action reopen)` — FOV slider (20–90°), screen height and viewing distance with "compute from screen", auto-zoom, flight data, recording, vsync, language (fr/en, applied immediately by reopening the screen).
  - `DisplaySettings.Apply(AppSettings settings)` (vsync).
  - `Main`: default screen is the main menu; Esc in flight returns to the menu; all command-line modes from Tasks 9–13 still work.

- [ ] **Step 1: Write the scripts**

`game/Scripts/DisplaySettings.cs`:
```csharp
using Godot;
using Symlab.App.Settings;

namespace Symlab.Game;

public static class DisplaySettings
{
    public static void Apply(AppSettings settings) =>
        DisplayServer.WindowSetVsyncMode(settings.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
}
```

`game/Scripts/Menu/MainMenu.cs`:
```csharp
using Godot;
using Symlab.App.Session;
using Symlab.App.Settings;

namespace Symlab.Game.Menu;

public partial class MainMenu : Control
{
    public void Init(Services services, System.Action<string> fly, System.Action radio, System.Action settings, System.Action quit)
    {
        var column = Ui.Screen(this, Ui.T("APP_TITLE"));
        var aircraft = AircraftCatalog.List(AppPaths.AircraftRoot, out var errors);

        var picker = new OptionButton();
        int selected = 0;
        for (int i = 0; i < aircraft.Count; i++)
        {
            picker.AddItem(aircraft[i].Name, i);
            if (aircraft[i].Id == services.Settings.LastAircraft) selected = i;
        }
        var description = Ui.Text("", 16);
        void Describe(long index) => description.Text = index >= 0 && index < aircraft.Count ? aircraft[(int)index].Description : "";
        if (aircraft.Count > 0)
        {
            picker.Selected = selected;
            Describe(selected);
        }
        picker.ItemSelected += Describe;
        column.AddChild(Ui.Row(Ui.Text(Ui.T("MENU_AIRCRAFT")), picker));
        column.AddChild(description);

        column.AddChild(Ui.Text(Ui.T("MENU_CONDITIONS"), 24));
        void Change(System.Func<FlightConditions, FlightConditions> update)
        {
            services.Settings = services.Settings with { Conditions = update(services.Settings.Conditions) };
            services.SaveSettings();
        }
        var c = services.Settings.Conditions;
        column.AddChild(Ui.Slider(Ui.T("COND_WIND_SPEED"), 0, 12, 0.5, c.WindSpeed, v => Change(x => x with { WindSpeed = v }), "0.0"));
        column.AddChild(Ui.Slider(Ui.T("COND_WIND_DIR"), 0, 355, 5, c.WindFromDeg, v => Change(x => x with { WindFromDeg = v }), "0"));
        column.AddChild(Ui.Slider(Ui.T("COND_TURBULENCE"), 0, 1.5, 0.1, c.Turbulence, v => Change(x => x with { Turbulence = v }), "0.0"));
        column.AddChild(Ui.Slider(Ui.T("COND_SUN_AZIMUTH"), 0, 355, 5, c.SunAzimuthDeg, v => Change(x => x with { SunAzimuthDeg = v }), "0"));
        column.AddChild(Ui.Slider(Ui.T("COND_SUN_ELEVATION"), 5, 85, 5, c.SunElevationDeg, v => Change(x => x with { SunElevationDeg = v }), "0"));

        foreach (var error in errors) column.AddChild(Ui.Text(error, 14));

        column.AddChild(Ui.Row(
            Ui.Button(Ui.T("MENU_FLY"), () =>
            {
                if (aircraft.Count == 0) return;
                var id = aircraft[picker.Selected].Id;
                services.Settings = services.Settings with { LastAircraft = id };
                services.SaveSettings();
                fly(id);
            }),
            Ui.Button(Ui.T("MENU_RADIO"), radio),
            Ui.Button(Ui.T("MENU_SETTINGS"), settings),
            Ui.Button(Ui.T("MENU_QUIT"), quit)));
    }
}
```

`game/Scripts/Menu/SettingsScreen.cs`:
```csharp
using Godot;
using Symlab.App.Cameras;
using Symlab.App.Settings;

namespace Symlab.Game.Menu;

public partial class SettingsScreen : Control
{
    public void Init(Services services, System.Action back, System.Action reopen)
    {
        var column = Ui.Screen(this, Ui.T("SET_TITLE"));
        void Change(System.Func<AppSettings, AppSettings> update)
        {
            services.Settings = update(services.Settings).Sanitized();
            services.SaveSettings();
        }
        var s = services.Settings;

        var fovRow = Ui.Slider(Ui.T("SET_FOV"), 20, 90, 1, s.FovDeg, v => Change(x => x with { FovDeg = v }), "0");
        column.AddChild(fovRow);
        double screenCm = 30, distanceCm = 60;
        column.AddChild(Ui.Slider(Ui.T("SET_SCREEN_HEIGHT"), 10, 150, 1, screenCm, v => screenCm = v, "0"));
        column.AddChild(Ui.Slider(Ui.T("SET_VIEW_DISTANCE"), 30, 400, 5, distanceCm, v => distanceCm = v, "0"));
        column.AddChild(Ui.Button(Ui.T("SET_FOV_FROM_SCREEN"), () =>
        {
            double fov = LineOfSightRig.FovForScreen(screenCm, distanceCm);
            Change(x => x with { FovDeg = fov });
            fovRow.GetChild<HSlider>(1).Value = services.Settings.FovDeg;
        }));

        column.AddChild(Ui.Check(Ui.T("SET_AUTOZOOM"), s.AutoZoom, v => Change(x => x with { AutoZoom = v })));
        column.AddChild(Ui.Check(Ui.T("SET_FLIGHT_DATA"), s.ShowFlightData, v => Change(x => x with { ShowFlightData = v })));
        column.AddChild(Ui.Check(Ui.T("SET_RECORD"), s.RecordFlights, v => Change(x => x with { RecordFlights = v })));
        column.AddChild(Ui.Check(Ui.T("SET_VSYNC"), s.VSync, v =>
        {
            Change(x => x with { VSync = v });
            DisplaySettings.Apply(services.Settings);
        }));

        var language = new OptionButton();
        language.AddItem("Français", 0);
        language.AddItem("English", 1);
        language.Selected = s.Language == "en" ? 1 : 0;
        language.ItemSelected += index =>
        {
            Change(x => x with { Language = index == 1 ? "en" : "fr" });
            TranslationServer.SetLocale(services.Settings.Language);
            reopen();
        };
        column.AddChild(Ui.Row(Ui.Text(Ui.T("SET_LANGUAGE")), language));
        column.AddChild(Ui.Button(Ui.T("BACK"), back));
    }
}
```

`game/Scripts/Main.cs` (replace the whole file):
```csharp
using System.Globalization;
using Godot;
using Symlab.App.Field;
using Symlab.App.Session;
using Symlab.App.Settings;
using Symlab.App.Visual;
using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Game.Flight;
using Symlab.Game.Menu;
using Symlab.Game.Radio;
using Symlab.Game.World;

namespace Symlab.Game;

public partial class Main : Node
{
    Services _services = null!;
    Node? _current;
    double _smokeSeconds = -1;
    string? _smokeScreenshot;
    string _smokeAircraft = "";

    public override void _Ready()
    {
        var settings = AppSettings.Load(AppPaths.SettingsFile);
        Translations.Register(AppPaths.TranslationsCsv, settings.Language);
        var store = new RadioProfileStore(AppPaths.RadioDir);
        _services = new Services { Settings = settings, Radios = store, Router = new InputRouter(guid => store.Load(guid, out _)) };
        DisplaySettings.Apply(settings);
        if (!RunCommandLine(OS.GetCmdlineUserArgs())) ShowMenu();
    }

    public void ShowMenu()
    {
        var menu = new MainMenu();
        menu.Init(_services, id => StartFlight(id), ShowRadio, ShowSettings, () => GetTree().Quit());
        Switch(menu);
    }

    public void ShowRadio()
    {
        var screen = new RadioScreen();
        screen.Init(_services, ShowMenu);
        Switch(screen);
    }

    public void ShowSettings()
    {
        var screen = new SettingsScreen();
        screen.Init(_services, ShowMenu, ShowSettings);
        Switch(screen);
    }

    public void StartFlight(string aircraftId, System.Func<double, ControlInputs>? script = null)
    {
        var scene = new FlightScene();
        scene.Init(_services, aircraftId, ShowMenu, script);
        Switch(scene);
    }

    public override void _Process(double delta)
    {
        if (_smokeSeconds < 0 || _current is not FlightScene flight) return;
        var session = flight.Session;
        bool crashed = session.Aircraft.Crash != CrashCause.None;
        if (session.Simulation.Time < _smokeSeconds && !crashed) return;
        _smokeSeconds = -1;
        if (_smokeScreenshot is { } path)
        {
            CaptureAfterFrames(2, path);
            return;
        }
        var inv = CultureInfo.InvariantCulture;
        var p = session.Aircraft.State.Position;
        GD.Print($"SYMLAB_SMOKE_OK aircraft={_smokeAircraft} t={session.Simulation.Time.ToString("0.00", inv)} x={p.X.ToString("0.0", inv)} y={p.Y.ToString("0.00", inv)} z={p.Z.ToString("0.0", inv)} crash={session.Aircraft.Crash}");
        GetTree().Quit(0);
    }

    bool RunCommandLine(string[] args)
    {
        if (Has(args, "--smoke-boot"))
        {
            GD.Print($"SYMLAB_BOOT_OK locale={TranslationServer.GetLocale()} title={Tr("APP_TITLE")}");
            GetTree().Quit(0);
            return true;
        }
        if (Has(args, "--smoke-radio"))
        {
            var pads = JoypadReader.Poll();
            GD.Print($"SYMLAB_RADIO_OK joypads={pads.Count}");
            foreach (var pad in pads)
                GD.Print($"JOYPAD guid={pad.Guid} name={pad.Name} axes={string.Join(";", pad.Frame.Axes.Select(a => a.ToString("0.00", CultureInfo.InvariantCulture)))}");
            GetTree().Quit(0);
            return true;
        }
        if (ArgValue(args, "--screen") == "radio")
        {
            ShowRadio();
            return true;
        }
        if (ArgValue(args, "--screenshot-field") is { } fieldShot)
        {
            PreviewField(fieldShot);
            return true;
        }
        int aircraftShot = System.Array.IndexOf(args, "--screenshot-aircraft");
        if (aircraftShot >= 0 && aircraftShot + 2 < args.Length)
        {
            PreviewAircraft(args[aircraftShot + 1], args[aircraftShot + 2]);
            return true;
        }
        int smoke = System.Array.IndexOf(args, "--smoke-flight");
        if (smoke >= 0 && smoke + 2 < args.Length)
        {
            _smokeAircraft = args[smoke + 1];
            _smokeSeconds = double.Parse(args[smoke + 2], CultureInfo.InvariantCulture);
            StartFlight(_smokeAircraft, _ => new ControlInputs(0.7, 0, 0, 0));
            return true;
        }
        int flightShot = System.Array.IndexOf(args, "--screenshot-flight");
        if (flightShot >= 0 && flightShot + 3 < args.Length)
        {
            _smokeAircraft = args[flightShot + 1];
            _smokeSeconds = double.Parse(args[flightShot + 2], CultureInfo.InvariantCulture);
            _smokeScreenshot = args[flightShot + 3];
            StartFlight(_smokeAircraft, t => new ControlInputs(1, 0, t > 3.5 && t < 5 ? 0.25 : 0.05, 0));
            return true;
        }
        return false;
    }

    void PreviewField(string path)
    {
        var preview = new Node3D();
        Switch(preview);
        var terrain = new ClubFieldTerrain(TreePlanter.Plant(FlightSession.TreeSeed));
        FieldBuilder.Build(preview, terrain, _services.Settings.Conditions);
        var eye = ClubField.PilotPosition.ToGodot() + new Vector3(0, (float)ClubField.EyeHeight, 0);
        var camera = new Camera3D { Current = true, Fov = (float)_services.Settings.FovDeg, Far = 4000f };
        preview.AddChild(camera);
        camera.LookAtFromPosition(eye, new Vector3(-30, 8, 0), Vector3.Up);
        CaptureAfterFrames(20, path);
    }

    void PreviewAircraft(string aircraftId, string path)
    {
        var definition = AircraftLoader.Load(System.IO.Path.Combine(AppPaths.AircraftRoot, aircraftId));
        var session = new FlightSession(definition, _services.Settings.Conditions);
        var preview = new Node3D();
        Switch(preview);
        FieldBuilder.Build(preview, session.Terrain, _services.Settings.Conditions);
        var visual = new AircraftVisual();
        preview.AddChild(visual);
        visual.Build(AircraftMeshBuilder.Build(definition, session.Aircraft.Aero.Segments));
        var start = session.StartState();
        visual.UpdateFrom(session.Aircraft, start);
        visual.SetAllDeflections(0.35);
        var p = start.Position.ToGodot();
        var forward = start.Orientation.Rotate(Vec3.UnitX).ToGodot();
        var left = -start.Orientation.Rotate(Vec3.UnitZ).ToGodot();
        var camera = new Camera3D { Current = true, Fov = 50f, Near = 0.05f, Far = 4000f };
        preview.AddChild(camera);
        camera.LookAtFromPosition(p + forward * 2.5f + left * 3f + Vector3.Up * 1.2f, p, Vector3.Up);
        CaptureAfterFrames(20, path);
    }

    async void CaptureAfterFrames(int frames, string path)
    {
        for (int i = 0; i < frames; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"SYMLAB_SCREENSHOT path={path} error={error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    void Switch(Node next)
    {
        _current?.QueueFree();
        _current = next;
        AddChild(next);
    }

    static bool Has(string[] args, string flag) => System.Array.IndexOf(args, flag) >= 0;

    static string? ArgValue(string[] args, string flag)
    {
        int i = System.Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
```

- [ ] **Step 2: Build and re-run every smoke mode**

```bash
dotnet build game/Symlab.Game.csproj
"$GODOT" --headless --path game -- --smoke-boot; echo "exit=$?"
"$GODOT" --headless --path game -- --smoke-radio; echo "exit=$?"
"$GODOT" --headless --path game -- --smoke-flight trainer 5; echo "exit=$?"
"$GODOT" --path game -- --screenshot-aircraft sport /tmp/symlab-sport.png; echo "exit=$?"
```
Expected: all exit 0 with their marker lines.

- [ ] **Step 3: Screenshot the menus**

Run the app windowed, wait for the menu, capture it: add nothing to the code — use macOS screencapture:
```bash
"$GODOT" --path game & sleep 6; screencapture -x /tmp/symlab-menu.png; kill %1
```
Open `/tmp/symlab-menu.png` and confirm in the report: French title, aircraft picker showing "Trainer 1.5 m", condition sliders, four buttons. (If `screencapture` is not permitted in this environment, say so in the report instead.)

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(game): add main menu, settings screen and final navigation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Diagnostics overlay, documentation and acceptance checklist

**Files:**
- Create: `game/Scripts/Flight/DiagnosticsOverlay.cs`, `docs/manual-acceptance.md`
- Modify: `game/Scripts/Flight/FlightScene.cs` (create and update the overlay), `README.md`, `docs/dev-setup.md`

**Interfaces:**
- Consumes: `FlightScene` (`Session`, `Latency`, `LastInput`, `LastSteps`), `Ui`.
- Produces: `DiagnosticsOverlay : CanvasLayer` toggled with F3, showing FPS, estimated latency (ms), input source/device, physics steps in the last frame, simulation time; `UpdateDiagnostics(FlightScene scene)`.

- [ ] **Step 1: Write the overlay and wire it**

`game/Scripts/Flight/DiagnosticsOverlay.cs`:
```csharp
using System.Globalization;
using Godot;
using Symlab.App.Session;

namespace Symlab.Game.Flight;

/// <summary>F3: frame rate, estimated input-to-display latency, input source and physics steps.</summary>
public partial class DiagnosticsOverlay : CanvasLayer
{
    Label _label = null!;

    public override void _Ready()
    {
        _label = Ui.Text("", 14);
        _label.Position = new Vector2(1240, 20);
        _label.Visible = false;
        AddChild(_label);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F3 }) _label.Visible = !_label.Visible;
    }

    public void UpdateDiagnostics(FlightScene scene)
    {
        if (!_label.Visible) return;
        var inv = CultureInfo.InvariantCulture;
        string source = scene.LastInput.Source == InputSource.Radio ? scene.LastInput.DeviceName : "keyboard";
        _label.Text =
            $"{Ui.T("HUD_FPS")} : {Engine.GetFramesPerSecond().ToString("0", inv)}\n" +
            $"{Ui.T("HUD_LATENCY")} : {scene.Latency.AverageMs.ToString("0.0", inv)} ms\n" +
            $"{Ui.T("HUD_INPUT")} : {source}\n" +
            $"physics steps/frame : {scene.LastSteps}\n" +
            $"t = {scene.Session.Simulation.Time.ToString("0.0", inv)} s";
    }
}
```

In `FlightScene`: add a field `DiagnosticsOverlay _diagnostics = null!;`; in `Init`, after the crash overlay:
```csharp
        _diagnostics = new DiagnosticsOverlay();
        AddChild(_diagnostics);
```
and at the end of `_Process`:
```csharp
        _diagnostics.UpdateDiagnostics(this);
```

- [ ] **Step 2: Write the acceptance checklist**

`docs/manual-acceptance.md`:
```markdown
# Manual acceptance — sub-project 1

Run by the pilot with the real radio. Tick each line; note anything that feels wrong in `docs/realism-backlog.md`.

## Setup
- [ ] TX16S in USB Joystick (HID) mode, a "Simulator" model with no mixes, channels 1–4 = sticks.
- [ ] `"$GODOT" --headless --path game -- --smoke-radio` lists the radio and its axes change with the sticks.

## Radio screen
- [ ] The radio appears in the device list; the 10 raw bars move with the sticks and switches.
- [ ] Calibration: the four steps complete; the status turns green ("Calibrée — prête à voler").
- [ ] Mode 1/Mode 2 changes the instructions (which stick to move).
- [ ] A switch assigned to "reset" resets the aircraft in flight; "pause" and "vent" work too.

## Flight (for each of trainer, sport, wing)
- [ ] Right aileron rolls right, pulling the elevator stick pitches up, right rudder yaws right, throttle up accelerates.
- [ ] Control surfaces visibly move in the right direction on the model.
- [ ] Take off (hand launch for the wing), fly a circuit, land; crash screen shows a cause after a crash; R/switch resets.
- [ ] Wind 5 m/s + turbulence 1: the windsock points downwind and the aircraft is buffeted; V toggles the wind.
- [ ] F3: estimated latency below 20 ms; with vsync off, frame rate at or above 120 fps on this Mac.

## Settings
- [ ] FOV "compute from screen" gives a plausible value; auto-zoom off shows the true apparent size.
- [ ] Language switch fr ↔ en works immediately.
- [ ] A CSV appears in the recordings folder after a flight (Settings → record flights).
```

- [ ] **Step 3: Update the docs**

In `README.md`, replace the "Next:" paragraph of the Status section with:
```markdown
- `src/Symlab.App` — testable game logic: club field and terrain, line-of-sight camera, procedural aircraft
  meshes, input routing, flight session, settings, translations.
- `game/` — Godot 4 (.NET) simulator: radio setup and calibration, club field, line-of-sight flying, HUD,
  crash screen, French/English menus. See `docs/dev-setup.md` to build and run, and
  `docs/manual-acceptance.md` for the pilot's checklist.

Next: VSPAERO/CFD import and telemetry replay (sub-project 2), FPV and chase cameras (sub-project 3),
VTOL/drones (sub-project 4).

## Keyboard (without a radio)

W/S (Z/S on AZERTY) throttle · arrows aileron/elevator (↓ = pull) · A/D (Q/D on AZERTY) rudder ·
R reset · P pause · V wind on/off · F3 diagnostics · Esc menu.
```

Append to `docs/dev-setup.md` the full list of command-line modes:
````markdown
## Command-line modes (after `--`)

| Flag | What it does |
|------|--------------|
| `--smoke-boot` | Loads settings and translations, prints `SYMLAB_BOOT_OK`, exits |
| `--smoke-radio` | Lists joypads with their raw axes, exits |
| `--screen radio` | Opens the radio screen directly |
| `--smoke-flight <id> <s>` | Headless scripted flight, prints `SYMLAB_SMOKE_OK ...` |
| `--screenshot-field <png>` | Pilot's view of the empty field |
| `--screenshot-aircraft <id> <png>` | Close-up of an aircraft with deflected controls |
| `--screenshot-flight <id> <s> <png>` | Scripted takeoff seen from the pilot box |
````

- [ ] **Step 4: Final verification**

```bash
dotnet test
dotnet build game/Symlab.Game.csproj
for id in trainer sport wing; do "$GODOT" --headless --path game -- --smoke-flight $id 5; done
"$GODOT" --path game -- --screenshot-flight sport 8 /tmp/symlab-final.png; echo "exit=$?"
```
Expected: all tests pass, 0 warnings, three `SYMLAB_SMOKE_OK` lines, screenshot exit 0 (open it and describe it in the report).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(game): add diagnostics overlay, docs and manual acceptance checklist

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Controller hands over to the pilot**

The controller asks the user to run `docs/manual-acceptance.md` with the TX16S and reports the results; findings go to `docs/realism-backlog.md` or become fixes.
