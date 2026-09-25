# Camera Views Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an FPV (first person) and a chase (third person) camera to the flight scene, next to the existing
ground line-of-sight view, switchable with the C key, a radio switch, and later a HUD button.

**Architecture:** Pure C# camera rigs in `SimLab.App.Cameras` behind the existing `ICameraRig`, a `CameraDirector`
that owns the three rigs and cycles them, an optional `fpvCamera` block in `aircraft.json` parsed by the flight
library's loader, and Godot wiring in `FlightScene` (camera up vector, cull mask to hide the aircraft in FPV, reset
detection, remembered view).

**Tech Stack:** .NET 10, C#, xUnit, Godot 4.7 .NET.

**Spec:** `docs/superpowers/specs/2026-09-25-camera-views-design.md`

## Global Constraints

- Code, comments, docs and commit messages in English; translations in `game/translations/strings.csv` have `fr` and `en`.
- Body axes: x back, y right, z up (`BodyAxes.Forward` = (−1, 0, 0)); world axes ENU (x east, y north, z up).
  Quaternions (`RigidBodyState.Orientation`) rotate body → world.
- `TreatWarningsAsErrors` is on: no unused usings, variables or nullable warnings.
- Never regenerate `tests/SimLab.Flight.Tests/Behavior/FrameInvarianceGoldenTests.cs` golden data.
- If `dotnet` is not found: `export PATH="/usr/local/share/dotnet:$PATH"`.
- Godot binary: `/Applications/Godot_mono.app/Contents/MacOS/Godot` (below `$GODOT`).
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Work on branch `feat/camera-views`.

---

### Task 1: `fpvCamera` block in aircraft.json

**Files:**
- Create: `src/SimLab.Flight/Airframe/FpvCameraSpec.cs`
- Modify: `src/SimLab.Flight/Airframe/AircraftDefinition.cs` (new last record parameter)
- Modify: `src/SimLab.Flight/Airframe/LoaderDtos.cs` (new DTO)
- Modify: `src/SimLab.Flight/Airframe/AircraftLoader.cs` (validate, shift by cg, pass on)
- Modify: `aircraft/wing/aircraft.json`, `aircraft/README.md`
- Test: `tests/SimLab.Flight.Tests/Airframe/AircraftLoaderTests.cs`

**Interfaces:**
- Produces: `public sealed record FpvCameraSpec(Vec3 Position, double UptiltDeg, double HorizontalFovDeg)` with
  `static FpvCameraSpec For(AircraftDefinition definition)`, and `AircraftDefinition.FpvCamera` (`FpvCameraSpec?`,
  last positional parameter, default `null`). `Position` is from the CG, in body axes.

- [ ] **Step 1: Write the failing tests** (append to `AircraftLoaderTests`)

```csharp
    [Fact]
    public void Fpv_camera_block_is_shifted_by_the_cg()
    {
        var json = Edit(Aircraft(), "\"cg\": [0, 0, 0],", """
            "cg": [0.1, 0, 0], "fpvCamera": { "position": [-0.2, 0, 0.05], "uptiltDeg": 25, "fovDeg": 120 },
            """);
        var mount = AircraftLoader.Load(Write(json)).FpvCamera;
        Assert.NotNull(mount);
        Assert.Equal(-0.3, mount!.Position.X, 12);
        Assert.Equal(0.05, mount.Position.Z, 12);
        Assert.Equal(25, mount.UptiltDeg);
        Assert.Equal(120, mount.HorizontalFovDeg);
    }

    [Fact]
    public void Without_a_block_the_fpv_camera_sits_on_the_nose_hull_point()
    {
        var def = AircraftLoader.Load(Write(Aircraft()));
        Assert.Null(def.FpvCamera);
        var mount = FpvCameraSpec.For(def);
        Assert.Equal(new Vec3(-0.3, 0, 0), mount.Position);
        Assert.Equal(FpvCameraSpec.DefaultUptiltDeg, mount.UptiltDeg);
        Assert.Equal(FpvCameraSpec.DefaultHorizontalFovDeg, mount.HorizontalFovDeg);
    }

    [Fact]
    public void Without_a_nose_tag_the_fpv_camera_sits_on_the_most_forward_hull_point()
    {
        var json = Edit(Aircraft(), """
            "hull": [ { "name": "nose", "position": [-0.3, 0, 0], "tag": "nose" } ],
            """, """
            "hull": [ { "name": "a", "position": [0.2, 0, 0], "tag": "belly" }, { "name": "b", "position": [-0.25, 0, 0.1], "tag": "canopy" } ],
            """);
        Assert.Equal(new Vec3(-0.25, 0, 0.1), FpvCameraSpec.For(AircraftLoader.Load(Write(json))).Position);
    }

    [Theory]
    [InlineData(-1, 110)]
    [InlineData(61, 110)]
    [InlineData(20, 59)]
    [InlineData(20, 151)]
    public void Out_of_range_fpv_camera_is_rejected(double uptilt, double fov)
        => AssertRejected(Edit(Aircraft(), "\"cg\": [0, 0, 0],",
            $$"""
            "cg": [0, 0, 0], "fpvCamera": { "position": [0, 0, 0], "uptiltDeg": {{uptilt}}, "fovDeg": {{fov}} },
            """));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~AircraftLoaderTests"`
Expected: build error, `FpvCameraSpec` / `FpvCamera` not defined.

- [ ] **Step 3: Implement**

`src/SimLab.Flight/Airframe/FpvCameraSpec.cs`:

