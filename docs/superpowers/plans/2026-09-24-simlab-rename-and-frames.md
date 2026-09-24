# SimLab Rename and Axis Conventions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the project Symlab → SimLab everywhere, and switch the code to the pilot's conventions: world axes **x east, y north, z up (ENU)**, body axes **x back, y right, z up** (OpenVSP), with aircraft positions entered from a free datum plus a required `cg` — without changing any flight result.

**Architecture:** First freeze a set of frame-invariant flight metrics (airspeed, height, heading, pitch, roll, α, β, north/east displacement, pilot-convention body rates, rpm, crash cause) from scripted flights into a golden file. Then rename, switch the world frame, switch the body frame and data, and switch the app/game mapping — each step keeps every existing test green after relabeling and must reproduce the golden metrics. Behavior-test thresholds never change.

**Tech Stack:** .NET 10 SDK, C#, xUnit, Godot 4.7.2 .NET, Python 3 (one-off data conversion only).

**Spec:** `docs/superpowers/specs/2026-09-22-symlab-core-design.md` (renamed and amended in Task 6). User decisions (2026-09-24): name SimLab; body x back / y right / z up; world ENU; aircraft.json positions from a free datum (e.g. nose) + `cg`.

## Global Constraints

- **Physics must not change.** Every existing test keeps its assertions' meaning and thresholds; only frame relabeling of literals is allowed. The golden test (Task 1) must pass after every task with the tolerances it defines.
- World frame: right-handed **x east, y north, z up**. Gravity is `(0, 0, −g)`. Terrain height is `Height(x, y)`; heading is clockwise from north.
- Body frame: right-handed **x back (toward the tail), y right (right wing), z up**. Forward = −x. Pilot rates: roll right = **−ω_x**, pitch up = **+ω_y**, yaw right = **−ω_z**.
- Control deflection sign unchanged: positive = trailing edge down relative to the surface normal.
- `aircraft.json`: all body positions are measured from a free datum in body axes; the field `"cg": [x, y, z]` (same datum and axes) is **required**; the loader subtracts it.
- Name: **SimLab** in namespaces (`SimLab.Flight`, `SimLab.Input`, `SimLab.App`, `SimLab.Game`), project/solution/file names, docs, UI title, smoke markers (`SIMLAB_...`). The repository folder `3_SYMLAB` is not renamed.
- All code/comments/docs in English; UI French default.
- `TreatWarningsAsErrors` stays on; `dotnet test` and `dotnet build game/SimLab.Game.csproj` stay at 0 warnings.
- Commit after every task; message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Branch: `feat/godot-game` (continue on it).

## Frame conversion reference (old → new)

| Quantity | Old | New |
|---|---|---|
| World vector | (E, U, S) = (x, y, z) | (x, −z, y) → (E, N, U) |
| Body vector | (fwd, up, right) = (x, y, z) | (−x, z, y) → (back, right, up) |
| Body angular velocity ω | (roll-right+, yaw-left+, pitch-up+) | (−ω_x_old, ω_z_old, ω_y_old) — i.e. new ω_x = −roll-right rate, ω_y = pitch-up rate, ω_z = −yaw-right rate |
| Orientation | maps old body → old world | maps new body → new world (rebuild from Attitude; do not convert quaternions component-wise) |
| Terrain | `Height(x, z)`, `Normal(x, z)`, `CylinderObstacle(X, Z, R, H, BaseY)` | `Height(x, y)`, `Normal(x, y)`, `CylinderObstacle(X, Y, R, H, BaseZ)` |
| Air data | α = atan2(−v_y, v_x), β = asin(v_z/V) | α = atan2(−u_z, −u_x), β = asin(u_y/V) |
| Heading/pitch/roll | from old axes | same pilot meaning, computed from new axes |

---

### Task 1: Golden frame-invariant regression test

**Files:**
- Create: `tests/Symlab.Flight.Tests/Behavior/FrameInvarianceGoldenTests.cs`, `tests/Symlab.Flight.Tests/Behavior/PilotFrame.cs`, `tests/Symlab.Flight.Tests/Behavior/Golden/frame-invariance.json`