```csharp
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Airframe;

/// <summary>
/// Onboard FPV camera: mount position from the CG in body axes, uptilt above the body forward axis, and horizontal
/// field of view (the figure FPV camera specs give).
/// </summary>
public sealed record FpvCameraSpec(Vec3 Position, double UptiltDeg, double HorizontalFovDeg)
{
    public const double DefaultUptiltDeg = 10;
    public const double DefaultHorizontalFovDeg = 110;
    public const double MinUptiltDeg = 0, MaxUptiltDeg = 60;
    public const double MinFovDeg = 60, MaxFovDeg = 150;

    /// <summary>The aircraft's own mount, else the default one on the hull point tagged <c>nose</c> (else the most
    /// forward hull point, else the CG).</summary>
    public static FpvCameraSpec For(AircraftDefinition definition)
    {
        if (definition.FpvCamera is { } mount) return mount;
        var nose = definition.Hull.FirstOrDefault(h => h.Tag == "nose")
            ?? definition.Hull.OrderBy(h => h.Position.X).FirstOrDefault();
        return new FpvCameraSpec(nose?.Position ?? Vec3.Zero, DefaultUptiltDeg, DefaultHorizontalFovDeg);
    }
}
```

`AircraftDefinition.cs`: add a `<param name="FpvCamera">` doc line ("Onboard FPV camera from aircraft.json, or null
for the default mount: see <see cref="FpvCameraSpec.For"/>.") and a last parameter after `Provenance`:

```csharp
    IReadOnlyDictionary<string, string> Provenance,
    FpvCameraSpec? FpvCamera = null);
```

`LoaderDtos.cs`: in `AircraftDto` add `public FpvCameraDto? FpvCamera { get; set; }` (after `Crash`), and at the end
of the file:

```csharp
internal sealed class FpvCameraDto
{
    public Vec3 Position { get; set; }
    public double UptiltDeg { get; set; } = FpvCameraSpec.DefaultUptiltDeg;
    public double FovDeg { get; set; } = FpvCameraSpec.DefaultHorizontalFovDeg;
}
```

`AircraftLoader.cs`: next to the wheel checks (before the `try` that builds the aero model), add:

```csharp
        if (dto.FpvCamera is { } fpv
            && (fpv.UptiltDeg is < FpvCameraSpec.MinUptiltDeg or > FpvCameraSpec.MaxUptiltDeg
                || fpv.FovDeg is < FpvCameraSpec.MinFovDeg or > FpvCameraSpec.MaxFovDeg))
            throw Invalid(path, $"fpvCamera uptiltDeg must be {FpvCameraSpec.MinUptiltDeg}–{FpvCameraSpec.MaxUptiltDeg} and fovDeg {FpvCameraSpec.MinFovDeg}–{FpvCameraSpec.MaxFovDeg}.");
```

and pass the last argument of `new AircraftDefinition(...)`:

```csharp
            dto.Provenance,
            dto.FpvCamera is { } camera ? new FpvCameraSpec(camera.Position - cg, camera.UptiltDeg, camera.FovDeg) : null);
```

`aircraft/wing/aircraft.json`: after the `"cg"` line add
`"fpvCamera": { "position": [0.04, 0, 0.03], "uptiltDeg": 25, "fovDeg": 120 },`
(camera in the front of the pod, looking up for fast forward flight). The trainer, sport and 3d keep the default:
their nose point is ahead of the propeller plane, and FPV does not render the aircraft.

`aircraft/README.md`: add a section after "Hull points":

```markdown
## FPV camera (optional)

`"fpvCamera": { "position": [x, y, z], "uptiltDeg": 25, "fovDeg": 120 }` places the onboard camera of the FPV view:
`position` from the datum in body axes like every other position, `uptiltDeg` (0–60) tilts it up from the body
forward axis, `fovDeg` (60–150) is its **horizontal** field of view, as FPV camera specs give it. Without the block,
the camera sits on the hull point tagged `nose` (else the most forward hull point) with 10° uptilt and 110°.
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SimLab.Flight.Tests`
Expected: all pass (the fleet tests load the edited wing too).

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.Flight/Airframe tests/SimLab.Flight.Tests/Airframe aircraft/wing/aircraft.json aircraft/README.md
git commit -m "feat(aircraft): optional fpvCamera mount in aircraft.json"
```

---

### Task 2: Camera up vector, richer context, `FpvRig`

**Files:**
- Modify: `src/SimLab.App/Cameras/ICameraRig.cs`
- Modify: `src/SimLab.App/Cameras/LineOfSightRig.cs:42` (pass world up)
- Create: `src/SimLab.App/Cameras/FpvRig.cs`
- Modify: `game/Scripts/Flight/FlightScene.cs` (use `pose.Up`; build the new context)
- Test: `tests/SimLab.App.Tests/Cameras/FpvRigTests.cs`, `tests/SimLab.App.Tests/Cameras/LineOfSightRigTests.cs`

**Interfaces:**
- Consumes: `FpvCameraSpec` (Task 1).
- Produces:
  - `CameraPose(Vec3 Position, Vec3 LookAt, double VerticalFovDeg, Vec3 Up)`
  - `CameraContext(Vec3 AircraftPosition, Quat AircraftOrientation, double AircraftSpan, Func<double, double, double>? TerrainHeight = null, double Aspect = 16.0 / 9.0)`
  - `CameraContext.GroundAt(double x, double y)`: terrain height, or −∞ without a terrain.
  - `sealed class FpvRig(FpvCameraSpec mount) : ICameraRig`, `static double VerticalFov(double horizontalFovDeg, double aspect)`.

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.App.Tests/Cameras/FpvRigTests.cs`:

```csharp
using SimLab.App.Cameras;
using SimLab.Flight.Airframe;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Cameras;

public class FpvRigTests
{
    static readonly Vec3 Cg = new(10, 20, 30);
    static readonly FpvCameraSpec Mount = new(new Vec3(-0.5, 0, 0.1), 20, 110);

    static CameraPose Pose(double rollDeg, double pitchDeg, double headingDeg) =>
        new FpvRig(Mount).Update(0.016, new CameraContext(Cg,
            Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg)), 1.5));

    static Vec3 Look(CameraPose p) => (p.LookAt - p.Position).Normalized();

    [Fact]
    public void Level_heading_north_looks_north_tilted_up_by_the_uptilt()
    {
        var pose = Pose(0, 0, 0);
        var look = Look(pose);
        Assert.Equal(0, look.X, 9);
        Assert.Equal(Math.Cos(Angle.Rad(20)), look.Y, 9);
        Assert.Equal(Math.Sin(Angle.Rad(20)), look.Z, 9);
        Assert.Equal(0, Vec3.Dot(look, pose.Up), 9);
        Assert.True(pose.Up.Z > 0.9);
    }

    [Fact]
    public void Mount_is_carried_around_the_cg()
    {
        // Heading east: the body forward axis (−x) points east, so the mount 0.5 m forward is 0.5 m east of the CG.
        var pose = Pose(0, 0, 90);
        Assert.Equal(Cg.X + 0.5, pose.Position.X, 9);
        Assert.Equal(Cg.Y, pose.Position.Y, 9);
        Assert.Equal(Cg.Z + 0.1, pose.Position.Z, 9);
    }

    [Fact]
    public void Up_rolls_with_the_aircraft()
    {
        // 90° right bank heading north: body up points east (world +x), and the uptilt leans it back (south).
        var up = Pose(90, 0, 0).Up;
        Assert.Equal(Math.Cos(Angle.Rad(20)), up.X, 9);
        Assert.Equal(-Math.Sin(Angle.Rad(20)), up.Y, 9);
        Assert.Equal(0, up.Z, 9);
    }

    [Fact]
    public void Horizontal_fov_is_turned_into_the_vertical_one_for_the_screen()
    {
        double expected = Angle.Deg(2 * Math.Atan(Math.Tan(Angle.Rad(55)) / (16.0 / 9.0)));
        Assert.Equal(expected, FpvRig.VerticalFov(110, 16.0 / 9.0), 9);
        Assert.Equal(expected, Pose(0, 0, 0).VerticalFovDeg, 9);
    }
}
```

In `LineOfSightRigTests.Camera_sits_at_the_eye_and_converges_on_the_aircraft`, after the FOV assert, add:

```csharp
        Assert.Equal(Vec3.UnitZ, pose.Up);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.App.Tests --filter "FullyQualifiedName~Cameras"`
Expected: build errors (`FpvRig`, `CameraPose.Up`).

- [ ] **Step 3: Implement**

Replace the two records in `ICameraRig.cs`:

```csharp
/// <param name="Up">Camera up direction, world ENU axes (world up except in views that roll with the aircraft).</param>
public readonly record struct CameraPose(Vec3 Position, Vec3 LookAt, double VerticalFovDeg, Vec3 Up);

/// <param name="AircraftPosition">Interpolated aircraft CG position, world ENU axes.</param>
/// <param name="AircraftOrientation">Interpolated attitude, body → world.</param>
/// <param name="AircraftSpan">Largest dimension of the aircraft (wingspan), m.</param>
/// <param name="TerrainHeight">Ground height (m) at an east, north position; null when there is no ground.</param>
/// <param name="Aspect">Viewport width / height.</param>
public readonly record struct CameraContext(
    Vec3 AircraftPosition,
    Quat AircraftOrientation,
    double AircraftSpan,
    Func<double, double, double>? TerrainHeight = null,
    double Aspect = 16.0 / 9.0)
{
    public double GroundAt(double x, double y) => TerrainHeight?.Invoke(x, y) ?? double.NegativeInfinity;
}
```

and update the interface summary to "A camera behaviour: line of sight from the ground, FPV or chase."

`LineOfSightRig.cs` line 42: `return new CameraPose(Eye, Eye + _look * Math.Max(distance, 1.0), fov, Vec3.UnitZ);`

`src/SimLab.App/Cameras/FpvRig.cs`:

```csharp
using SimLab.Flight.Airframe;
using SimLab.Flight.Geometry;

namespace SimLab.App.Cameras;

/// <summary>Onboard FPV camera: rigidly fixed to the aircraft at its mount, tilted up, rolling with it. No lag.</summary>
public sealed class FpvRig : ICameraRig
{
    readonly FpvCameraSpec _mount;
    readonly Vec3 _lookBody;
    readonly Vec3 _upBody;

    public FpvRig(FpvCameraSpec mount)
    {
        _mount = mount;
        double tilt = Angle.Rad(mount.UptiltDeg);
        // Body forward (−x) turned up toward +z by the uptilt; up is +z leaned back (+x) by the same angle.
        _lookBody = new Vec3(-Math.Cos(tilt), 0, Math.Sin(tilt));
        _upBody = new Vec3(Math.Sin(tilt), 0, Math.Cos(tilt));
    }

    public void Reset(in CameraContext ctx) { }

    public CameraPose Update(double dt, in CameraContext ctx)
    {
        var q = ctx.AircraftOrientation;
        var eye = ctx.AircraftPosition + q.Rotate(_mount.Position);
        return new CameraPose(eye, eye + q.Rotate(_lookBody), VerticalFov(_mount.HorizontalFovDeg, ctx.Aspect), q.Rotate(_upBody));
    }

    /// <summary>Vertical field of view showing <paramref name="horizontalFovDeg"/> across a screen of that aspect.</summary>
    public static double VerticalFov(double horizontalFovDeg, double aspect) =>
        Angle.Deg(2 * Math.Atan(Math.Tan(Angle.Rad(horizontalFovDeg) / 2) / Math.Max(aspect, 0.1)));
}
```

`FlightScene.cs`:
- In `_Process`, replace the `up` line and the `LookAtFromPosition` call with:

```csharp
        var up = pose.Up.WorldToGodot();
        if (Mathf.Abs((to - from).Normalized().Dot(up.Normalized())) > 0.999f) up = Vector3.Back;
        _camera.Fov = (float)pose.VerticalFovDeg;
        _camera.LookAtFromPosition(from, to, up);
```

  (`GodotConvert.WorldToGodot` is documented for positions and directions: a pure axis swap.)
- `Context()` becomes:

```csharp
    CameraContext Context()
    {
        var display = _session.DisplayState;
        // Init runs before the scene enters the tree (Main.StartFlight), when there is no viewport yet.
        var size = IsInsideTree() ? GetViewport().GetVisibleRect().Size : Vector2.Zero;
        double aspect = size.Y > 0 ? size.X / size.Y : 16.0 / 9.0;
        return new CameraContext(display.Position, display.Orientation, _session.Span, _session.Terrain.Height, aspect);
    }
```

- [ ] **Step 4: Run the tests and build the game**

Run: `dotnet test tests/SimLab.App.Tests && dotnet build game/SimLab.Game.csproj`
Expected: all pass, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.App/Cameras tests/SimLab.App.Tests/Cameras game/Scripts/Flight/FlightScene.cs
git commit -m "feat(cameras): FPV rig, camera up vector and terrain-aware context"
```

---

### Task 3: `ChaseRig`

**Files:**
- Create: `src/SimLab.App/Cameras/ChaseRig.cs`
- Test: `tests/SimLab.App.Tests/Cameras/ChaseRigTests.cs`

**Interfaces:**
- Consumes: `CameraPose`, `CameraContext`, `CameraContext.GroundAt` (Task 2).
- Produces: `sealed class ChaseRig : ICameraRig` with constants `DistanceSpans = 3`, `HeightSpans = 0.8`,
  `HeadingTimeConstant = 0.4`, `GroundClearance = 0.5`, `FovDeg = 60`, `VerticalLimitDeg = 10`.

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.App.Tests/Cameras/ChaseRigTests.cs`:

```csharp
using SimLab.App.Cameras;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Cameras;

public class ChaseRigTests
{
    const double Span = 1.5;
    static readonly Vec3 Cg = new(0, 0, 50);

    static CameraContext At(double rollDeg, double pitchDeg, double headingDeg, Func<double, double, double>? ground = null) =>
        new(Cg, Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg)), Span, ground);

    /// <summary>Compass heading (deg, clockwise from north) the camera looks along, horizontally.</summary>
    static double CameraHeading(CameraPose p)
    {
        var d = p.LookAt - p.Position;
        double h = Angle.Deg(Math.Atan2(d.X, d.Y));
        return h < 0 ? h + 360 : h;
    }

    [Fact]
    public void Sits_behind_and_above_with_a_level_horizon_whatever_the_bank()
    {
        var rig = new ChaseRig();
        rig.Reset(At(60, 0, 0));
        var pose = rig.Update(0.016, At(60, 0, 0));
        Assert.Equal(Vec3.UnitZ, pose.Up);
        Assert.Equal(0, pose.Position.X, 9);
        Assert.Equal(Cg.Y - ChaseRig.DistanceSpans * Span, pose.Position.Y, 9);
        Assert.Equal(Cg.Z + ChaseRig.HeightSpans * Span, pose.Position.Z, 9);
        Assert.Equal(Cg, pose.LookAt);
        Assert.Equal(ChaseRig.FovDeg, pose.VerticalFovDeg);
    }

    [Fact]
    public void Follows_a_heading_change_with_a_lag()
    {
        var rig = new ChaseRig();
        rig.Reset(At(0, 0, 0));
        CameraPose pose = default;
        for (int i = 0; i < 6; i++) pose = rig.Update(1.0 / 60, At(0, 0, 90));
        Assert.InRange(CameraHeading(pose), 1, 45);
        for (int i = 0; i < 114; i++) pose = rig.Update(1.0 / 60, At(0, 0, 90));
        Assert.InRange(CameraHeading(pose), 89, 90);
    }

    [Fact]
    public void Turns_the_short_way_across_north()
    {
        var rig = new ChaseRig();
        rig.Reset(At(0, 0, 350));
        var pose = rig.Update(0.2, At(0, 0, 10));
        double h = CameraHeading(pose);
        Assert.True(h > 350 || h < 10, $"went the long way: {h}");
    }

    [Fact]
    public void Holds_the_last_heading_in_a_vertical_climb()
    {
        var rig = new ChaseRig();
        rig.Reset(At(0, 0, 90));
        CameraPose pose = default;
        for (int i = 0; i < 120; i++) pose = rig.Update(1.0 / 60, At(0, 89, 180));
        Assert.Equal(90, CameraHeading(pose), 6);
    }

    [Fact]
    public void Never_goes_below_the_ground()
    {
        var rig = new ChaseRig();
        var ctx = At(0, 0, 0, (_, _) => 100);
        rig.Reset(ctx);
        Assert.Equal(100 + ChaseRig.GroundClearance, rig.Update(0.016, ctx).Position.Z, 9);
    }

    [Fact]
    public void Reset_snaps_to_the_heading()
    {
        var rig = new ChaseRig();
        rig.Reset(At(0, 0, 0));
        rig.Reset(At(0, 0, 200));
        Assert.Equal(200, CameraHeading(rig.Update(0.016, At(0, 0, 200))), 6);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.App.Tests --filter "FullyQualifiedName~ChaseRigTests"`
Expected: build error, `ChaseRig` not defined.

- [ ] **Step 3: Implement**

`src/SimLab.App/Cameras/ChaseRig.cs`:

```csharp
using SimLab.Flight.Geometry;

namespace SimLab.App.Cameras;

/// <summary>
/// Third-person chase camera: behind and above the aircraft along its smoothed horizontal heading, looking at it with
/// a level horizon (it never rolls or pitches with the aircraft), and never below the ground.
/// </summary>
public sealed class ChaseRig : ICameraRig
{
    public const double DistanceSpans = 3;
    public const double HeightSpans = 0.8;
    public const double HeadingTimeConstant = 0.4;
    public const double GroundClearance = 0.5;
    public const double FovDeg = 60;
    /// <summary>Within this angle of vertical the nose gives no heading, and the last one is kept.</summary>
    public const double VerticalLimitDeg = 10;

    /// <summary>Math angle of the heading in the horizontal plane (rad, from east toward north).</summary>
    double _heading;
    bool _initialized;

    public void Reset(in CameraContext ctx)
    {
        _heading = NoseHeading(ctx.AircraftOrientation)
            ?? (_initialized ? _heading : Horizontal(-ctx.AircraftOrientation.Rotate(BodyAxes.Up)) ?? 0);
        _initialized = true;
    }

    public CameraPose Update(double dt, in CameraContext ctx)
    {
        if (!_initialized) Reset(ctx);
        if (NoseHeading(ctx.AircraftOrientation) is { } target)
        {
            double k = 1 - Math.Exp(-Math.Max(dt, 0) / HeadingTimeConstant);
            _heading += Math.IEEERemainder(target - _heading, 2 * Math.PI) * k;
        }
        var back = new Vec3(-Math.Cos(_heading), -Math.Sin(_heading), 0);
        var p = ctx.AircraftPosition;
        var eye = p + back * (DistanceSpans * ctx.AircraftSpan) + Vec3.UnitZ * (HeightSpans * ctx.AircraftSpan);
        double floor = ctx.GroundAt(eye.X, eye.Y) + GroundClearance;
        if (eye.Z < floor) eye = eye with { Z = floor };
        return new CameraPose(eye, p, FovDeg, Vec3.UnitZ);
    }

    /// <summary>Math heading of the nose, or null when it points within <see cref="VerticalLimitDeg"/> of vertical.</summary>
    static double? NoseHeading(Quat orientation) => Horizontal(orientation.Rotate(BodyAxes.Forward));

    static double? Horizontal(Vec3 v) =>
        Math.Sqrt(v.X * v.X + v.Y * v.Y) < Math.Sin(Angle.Rad(VerticalLimitDeg)) * v.Length ? null : Math.Atan2(v.Y, v.X);
}
```

Note: `Reset` falls back to the direction the aircraft's belly faces when the nose is vertical and there is no heading
yet (after a vertical pull-up, that is where it came from).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SimLab.App.Tests --filter "FullyQualifiedName~Cameras"`
Expected: all pass. If `Follows_a_heading_change_with_a_lag` fails on the first range, the time constant is wrong,
not the test: after 0.1 s, 1 − e^(−0.25) ≈ 22 % of 90° ≈ 20°.

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.App/Cameras/ChaseRig.cs tests/SimLab.App.Tests/Cameras/ChaseRigTests.cs
git commit -m "feat(cameras): chase rig with a level horizon and ground clearance"
```

---

### Task 4: `CameraDirector` and the remembered view

**Files:**
- Create: `src/SimLab.App/Cameras/CameraDirector.cs` (holds `CameraView` too)
- Modify: `src/SimLab.App/Settings/AppSettings.cs`
- Test: `tests/SimLab.App.Tests/Cameras/CameraDirectorTests.cs`, `tests/SimLab.App.Tests/Settings/SettingsTests.cs`

**Interfaces:**
- Consumes: `ICameraRig`, `CameraContext`, `CameraPose`.
- Produces:
  - `enum CameraView { Ground, Fpv, Chase }`
  - `sealed class CameraDirector(ICameraRig ground, ICameraRig fpv, ICameraRig chase, CameraView initial)` with
    `CameraView Current`, `void Next(in CameraContext ctx)`, `void Select(CameraView view, in CameraContext ctx)`,
    `void Reset(in CameraContext ctx)` (resets the current rig), `CameraPose Update(double dt, in CameraContext ctx)`.
  - `AppSettings.CameraView` (default `CameraView.Ground`).

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.App.Tests/Cameras/CameraDirectorTests.cs`:

```csharp
using SimLab.App.Cameras;
using SimLab.Flight.Geometry;

namespace SimLab.App.Tests.Cameras;

public class CameraDirectorTests
{
    sealed class FakeRig(double fov) : ICameraRig
    {
        public int Resets;
        public void Reset(in CameraContext ctx) => Resets++;
        public CameraPose Update(double dt, in CameraContext ctx) => new(Vec3.Zero, Vec3.UnitY, fov, Vec3.UnitZ);
    }

    static readonly CameraContext Ctx = new(Vec3.Zero, Quat.Identity, 1.5);

    [Fact]
    public void Next_cycles_ground_fpv_chase_and_resets_the_new_rig()
    {
        FakeRig ground = new(1), fpv = new(2), chase = new(3);
        var director = new CameraDirector(ground, fpv, chase, CameraView.Ground);
        Assert.Equal(1, director.Update(0.016, Ctx).VerticalFovDeg);
        director.Next(Ctx);
        Assert.Equal(CameraView.Fpv, director.Current);
        Assert.Equal(1, fpv.Resets);
        Assert.Equal(2, director.Update(0.016, Ctx).VerticalFovDeg);
        director.Next(Ctx);
        Assert.Equal(CameraView.Chase, director.Current);
        Assert.Equal(1, chase.Resets);
        director.Next(Ctx);
        Assert.Equal(CameraView.Ground, director.Current);
        Assert.Equal(1, ground.Resets);
    }

    [Fact]
    public void Select_jumps_to_a_view_and_reset_resets_only_the_current_one()
    {
        FakeRig ground = new(1), fpv = new(2), chase = new(3);
        var director = new CameraDirector(ground, fpv, chase, CameraView.Fpv);
        director.Select(CameraView.Chase, Ctx);
        Assert.Equal(3, director.Update(0.016, Ctx).VerticalFovDeg);
        director.Reset(Ctx);
        Assert.Equal(2, chase.Resets);
        Assert.Equal(0, ground.Resets);
        Assert.Equal(0, fpv.Resets);
    }
}
```

Append to `SettingsTests`:

```csharp
    [Fact]
    public void Camera_view_is_saved_and_an_unknown_one_falls_back_to_ground()
    {
        var path = Path.Combine(_dir, "settings.json");
        Assert.Equal(SimLab.App.Cameras.CameraView.Ground, new AppSettings().CameraView);
        new AppSettings { CameraView = SimLab.App.Cameras.CameraView.Chase }.Save(path);
        Assert.Equal(SimLab.App.Cameras.CameraView.Chase, AppSettings.Load(path).CameraView);
        Assert.Equal(SimLab.App.Cameras.CameraView.Ground,
            new AppSettings { CameraView = (SimLab.App.Cameras.CameraView)42 }.Sanitized().CameraView);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.App.Tests --filter "FullyQualifiedName~CameraDirectorTests|FullyQualifiedName~SettingsTests"`
Expected: build errors.

- [ ] **Step 3: Implement**

`src/SimLab.App/Cameras/CameraDirector.cs`:

```csharp
namespace SimLab.App.Cameras;

public enum CameraView { Ground, Fpv, Chase }

/// <summary>Owns one rig per view and drives the current one. Switching resets the new rig so it starts on target.</summary>
public sealed class CameraDirector
{
    readonly ICameraRig[] _rigs;

    public CameraDirector(ICameraRig ground, ICameraRig fpv, ICameraRig chase, CameraView initial)
    {
        _rigs = [ground, fpv, chase];
        Current = Enum.IsDefined(initial) ? initial : CameraView.Ground;
    }

    public CameraView Current { get; private set; }

    ICameraRig Rig => _rigs[(int)Current];

    /// <summary>Ground → FPV → chase → ground.</summary>
    public void Next(in CameraContext ctx) => Select((CameraView)(((int)Current + 1) % _rigs.Length), ctx);

    public void Select(CameraView view, in CameraContext ctx)
    {
        Current = view;
        Rig.Reset(ctx);
    }

    public void Reset(in CameraContext ctx) => Rig.Reset(ctx);

    public CameraPose Update(double dt, in CameraContext ctx) => Rig.Update(dt, ctx);
}
```

`AppSettings.cs`: add `using SimLab.App.Cameras;`, the property after `AutoZoom`:

```csharp
    /// <summary>Flight camera view, remembered from the last flight.</summary>
    public CameraView CameraView { get; init; } = CameraView.Ground;
```

and in `Sanitized()`: `CameraView = Enum.IsDefined(CameraView) ? CameraView : CameraView.Ground,`

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SimLab.App.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.App/Cameras/CameraDirector.cs src/SimLab.App/Settings/AppSettings.cs tests/SimLab.App.Tests
git commit -m "feat(cameras): camera director cycling ground, FPV and chase; remembered view"
```

---

### Task 5: C key and radio view switch

**Files:**
- Modify: `src/SimLab.App/Session/InputRouter.cs:10,45-48`
- Modify: `game/Scripts/Flight/KeyboardInput.cs:18`
- Modify: `game/Scripts/Radio/RadioScreen.cs:94-97`
- Modify: `game/translations/strings.csv` (new `RADIO_BIND_CAMERA`, edited `HUD_KEYBOARD`)
- Modify: `README.md:28`
- Test: `tests/SimLab.App.Tests/Session/InputTests.cs`

**Interfaces:**
- Produces: `KeyboardCommands(bool Reset, bool Pause, bool ToggleWind, bool NextCamera = false)`; the router emits
  `SwitchAction.NextCamera` once per C press.

- [ ] **Step 1: Write the failing test** (append to `InputTests`)

```csharp
    [Fact]
    public void C_key_asks_for_the_next_camera_once_per_press()
    {
        var router = new InputRouter(_ => null);
        var c = new KeyboardCommands(false, false, false, NextCamera: true);
        Assert.Equal(SwitchAction.NextCamera, Assert.Single(router.Update(0.016, [], default, c).Actions));
        Assert.Empty(router.Update(0.016, [], default, c).Actions);
        Assert.Empty(router.Update(0.016, [], default, default).Actions);
        Assert.Equal(SwitchAction.NextCamera, Assert.Single(router.Update(0.016, [], default, c).Actions));
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/SimLab.App.Tests --filter "FullyQualifiedName~InputTests"`
Expected: build error (no `NextCamera` parameter).

- [ ] **Step 3: Implement**

`InputRouter.cs`:

```csharp
public readonly record struct KeyboardCommands(bool Reset, bool Pause, bool ToggleWind, bool NextCamera = false);
```

and after the `ToggleWind` line in `Update`:

```csharp
        if (commands.NextCamera && !_previousCommands.NextCamera) actions.Add(SwitchAction.NextCamera);
```

`KeyboardInput.cs`: `public static KeyboardCommands Commands() => new(Down(Key.R), Down(Key.P), Down(Key.V), Down(Key.C));`

`RadioScreen.cs`: add a fourth button to the bind row:

```csharp
            Ui.Button(Ui.T("RADIO_BIND_WIND"), () => StartCapture(SwitchAction.ToggleWind)),
            Ui.Button(Ui.T("RADIO_BIND_CAMERA"), () => StartCapture(SwitchAction.NextCamera))));
```

`strings.csv`: after `RADIO_BIND_WIND` add

```
RADIO_BIND_CAMERA,Interrupteur « vue »,View switch
```

and change `HUD_KEYBOARD` to

```
HUD_KEYBOARD,"Clavier — Z/S gaz, flèches ailerons/profondeur, Q/D dérive, R reset, P pause, V vent, C vue","Keyboard — W/S throttle, arrows aileron/elevator, A/D rudder, R reset, P pause, V wind, C view"
```

The capture prompt (`RADIO_BIND_WAIT`) is the same for every action, so nothing else changes in the radio screen.

`README.md` line 28: `R reset · P pause · V wind on/off · C camera view · F3 diagnostics · Esc menu.`

- [ ] **Step 4: Run the tests and build**

Run: `dotnet test tests/SimLab.App.Tests && dotnet build game/SimLab.Game.csproj`
Expected: all pass (translation tests check every key has fr and en), 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.App/Session/InputRouter.cs game/Scripts/Flight/KeyboardInput.cs game/Scripts/Radio/RadioScreen.cs game/translations/strings.csv README.md tests/SimLab.App.Tests/Session/InputTests.cs
git commit -m "feat(input): C key and a bindable radio switch for the camera view"
```

---

### Task 6: Wire the views into the flight scene

**Files:**
- Modify: `game/Scripts/Flight/FlightScene.cs`
- Modify: `game/Scripts/Flight/AircraftVisual.cs` (meshes on their own render layer)
- Modify: `game/Scripts/Main.cs` (`--view` for the flight screenshots)
- Modify: `docs/dev-setup.md`, `docs/manual-acceptance.md`

**Interfaces:**
- Consumes: `CameraDirector`, `CameraView`, `FpvRig`, `ChaseRig`, `LineOfSightRig`, `FpvCameraSpec.For`,
  `AppSettings.CameraView`, `SwitchAction.NextCamera`.
- Produces (for the HUD branch): `FlightScene.CameraView` (current view), `FlightScene.NextCamera()`,
  `FlightScene.SelectCamera(CameraView view)`. Both switchers save the choice in the settings.
  `AircraftVisual.Layer` = 2 (render layer number of the aircraft meshes).

- [ ] **Step 1: Put the aircraft meshes on their own layer**

In `AircraftVisual.cs` add:

```csharp
    /// <summary>Render layer (1-based) of the aircraft meshes, so a camera can leave them out (the FPV view).</summary>
    public const int Layer = 2;
```

and in `MeshFor`, set it on the returned instance: `Layers = 1u << (Layer - 1),` inside the `new MeshInstance3D { ... }`
initializer. The menu and radio previews keep rendering it, because a `Camera3D` cull mask includes every layer by default.

- [ ] **Step 2: Replace the single rig with the director in `FlightScene`**

- Fields: replace `ICameraRig _rig = null!;` with `CameraDirector _cameras = null!;` and add `int _resetCount;`.
- In `Init`, replace the two `_rig` lines with:

```csharp
        var ground = new LineOfSightRig(eye, services.Settings.FovDeg, services.Settings.AutoZoom);
        _cameras = new CameraDirector(ground, new FpvRig(FpvCameraSpec.For(definition)), new ChaseRig(), services.Settings.CameraView);
        _cameras.Reset(Context());
        _resetCount = _session.ResetCount;
```

  (`Context()` falls back to 16/9 before the scene is in the tree, see Task 2; no rig uses the aspect in `Reset`.)
- Public API:

```csharp
    public CameraView CameraView => _cameras.Current;

    /// <summary>Ground → FPV → chase → ground; remembered for the next flight.</summary>
    public void NextCamera()
    {
        _cameras.Next(Context());
        RememberCamera();
    }

    /// <summary>Jumps to a view; remembered for the next flight.</summary>
    public void SelectCamera(CameraView view)
    {
        _cameras.Select(view, Context());
        RememberCamera();
    }

    void RememberCamera()
    {
        if (_services.Settings.CameraView == _cameras.Current) return;
        _services.Settings = _services.Settings with { CameraView = _cameras.Current };
        _services.SaveSettings();
    }
```
- In `_Process`, change the action loop and the camera update:

```csharp
        foreach (var action in LastInput.Actions)
        {
            if (action == SwitchAction.NextCamera && _script is null) NextCamera();
            else _session.Handle(action);
        }
```

```csharp
        _visual.UpdateFrom(_session.Aircraft, _session.DisplayState);
        if (_session.ResetCount != _resetCount)
        {
            _resetCount = _session.ResetCount;
            _cameras.Reset(Context());
        }
        var pose = _cameras.Update(delta, Context());
        _camera.SetCullMaskValue(AircraftVisual.Layer, _cameras.Current != CameraView.Fpv);
```

  Keep the `from`/`to`/`up`/`LookAtFromPosition` lines from Task 2. Add `using SimLab.Input;` if `SwitchAction` is not
  yet in scope (it is in `SimLab.Input`), and `using SimLab.App.Cameras;` is already there.

- [ ] **Step 3: `--view` for the flight screenshots**

In `Main.cs`, in the `--screenshot-flight`/`--screenshot-diagnostics` loop, after `StartFlight(...)` succeeds, apply
an optional `--view ground|fpv|chase` without saving it:

```csharp
            if (StartFlight(_smokeAircraft, t => new ControlInputs(1, 0, t > 3.5 && t < 5 ? 0.25 : 0.05, 0)))
            {
                var scene = (FlightScene)_current!;
                if (diagnostics) scene.Diagnostics.Shown = true;
                if (ArgValue(args, "--view") is { } view && System.Enum.TryParse<CameraView>(view, true, out var parsed))
                    scene.ShowCamera(parsed);
            }
            return true;
```

and in `FlightScene` add a non-saving variant used only there:

```csharp
    /// <summary>Switches the view without remembering it (command-line screenshots).</summary>
    public void ShowCamera(CameraView view) => _cameras.Select(view, Context());
```

Add `using SimLab.App.Cameras;` to `Main.cs`.

- [ ] **Step 4: Build, run all tests, take the screenshots**

```bash
dotnet build game/SimLab.Game.csproj && dotnet test
"$GODOT" --headless --path game -- --smoke-flight trainer 3
"$GODOT" --path game -- --screenshot-flight trainer 8 /tmp/claude-501/view-ground.png
"$GODOT" --path game -- --screenshot-flight trainer 8 /tmp/claude-501/view-fpv.png --view fpv
"$GODOT" --path game -- --screenshot-flight trainer 8 /tmp/claude-501/view-chase.png --view chase
"$GODOT" --path game -- --screenshot-flight wing 8 /tmp/claude-501/view-wing-fpv.png --view fpv
```

(Use the session scratchpad directory instead of `/tmp/claude-501` if one is given.) Expected:
`SIMLAB_SMOKE_OK`. Open each PNG and check:
- The FPV shots show no part of the aircraft, the horizon tilts with the bank, and the wing looks further up than
  the trainer.
- The chase shot shows the aircraft from behind with a level horizon.
- The ground shot is unchanged.

The screenshots do not change the saved view: check that `CameraView` in the settings file is unchanged.

- [ ] **Step 5: Docs**

`docs/dev-setup.md`:
- The `--screenshot-flight` row becomes: "Scripted takeoff seen from the pilot box, or with `--view fpv|chase` from
  that camera (the saved view is unchanged)".
- In the `--screenshot-menu-live` row, delete the sentence "Plays the motor voice briefly (needs a windowed run
  like every screenshot)". The home screen no longer plays the motor.

`docs/manual-acceptance.md`: add a section before the sound sections:

```markdown
## Camera views

- [ ] In flight, C cycles ground → FPV → chase → ground; the keyboard help line shows "C vue" / "C view".
- [ ] Radio screen → "Interrupteur « vue »" binds a switch; in flight it cycles the views like C.
- [ ] FPV: no part of the aircraft is seen, the horizon rolls and pitches with it; the wing's camera looks up
  about 25° (the horizon sits low in level flight at speed).
- [ ] Chase: the aircraft is seen from behind and above, the horizon stays level through banks and loops, the
  camera swings round smoothly in turns and never goes below the ground (land and taxi in chase view).
- [ ] R (or the radio reset switch) puts the camera straight back behind or on the aircraft, with no swing.
- [ ] Leave the flight in chase view and start another: it starts in chase view.
- [ ] In FPV the motor is loud and steady (no Doppler); from the ground view it still shifts as it flies by.
```

- [ ] **Step 6: Commit**

```bash
git add game/Scripts docs/dev-setup.md docs/manual-acceptance.md
git commit -m "feat(flight): switch between ground, FPV and chase views in flight"
```

---

## Final check

- `dotnet test`: everything green except the known skip (sport roll rate).
- `dotnet build game/SimLab.Game.csproj`: 0 warnings.
- The four screenshots of Task 6 checked.
- Spec sections covered:
  - 1.1–1.3 rigs: Tasks 1–3.
  - 1.4 reset: Tasks 4 and 6.
  - 2 switching: Tasks 4–6.
  - 3 sound: no code (the listener follows the current camera).
  - 4 tests: across tasks.