**Interfaces:**
- Produces: `PilotFrame` static helpers — the **only** place later tasks edit to follow the frame switch: `North(Vec3 worldPos)`, `East(Vec3)`, `Up(Vec3)`, `RollRightRate(Vec3 omegaBody)`, `PitchUpRate(Vec3)`, `YawRightRate(Vec3)`. Golden JSON of snapshots per scenario.

- [ ] **Step 1: Write the helpers (current = old frames)**

`tests/Symlab.Flight.Tests/Behavior/PilotFrame.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Behavior;

/// <summary>
/// Frame-invariant views of world positions and body rates. When the axis conventions change, only these
/// bodies change — the golden values they feed must not.
/// </summary>
internal static class PilotFrame
{
    public static double East(Vec3 p) => p.X;
    public static double North(Vec3 p) => -p.Z;
    public static double Up(Vec3 p) => p.Y;
    public static double RollRightRate(Vec3 omega) => omega.X;
    public static double PitchUpRate(Vec3 omega) => omega.Z;
    public static double YawRightRate(Vec3 omega) => -omega.Y;
}
```

- [ ] **Step 2: Write the golden test**

`tests/Symlab.Flight.Tests/Behavior/FrameInvarianceGoldenTests.cs`:
```csharp
using System.Globalization;
using System.Text.Json;
using Symlab.Flight.Airframe;
using Symlab.Flight.Atmosphere;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Sim;
using Symlab.Flight.Terrain;

namespace Symlab.Flight.Tests.Behavior;

/// <summary>
/// Frame-invariant metrics of scripted flights, frozen before the axis-convention refactor.
/// Regenerate ONLY with SIMLAB_WRITE_GOLDEN=1 and only when a physics change is intended.
/// </summary>
public class FrameInvarianceGoldenTests
{
    public sealed record Snapshot(
        double T, double Airspeed, double East, double North, double Up,
        double HeadingDeg, double PitchDeg, double RollDeg, double AlphaDeg, double BetaDeg,
        double RollRightRate, double PitchUpRate, double YawRightRate, double Rpm, string Crash);

    static readonly string GoldenPath = Path.Combine(Fleet.RepoRoot, "tests", "Symlab.Flight.Tests", "Behavior", "Golden", "frame-invariance.json");

    public static IEnumerable<object[]> Scenarios() =>
        new[] { "trainer_takeoff", "sport_rolls", "wing_launch", "trainer_wind" }.Select(s => new object[] { s });

    static (Simulation Sim, double Seconds, Func<double, ControlInputs> Pilot) Build(string scenario)
    {
        switch (scenario)
        {
            case "trainer_takeoff":
            {
                var def = Fleet.Load("trainer");
                var sim = new Simulation(new Aircraft(def), FlightEnvironment.Calm());
                sim.Reset(InitialConditions.OnGround(def, sim.Environment.Terrain, 12, -7, 130));
                return (sim, 8, t => new ControlInputs(1, 0, t > 3 ? 0.3 : 0, 0.1));
            }
            case "sport_rolls":
            {
                var sim = new Simulation(new Aircraft(Fleet.Load("sport")), FlightEnvironment.Calm());
                sim.Reset(InitialConditions.InFlight(new Vec3(5, 80, -3), 30, 18, pitchDeg: 3, rollDeg: -10));
                return (sim, 5, t => new ControlInputs(0.6, t is > 1 and < 2 ? 0.5 : 0, t is > 2.5 and < 3 ? 0.3 : 0, t is > 3 and < 4 ? 0.4 : 0));
            }
            case "wing_launch":
            {
                var sim = new Simulation(new Aircraft(Fleet.Load("wing")), FlightEnvironment.Calm());
                sim.Reset(InitialConditions.InFlight(new Vec3(0, 1.8, 22), 250, 10, pitchDeg: 10));
                return (sim, 5, _ => new ControlInputs(1, 0, 0.1, 0));
            }
            case "trainer_wind":
            {
                var env = new FlightEnvironment(new FlatTerrain(), new WindField(new WindSettings(SpeedAt10m: 5, FromDirectionDeg: 60, Turbulence: 1), seed: 3));
                var sim = new Simulation(new Aircraft(Fleet.Load("trainer")), env);
                sim.Reset(InitialConditions.InFlight(new Vec3(-20, 60, 10), 200, 16));
                return (sim, 6, t => new ControlInputs(0.65, t is > 2 and < 2.5 ? -0.4 : 0, 0.05, 0));
            }
            default: throw new ArgumentException(scenario);
        }
    }

    static List<Snapshot> Run(string scenario)
    {
        var (sim, seconds, pilot) = Build(scenario);
        var snapshots = new List<Snapshot>();
        int steps = (int)Math.Round(seconds / Simulation.FixedStep);
        int every = (int)Math.Round(0.5 / Simulation.FixedStep);
        for (int i = 1; i <= steps; i++)
        {
            sim.StepOnce(pilot(sim.Time));
            if (i % every != 0) continue;
            var a = sim.Aircraft;
            var s = a.State;
            var att = Attitude.FromOrientation(s.Orientation);
            snapshots.Add(new Snapshot(
                Math.Round(sim.Time, 6), a.AirData.Airspeed,
                PilotFrame.East(s.Position), PilotFrame.North(s.Position), PilotFrame.Up(s.Position),
                Angle.Deg(att.Heading), Angle.Deg(att.Pitch), Angle.Deg(att.Roll),
                Angle.Deg(a.AirData.Alpha), Angle.Deg(a.AirData.Beta),
                PilotFrame.RollRightRate(s.AngularVelocity), PilotFrame.PitchUpRate(s.AngularVelocity), PilotFrame.YawRightRate(s.AngularVelocity),
                a.Power?.Telemetry.Rpm ?? 0, a.Crash.ToString()));
        }
        return snapshots;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Flight_metrics_match_the_golden_file(string scenario)
    {
        var actual = Run(scenario);
        if (Environment.GetEnvironmentVariable("SIMLAB_WRITE_GOLDEN") == "1")
        {
            var all = File.Exists(GoldenPath)
                ? JsonSerializer.Deserialize<Dictionary<string, List<Snapshot>>>(File.ReadAllText(GoldenPath))!
                : new Dictionary<string, List<Snapshot>>();
            all[scenario] = actual;
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath)!);
            File.WriteAllText(GoldenPath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        var golden = JsonSerializer.Deserialize<Dictionary<string, List<Snapshot>>>(File.ReadAllText(GoldenPath))![scenario];
        Assert.Equal(golden.Count, actual.Count);
        for (int i = 0; i < golden.Count; i++)
        {
            var g = golden[i];
            var a = actual[i];
            string at = $"{scenario} t={g.T.ToString(CultureInfo.InvariantCulture)}";
            Assert.Equal(g.Crash, a.Crash);
            Near(g.Airspeed, a.Airspeed, 1e-4, at + " airspeed");
            Near(g.East, a.East, 1e-4, at + " east");
            Near(g.North, a.North, 1e-4, at + " north");
            Near(g.Up, a.Up, 1e-4, at + " up");
            NearAngle(g.HeadingDeg, a.HeadingDeg, 1e-3, at + " heading");
            Near(g.PitchDeg, a.PitchDeg, 1e-3, at + " pitch");
            NearAngle(g.RollDeg, a.RollDeg, 1e-3, at + " roll");
            Near(g.AlphaDeg, a.AlphaDeg, 1e-3, at + " alpha");
            Near(g.BetaDeg, a.BetaDeg, 1e-3, at + " beta");
            Near(g.RollRightRate, a.RollRightRate, 1e-5, at + " roll rate");
            Near(g.PitchUpRate, a.PitchUpRate, 1e-5, at + " pitch rate");
            Near(g.YawRightRate, a.YawRightRate, 1e-5, at + " yaw rate");
            Near(g.Rpm, a.Rpm, 1e-2, at + " rpm");
        }
    }

    static void Near(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: expected {expected}, got {actual}");

    static void NearAngle(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(Math.IEEERemainder(expected - actual, 360)) <= tolerance, $"{what}: expected {expected}, got {actual}");
}
```

The test reads the golden file from the repository (`Fleet.RepoRoot`), so no csproj change is needed.

- [ ] **Step 3: Generate the golden file, then verify it passes**

```bash
export PATH="/usr/local/share/dotnet:$PATH"
SIMLAB_WRITE_GOLDEN=1 dotnet test tests/Symlab.Flight.Tests --filter FullyQualifiedName~FrameInvarianceGoldenTests
dotnet test tests/Symlab.Flight.Tests --filter FullyQualifiedName~FrameInvarianceGoldenTests
```
Expected: the file `tests/Symlab.Flight.Tests/Behavior/Golden/frame-invariance.json` contains the four scenarios (16, 10, 10, 12 snapshots); the second run passes. Sanity-check a few values in the report (trainer_takeoff airspeed grows, heading near 130; wing_launch heading near 250; trainer_wind non-zero beta). If a scenario crashes early, that is fine — it is frozen as-is.

- [ ] **Step 4: Run the full suite and commit**

```bash
dotnet test
git add -A
git commit -m "test: freeze frame-invariant flight metrics before the axis refactor

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Rename Symlab → SimLab

**Files:** every project, namespace, file and doc containing `Symlab`/`SYMLAB` (not the repository folder name `3_SYMLAB`, not `docs/superpowers/plans/*` history files).

**Interfaces:** Produces the new names used by all later tasks: `SimLab.Flight`, `SimLab.Input`, `SimLab.App`, `SimLab.Game` (namespaces and assemblies), `src/SimLab.*`, `tests/SimLab.*`, `SimLab.slnx`, `game/SimLab.Game.csproj`, smoke markers `SIMLAB_*`, env var `SIMLAB_AIRCRAFT_DIR`.

- [ ] **Step 1: Move folders and files with git**

```bash
cd /Users/axel.ldq/3_SYMLAB
for p in Flight Input App; do
  git mv src/Symlab.$p src/SimLab.$p
  git mv src/SimLab.$p/Symlab.$p.csproj src/SimLab.$p/SimLab.$p.csproj
  git mv tests/Symlab.$p.Tests tests/SimLab.$p.Tests
  git mv tests/SimLab.$p.Tests/Symlab.$p.Tests.csproj tests/SimLab.$p.Tests/SimLab.$p.Tests.csproj
done
git mv game/Symlab.Game.csproj game/SimLab.Game.csproj
git mv Symlab.slnx SimLab.slnx
```

- [ ] **Step 2: Replace identifiers and text**

```bash
grep -rlI --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj --exclude-dir=.godot --exclude-dir=.superpowers -e Symlab -e SYMLAB . \
  | grep -v '^./docs/superpowers/plans/' \
  | xargs sed -i '' -e 's/Symlab/SimLab/g' -e 's/SYMLAB_/SIMLAB_/g'
grep -rnI --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj --exclude-dir=.godot --exclude-dir=.superpowers -e Symlab -e 'SYMLAB_' . | grep -v '^./docs/superpowers/plans/'
```
Expected: the second grep prints nothing. Check `game/project.godot` (`config/name="SimLab"`, `project/assembly_name="SimLab.Game"`), `game/translations/strings.csv` (`APP_TITLE`: "SimLab — simulateur de vol RC" / "SimLab — RC flight simulator"), `README.md`, `docs/*.md`, `Directory.Build.props`, `InternalsVisibleTo`, the golden test path (`tests/SimLab.Flight.Tests/...`). The spec file keeps its file name; its text now says SimLab. Rename the spec file too: `git mv docs/superpowers/specs/2026-09-22-symlab-core-design.md docs/superpowers/specs/2026-09-22-simlab-core-design.md` and fix references to it (README, docs).

- [ ] **Step 3: Clean build and verify**

```bash
rm -rf src/*/bin src/*/obj tests/*/bin tests/*/obj game/.godot/mono
dotnet test
dotnet build game/SimLab.Game.csproj
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
"$GODOT" --headless --path game --import
"$GODOT" --headless --path game -- --smoke-boot
"$GODOT" --headless --path game -- --smoke-flight trainer 5
```
Expected: all tests pass (golden included), 0 warnings, `SIMLAB_BOOT_OK locale=fr title=SimLab — simulateur de vol RC`, `SIMLAB_SMOKE_OK aircraft=trainer ...`. Note in the report that Godot user data now lives under `app_userdata/SimLab` (old settings/radio profiles under `app_userdata/Symlab` are not migrated).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: rename Symlab to SimLab

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: World frame → ENU (x east, y north, z up)

Body frame is **unchanged** in this task.

**Files (SimLab.Flight):** `Geometry/Attitude.cs`, `Atmosphere/WindField.cs`, `Terrain/ITerrain.cs`, `Terrain/FlatTerrain.cs`, `Ground/GroundContactModel.cs`, `Airframe/Aircraft.cs`, `Sim/Simulation.cs`, `Sim/InitialConditions.cs`, `Sim/FlightEnvironment.cs`, `Recording/FlightRecorder.cs` (doc only), and every test using world vectors (`tests/SimLab.Flight.Tests/**`), plus `PilotFrame.East/North/Up`.
**Files (SimLab.App):** everything that stores world positions or calls terrain/attitude: `Field/*`, `Session/FlightSession.cs`, `Cameras/*` (tests only), `Mapping/GodotBasis.cs` (world mapping), and their tests. **Files (game):** every place that converts world vectors to Godot (`GodotConvert`, FieldBuilder, WindsockNode, FlightScene, AircraftVisual, Main previews).

**Interfaces (changed signatures):**
- `ITerrain.Height(double x, double y)`, `ITerrain.Normal(double x, double y)` (unit, +z up); `CylinderObstacle(double X, double Y, double Radius, double Height, double BaseZ = 0)`.
- `WindField.SteadyAt/At` return ENU vectors; downwind for "from D" = `(−sin D, −cos D, 0)`; turbulence vertical component along +z.
- Gravity `(0, 0, −m·g)`; height above ground `p.Z − Height(p.X, p.Y)`.
- `Attitude.FromOrientation(q)` with world up = +z, north = +y, east = +x: `pitch = asin(f.Z)`, `heading = atan2(f.X, f.Y)` (wrapped to [0, 2π)), `roll = atan2(−r.Z, u.Z)` where f, r, u are the body forward/right/up axes in world. `Attitude.ToOrientation` must round-trip (derive the heading rotation about +z for the **current** body axes; tests below pin it).
- `InitialConditions.InFlight(Vec3 position, ...)` and `OnGround(def, terrain, double x, double y, double headingDeg)` — position `(east, north, up)`.
- `SimLab.App`: `ClubField.PilotPosition = (0, −25, 0)`, `WindsockPosition = (20, −28, 0)`, `TakeoffPoint(heading) : (double X, double Y)`, `HandLaunchPoint : (X, Y, HeadingDeg)` with Y = −22; `SunMath.Direction(az, el) = (sin az·cos el, cos az·cos el, sin el)`; `Windsock.Pose` heading = `atan2(w.X, w.Y)`; `ClubFieldTerrain.GroundHeight(x, y)`; `TreePlanter` returns `CylinderObstacle(X, Y=−oldZ, ...)` (generate exactly as before and map z→−y so the forest is the mirror-consistent same forest); `TreePlanter.Excluded(x, y)`.
- `GodotBasis.WorldToGodot(Vec3 enu) : Vec3` = `(x, z, −y)`; `GodotBasis.NodeRotation` includes the world change. Game code must convert every world position/direction through `WorldToGodot` (add `GodotConvert.WorldToGodot(this Vec3)` and stop using a plain component copy for world vectors).

- [ ] **Step 1: Update tests first (they define the new frame)**

Relabel world literals in every test with the reference table (old `(x, y, z)` → new `(x, −z, y)`); keep assertions' meaning. In the golden test, relabel only the world positions in `Build()`: `OnGround(..., 12, -7, 130)` → `(..., 12, 7, 130)`, `(5, 80, -3)` → `(5, 3, 80)`, `(0, 1.8, 22)` → `(0, -22, 1.8)`, `(-20, 60, 10)` → `(-20, -10, 60)`; never touch the golden JSON. Add these new tests to `tests/SimLab.Flight.Tests/Geometry/GeometryTests.cs` (AttitudeTests):
```csharp
    [Fact]
    public void Heading_zero_points_north_in_enu()
    {
        var q = Attitude.ToOrientation(0, 0, 0);
        var forwardWorld = q.Rotate(BodyAxes.Forward);
        Approx.Equal(new Vec3(0, 1, 0), forwardWorld);
    }

    [Fact]
    public void Heading_ninety_points_east_and_pitch_up_raises_the_nose()
    {
        Approx.Equal(new Vec3(1, 0, 0), Attitude.ToOrientation(0, 0, Math.PI / 2).Rotate(BodyAxes.Forward));
        Assert.True(Attitude.ToOrientation(0, 0.3, 0).Rotate(BodyAxes.Forward).Z > 0);
        Assert.True(Attitude.ToOrientation(0.3, 0, 0).Rotate(BodyAxes.Right).Z < 0, "roll right lowers the right wing");
    }
```
Create `src/SimLab.Flight/Geometry/BodyAxes.cs` so tests do not hard-code body axes (Task 4 changes only this file for the body frame):
```csharp
namespace SimLab.Flight.Geometry;

/// <summary>Body-axis unit vectors. Current convention: x forward, y up, z right (switched to x back, y right, z up in the body-frame task).</summary>
public static class BodyAxes
{
    public static readonly Vec3 Forward = Vec3.UnitX;
    public static readonly Vec3 Up = Vec3.UnitY;
    public static readonly Vec3 Right = Vec3.UnitZ;
}
```
Add to `tests/SimLab.Flight.Tests/Terrain/TerrainTests.cs`: `FlatTerrain.Normal(0,0) == Vec3.UnitZ`; an obstacle `CylinderObstacle(10, 20, 2, 8)` contains `(11, 20, 5)` but not `(11, 20, 9)`. Update `PilotFrame.East/North/Up` to `p.X`, `p.Y`, `p.Z`.

- [ ] **Step 2: Run tests (expect failures), switch the code, iterate to green**

Apply the interface changes above in `SimLab.Flight`, then `SimLab.App` (+ tests), then `game/`. Use `BodyAxes.*` instead of `Vec3.UnitX/UnitY/UnitZ` wherever code means a body axis (Aircraft, InitialConditions, Attitude, AirData, SurfaceGeometry, PowerPlantLoads defaults, GroundContactModel wheel roll direction, AircraftMeshBuilder, GodotBasis, tests) — this makes Task 4 small.

```bash
dotnet test
```
Expected at the end: all green **including the golden test unchanged**.

- [ ] **Step 3: Game checks**

```bash
dotnet build game/SimLab.Game.csproj
"$GODOT" --headless --path game -- --smoke-flight trainer 5
"$GODOT" --path game -- --screenshot-field /tmp/simlab-field.png
"$GODOT" --path game -- --screenshot-flight trainer 9 /tmp/simlab-flight.png
```
Open both PNGs: same scene as before the change (pilot box south of the runway looking north-west toward the runway with the windsock to the right; trainer visible climbing). Describe in the report.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: use ENU world axes (x east, y north, z up)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Body frame → x back, y right, z up; aircraft datum + required cg

**Files (SimLab.Flight):** `Geometry/BodyAxes.cs`, `Geometry/Attitude.cs`, `Airframe/AirData.cs`, `Aero/SurfaceGeometry.cs` (base axes, incidence/dihedral rotations, sweep offset, mirror across y), `Aero/SurfaceSpec.cs` (docs), `Dynamics/MassProperties.cs` (document axis mapping of roll/yaw/pitch and product sign), `Airframe/AircraftLoader.cs` (`cg` required), `Airframe/AircraftDefinition.cs` (docs), any code with body literals; tests with body literals (`TestDefinitions`, `SurfaceGeometryTests`, `SurfaceAeroModelTests`, `PropulsionTests`, `GroundContactTests`, `Rk4IntegratorTests`, `AircraftLoaderTests`, behavior tests) + `PilotFrame` rates.
**Files (data):** `aircraft/*/aircraft.json`, `aircraft/*/power.json`.
**Files (App/game):** `Mapping/GodotBasis.cs` (`BodyToNodeLocal(b) = (b.Y, b.Z, b.X)`, NodeRotation), `Visual/AircraftMeshBuilder.cs`, their tests; `game/` previews using body axes.

**Interfaces:**
- `BodyAxes.Forward = (−1,0,0)`, `Right = (0,1,0)`, `Up = (0,0,1)`.
- `Attitude.ToOrientation(roll, pitch, heading) = Rz(−(heading + π/2)) · Ry(pitch) · Rx(−roll)` (Rk = `Quat.FromAxisAngle(unit k, angle)`); `FromOrientation` unchanged in form (uses `BodyAxes`).
- `AirData.From(u)`: `α = atan2(−u.Z, −u.X)`, `β = asin(u.Y / V)`.
- `MassProperties.FromPrincipal(mass, roll, yaw, pitch, rollYaw)`: roll = I_xx, pitch = I_yy, yaw = I_zz; product term between x and z (document sign: `rollYaw` = classical I_xz measured in these axes, entered as −I_xz off-diagonal).
- Surface geometry in body axes: chord axis toward the leading edge = `Forward`; normal = `Up`; span = `Right`; incidence = rotation about +y by +i; dihedral = rotation about +x by +Γ; quarter-chord sweep offset `(+span·t·tanΛ, span·t, 0)`; mirror = `(x, −y, z)`. Fin with dihedral 90° still has its normal pointing left (−y) and span up.
- `aircraft.json`: `"cg": [x, y, z]` **required** (InvalidDataException naming the file if missing); positions from a free datum.

- [ ] **Step 1: Convert the aircraft data (nose datum)**

Save as `/tmp/convert_frames.py` (not committed) and run it once:
```python
import json, pathlib
M = lambda v: [-v[0], v[2], v[1]]
root = pathlib.Path('/Users/axel.ldq/3_SYMLAB/aircraft')
for folder in [p for p in root.iterdir() if (p / 'aircraft.json').exists()]:
    a = json.loads((folder / 'aircraft.json').read_text())
    old_cg = a.get('cg', [0, 0, 0])
    nose = next(h['position'] for h in a['hull'] if h['tag'] == 'nose')
    to_datum = lambda p: M([p[i] - old_cg[i] - (nose[i] - old_cg[i]) for i in range(3)])
    for s in a['surfaces']: s['root'] = to_datum(s['root'])
    for b in a.get('bodies', []):
        b['position'] = to_datum(b['position'])
        b['cdA'] = [b['cdA'][0], b['cdA'][2], b['cdA'][1]]
    for w in a.get('gear', []): w['position'] = to_datum(w['position'])
    for h in a['hull']: h['position'] = to_datum(h['position'])
    a['cg'] = M([old_cg[i] - nose[i] for i in range(3)])
    (folder / 'aircraft.json').write_text(json.dumps(a, indent=2, ensure_ascii=False) + '\n')
    p = json.loads((folder / 'power.json').read_text())
    p['position'] = to_datum(p['position'])
    p['thrustAxis'] = M(p['thrustAxis'])
    (folder / 'power.json').write_text(json.dumps(p, indent=2) + '\n')
    print(folder.name, 'cg', a['cg'])
```
Expected: e.g. trainer `cg [0.5, 0.0, 0.0]` (0.5 m behind the nose), all positions have x ≥ 0 near the nose. Keep the key order and the `provenance` entries; add a provenance note `"cg": "estimated"`. (The JSON loses comments — none exist in these files.)

- [ ] **Step 2: Tests first**

- Update `BodyAxes` expectations and body literals in all tests (reference table: body `(x, y, z)` old → `(−x, z, y)`; angular rates the same map). Update `PilotFrame` rates: `RollRightRate = −ω.X`, `PitchUpRate = ω.Y`, `YawRightRate = −ω.Z`.
- Add to `AircraftLoaderTests`: a definition without `cg` → `InvalidDataException` whose message contains `aircraft.json`; with `"cg": [0.5, 0, 0]` and a nose hull point at `[0, 0, 0]` → loaded nose position `(−0.5, 0, 0)`.
- Add to `GeometryTests` (AttitudeTests): `Attitude.ToOrientation(0,0,0).Rotate(BodyAxes.Forward) == (0,1,0)`; round-trip theory unchanged.
- Add to `SurfaceGeometryTests`: right panel of a mirrored wing has segment positions with `Y > 0`; with 5° dihedral the tip has `Z > 0` and its normal has `Y < 0`; a fin (dihedral 90) has normal `(0, −1, 0)`.

- [ ] **Step 3: Switch the code and iterate to green**

Change `BodyAxes`, `Attitude.ToOrientation`, `AirData`, `SurfaceGeometry`, `MassProperties` docs, loader (`cg` required), `GodotBasis.BodyToNodeLocal`/`NodeRotation`, `AircraftMeshBuilder` (fuselage along x between min/max hull x; boxes/discs from body axes), and any remaining body literal. Behavior thresholds and scenarios stay untouched.
```bash
dotnet test
```
Expected: all green, **golden test unchanged**.

- [ ] **Step 4: Game checks**

```bash
dotnet build game/SimLab.Game.csproj
for id in trainer sport wing; do "$GODOT" --headless --path game -- --smoke-flight $id 5; done
for id in trainer wing; do "$GODOT" --path game -- --screenshot-aircraft $id /tmp/simlab-$id.png; done
"$GODOT" --path game -- --screenshot-flight trainer 9 /tmp/simlab-flight.png
```
Open the PNGs: aircraft right way up, fin up, controls at trailing edges deflected (trailing edge down), same pilot view as before. Describe in the report.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: use OpenVSP body axes (x back, y right, z up) and datum-based aircraft positions with cg

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Documentation and spec

**Files:** `docs/superpowers/specs/2026-09-22-simlab-core-design.md` (§3 coordinate conventions, §5 aircraft format: datum + required `cg`), `README.md` (conventions section), `docs/dev-setup.md`, `aircraft/README.md` (create: how to enter an aircraft — datum, axes, `cg`, angles), memory is handled by the controller.

- [ ] **Step 1: Write the conventions**

Replace the conventions text in the spec §3 and README with:
```markdown
- World: right-handed **x east, y north, z up** (ENU). Heading clockwise from north. Gravity −z.
- Body: right-handed **x back (toward the tail), y right (right wing), z up** — the OpenVSP convention.
  Forward = −x. Pilot rates: roll right = −ω_x, pitch up = +ω_y, yaw right = −ω_z.
- Control deflection: positive = trailing edge down relative to the surface normal.
- Godot mapping (game layer only, `SimLab.App.Mapping.GodotBasis`): world ENU → Godot `(x, z, −y)`; body → node local `(y, z, x)`.
```

`aircraft/README.md`:
```markdown
# Entering an aircraft

Positions are in body axes **x back, y right, z up**, in metres, measured from any datum you like
(the nose tip is used for the shipped aircraft). Give the centre of gravity with `"cg": [x, y, z]` in the
same datum — it is required. The simulator moves everything so the CG is the origin.

- Surfaces are defined by their right panel (`"mirror": true` makes the left one). `root` is the root
  quarter-chord point; `span` is the panel span; `sweepDeg`, `dihedralDeg`, `incidenceDeg`, `twistDeg`
  keep their usual meaning; a fin is a surface with `dihedralDeg: 90`.
- `power.json`: `position` of the prop disc (same datum), `thrustAxis` is the thrust direction
  (`[-1, 0, 0]` = straight ahead).
- Inertia: `roll` = I_xx, `pitch` = I_yy, `yaw` = I_zz (kg·m², about the CG).
```

- [ ] **Step 2: Verify and commit**

```bash
grep -rn "z south\|x forward, y up, z right" README.md docs/*.md docs/superpowers/specs/ aircraft/ || true
dotnet test
git add -A
git commit -m "docs: document ENU world axes, OpenVSP body axes and datum-based aircraft entry

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
Expected: the grep prints nothing (old conventions gone from current docs; historical plans are exempt).
