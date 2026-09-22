# Symlab Core Libraries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the headless, fully tested core of the Symlab RC flight simulator: the `Symlab.Flight` physics library (6DOF, discrete-surface aero, electric propulsion, ground contact, wind, three aircraft definitions) and the `Symlab.Input` radio-input library.

**Architecture:** Two pure .NET class libraries with no Godot dependency, each with an xUnit test project. Physics runs at a fixed 2 ms step (RK4) behind a `Simulation` accumulator; aerodynamics sum forces from spanwise surface segments using airfoil polars blended to flat-plate post-stall; aircraft are data folders (`aircraft.json`, `power.json`, airfoil polars). The Godot game layer (rendering, field, camera, menus) is a separate plan written after this one, against the real API produced here.

**Tech Stack:** .NET 10 SDK, C# (latest), libraries target `net8.0` (Godot-compatible), tests target `net10.0`, xUnit v2, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-09-22-symlab-core-design.md`

## Global Constraints

- All code, identifiers, comments, commit messages and docs in **English**.
- `Symlab.Flight` and `Symlab.Input` must **not** reference Godot or any third-party runtime package (BCL only).
- Units: SI, `double` precision everywhere. Angles in radians in code, degrees in JSON (fields end with `Deg`).
- World frame: right-handed, y-up, x east, z south. Body frame: **x forward, y up, z right**. +ω_x = roll right, +ω_z = pitch up, +ω_y = yaw **left**.
- Control-surface deflection sign: positive deflection = trailing edge **down** relative to the surface normal (increases the surface's lift along its normal).
- Physics fixed step: **2 ms (500 Hz)**, RK4.
- Mixing (elevons, V-tail, flaperons) lives in `aircraft.json` `mix` dictionaries; valid channel names: `throttle`, `aileron`, `elevator`, `rudder`, `flap`.
- `TreatWarningsAsErrors` is on; nullable reference types are on.
- Commit after every task with a Conventional Commit message ending with the line
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File Map

```
Directory.Build.props                      shared compiler settings
Symlab.slnx                                solution
src/Symlab.Flight/
  Symlab.Flight.csproj
  Geometry/  Vec3.cs Quat.cs Mat3.cs Angle.cs Attitude.cs
  Numerics/  Interpolation.cs
  Dynamics/  MassProperties.cs RigidBodyState.cs Rk4Integrator.cs
  Atmosphere/ Isa.cs WindSettings.cs WindField.cs
  Terrain/   ITerrain.cs FlatTerrain.cs
  Aero/      Airfoil.cs IAeroModel.cs InducedFlow.cs SurfaceSpec.cs SurfaceSegment.cs SurfaceGeometry.cs SurfaceAeroModel.cs
  Propulsion/ PowerPlantSpec.cs PropellerAero.cs PowerPlant.cs PowerPlantLoads.cs ThrustStand.cs ApcPerformanceFile.cs
  Controls/  ControlInputs.cs Servo.cs
  Ground/    GroundSpec.cs GroundContactModel.cs
  Airframe/  AircraftDefinition.cs AircraftLoader.cs LoaderDtos.cs Vec3JsonConverter.cs Aircraft.cs AirData.cs
  Sim/       FlightEnvironment.cs Simulation.cs InitialConditions.cs
  Recording/ FlightRecorder.cs
src/Symlab.Input/
  Symlab.Input.csproj
  RawInputFrame.cs AxisCalibration.cs ChannelPipeline.cs RadioProfile.cs CalibrationWizard.cs Switches.cs Keyboard.cs
tests/Symlab.Flight.Tests/   (mirrors folders above + Behavior/)
tests/Symlab.Input.Tests/
aircraft/airfoils/  clarky.json naca0012.json reflex-mh45.json
aircraft/trainer/   aircraft.json power.json
aircraft/sport/     aircraft.json power.json
aircraft/wing/      aircraft.json power.json
docs/tuning-log.md
```

---

### Task 1: Toolchain, solution scaffold, geometry and interpolation

**Files:**
- Create: `Directory.Build.props`, `Symlab.slnx`, `src/Symlab.Flight/Symlab.Flight.csproj`
- Create: `src/Symlab.Flight/Geometry/Vec3.cs`, `Quat.cs`, `Mat3.cs`, `Angle.cs`, `Attitude.cs`
- Create: `src/Symlab.Flight/Numerics/Interpolation.cs`
- Create: `tests/Symlab.Flight.Tests/Symlab.Flight.Tests.csproj`
- Test: `tests/Symlab.Flight.Tests/Geometry/GeometryTests.cs`, `tests/Symlab.Flight.Tests/Numerics/InterpolationTests.cs`

**Interfaces:**
- Produces: `Vec3(X,Y,Z)` with `+ - * /`, `Dot`, `Cross`, `Length`, `Normalized()`, `Zero`, `UnitX/Y/Z`; `Quat(X,Y,Z,W)` with `Identity`, `FromAxisAngle(Vec3, double)`, `*` (Hamilton product), `+`, `* double`, `Conjugate()`, `Normalized()`, `Rotate(Vec3)`, `InverseRotate(Vec3)`; `Mat3` with `Diagonal`, `* Vec3`, `Inverse()`; `Angle.Rad(deg)`, `Angle.Deg(rad)`; `Attitude(Roll, Pitch, Heading)` with `FromOrientation(Quat)` and `ToOrientation(roll, pitch, heading)` (radians, heading 0 = north = −Z, clockwise); `Interpolation.Linear(double[] xs, double[] ys, double x)` (clamped), `Interpolation.RequireIncreasing(double[] xs, string what)`.

- [ ] **Step 1: Install the toolchain**

```bash
brew install --cask dotnet-sdk
dotnet --version
```
Expected: `10.0.x`.

- [ ] **Step 2: Create shared build settings and projects**

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`src/Symlab.Flight/Symlab.Flight.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Symlab.Flight</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Symlab.Flight.Tests" />
  </ItemGroup>
</Project>
```

`tests/Symlab.Flight.Tests/Symlab.Flight.Tests.csproj`:
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
    <ProjectReference Include="../../src/Symlab.Flight/Symlab.Flight.csproj" />
  </ItemGroup>
</Project>
```

Then:
```bash
cd /Users/axel.ldq/3_SYMLAB
dotnet add tests/Symlab.Flight.Tests package Microsoft.NET.Test.Sdk
dotnet add tests/Symlab.Flight.Tests package xunit
dotnet add tests/Symlab.Flight.Tests package xunit.runner.visualstudio
dotnet new sln -n Symlab
dotnet sln add src/Symlab.Flight/Symlab.Flight.csproj tests/Symlab.Flight.Tests/Symlab.Flight.Tests.csproj
```
(`dotnet new sln` on SDK 10 creates `Symlab.slnx`.)

- [ ] **Step 3: Write the failing tests**

`tests/Symlab.Flight.Tests/Geometry/GeometryTests.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Geometry;

internal static class Approx
{
    public static void Equal(Vec3 expected, Vec3 actual, int precision = 9)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }
}

public class Vec3Tests
{
    [Fact]
    public void Cross_of_x_and_y_is_z() => Assert.Equal(Vec3.UnitZ, Vec3.Cross(Vec3.UnitX, Vec3.UnitY));

    [Fact]
    public void Length_of_3_4_0_is_5() => Assert.Equal(5.0, new Vec3(3, 4, 0).Length, 12);

    [Fact]
    public void Normalizing_zero_returns_zero() => Assert.Equal(Vec3.Zero, Vec3.Zero.Normalized());
}

public class QuatTests
{
    [Fact]
    public void Positive_rotation_about_z_pitches_nose_up()
        => Approx.Equal(Vec3.UnitY, Quat.FromAxisAngle(Vec3.UnitZ, Math.PI / 2).Rotate(Vec3.UnitX));

    [Fact]
    public void Positive_rotation_about_x_lowers_the_right_wing()
        => Approx.Equal(new Vec3(0, -1, 0), Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2).Rotate(Vec3.UnitZ));

    [Fact]
    public void Positive_rotation_about_y_yaws_nose_left()
        => Approx.Equal(new Vec3(0, 0, -1), Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2).Rotate(Vec3.UnitX));

    [Fact]
    public void InverseRotate_undoes_Rotate()
    {
        var q = Quat.FromAxisAngle(new Vec3(1, 2, 3), 0.7);
        var v = new Vec3(0.3, -1.2, 2.5);
        Approx.Equal(v, q.InverseRotate(q.Rotate(v)));
    }

    [Fact]
    public void Product_composes_rotations_right_to_left()
    {
        var a = Quat.FromAxisAngle(Vec3.UnitY, 0.4);
        var b = Quat.FromAxisAngle(Vec3.UnitX, -1.1);
        var v = new Vec3(1, 2, 3);
        Approx.Equal(a.Rotate(b.Rotate(v)), (a * b).Rotate(v));
    }
}

public class Mat3Tests
{
    [Fact]
    public void Inverse_undoes_multiplication()
    {
        var m = new Mat3(0.2, 0, -0.01, 0, 0.35, 0, -0.01, 0, 0.25);
        var v = new Vec3(1, -2, 0.5);
        Approx.Equal(v, m.Inverse() * (m * v));
    }

    [Fact]
    public void Singular_matrix_throws() => Assert.Throws<InvalidOperationException>(() => Mat3.Diagonal(1, 0, 1).Inverse());
}

public class AttitudeTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(30, 10, 45)]
    [InlineData(-60, -20, 270)]
    [InlineData(10, 5, 359)]
    public void Euler_round_trip(double rollDeg, double pitchDeg, double headingDeg)
    {
        var q = Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg));
        var a = Attitude.FromOrientation(q);
        Assert.Equal(rollDeg, Angle.Deg(a.Roll), 6);
        Assert.Equal(pitchDeg, Angle.Deg(a.Pitch), 6);
        Assert.Equal(headingDeg, Angle.Deg(a.Heading), 6);
    }

    [Fact]
    public void Heading_zero_points_north()
        => Approx.Equal(new Vec3(0, 0, -1), Attitude.ToOrientation(0, 0, 0).Rotate(Vec3.UnitX));

    [Fact]
    public void Heading_ninety_points_east()
        => Approx.Equal(Vec3.UnitX, Attitude.ToOrientation(0, 0, Math.PI / 2).Rotate(Vec3.UnitX));
}
```

`tests/Symlab.Flight.Tests/Numerics/InterpolationTests.cs`:
```csharp
using Symlab.Flight.Numerics;

namespace Symlab.Flight.Tests.Numerics;

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
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS (types `Vec3`, `Quat`, … not found).

- [ ] **Step 5: Implement geometry and interpolation**

`src/Symlab.Flight/Geometry/Vec3.cs`:
```csharp
namespace Symlab.Flight.Geometry;

/// <summary>Immutable double-precision 3-vector.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    public Vec3 Normalized()
    {
        var length = Length;
        return length > 1e-12 ? this / length : Zero;
    }
}
```

`src/Symlab.Flight/Geometry/Quat.cs`:
```csharp
namespace Symlab.Flight.Geometry;

/// <summary>Unit quaternion (x, y, z, w). As an orientation it maps body vectors to world vectors.</summary>
public readonly record struct Quat(double X, double Y, double Z, double W)
{
    public static readonly Quat Identity = new(0, 0, 0, 1);

    public static Quat FromAxisAngle(Vec3 axis, double angle)
    {
        var n = axis.Normalized();
        var s = Math.Sin(angle / 2);
        return new Quat(n.X * s, n.Y * s, n.Z * s, Math.Cos(angle / 2));
    }

    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public static Quat operator +(Quat a, Quat b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Quat operator *(Quat a, double s) => new(a.X * s, a.Y * s, a.Z * s, a.W * s);

    public Quat Conjugate() => new(-X, -Y, -Z, W);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z + W * W);

    public Quat Normalized()
    {
        var length = Length;
        return length > 1e-12 ? this * (1.0 / length) : Identity;
    }

    public Vec3 Rotate(Vec3 v)
    {
        var u = new Vec3(X, Y, Z);
        var t = 2.0 * Vec3.Cross(u, v);
        return v + W * t + Vec3.Cross(u, t);
    }

    public Vec3 InverseRotate(Vec3 v) => Conjugate().Rotate(v);
}
```

`src/Symlab.Flight/Geometry/Mat3.cs`:
```csharp
namespace Symlab.Flight.Geometry;

/// <summary>Row-major 3x3 matrix, used for inertia tensors.</summary>
public readonly record struct Mat3(
    double M00, double M01, double M02,
    double M10, double M11, double M12,
    double M20, double M21, double M22)
{
    public static Mat3 Diagonal(double a, double b, double c) => new(a, 0, 0, 0, b, 0, 0, 0, c);

    public static Vec3 operator *(Mat3 m, Vec3 v) => new(
        m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
        m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
        m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z);

    public double Determinant =>
        M00 * (M11 * M22 - M12 * M21) - M01 * (M10 * M22 - M12 * M20) + M02 * (M10 * M21 - M11 * M20);

    public Mat3 Inverse()
    {
        var det = Determinant;
        if (Math.Abs(det) < 1e-15) throw new InvalidOperationException("Matrix is singular.");
        var k = 1.0 / det;
        return new Mat3(
            (M11 * M22 - M12 * M21) * k, (M02 * M21 - M01 * M22) * k, (M01 * M12 - M02 * M11) * k,
            (M12 * M20 - M10 * M22) * k, (M00 * M22 - M02 * M20) * k, (M02 * M10 - M00 * M12) * k,
            (M10 * M21 - M11 * M20) * k, (M01 * M20 - M00 * M21) * k, (M00 * M11 - M01 * M10) * k);
    }
}
```

`src/Symlab.Flight/Geometry/Angle.cs`:
```csharp
namespace Symlab.Flight.Geometry;

public static class Angle
{
    public const double DegToRad = Math.PI / 180.0;
    public static double Rad(double degrees) => degrees * DegToRad;
    public static double Deg(double radians) => radians / DegToRad;
}
```

`src/Symlab.Flight/Geometry/Attitude.cs`:
```csharp
namespace Symlab.Flight.Geometry;

/// <summary>Pilot-convention Euler angles in radians: roll right +, pitch up +, heading clockwise from north in [0, 2π).</summary>
public readonly record struct Attitude(double Roll, double Pitch, double Heading)
{
    public static Attitude FromOrientation(Quat q)
    {
        var forward = q.Rotate(Vec3.UnitX);
        var up = q.Rotate(Vec3.UnitY);
        var right = q.Rotate(Vec3.UnitZ);
        var pitch = Math.Asin(Math.Clamp(forward.Y, -1, 1));
        var heading = Math.Atan2(forward.X, -forward.Z);
        if (heading < 0) heading += 2 * Math.PI;
        var roll = Math.Atan2(-right.Y, up.Y);
        return new Attitude(roll, pitch, heading);
    }

    /// <summary>Intrinsic heading, then pitch, then roll.</summary>
    public static Quat ToOrientation(double roll, double pitch, double heading) =>
        Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2 - heading)
        * Quat.FromAxisAngle(Vec3.UnitZ, pitch)
        * Quat.FromAxisAngle(Vec3.UnitX, roll);
}
```

`src/Symlab.Flight/Numerics/Interpolation.cs`:
```csharp
namespace Symlab.Flight.Numerics;

public static class Interpolation
{
    /// <summary>Piecewise-linear lookup on a strictly increasing axis, clamped to the end values.</summary>
    public static double Linear(double[] xs, double[] ys, double x)
    {
        int n = xs.Length;
        if (n == 0 || n != ys.Length) throw new ArgumentException("Table axes must be non-empty and of equal length.");
        if (x <= xs[0]) return ys[0];
        if (x >= xs[n - 1]) return ys[n - 1];
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (xs[mid] <= x) lo = mid; else hi = mid;
        }
        double t = (x - xs[lo]) / (xs[hi] - xs[lo]);
        return ys[lo] + t * (ys[hi] - ys[lo]);
    }

    public static void RequireIncreasing(double[] xs, string what)
    {
        for (int i = 1; i < xs.Length; i++)
            if (xs[i] <= xs[i - 1]) throw new ArgumentException($"{what} must be strictly increasing.");
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(flight): scaffold solution, geometry and interpolation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Rigid-body dynamics and RK4 integrator

**Files:**
- Create: `src/Symlab.Flight/Dynamics/MassProperties.cs`, `RigidBodyState.cs`, `Rk4Integrator.cs`
- Test: `tests/Symlab.Flight.Tests/Dynamics/Rk4IntegratorTests.cs`

**Interfaces:**
- Consumes: `Vec3`, `Quat`, `Mat3` (Task 1).
- Produces:
  - `MassProperties(double mass, Mat3 inertia)` with `Mass`, `Inertia`, `InverseInertia`; `MassProperties.FromPrincipal(double mass, double roll, double yaw, double pitch, double rollYaw = 0)` (roll = I_xx, yaw = I_yy, pitch = I_zz).
  - `RigidBodyState(Vec3 Position, Vec3 Velocity, Quat Orientation, Vec3 AngularVelocity)` — position/velocity in world, angular velocity in body.
  - `BodyLoad(Vec3 Force, Vec3 Moment)` (body axes, moment about CG) with `+` and `Zero`.
  - `Wrench(Vec3 ForceWorld, Vec3 TorqueBody)`; `delegate Wrench WrenchFunction(in RigidBodyState state)`.
  - `Rk4Integrator.Step(in RigidBodyState s, double dt, MassProperties m, WrenchFunction f)`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Dynamics/Rk4IntegratorTests.cs`:
```csharp
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Dynamics;

public class Rk4IntegratorTests
{
    const double Dt = 0.002;

    static RigidBodyState Run(RigidBodyState s, MassProperties m, WrenchFunction f, double seconds, double dt = Dt)
    {
        int n = (int)Math.Round(seconds / dt);
        for (int i = 0; i < n; i++) s = Rk4Integrator.Step(s, dt, m, f);
        return s;
    }

    static RigidBodyState AtRest => new(Vec3.Zero, Vec3.Zero, Quat.Identity, Vec3.Zero);

    [Fact]
    public void Free_fall_matches_analytic_solution()
    {
        var m = MassProperties.FromPrincipal(2, 1, 1, 1);
        var s = Run(AtRest, m, (in RigidBodyState _) => new Wrench(new Vec3(0, -2 * 9.81, 0), Vec3.Zero), 2.0);
        Assert.Equal(-0.5 * 9.81 * 4, s.Position.Y, 9);
        Assert.Equal(-9.81 * 2, s.Velocity.Y, 9);
    }

    [Fact]
    public void Torque_free_rotation_conserves_energy_and_angular_momentum()
    {
        var m = MassProperties.FromPrincipal(1, 0.1, 0.3, 0.2);
        var s0 = AtRest with { AngularVelocity = new Vec3(0.3, 5, 0.2) };
        static double Energy(RigidBodyState s, MassProperties m) => 0.5 * Vec3.Dot(s.AngularVelocity, m.Inertia * s.AngularVelocity);
        static Vec3 MomentumWorld(RigidBodyState s, MassProperties m) => s.Orientation.Rotate(m.Inertia * s.AngularVelocity);

        var s1 = Run(s0, m, (in RigidBodyState _) => new Wrench(Vec3.Zero, Vec3.Zero), 10.0);

        Assert.Equal(1.0, Energy(s1, m) / Energy(s0, m), 6);
        var l0 = MomentumWorld(s0, m);
        var l1 = MomentumWorld(s1, m);
        Assert.True((l1 - l0).Length / l0.Length < 1e-6, $"angular momentum drift {(l1 - l0).Length}");
        Assert.Equal(1.0, s1.Orientation.Length, 12);
    }

    [Fact]
    public void Constant_pitch_torque_gives_uniform_angular_acceleration()
    {
        var m = MassProperties.FromPrincipal(1, 0.1, 0.1, 0.5);
        var s = Run(AtRest, m, (in RigidBodyState _) => new Wrench(Vec3.Zero, new Vec3(0, 0, 0.5)), 1.0);
        Assert.Equal(1.0, s.AngularVelocity.Z, 9);
        var nose = s.Orientation.Rotate(Vec3.UnitX);
        Assert.Equal(Math.Cos(0.5), nose.X, 9);
        Assert.Equal(Math.Sin(0.5), nose.Y, 9);
    }

    [Fact]
    public void Error_shrinks_at_fourth_order()
    {
        var m = MassProperties.FromPrincipal(1, 0.1, 0.3, 0.2);
        var s0 = AtRest with { AngularVelocity = new Vec3(2, 1, 3) };
        WrenchFunction f = (in RigidBodyState _) => new Wrench(Vec3.Zero, Vec3.Zero);
        var reference = Run(s0, m, f, 2.0, 0.02 / 16).Orientation.Rotate(Vec3.UnitX);
        var coarse = Run(s0, m, f, 2.0, 0.02).Orientation.Rotate(Vec3.UnitX);
        var fine = Run(s0, m, f, 2.0, 0.01).Orientation.Rotate(Vec3.UnitX);
        var ratio = (coarse - reference).Length / (fine - reference).Length;
        Assert.True(ratio > 10, $"error ratio {ratio}");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~Rk4IntegratorTests`
Expected: build FAILS (`Rk4Integrator` not found).

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Dynamics/MassProperties.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Dynamics;

public sealed class MassProperties
{
    public MassProperties(double mass, Mat3 inertia)
    {
        if (mass <= 0) throw new ArgumentOutOfRangeException(nameof(mass), "Mass must be positive.");
        Mass = mass;
        Inertia = inertia;
        InverseInertia = inertia.Inverse();
    }

    public double Mass { get; }
    public Mat3 Inertia { get; }
    public Mat3 InverseInertia { get; }

    /// <summary>Body-axis inertia: roll = I_xx, yaw = I_yy, pitch = I_zz, rollYaw = product I_xy.</summary>
    public static MassProperties FromPrincipal(double mass, double roll, double yaw, double pitch, double rollYaw = 0) =>
        new(mass, new Mat3(roll, -rollYaw, 0, -rollYaw, yaw, 0, 0, 0, pitch));
}
```

`src/Symlab.Flight/Dynamics/RigidBodyState.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Dynamics;

/// <summary>Position and velocity in world axes; orientation maps body to world; angular velocity in body axes.</summary>
public readonly record struct RigidBodyState(Vec3 Position, Vec3 Velocity, Quat Orientation, Vec3 AngularVelocity);

/// <summary>Force and moment about the CG, both in body axes.</summary>
public readonly record struct BodyLoad(Vec3 Force, Vec3 Moment)
{
    public static readonly BodyLoad Zero = new(Vec3.Zero, Vec3.Zero);
    public static BodyLoad operator +(BodyLoad a, BodyLoad b) => new(a.Force + b.Force, a.Moment + b.Moment);
}

public readonly record struct Wrench(Vec3 ForceWorld, Vec3 TorqueBody);

public delegate Wrench WrenchFunction(in RigidBodyState state);
```

`src/Symlab.Flight/Dynamics/Rk4Integrator.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Dynamics;

public static class Rk4Integrator
{
    readonly record struct Derivative(Vec3 Position, Vec3 Velocity, Quat Orientation, Vec3 AngularVelocity);

    public static RigidBodyState Step(in RigidBodyState s, double dt, MassProperties m, WrenchFunction f)
    {
        var k1 = Evaluate(s, m, f);
        var k2 = Evaluate(Apply(s, k1, dt / 2), m, f);
        var k3 = Evaluate(Apply(s, k2, dt / 2), m, f);
        var k4 = Evaluate(Apply(s, k3, dt), m, f);
        var h = dt / 6.0;
        return new RigidBodyState(
            s.Position + (k1.Position + 2 * k2.Position + 2 * k3.Position + k4.Position) * h,
            s.Velocity + (k1.Velocity + 2 * k2.Velocity + 2 * k3.Velocity + k4.Velocity) * h,
            (s.Orientation + (k1.Orientation + k2.Orientation * 2 + k3.Orientation * 2 + k4.Orientation) * h).Normalized(),
            s.AngularVelocity + (k1.AngularVelocity + 2 * k2.AngularVelocity + 2 * k3.AngularVelocity + k4.AngularVelocity) * h);
    }

    static Derivative Evaluate(in RigidBodyState s, MassProperties m, WrenchFunction f)
    {
        var w = f(s);
        var omega = s.AngularVelocity;
        var spin = new Quat(omega.X, omega.Y, omega.Z, 0);
        var angularAcceleration = m.InverseInertia * (w.TorqueBody - Vec3.Cross(omega, m.Inertia * omega));
        return new Derivative(s.Velocity, w.ForceWorld / m.Mass, (s.Orientation * spin) * 0.5, angularAcceleration);
    }

    static RigidBodyState Apply(in RigidBodyState s, in Derivative d, double h) => new(
        s.Position + d.Position * h,
        s.Velocity + d.Velocity * h,
        (s.Orientation + d.Orientation * h).Normalized(),
        s.AngularVelocity + d.AngularVelocity * h);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~Rk4IntegratorTests`
Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(flight): add rigid-body state and RK4 integrator

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Atmosphere, wind and terrain

**Files:**
- Create: `src/Symlab.Flight/Atmosphere/Isa.cs`, `WindSettings.cs`, `WindField.cs`
- Create: `src/Symlab.Flight/Terrain/ITerrain.cs`, `FlatTerrain.cs`
- Test: `tests/Symlab.Flight.Tests/Atmosphere/AtmosphereTests.cs`, `tests/Symlab.Flight.Tests/Terrain/TerrainTests.cs`

**Interfaces:**
- Consumes: `Vec3`, `Angle` (Task 1).
- Produces:
  - `Isa.Density(double altitudeM, double temperatureOffsetK = 0)`, `Isa.SeaLevelDensity = 1.225`, `Isa.DynamicViscosity = 1.81e-5`.
  - `WindSettings(double SpeedAt10m = 0, double FromDirectionDeg = 0, double Turbulence = 0, double RoughnessLength = 0.03)` — `FromDirectionDeg` is the meteorological direction the wind blows **from** (0 = north, 90 = east); `Turbulence` scales σ (1 = moderate).
  - `WindField(WindSettings settings, int seed)` with `SteadyAt(double heightAgl)`, `Turbulence` (Vec3), `At(double heightAgl)`, `Advance(double dt, double heightAgl, double airspeed)`.
  - `ITerrain { double Height(double x, double z); Vec3 Normal(double x, double z); bool HitsObstacle(Vec3 p); }`
  - `CylinderObstacle(double X, double Z, double Radius, double Height, double BaseY = 0)` with `Contains(Vec3)`.
  - `FlatTerrain(double elevation = 0, IEnumerable<CylinderObstacle>? obstacles = null)`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Atmosphere/AtmosphereTests.cs`:
```csharp
using Symlab.Flight.Atmosphere;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Atmosphere;

public class IsaTests
{
    [Fact]
    public void Sea_level_density() => Assert.Equal(1.225, Isa.Density(0), 3);

    [Fact]
    public void Density_at_1000_m() => Assert.Equal(1.112, Isa.Density(1000), 2);

    [Fact]
    public void Warmer_air_is_thinner() => Assert.True(Isa.Density(0, 15) < Isa.Density(0));
}

public class WindFieldTests
{
    [Fact]
    public void Wind_from_west_blows_toward_east()
    {
        var wind = new WindField(new WindSettings(SpeedAt10m: 5, FromDirectionDeg: 270), seed: 1);
        var v = wind.SteadyAt(10);
        Assert.Equal(5, v.X, 9);
        Assert.Equal(0, v.Y, 9);
        Assert.Equal(0, v.Z, 9);
    }

    [Fact]
    public void Wind_from_north_blows_toward_south()
        => Assert.Equal(4, new WindField(new WindSettings(4, 0), 1).SteadyAt(10).Z, 9);

    [Fact]
    public void Log_profile_is_weaker_near_the_ground()
    {
        var wind = new WindField(new WindSettings(SpeedAt10m: 6), 1);
        Assert.True(wind.SteadyAt(2).Length < wind.SteadyAt(10).Length);
        Assert.True(wind.SteadyAt(50).Length > wind.SteadyAt(10).Length);
    }

    [Fact]
    public void No_turbulence_setting_means_zero_gusts()
    {
        var wind = new WindField(new WindSettings(SpeedAt10m: 6, Turbulence: 0), 1);
        for (int i = 0; i < 1000; i++) wind.Advance(0.002, 30, 15);
        Assert.Equal(Vec3.Zero, wind.Turbulence);
    }

    [Fact]
    public void Dryden_vertical_gust_has_expected_standard_deviation()
    {
        var wind = new WindField(new WindSettings(SpeedAt10m: 6, Turbulence: 1), seed: 42);
        double expectedSigma = 0.1 * wind.SteadyAt(6.1).Length;
        double sum = 0, sumSq = 0;
        int n = 0;
        for (int i = 0; i < 100_000; i++)
        {
            wind.Advance(0.002, 50, 15);
            double w = wind.Turbulence.Y;
            sum += w; sumSq += w * w; n++;
        }
        double mean = sum / n;
        double sigma = Math.Sqrt(sumSq / n - mean * mean);
        Assert.InRange(sigma, 0.75 * expectedSigma, 1.25 * expectedSigma);
        Assert.True(Math.Abs(mean) < 0.4 * expectedSigma, $"mean {mean}");
    }

    [Fact]
    public void Same_seed_gives_same_turbulence()
    {
        var a = new WindField(new WindSettings(6, 0, 1), 7);
        var b = new WindField(new WindSettings(6, 0, 1), 7);
        for (int i = 0; i < 500; i++) { a.Advance(0.002, 20, 12); b.Advance(0.002, 20, 12); }
        Assert.Equal(a.Turbulence, b.Turbulence);
    }
}
```

`tests/Symlab.Flight.Tests/Terrain/TerrainTests.cs`:
```csharp
using Symlab.Flight.Geometry;
using Symlab.Flight.Terrain;

namespace Symlab.Flight.Tests.Terrain;

public class TerrainTests
{
    [Fact]
    public void Flat_terrain_has_constant_height_and_up_normal()
    {
        var t = new FlatTerrain(12.5);
        Assert.Equal(12.5, t.Height(100, -300));
        Assert.Equal(Vec3.UnitY, t.Normal(0, 0));
    }

    [Fact]
    public void Point_inside_tree_cylinder_hits_obstacle()
    {
        var t = new FlatTerrain(0, [new CylinderObstacle(10, 20, 2, 8)]);
        Assert.True(t.HitsObstacle(new Vec3(11, 5, 20)));
        Assert.False(t.HitsObstacle(new Vec3(11, 9, 20)));
        Assert.False(t.HitsObstacle(new Vec3(13, 5, 20)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~Atmosphere|FullyQualifiedName~Terrain"`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Atmosphere/Isa.cs`:
```csharp
namespace Symlab.Flight.Atmosphere;

/// <summary>International Standard Atmosphere, troposphere only.</summary>
public static class Isa
{
    public const double SeaLevelDensity = 1.225;
    public const double DynamicViscosity = 1.81e-5;
    const double SeaLevelPressure = 101325;
    const double SeaLevelTemperature = 288.15;
    const double LapseRate = 0.0065;
    const double GasConstant = 287.05;

    public static double Density(double altitudeM, double temperatureOffsetK = 0)
    {
        double pressure = SeaLevelPressure * Math.Pow(1 - LapseRate * altitudeM / SeaLevelTemperature, 5.2559);
        double temperature = SeaLevelTemperature + temperatureOffsetK - LapseRate * altitudeM;
        return pressure / (GasConstant * temperature);
    }
}
```

`src/Symlab.Flight/Atmosphere/WindSettings.cs`:
```csharp
namespace Symlab.Flight.Atmosphere;

/// <param name="SpeedAt10m">Mean wind speed at 10 m above ground, m/s.</param>
/// <param name="FromDirectionDeg">Direction the wind blows from, degrees clockwise from north.</param>
/// <param name="Turbulence">Turbulence scale factor: 0 none, 1 moderate.</param>
/// <param name="RoughnessLength">Surface roughness length for the log wind profile, m.</param>
public sealed record WindSettings(
    double SpeedAt10m = 0,
    double FromDirectionDeg = 0,
    double Turbulence = 0,
    double RoughnessLength = 0.03);
```

`src/Symlab.Flight/Atmosphere/WindField.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Atmosphere;

/// <summary>
/// Steady wind with a logarithmic ground profile plus low-altitude Dryden turbulence
/// (MIL-F-8785C), implemented as seeded first-order shaping filters.
/// </summary>
public sealed class WindField
{
    const double ReferenceHeight = 10.0;
    const double TwentyFeet = 6.1;
    readonly Random _random;
    double _u, _v, _w;

    public WindField(WindSettings settings, int seed)
    {
        Settings = settings;
        _random = new Random(seed);
    }

    public WindSettings Settings { get; }
    public Vec3 Turbulence { get; private set; }

    Vec3 Downwind
    {
        get
        {
            var from = Angle.Rad(Settings.FromDirectionDeg);
            return new Vec3(-Math.Sin(from), 0, Math.Cos(from));
        }
    }

    public Vec3 SteadyAt(double heightAgl)
    {
        if (Settings.SpeedAt10m <= 0) return Vec3.Zero;
        double z0 = Settings.RoughnessLength;
        double h = Math.Max(heightAgl, 0.5);
        double speed = Settings.SpeedAt10m * Math.Log(h / z0) / Math.Log(ReferenceHeight / z0);
        return Downwind * speed;
    }

    public Vec3 At(double heightAgl) => SteadyAt(heightAgl) + Turbulence;

    public void Advance(double dt, double heightAgl, double airspeed)
    {
        double sigmaW = 0.1 * SteadyAt(TwentyFeet).Length * Settings.Turbulence;
        if (sigmaW <= 0)
        {
            _u = _v = _w = 0;
            Turbulence = Vec3.Zero;
            return;
        }

        double h = Math.Clamp(heightAgl, 3.0, 300.0);
        double k = 0.177 + 0.000823 * h * 3.28084;
        double sigmaUV = sigmaW / Math.Pow(k, 0.4);
        double lengthW = h;
        double lengthUV = h / Math.Pow(k, 1.2);
        double speed = Math.Max(airspeed, 1.0);

        _u = Filter(_u, sigmaUV, lengthUV, speed, dt);
        _v = Filter(_v, sigmaUV, lengthUV, speed, dt);
        _w = Filter(_w, sigmaW, lengthW, speed, dt);

        var along = Downwind;
        var across = Vec3.Cross(Vec3.UnitY, along);
        Turbulence = along * _u + across * _v + Vec3.UnitY * _w;
    }

    double Filter(double x, double sigma, double length, double speed, double dt)
    {
        double a = Math.Exp(-speed * dt / length);
        return a * x + sigma * Math.Sqrt(1 - a * a) * Gaussian();
    }

    double Gaussian()
    {
        double u1 = 1.0 - _random.NextDouble();
        double u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
```

`src/Symlab.Flight/Terrain/ITerrain.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Terrain;

public interface ITerrain
{
    /// <summary>Ground height (world y) at a horizontal position.</summary>
    double Height(double x, double z);

    /// <summary>Unit ground normal at a horizontal position.</summary>
    Vec3 Normal(double x, double z);

    /// <summary>True if the world point is inside an obstacle (tree, fence, building).</summary>
    bool HitsObstacle(Vec3 p);
}

/// <summary>Vertical cylinder obstacle, e.g. a tree.</summary>
public readonly record struct CylinderObstacle(double X, double Z, double Radius, double Height, double BaseY = 0)
{
    public bool Contains(Vec3 p)
    {
        if (p.Y < BaseY || p.Y > BaseY + Height) return false;
        double dx = p.X - X, dz = p.Z - Z;
        return dx * dx + dz * dz <= Radius * Radius;
    }
}
```

`src/Symlab.Flight/Terrain/FlatTerrain.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Terrain;

public sealed class FlatTerrain : ITerrain
{
    readonly double _elevation;
    readonly CylinderObstacle[] _obstacles;

    public FlatTerrain(double elevation = 0, IEnumerable<CylinderObstacle>? obstacles = null)
    {
        _elevation = elevation;
        _obstacles = obstacles?.ToArray() ?? [];
    }

    public double Height(double x, double z) => _elevation;

    public Vec3 Normal(double x, double z) => Vec3.UnitY;

    public bool HitsObstacle(Vec3 p)
    {
        foreach (var o in _obstacles)
            if (o.Contains(p)) return true;
        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~Atmosphere|FullyQualifiedName~Terrain"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(flight): add ISA, wind field with Dryden turbulence and flat terrain

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Airfoil polars with ±180° post-stall

**Files:**
- Create: `src/Symlab.Flight/Aero/Airfoil.cs`
- Test: `tests/Symlab.Flight.Tests/Aero/AirfoilTests.cs`

**Interfaces:**
- Consumes: `Interpolation`, `Angle`.
- Produces:
  - `AirfoilTable(double Reynolds, double[] AlphaDeg, double[] Cl, double[] Cd, double[] Cm)`.
  - `AirfoilCoefficients(double Cl, double Cd, double Cm)` with static `Lerp(a, b, t)`.
  - `Airfoil(string name, IEnumerable<AirfoilTable> tables)` with `Name`, `Evaluate(double alphaRad, double reynolds)`, static `FlatPlate(double alphaRad, double cd0)`, constants `FlatPlateNormalCoefficient = 1.98`, `BlendWidthRad` (8°).

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Aero/AirfoilTests.cs`:
```csharp
using Symlab.Flight.Aero;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Aero;

public class AirfoilTests
{
    static Airfoil Sample() => new("sample",
    [
        new AirfoilTable(100_000, [-10, 0, 10, 14], [-0.8, 0.2, 1.1, 1.2], [0.02, 0.01, 0.02, 0.05], [-0.05, -0.05, -0.05, -0.06]),
        new AirfoilTable(300_000, [-10, 0, 10, 14], [-0.9, 0.2, 1.2, 1.3], [0.015, 0.008, 0.015, 0.04], [-0.05, -0.05, -0.05, -0.06]),
    ]);

    [Fact]
    public void Returns_table_values_at_nodes()
    {
        var c = Sample().Evaluate(Angle.Rad(10), 100_000);
        Assert.Equal(1.1, c.Cl, 9);
        Assert.Equal(0.02, c.Cd, 9);
    }

    [Fact]
    public void Interpolates_between_alpha_nodes() => Assert.Equal(0.65, Sample().Evaluate(Angle.Rad(5), 100_000).Cl, 9);

    [Fact]
    public void Interpolates_between_reynolds_tables() => Assert.Equal(1.15, Sample().Evaluate(Angle.Rad(10), 200_000).Cl, 9);

    [Fact]
    public void Clamps_reynolds_outside_the_data() => Assert.Equal(1.2, Sample().Evaluate(Angle.Rad(10), 5_000_000).Cl, 9);

    [Fact]
    public void At_ninety_degrees_behaves_like_a_flat_plate()
    {
        var c = Sample().Evaluate(Math.PI / 2, 200_000);
        Assert.True(Math.Abs(c.Cl) < 0.05, $"Cl {c.Cl}");
        Assert.InRange(c.Cd, 1.9, 2.1);
        Assert.True(c.Cm < 0);
    }

    [Fact]
    public void Coefficients_are_continuous_over_the_full_circle()
    {
        var airfoil = Sample();
        var previous = airfoil.Evaluate(Angle.Rad(-180), 200_000);
        for (double a = -179.9; a <= 180.0; a += 0.1)
        {
            var c = airfoil.Evaluate(Angle.Rad(a), 200_000);
            Assert.True(Math.Abs(c.Cl - previous.Cl) < 0.05, $"Cl jump at {a:F1} deg");
            Assert.True(Math.Abs(c.Cd - previous.Cd) < 0.05, $"Cd jump at {a:F1} deg");
            previous = c;
        }
    }

    [Fact]
    public void Lift_drops_after_the_last_data_point()
    {
        var airfoil = Sample();
        Assert.True(airfoil.Evaluate(Angle.Rad(24), 100_000).Cl < airfoil.Evaluate(Angle.Rad(14), 100_000).Cl);
    }

    [Fact]
    public void Rejects_mismatched_arrays()
        => Assert.Throws<ArgumentException>(() => new Airfoil("bad", [new AirfoilTable(1e5, [0, 1], [0], [0, 0], [0, 0])]));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~AirfoilTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Aero/Airfoil.cs`:
```csharp
using Symlab.Flight.Geometry;
using Symlab.Flight.Numerics;

namespace Symlab.Flight.Aero;

public sealed record AirfoilTable(double Reynolds, double[] AlphaDeg, double[] Cl, double[] Cd, double[] Cm);

public readonly record struct AirfoilCoefficients(double Cl, double Cd, double Cm)
{
    public static AirfoilCoefficients Lerp(AirfoilCoefficients a, AirfoilCoefficients b, double t) =>
        new(a.Cl + (b.Cl - a.Cl) * t, a.Cd + (b.Cd - a.Cd) * t, a.Cm + (b.Cm - a.Cm) * t);
}

/// <summary>
/// 2D airfoil polars over one or more Reynolds numbers. Outside the tabulated alpha range the
/// coefficients blend smoothly (over <see cref="BlendWidthRad"/>) into a flat-plate model valid to ±180°.
/// </summary>
public sealed class Airfoil
{
    public const double FlatPlateNormalCoefficient = 1.98;
    public static readonly double BlendWidthRad = Angle.Rad(8);

    sealed record Prepared(double Reynolds, double[] Alpha, double[] Cl, double[] Cd, double[] Cm, double Cd0);

    readonly Prepared[] _tables;

    public Airfoil(string name, IEnumerable<AirfoilTable> tables)
    {
        Name = name;
        _tables = tables.OrderBy(t => t.Reynolds).Select(Prepare).ToArray();
        if (_tables.Length == 0) throw new ArgumentException($"Airfoil '{name}' has no tables.");
    }

    public string Name { get; }

    public AirfoilCoefficients Evaluate(double alphaRad, double reynolds)
    {
        double a = Math.IEEERemainder(alphaRad, 2 * Math.PI);
        if (_tables.Length == 1 || reynolds <= _tables[0].Reynolds) return Sample(_tables[0], a);
        var last = _tables[^1];
        if (reynolds >= last.Reynolds) return Sample(last, a);
        int i = 0;
        while (_tables[i + 1].Reynolds < reynolds) i++;
        var lo = _tables[i];
        var hi = _tables[i + 1];
        double t = (reynolds - lo.Reynolds) / (hi.Reynolds - lo.Reynolds);
        return AirfoilCoefficients.Lerp(Sample(lo, a), Sample(hi, a), t);
    }

    public static AirfoilCoefficients FlatPlate(double alphaRad, double cd0)
    {
        double cn = FlatPlateNormalCoefficient * Math.Sin(alphaRad);
        double centerOfPressureArm = 0.25 - 0.175 * (1 - 2 * Math.Abs(alphaRad) / Math.PI);
        return new AirfoilCoefficients(cn * Math.Cos(alphaRad), cd0 + cn * Math.Sin(alphaRad), -cn * centerOfPressureArm);
    }

    static AirfoilCoefficients Sample(Prepared t, double a)
    {
        double lo = t.Alpha[0], hi = t.Alpha[^1];
        double clamped = Math.Clamp(a, lo, hi);
        var attached = new AirfoilCoefficients(
            Interpolation.Linear(t.Alpha, t.Cl, clamped),
            Interpolation.Linear(t.Alpha, t.Cd, clamped),
            Interpolation.Linear(t.Alpha, t.Cm, clamped));
        double excess = a > hi ? a - hi : a < lo ? lo - a : 0;
        if (excess <= 0) return attached;
        return AirfoilCoefficients.Lerp(attached, FlatPlate(a, t.Cd0), SmoothStep(excess / BlendWidthRad));
    }

    static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    static Prepared Prepare(AirfoilTable t)
    {
        int n = t.AlphaDeg.Length;
        if (n < 2 || t.Cl.Length != n || t.Cd.Length != n || t.Cm.Length != n)
            throw new ArgumentException("Airfoil table arrays must all have the same length (at least 2).");
        Interpolation.RequireIncreasing(t.AlphaDeg, "alphaDeg");
        return new Prepared(t.Reynolds, t.AlphaDeg.Select(Angle.Rad).ToArray(), t.Cl, t.Cd, t.Cm, t.Cd.Min());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~AirfoilTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(aero): add airfoil polars with flat-plate post-stall blend

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Surface geometry and the discrete-surface aero model

**Files:**
- Create: `src/Symlab.Flight/Aero/IAeroModel.cs`, `InducedFlow.cs`, `SurfaceSpec.cs`, `SurfaceSegment.cs`, `SurfaceGeometry.cs`, `SurfaceAeroModel.cs`
- Test: `tests/Symlab.Flight.Tests/Aero/TestAirfoils.cs`, `SurfaceGeometryTests.cs`, `InducedFlowTests.cs`, `SurfaceAeroModelTests.cs`

**Interfaces:**
- Consumes: `Airfoil` (Task 4), `BodyLoad` (Task 2), `Isa.DynamicViscosity` (Task 3), geometry.
- Produces:
  - `enum SurfaceRole { Wing, HorizontalTail, VerticalTail, Other }`, `enum Side { Both, Right, Left }` (`Right` = the panel as defined, `Left` = its mirror).
  - `SurfaceSpec(string Name, SurfaceRole Role, Vec3 Root, double Span, double RootChord, double TipChord, double SweepDeg, double DihedralDeg, double IncidenceDeg, double TwistDeg, string Airfoil, int Segments, bool Mirror, double Oswald = 0.85)` with `PanelArea`, `TotalArea`, `TotalSpan`, `AspectRatio`. `Root` is the root quarter-chord point in body axes; a mirrored surface is defined as its right (+z) panel.
  - `ControlSurfaceSpec(string Name, string Surface, Side Side, double ChordFraction, double SpanStart, double SpanEnd, double MaxPositiveDeg, double MaxNegativeDeg, double ServoSecondsPer60Deg, IReadOnlyDictionary<string, double> Mix)`.
  - `BodySpec(string Name, Vec3 Position, Vec3 CdA)` — drag areas (m²) along body x, y, z.
  - `SurfaceSegment` (see code), `SurfaceGeometry.Build(SurfaceSpec, Airfoil)`, `SurfaceGeometry.FlapEffectiveness(double chordFraction)`.
  - `PropWash(Vec3 PositionBody, Vec3 AxisBody, double Radius, double Velocity)`.
  - `AeroContext(Vec3 AirVelocityBody, Vec3 AngularVelocityBody, double Density, double HeightAboveGround, Vec3 UpBody, IReadOnlyList<double> Deflections, PropWash Wash)` — `AirVelocityBody` is the CG velocity **relative to the air mass**, body axes; `Deflections` are radians indexed like the model's control list.
  - `interface IAeroModel { BodyLoad Evaluate(in AeroContext ctx); void Advance(double dt); void Reset(); }`
  - `InducedFlow.SolveInducedAngle(Airfoil, double reynolds, double alphaGeo, double inducedFactor)`, `InducedFlow.GroundEffectFactor(double height, double span)`.
  - `SurfaceAeroModel(IEnumerable<SurfaceSpec>, IReadOnlyDictionary<string, Airfoil>, IReadOnlyList<ControlSurfaceSpec>, IEnumerable<BodySpec>)` implementing `IAeroModel`, with `Segments`, `Downwash`, `WingSpan`, `WingArea`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Aero/TestAirfoils.cs`:
```csharp
using Symlab.Flight.Aero;

namespace Symlab.Flight.Tests.Aero;

internal static class TestAirfoils
{
    /// <summary>Thin-airfoil lift slope (2π/rad) between -10° and +10°, constant Cd 0.01, Cm 0.</summary>
    public static Airfoil Linear()
    {
        double[] alpha = [-10, -5, 0, 5, 10];
        return new Airfoil("linear", [new AirfoilTable(
            200_000, alpha,
            alpha.Select(a => 2 * Math.PI * a * Math.PI / 180).ToArray(),
            alpha.Select(_ => 0.01).ToArray(),
            alpha.Select(_ => 0.0).ToArray())]);
    }

    public static IReadOnlyDictionary<string, Airfoil> Map() => new Dictionary<string, Airfoil> { ["linear"] = Linear() };
}
```

`tests/Symlab.Flight.Tests/Aero/SurfaceGeometryTests.cs`:
```csharp
using Symlab.Flight.Aero;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Aero;

public class SurfaceGeometryTests
{
    static SurfaceSpec Wing(double dihedral = 0) => new("wing", SurfaceRole.Wing, new Vec3(0, 0, 0), 0.75, 0.3, 0.2,
        SweepDeg: 0, DihedralDeg: dihedral, IncidenceDeg: 0, TwistDeg: 0, Airfoil: "linear", Segments: 5, Mirror: true);

    [Fact]
    public void Mirrored_surface_has_twice_the_segments_and_the_full_area()
    {
        var segments = SurfaceGeometry.Build(Wing(), TestAirfoils.Linear());
        Assert.Equal(10, segments.Count);
        Assert.Equal(Wing().TotalArea, segments.Sum(s => s.Area), 12);
        Assert.All(segments.Where(s => s.Side == Side.Right), s => Assert.True(s.Position.Z > 0));
        Assert.All(segments.Where(s => s.Side == Side.Left), s => Assert.True(s.Position.Z < 0));
    }

    [Fact]
    public void Dihedral_raises_the_tips_and_tilts_normals_inward()
    {
        var segments = SurfaceGeometry.Build(Wing(dihedral: 5), TestAirfoils.Linear());
        var rightTip = segments.Where(s => s.Side == Side.Right).MaxBy(s => s.Position.Z)!;
        var leftTip = segments.Where(s => s.Side == Side.Left).MinBy(s => s.Position.Z)!;
        Assert.True(rightTip.Position.Y > 0);
        Assert.True(rightTip.NormalAxis.Z < 0);
        Assert.True(leftTip.NormalAxis.Z > 0);
        Assert.True(rightTip.PitchAxis.Z > 0.99 && leftTip.PitchAxis.Z > 0.99, "both panels pitch up about +z");
    }

    [Fact]
    public void Vertical_fin_normal_points_left_and_span_points_up()
    {
        var fin = new SurfaceSpec("fin", SurfaceRole.VerticalTail, new Vec3(-0.8, 0, 0), 0.2, 0.2, 0.1,
            0, 90, 0, 0, "linear", 3, Mirror: false);
        var segments = SurfaceGeometry.Build(fin, TestAirfoils.Linear());
        Assert.All(segments, s => Approx(new Vec3(0, 0, -1), s.NormalAxis));
        Assert.True(segments[2].Position.Y > segments[0].Position.Y);
    }

    [Fact]
    public void Flap_effectiveness_matches_thin_airfoil_theory()
        => Assert.Equal(0.609, SurfaceGeometry.FlapEffectiveness(0.25), 3);

    static void Approx(Vec3 e, Vec3 a)
    {
        Assert.Equal(e.X, a.X, 6);
        Assert.Equal(e.Y, a.Y, 6);
        Assert.Equal(e.Z, a.Z, 6);
    }
}
```

`tests/Symlab.Flight.Tests/Aero/InducedFlowTests.cs`:
```csharp
using Symlab.Flight.Aero;

namespace Symlab.Flight.Tests.Aero;

public class InducedFlowTests
{
    [Fact]
    public void Finite_wing_lift_slope_matches_lifting_line_estimate()
    {
        const double aspectRatio = 6, oswald = 0.9;
        double k = 1 / (Math.PI * oswald * aspectRatio);
        var airfoil = TestAirfoils.Linear();
        double alpha = 0.05;
        double ai = InducedFlow.SolveInducedAngle(airfoil, 200_000, alpha, k);
        double cl = airfoil.Evaluate(alpha - ai, 200_000).Cl;
        double a0 = 2 * Math.PI;
        double expected = a0 / (1 + a0 * k) * alpha;
        Assert.Equal(expected, cl, 3);
    }

    [Fact]
    public void Ground_effect_vanishes_far_from_the_ground_and_is_strong_near_it()
    {
        Assert.True(InducedFlow.GroundEffectFactor(20, 1.5) > 0.99);
        Assert.True(InducedFlow.GroundEffectFactor(0.05, 1.5) < 0.5);
        Assert.Equal(1.0, InducedFlow.GroundEffectFactor(1, 0));
    }
}
```

`tests/Symlab.Flight.Tests/Aero/SurfaceAeroModelTests.cs`:
```csharp
using Symlab.Flight.Aero;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Aero;

public class SurfaceAeroModelTests
{
    static readonly SurfaceSpec Wing = new("wing", SurfaceRole.Wing, Vec3.Zero, 0.75, 0.3, 0.3, 0, 0, 0, 0, "linear", 6, true);
    static readonly SurfaceSpec Stab = new("stab", SurfaceRole.HorizontalTail, new Vec3(-0.8, 0, 0), 0.25, 0.15, 0.15, 0, 0, 0, 0, "linear", 3, true);

    static readonly ControlSurfaceSpec AileronRight = new("aileronRight", "wing", Side.Right, 0.25, 0.4, 1.0, 15, 15, 0.1,
        new Dictionary<string, double> { ["aileron"] = -1 });
    static readonly ControlSurfaceSpec AileronLeft = AileronRight with { Name = "aileronLeft", Side = Side.Left, Mix = new Dictionary<string, double> { ["aileron"] = 1 } };
    static readonly ControlSurfaceSpec Elevator = new("elevator", "stab", Side.Both, 0.4, 0, 1, 20, 20, 0.1,
        new Dictionary<string, double> { ["elevator"] = -1 });

    static AeroContext Context(Vec3 air, Vec3 omega = default, double[]? deflections = null, PropWash wash = default) =>
        new(air, omega, 1.225, 100, Vec3.UnitY, deflections ?? [0, 0, 0], wash);

    static Vec3 Flow(double speed, double alphaDeg) =>
        new(speed * Math.Cos(Angle.Rad(alphaDeg)), -speed * Math.Sin(Angle.Rad(alphaDeg)), 0);

    static SurfaceAeroModel WingOnly() => new([Wing], TestAirfoils.Map(), [AileronRight, AileronLeft], []);

    [Fact]
    public void Symmetric_wing_at_positive_alpha_lifts_up_and_drags_back_without_roll_or_yaw()
    {
        var flow = Flow(15, 5);
        var load = WingOnly().Evaluate(Context(flow));
        Assert.True(load.Force.Y > 0);
        Assert.True(-Vec3.Dot(load.Force, flow.Normalized()) > 0, "drag opposes the flow");
        Assert.Equal(0, load.Moment.X, 9);
        Assert.Equal(0, load.Moment.Y, 9);
    }

    [Fact]
    public void Lift_matches_finite_wing_estimate()
    {
        var load = WingOnly().Evaluate(Context(Flow(15, 4)));
        double q = 0.5 * 1.225 * 15 * 15;
        double ar = Wing.AspectRatio;
        double a = 2 * Math.PI / (1 + 2 * Math.PI / (Math.PI * Wing.Oswald * ar));
        double expectedLift = q * Wing.TotalArea * a * Angle.Rad(4);
        double lift = load.Force.Y * Math.Cos(Angle.Rad(4)) + load.Force.X * Math.Sin(Angle.Rad(4));
        Assert.InRange(lift, 0.95 * expectedLift, 1.05 * expectedLift);
    }

    [Fact]
    public void Rolling_right_creates_an_opposing_roll_moment()
    {
        var load = WingOnly().Evaluate(Context(Flow(15, 3), omega: new Vec3(2, 0, 0)));
        Assert.True(load.Moment.X < 0, $"roll damping moment {load.Moment.X}");
    }

    [Fact]
    public void Right_aileron_up_and_left_aileron_down_rolls_right()
    {
        var load = WingOnly().Evaluate(Context(Flow(15, 3), deflections: [-0.2, 0.2, 0]));
        Assert.True(load.Moment.X > 0, $"roll moment {load.Moment.X}");
    }

    [Fact]
    public void Trailing_edge_up_elevator_pitches_nose_up()
    {
        var model = new SurfaceAeroModel([Wing, Stab], TestAirfoils.Map(), [Elevator], []);
        var neutral = model.Evaluate(Context(Flow(15, 2), deflections: [0]));
        var up = model.Evaluate(Context(Flow(15, 2), deflections: [-0.2]));
        Assert.True(up.Moment.Z > neutral.Moment.Z);
    }

    [Fact]
    public void Wing_lift_builds_downwash_at_the_tail()
    {
        var model = new SurfaceAeroModel([Wing, Stab], TestAirfoils.Map(), [Elevator], []);
        for (int i = 0; i < 500; i++)
        {
            model.Evaluate(Context(Flow(15, 5), deflections: [0]));
            model.Advance(0.002);
        }
        Assert.True(model.Downwash > 0.01, $"downwash {model.Downwash}");
        model.Reset();
        Assert.Equal(0, model.Downwash);
    }

    [Fact]
    public void Prop_wash_blows_over_a_stationary_tail()
    {
        var model = new SurfaceAeroModel([Stab], TestAirfoils.Map(), [Elevator], []);
        var wash = new PropWash(new Vec3(0, 0, 0), Vec3.UnitX, 0.5, 10);
        var still = model.Evaluate(Context(Vec3.Zero, deflections: [-0.3]));
        var blown = model.Evaluate(Context(Vec3.Zero, deflections: [-0.3], wash: wash));
        Assert.Equal(Vec3.Zero, still.Force);
        Assert.True(blown.Force.Y < 0, "trailing-edge-up elevator in prop wash pushes the tail down");
    }

    [Fact]
    public void Control_that_covers_no_segment_is_rejected()
    {
        var bad = AileronRight with { SpanStart = 0.99, SpanEnd = 0.995 };
        Assert.Throws<ArgumentException>(() => new SurfaceAeroModel([Wing], TestAirfoils.Map(), [bad], []));
    }

    [Fact]
    public void Overlapping_controls_are_rejected()
        => Assert.Throws<ArgumentException>(() => new SurfaceAeroModel([Wing], TestAirfoils.Map(), [AileronRight, AileronRight with { Name = "flap" }], []));

    [Fact]
    public void Fuselage_body_adds_drag()
    {
        var model = new SurfaceAeroModel([], TestAirfoils.Map(), [], [new BodySpec("fuselage", Vec3.Zero, new Vec3(0.01, 0.03, 0.03))]);
        var load = model.Evaluate(Context(new Vec3(20, 0, 0), deflections: []));
        Assert.Equal(-0.5 * 1.225 * 400 * 0.01, load.Force.X, 9);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~Aero"`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Aero/IAeroModel.cs`:
```csharp
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Aero;

/// <summary>Propeller slipstream: points behind the disk and within <see cref="Radius"/> of its axis get <see cref="Velocity"/> added along the axis.</summary>
public readonly record struct PropWash(Vec3 PositionBody, Vec3 AxisBody, double Radius, double Velocity);

/// <param name="AirVelocityBody">CG velocity relative to the air mass, body axes.</param>
/// <param name="UpBody">World up expressed in body axes.</param>
/// <param name="Deflections">Control-surface deflections in radians (positive = trailing edge down), indexed like the model's control list.</param>
public readonly record struct AeroContext(
    Vec3 AirVelocityBody,
    Vec3 AngularVelocityBody,
    double Density,
    double HeightAboveGround,
    Vec3 UpBody,
    IReadOnlyList<double> Deflections,
    PropWash Wash);

public interface IAeroModel
{
    /// <summary>Total aerodynamic force and moment about the CG, body axes. Must not change model state.</summary>
    BodyLoad Evaluate(in AeroContext ctx);

    /// <summary>Advances internal lagged states (e.g. downwash) using the last evaluation.</summary>
    void Advance(double dt);

    void Reset();
}
```

`src/Symlab.Flight/Aero/InducedFlow.cs`:
```csharp
namespace Symlab.Flight.Aero;

public static class InducedFlow
{
    /// <summary>
    /// Solves α_i = k · Cl(α_geo − α_i) with a relaxed fixed-point iteration,
    /// where k = 1 / (π e AR), optionally reduced by ground effect.
    /// </summary>
    public static double SolveInducedAngle(Airfoil airfoil, double reynolds, double alphaGeo, double inducedFactor)
    {
        if (inducedFactor <= 0) return 0;
        double ai = 0;
        for (int i = 0; i < 6; i++)
        {
            double target = inducedFactor * airfoil.Evaluate(alphaGeo - ai, reynolds).Cl;
            ai = 0.5 * (ai + target);
        }
        return ai;
    }

    /// <summary>McCormick ground-effect factor on induced flow: (16h/b)² / (1 + (16h/b)²).</summary>
    public static double GroundEffectFactor(double height, double span)
    {
        if (span <= 0) return 1.0;
        double x = 16 * Math.Max(height, 0) / span;
        return x * x / (1 + x * x);
    }
}
```

`src/Symlab.Flight/Aero/SurfaceSpec.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Aero;

public enum SurfaceRole { Wing, HorizontalTail, VerticalTail, Other }

/// <summary>Right = the panel as defined (toward +z for mirrored surfaces); Left = its mirror image.</summary>
public enum Side { Both, Right, Left }

/// <param name="Root">Root quarter-chord point, body axes (m).</param>
/// <param name="Span">Panel span from root to tip (m). A mirrored surface's total span is twice this.</param>
/// <param name="DihedralDeg">Rotation about body x lifting the tip; 90 makes a vertical fin pointing up.</param>
/// <param name="TwistDeg">Tip incidence relative to root (negative = washout), linear along the span.</param>
public sealed record SurfaceSpec(
    string Name,
    SurfaceRole Role,
    Vec3 Root,
    double Span,
    double RootChord,
    double TipChord,
    double SweepDeg,
    double DihedralDeg,
    double IncidenceDeg,
    double TwistDeg,
    string Airfoil,
    int Segments,
    bool Mirror,
    double Oswald = 0.85)
{
    public double PanelArea => Span * (RootChord + TipChord) / 2;
    public double TotalArea => Mirror ? 2 * PanelArea : PanelArea;
    public double TotalSpan => Mirror ? 2 * Span : Span;

    /// <summary>Effective aspect ratio. A single panel is treated as end-plated by the fuselage (image method).</summary>
    public double AspectRatio => Mirror ? TotalSpan * TotalSpan / TotalArea : 2 * Span * Span / PanelArea;
}

/// <param name="SpanStart">Start of the control along the panel span, fraction 0..1 from the root.</param>
/// <param name="MaxPositiveDeg">Maximum trailing-edge-down deflection (magnitude).</param>
/// <param name="MaxNegativeDeg">Maximum trailing-edge-up deflection (magnitude).</param>
/// <param name="Mix">Stick channel weights; the command is clamp(Σ weight·channel, −1, 1).</param>
public sealed record ControlSurfaceSpec(
    string Name,
    string Surface,
    Side Side,
    double ChordFraction,
    double SpanStart,
    double SpanEnd,
    double MaxPositiveDeg,
    double MaxNegativeDeg,
    double ServoSecondsPer60Deg,
    IReadOnlyDictionary<string, double> Mix);

/// <summary>Non-lifting body (fuselage, pod). <see cref="CdA"/> holds drag areas in m² along body x, y, z.</summary>
public sealed record BodySpec(string Name, Vec3 Position, Vec3 CdA);
```

`src/Symlab.Flight/Aero/SurfaceSegment.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Aero;

/// <summary>One spanwise strip of a lifting surface, with its own local frame.</summary>
public sealed class SurfaceSegment
{
    public required string SurfaceName { get; init; }
    public required SurfaceRole Role { get; init; }
    public required Side Side { get; init; }

    /// <summary>Quarter-chord point at mid-strip, body axes.</summary>
    public required Vec3 Position { get; init; }

    /// <summary>Unit vector along the chord, pointing to the leading edge.</summary>
    public required Vec3 ChordAxis { get; init; }

    /// <summary>Unit vector along which positive lift acts at small alpha.</summary>
    public required Vec3 NormalAxis { get; init; }

    public Vec3 PitchAxis => Vec3.Cross(ChordAxis, NormalAxis);
    public required double Chord { get; init; }
    public required double Area { get; init; }

    /// <summary>Mid-strip position along the panel span, 0 at the root and 1 at the tip.</summary>
    public required double SpanFraction { get; init; }

    public required Airfoil Airfoil { get; init; }

    /// <summary>1 / (π e AR) of the parent surface.</summary>
    public required double InducedFactor { get; init; }

    public int ControlIndex { get; set; } = -1;
    public double FlapEffectiveness { get; set; }
    public double ControlChordFraction { get; set; }
}
```

`src/Symlab.Flight/Aero/SurfaceGeometry.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Aero;

public static class SurfaceGeometry
{
    public static IReadOnlyList<SurfaceSegment> Build(SurfaceSpec spec, Airfoil airfoil)
    {
        if (spec.Segments < 1) throw new ArgumentException($"Surface '{spec.Name}' needs at least one segment.");
        if (spec.Span <= 0 || spec.RootChord <= 0 || spec.TipChord <= 0)
            throw new ArgumentException($"Surface '{spec.Name}' has non-positive dimensions.");

        var dihedral = Quat.FromAxisAngle(Vec3.UnitX, -Angle.Rad(spec.DihedralDeg));
        double tanSweep = Math.Tan(Angle.Rad(spec.SweepDeg));
        double inducedFactor = 1.0 / (Math.PI * spec.Oswald * spec.AspectRatio);
        var segments = new List<SurfaceSegment>(spec.Mirror ? 2 * spec.Segments : spec.Segments);

        for (int k = 0; k < spec.Segments; k++)
        {
            double t0 = (double)k / spec.Segments;
            double t1 = (double)(k + 1) / spec.Segments;
            double tm = 0.5 * (t0 + t1);
            double c0 = spec.RootChord + (spec.TipChord - spec.RootChord) * t0;
            double c1 = spec.RootChord + (spec.TipChord - spec.RootChord) * t1;
            double chord = 0.5 * (c0 + c1);
            double area = spec.Span * (t1 - t0) * chord;

            var orientation = dihedral * Quat.FromAxisAngle(Vec3.UnitZ, Angle.Rad(spec.IncidenceDeg + spec.TwistDeg * tm));
            var chordAxis = orientation.Rotate(Vec3.UnitX);
            var normal = orientation.Rotate(Vec3.UnitY);
            var position = spec.Root + dihedral.Rotate(new Vec3(-spec.Span * tm * tanSweep, 0, spec.Span * tm));

            segments.Add(Make(spec, Side.Right, position, chordAxis, normal, chord, area, tm, airfoil, inducedFactor));
            if (spec.Mirror)
                segments.Add(Make(spec, Side.Left, Mirror(position), Mirror(chordAxis), Mirror(normal), chord, area, tm, airfoil, inducedFactor));
        }
        return segments;
    }

    /// <summary>Thin-airfoil flap effectiveness τ for a plain flap of the given chord fraction.</summary>
    public static double FlapEffectiveness(double chordFraction)
    {
        double theta = Math.Acos(2 * Math.Clamp(chordFraction, 0, 1) - 1);
        return 1 - (theta - Math.Sin(theta)) / Math.PI;
    }

    static Vec3 Mirror(Vec3 v) => new(v.X, v.Y, -v.Z);

    static SurfaceSegment Make(SurfaceSpec spec, Side side, Vec3 position, Vec3 chordAxis, Vec3 normal,
        double chord, double area, double spanFraction, Airfoil airfoil, double inducedFactor) => new()
    {
        SurfaceName = spec.Name,
        Role = spec.Role,
        Side = side,
        Position = position,
        ChordAxis = chordAxis,
        NormalAxis = normal,
        Chord = chord,
        Area = area,
        SpanFraction = spanFraction,
        Airfoil = airfoil,
        InducedFactor = inducedFactor,
    };
}
```

`src/Symlab.Flight/Aero/SurfaceAeroModel.cs`:
```csharp
using Symlab.Flight.Atmosphere;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Aero;

/// <summary>
/// Strip-theory aerodynamics: every surface is cut into spanwise segments, each evaluated with its
/// own local flow (including rotation, prop wash, downwash and ground effect) and airfoil polar.
/// </summary>
public sealed class SurfaceAeroModel : IAeroModel
{
    const double DownwashGain = 1.6;
    const double FlapEfficiency = 0.85;
    const double MaxDownwash = 0.3;

    readonly SurfaceSegment[] _segments;
    readonly BodySpec[] _bodies;
    readonly double _wingAspectRatio;
    readonly double _tailArm;
    double _lastWingCl;
    double _lastAirspeed;

    public SurfaceAeroModel(
        IEnumerable<SurfaceSpec> surfaces,
        IReadOnlyDictionary<string, Airfoil> airfoils,
        IReadOnlyList<ControlSurfaceSpec> controls,
        IEnumerable<BodySpec> bodies)
    {
        var specs = surfaces.ToArray();
        _segments = specs.SelectMany(s => SurfaceGeometry.Build(s, Lookup(airfoils, s))).ToArray();
        _bodies = bodies.ToArray();

        var wings = specs.Where(s => s.Role == SurfaceRole.Wing).ToArray();
        WingSpan = wings.Length > 0 ? wings.Max(s => s.TotalSpan) : 0;
        WingArea = wings.Sum(s => s.TotalArea);
        _wingAspectRatio = WingArea > 0 ? WingSpan * WingSpan / WingArea : 0;

        var wingSegments = _segments.Where(s => s.Role == SurfaceRole.Wing).ToArray();
        var tailSegments = _segments.Where(s => s.Role == SurfaceRole.HorizontalTail).ToArray();
        _tailArm = wingSegments.Length > 0 && tailSegments.Length > 0
            ? Math.Max(0.1, Math.Abs(wingSegments.Average(s => s.Position.X) - tailSegments.Average(s => s.Position.X)))
            : 0.1;

        for (int i = 0; i < controls.Count; i++) AssignControl(controls[i], i);
    }

    public IReadOnlyList<SurfaceSegment> Segments => _segments;
    public double WingSpan { get; }
    public double WingArea { get; }
    public double Downwash { get; private set; }

    public BodyLoad Evaluate(in AeroContext ctx)
    {
        var force = Vec3.Zero;
        var moment = Vec3.Zero;
        double wingLift = 0, wingQa = 0;

        foreach (var seg in _segments)
        {
            var u = ctx.AirVelocityBody + Vec3.Cross(ctx.AngularVelocityBody, seg.Position) + WashAt(ctx.Wash, seg.Position);
            var c = seg.ChordAxis;
            var n = seg.NormalAxis;
            double uc = Vec3.Dot(u, c);
            double un = Vec3.Dot(u, n);
            double v = Math.Sqrt(uc * uc + un * un);
            if (v < 0.1) continue;

            double q = 0.5 * ctx.Density * v * v;
            double alpha = Math.Atan2(-un, uc);
            double height = ctx.HeightAboveGround + Vec3.Dot(seg.Position, ctx.UpBody);
            double groundEffect = InducedFlow.GroundEffectFactor(height, WingSpan);
            double delta = seg.ControlIndex >= 0 ? ctx.Deflections[seg.ControlIndex] : 0;

            double alphaGeo = alpha + seg.FlapEffectiveness * FlapEfficiency * delta;
            if (seg.Role == SurfaceRole.HorizontalTail) alphaGeo -= Downwash * groundEffect;

            double reynolds = ctx.Density * v * seg.Chord / Isa.DynamicViscosity;
            double ai = InducedFlow.SolveInducedAngle(seg.Airfoil, reynolds, alphaGeo, seg.InducedFactor * groundEffect);
            var coeff = seg.Airfoil.Evaluate(alphaGeo - ai, reynolds);
            double sinDelta = Math.Sin(delta);
            double cl = coeff.Cl * Math.Cos(ai);
            double cd = coeff.Cd + coeff.Cl * Math.Sin(ai) + seg.ControlChordFraction * sinDelta * sinDelta;

            var liftDir = (c * -un + n * uc) / v;
            var dragDir = (c * uc + n * un) / -v;
            double qa = q * seg.Area;
            var f = (liftDir * cl + dragDir * cd) * qa;
            force += f;
            moment += Vec3.Cross(seg.Position, f) + seg.PitchAxis * (qa * seg.Chord * coeff.Cm);

            if (seg.Role == SurfaceRole.Wing)
            {
                wingLift += qa * cl;
                wingQa += qa;
            }
        }

        foreach (var body in _bodies)
        {
            var u = ctx.AirVelocityBody + Vec3.Cross(ctx.AngularVelocityBody, body.Position);
            double k = -0.5 * ctx.Density * u.Length;
            var f = new Vec3(u.X * body.CdA.X, u.Y * body.CdA.Y, u.Z * body.CdA.Z) * k;
            force += f;
            moment += Vec3.Cross(body.Position, f);
        }

        _lastWingCl = wingQa > 0 ? wingLift / wingQa : 0;
        _lastAirspeed = ctx.AirVelocityBody.Length;
        return new BodyLoad(force, moment);
    }

    public void Advance(double dt)
    {
        if (_wingAspectRatio <= 0) return;
        double target = Math.Clamp(DownwashGain * _lastWingCl / (Math.PI * _wingAspectRatio), -MaxDownwash, MaxDownwash);
        double tau = _tailArm / Math.Max(_lastAirspeed, 1.0);
        Downwash += (target - Downwash) * Math.Min(1.0, dt / tau);
    }

    public void Reset()
    {
        Downwash = 0;
        _lastWingCl = 0;
        _lastAirspeed = 0;
    }

    static Vec3 WashAt(in PropWash wash, Vec3 p)
    {
        if (wash.Velocity <= 0 || wash.Radius <= 0) return Vec3.Zero;
        var rel = p - wash.PositionBody;
        double along = Vec3.Dot(rel, wash.AxisBody);
        if (along > 0) return Vec3.Zero;
        var radial = rel - wash.AxisBody * along;
        return radial.Length <= wash.Radius ? wash.AxisBody * wash.Velocity : Vec3.Zero;
    }

    void AssignControl(ControlSurfaceSpec control, int index)
    {
        int covered = 0;
        foreach (var seg in _segments)
        {
            if (seg.SurfaceName != control.Surface) continue;
            if (control.Side != Side.Both && seg.Side != control.Side) continue;
            if (seg.SpanFraction < control.SpanStart || seg.SpanFraction > control.SpanEnd) continue;
            if (seg.ControlIndex >= 0)
                throw new ArgumentException($"Control '{control.Name}' overlaps another control on surface '{control.Surface}'.");
            seg.ControlIndex = index;
            seg.FlapEffectiveness = SurfaceGeometry.FlapEffectiveness(control.ChordFraction);
            seg.ControlChordFraction = control.ChordFraction;
            covered++;
        }
        if (covered == 0)
            throw new ArgumentException($"Control '{control.Name}' does not cover any segment of surface '{control.Surface}'.");
    }

    static Airfoil Lookup(IReadOnlyDictionary<string, Airfoil> airfoils, SurfaceSpec s) =>
        airfoils.TryGetValue(s.Airfoil, out var a)
            ? a
            : throw new ArgumentException($"Surface '{s.Name}' references unknown airfoil '{s.Airfoil}'.");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~Aero"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(aero): add strip-theory surface aero model with downwash, ground effect and prop wash

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 6: Electric propulsion

**Files:**
- Create: `src/Symlab.Flight/Propulsion/PowerPlantSpec.cs`, `PropellerAero.cs`, `PowerPlant.cs`, `PowerPlantLoads.cs`
- Test: `tests/Symlab.Flight.Tests/Propulsion/PropulsionTests.cs`

**Interfaces:**
- Consumes: `Vec3`, `BodyLoad`, `Interpolation`.
- Produces:
  - `MotorSpec(double Kv, double ResistanceOhm, double NoLoadCurrentA, double MaxCurrentA, double RotorInertia)` — Kv in rpm/V, rotor inertia = motor + prop (kg·m²).
  - `BatterySpec(int Cells, double CapacityAh, double InternalResistanceOhm, double[] SocPoints, double[] CellVoltage)` with `OpenCircuitVoltage(double soc)` and `BatterySpec.Lipo(int cells, double capacityAh, double internalResistanceOhm)`.
  - `EscSpec(double[] ThrottleIn, double[] ThrottleOut, bool Brake)` with `Map(double throttle)` and `EscSpec.Linear(bool brake = false)`.
  - `PropellerSpec(double DiameterM, double PitchM, double[] J, double[] Ct, double[] Cp)` with `Generic(double diameterIn, double pitchIn)` and `Scaled(double ctScale, double cpScale)`.
  - `PowerPlantSpec(MotorSpec Motor, BatterySpec Battery, EscSpec Esc, PropellerSpec Propeller, Vec3 Position, Vec3 ThrustAxis, int SpinDirection, double PFactor)` — `SpinDirection` +1 = clockwise seen from behind (rotation vector along +ThrustAxis).
  - `PropellerLoad(double Thrust, double Torque)`; `PropellerAero.Evaluate(PropellerSpec, double omega, double axialSpeed, double density)`.
  - `PowerTelemetry(double Rpm, double Thrust, double MotorTorque, double MotorCurrent, double BatteryCurrent, double BatteryVoltage, double StateOfCharge, double Duty, double WashVelocity)`.
  - `PowerPlant(PowerPlantSpec)` with `Spec`, `Omega`, `StateOfCharge`, `Telemetry`, `Reset()`, `Step(double dt, double throttle, double axialSpeed, double density) : PowerTelemetry`, `SteadyState(double throttle, double axialSpeed, double density, double stateOfCharge = 1) : SteadyStateResult`.
  - `SteadyStateResult(double Omega, double Thrust, double Torque, double Current)` with `Rpm`.
  - `PowerPlantLoads.Compute(PowerPlantSpec, in PowerTelemetry, double propOmega, Vec3 airVelocityBody, Vec3 angularVelocityBody) : BodyLoad`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Propulsion/PropulsionTests.cs`:
```csharp
using Symlab.Flight.Geometry;
using Symlab.Flight.Propulsion;

namespace Symlab.Flight.Tests.Propulsion;

public class PropulsionTests
{
    internal static PowerPlantSpec TrainerLike() => new(
        new MotorSpec(Kv: 650, ResistanceOhm: 0.04, NoLoadCurrentA: 1.2, MaxCurrentA: 60, RotorInertia: 3e-4),
        BatterySpec.Lipo(cells: 4, capacityAh: 3.0, internalResistanceOhm: 0.028),
        EscSpec.Linear(),
        PropellerSpec.Generic(diameterIn: 12, pitchIn: 6),
        Position: new Vec3(0.45, 0, 0),
        ThrustAxis: Vec3.UnitX,
        SpinDirection: 1,
        PFactor: 0.1);

    static PowerPlant SpunUp(double seconds = 3)
    {
        var plant = new PowerPlant(TrainerLike());
        for (int i = 0; i < (int)(seconds / 0.002); i++) plant.Step(0.002, 1.0, 0, 1.225);
        return plant;
    }

    [Fact]
    public void Generic_propeller_thrust_falls_with_advance_ratio_to_zero_at_J0()
    {
        var p = PropellerSpec.Generic(12, 6);
        Assert.True(p.Ct[0] > p.Ct[2] && p.Ct[2] > 0);
        Assert.Equal(0, p.Ct[4], 12);
        Assert.True(p.Ct[5] < 0);
    }

    [Fact]
    public void Static_thrust_follows_the_thrust_coefficient()
    {
        var p = PropellerSpec.Generic(12, 6);
        var load = PropellerAero.Evaluate(p, 2 * Math.PI * 100, 0, 1.225);
        Assert.Equal(p.Ct[0] * 1.225 * 100 * 100 * Math.Pow(p.DiameterM, 4), load.Thrust, 9);
    }

    [Fact]
    public void Motor_spools_up_to_the_steady_state()
    {
        var plant = SpunUp();
        var steady = plant.SteadyState(1.0, 0, 1.225);
        Assert.InRange(plant.Omega, 0.97 * steady.Omega, 1.03 * steady.Omega);
        Assert.InRange(steady.Thrust, 15, 40);
    }

    [Fact]
    public void Battery_voltage_sags_under_load()
    {
        var plant = SpunUp();
        double open = plant.Spec.Battery.OpenCircuitVoltage(plant.StateOfCharge);
        Assert.True(plant.Telemetry.BatteryVoltage < open - 0.5, $"loaded {plant.Telemetry.BatteryVoltage} V, open {open} V");
    }

    [Fact]
    public void Idle_throttle_draws_no_current_and_the_prop_winds_down()
    {
        var plant = SpunUp(2);
        double before = plant.Omega;
        for (int i = 0; i < 1500; i++) plant.Step(0.002, 0, 0, 1.225);
        Assert.Equal(0, plant.Telemetry.MotorCurrent);
        Assert.True(plant.Omega < 0.2 * before);
    }

    [Fact]
    public void One_minute_at_full_throttle_uses_about_a_fifth_of_the_pack()
    {
        var plant = SpunUp(60);
        Assert.InRange(1 - plant.StateOfCharge, 0.12, 0.28);
    }

    [Fact]
    public void Loads_contain_thrust_and_a_roll_left_reaction_for_a_clockwise_prop()
    {
        var t = default(PowerTelemetry) with { Thrust = 20, MotorTorque = 0.5 };
        var load = PowerPlantLoads.Compute(TrainerLike(), t, 0, Vec3.Zero, Vec3.Zero);
        Assert.Equal(20, load.Force.X, 12);
        Assert.Equal(-0.5, load.Moment.X, 12);
        Assert.Equal(0, load.Moment.Y, 12);
    }

    [Fact]
    public void P_factor_yaws_left_at_positive_angle_of_attack()
    {
        var t = default(PowerTelemetry) with { Thrust = 20 };
        var level = PowerPlantLoads.Compute(TrainerLike(), t, 0, new Vec3(15, 0, 0), Vec3.Zero);
        var noseUp = PowerPlantLoads.Compute(TrainerLike(), t, 0, new Vec3(15, -3, 0), Vec3.Zero);
        Assert.True(noseUp.Moment.Y > level.Moment.Y);
    }

    [Fact]
    public void Pitching_up_with_a_clockwise_prop_yaws_right()
    {
        var load = PowerPlantLoads.Compute(TrainerLike(), default, 1000, Vec3.Zero, new Vec3(0, 0, 1));
        Assert.True(load.Moment.Y < 0, $"gyroscopic yaw moment {load.Moment.Y}");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~PropulsionTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Propulsion/PowerPlantSpec.cs`:
```csharp
using Symlab.Flight.Geometry;
using Symlab.Flight.Numerics;

namespace Symlab.Flight.Propulsion;

/// <param name="Kv">Speed constant, rpm per volt.</param>
/// <param name="RotorInertia">Motor rotor plus propeller moment of inertia, kg·m².</param>
public sealed record MotorSpec(double Kv, double ResistanceOhm, double NoLoadCurrentA, double MaxCurrentA, double RotorInertia);

public sealed record BatterySpec(int Cells, double CapacityAh, double InternalResistanceOhm, double[] SocPoints, double[] CellVoltage)
{
    static readonly double[] LipoSoc = [0, 0.05, 0.1, 0.2, 0.5, 0.8, 0.9, 1.0];
    static readonly double[] LipoVoltage = [3.3, 3.5, 3.6, 3.7, 3.8, 3.95, 4.05, 4.2];

    public static BatterySpec Lipo(int cells, double capacityAh, double internalResistanceOhm) =>
        new(cells, capacityAh, internalResistanceOhm, LipoSoc, LipoVoltage);

    public double OpenCircuitVoltage(double stateOfCharge) =>
        Cells * Interpolation.Linear(SocPoints, CellVoltage, Math.Clamp(stateOfCharge, 0, 1));
}

public sealed record EscSpec(double[] ThrottleIn, double[] ThrottleOut, bool Brake)
{
    public static EscSpec Linear(bool brake = false) => new([0, 1], [0, 1], brake);

    public double Map(double throttle) => Math.Clamp(Interpolation.Linear(ThrottleIn, ThrottleOut, Math.Clamp(throttle, 0, 1)), 0, 1);
}

/// <summary>Propeller coefficients vs advance ratio J = V / (n D): T = Ct ρ n² D⁴, P = Cp ρ n³ D⁵.</summary>
public sealed record PropellerSpec(double DiameterM, double PitchM, double[] J, double[] Ct, double[] Cp)
{
    const double MetersPerInch = 0.0254;

    /// <summary>Generic fixed-pitch propeller estimate from diameter and pitch (inches).</summary>
    public static PropellerSpec Generic(double diameterIn, double pitchIn)
    {
        double pd = pitchIn / diameterIn;
        double j0 = 1.05 * pd + 0.05;
        double ct0 = 0.09 + 0.03 * pd;
        double cp0 = 0.02 + 0.045 * pd;
        double[] ratio = [0, 0.25, 0.5, 0.75, 1.0, 1.25];
        return new PropellerSpec(
            diameterIn * MetersPerInch,
            pitchIn * MetersPerInch,
            ratio.Select(r => r * j0).ToArray(),
            ratio.Select(r => ct0 * (1 - r)).ToArray(),
            ratio.Select(r => cp0 * (1 - 0.7 * r * r * r)).ToArray());
    }

    public PropellerSpec Scaled(double ctScale, double cpScale) =>
        this with { Ct = Ct.Select(c => c * ctScale).ToArray(), Cp = Cp.Select(c => c * cpScale).ToArray() };
}

/// <param name="Position">Propeller disk center, body axes.</param>
/// <param name="ThrustAxis">Unit vector along which thrust pushes the aircraft, body axes.</param>
/// <param name="SpinDirection">+1 = clockwise seen from behind, −1 = counter-clockwise.</param>
/// <param name="PFactor">Thrust-center offset per unit of disk inflow angle, as a fraction of prop diameter.</param>
public sealed record PowerPlantSpec(
    MotorSpec Motor,
    BatterySpec Battery,
    EscSpec Esc,
    PropellerSpec Propeller,
    Vec3 Position,
    Vec3 ThrustAxis,
    int SpinDirection,
    double PFactor);
```

`src/Symlab.Flight/Propulsion/PropellerAero.cs`:
```csharp
using Symlab.Flight.Numerics;

namespace Symlab.Flight.Propulsion;

public readonly record struct PropellerLoad(double Thrust, double Torque);

public static class PropellerAero
{
    const double MinRevsPerSecond = 0.5;

    public static PropellerLoad Evaluate(PropellerSpec p, double omega, double axialSpeed, double density)
    {
        double n = omega / (2 * Math.PI);
        if (n < MinRevsPerSecond) return default;
        double d = p.DiameterM;
        double j = axialSpeed / (n * d);
        double ct = Interpolation.Linear(p.J, p.Ct, j);
        double cp = Interpolation.Linear(p.J, p.Cp, j);
        double thrust = ct * density * n * n * Math.Pow(d, 4);
        double power = cp * density * n * n * n * Math.Pow(d, 5);
        return new PropellerLoad(thrust, power / omega);
    }
}
```

`src/Symlab.Flight/Propulsion/PowerPlant.cs`:
```csharp
namespace Symlab.Flight.Propulsion;

public readonly record struct PowerTelemetry(
    double Rpm,
    double Thrust,
    double MotorTorque,
    double MotorCurrent,
    double BatteryCurrent,
    double BatteryVoltage,
    double StateOfCharge,
    double Duty,
    double WashVelocity);

public readonly record struct SteadyStateResult(double Omega, double Thrust, double Torque, double Current)
{
    public double Rpm => Omega * 60 / (2 * Math.PI);
}

/// <summary>Battery → ESC → brushless motor ↔ propeller, with rotor spin as an explicit state.</summary>
public sealed class PowerPlant
{
    const double FrictionSpeedScale = 5.0;
    readonly double _ke;

    public PowerPlant(PowerPlantSpec spec)
    {
        Spec = spec;
        _ke = 60.0 / (2 * Math.PI * spec.Motor.Kv);
    }

    public PowerPlantSpec Spec { get; }
    public double Omega { get; private set; }
    public double StateOfCharge { get; private set; } = 1.0;
    public PowerTelemetry Telemetry { get; private set; }

    public void Reset()
    {
        Omega = 0;
        StateOfCharge = 1.0;
        Telemetry = default;
    }

    public PowerTelemetry Step(double dt, double throttle, double axialSpeed, double density)
    {
        double duty = Spec.Esc.Map(throttle);
        double voc = Spec.Battery.OpenCircuitVoltage(StateOfCharge);
        double current = MotorCurrent(duty, voc, Omega);
        var load = PropellerAero.Evaluate(Spec.Propeller, Omega, axialSpeed, density);
        double motorTorque = _ke * current;

        Omega = Math.Max(0, Omega + dt * (motorTorque - Friction(Omega) - load.Torque) / Spec.Motor.RotorInertia);

        double batteryCurrent = duty * current;
        StateOfCharge = Math.Max(0, StateOfCharge - batteryCurrent * dt / (Spec.Battery.CapacityAh * 3600));

        double radius = Spec.Propeller.DiameterM / 2;
        double v = Math.Max(axialSpeed, 0);
        double wash = load.Thrust > 0 ? Math.Sqrt(v * v + 2 * load.Thrust / (density * Math.PI * radius * radius)) - v : 0;

        Telemetry = new PowerTelemetry(
            Omega * 60 / (2 * Math.PI), load.Thrust, motorTorque, current, batteryCurrent,
            voc - batteryCurrent * Spec.Battery.InternalResistanceOhm, StateOfCharge, duty, wash);
        return Telemetry;
    }

    public SteadyStateResult SteadyState(double throttle, double axialSpeed, double density, double stateOfCharge = 1.0)
    {
        double duty = Spec.Esc.Map(throttle);
        if (duty <= 0) return default;
        double voc = Spec.Battery.OpenCircuitVoltage(stateOfCharge);
        double lo = 0, hi = 1.05 * duty * voc / _ke;
        for (int i = 0; i < 100; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (NetTorque(mid, duty, voc, axialSpeed, density) > 0) lo = mid; else hi = mid;
        }
        double omega = 0.5 * (lo + hi);
        var load = PropellerAero.Evaluate(Spec.Propeller, omega, axialSpeed, density);
        return new SteadyStateResult(omega, load.Thrust, load.Torque, MotorCurrent(duty, voc, omega));
    }

    double NetTorque(double omega, double duty, double voc, double axialSpeed, double density) =>
        _ke * MotorCurrent(duty, voc, omega) - Friction(omega) - PropellerAero.Evaluate(Spec.Propeller, omega, axialSpeed, density).Torque;

    double Friction(double omega) => _ke * Spec.Motor.NoLoadCurrentA * Math.Tanh(omega / FrictionSpeedScale);

    double MotorCurrent(double duty, double voc, double omega)
    {
        if (duty <= 0 && !Spec.Esc.Brake) return 0;
        double emf = omega * _ke;
        double current = (duty * voc - emf) / (Spec.Motor.ResistanceOhm + duty * duty * Spec.Battery.InternalResistanceOhm);
        if (current < 0 && !Spec.Esc.Brake) current = 0;
        return Math.Min(current, Spec.Motor.MaxCurrentA);
    }
}
```

`src/Symlab.Flight/Propulsion/PowerPlantLoads.cs`:
```csharp
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Propulsion;

public static class PowerPlantLoads
{
    /// <summary>Thrust (with P-factor offset), motor reaction torque and rotor gyroscopic moment, body axes.</summary>
    public static BodyLoad Compute(PowerPlantSpec spec, in PowerTelemetry telemetry, double propOmega, Vec3 airVelocityBody, Vec3 angularVelocityBody)
    {
        var axis = spec.ThrustAxis;
        var thrust = axis * telemetry.Thrust;

        var inflow = airVelocityBody + Vec3.Cross(angularVelocityBody, spec.Position);
        var crossflow = inflow - axis * Vec3.Dot(inflow, axis);
        double speed = inflow.Length;
        var offset = speed > 1.0
            ? Vec3.Cross(axis, crossflow) * (-spec.SpinDirection * spec.PFactor * spec.Propeller.DiameterM / speed)
            : Vec3.Zero;

        var moment = Vec3.Cross(spec.Position + offset, thrust);
        moment += axis * (-spec.SpinDirection * telemetry.MotorTorque);
        var rotorMomentum = axis * (spec.SpinDirection * spec.Motor.RotorInertia * propOmega);
        moment -= Vec3.Cross(angularVelocityBody, rotorMomentum);
        return new BodyLoad(thrust, moment);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~PropulsionTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(propulsion): add battery, ESC, motor and propeller model with airframe loads

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Thrust-stand calibration and APC performance import

**Files:**
- Create: `src/Symlab.Flight/Propulsion/ThrustStand.cs`, `ApcPerformanceFile.cs`
- Test: `tests/Symlab.Flight.Tests/Propulsion/ThrustStandTests.cs`, `ApcPerformanceFileTests.cs`

**Interfaces:**
- Consumes: `PowerPlantSpec`, `PowerPlant.SteadyState`, `PropellerSpec.Scaled` (Task 6).
- Produces:
  - `ThrustStandPoint(double Throttle, double ThrustN, double CurrentA)`.
  - `ThrustStand.ParseCsv(string text) : IReadOnlyList<ThrustStandPoint>` — columns `throttle,thrust_n,current_a`, optional header, throttle in 0..1.
  - `ThrustStand.Calibrate(PowerPlantSpec spec, IReadOnlyList<ThrustStandPoint> points, double density = 1.225) : PowerPlantSpec`.
  - `ApcPerformanceFile.Parse(string text, double diameterM, double pitchM, double targetRpm) : PropellerSpec`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Propulsion/ThrustStandTests.cs`:
```csharp
using Symlab.Flight.Propulsion;

namespace Symlab.Flight.Tests.Propulsion;

public class ThrustStandTests
{
    [Fact]
    public void Parses_csv_with_header()
    {
        var points = ThrustStand.ParseCsv("throttle,thrust_n,current_a\n0.5,8.2,12.1\n1.0,24.0,38.5\n");
        Assert.Equal(2, points.Count);
        Assert.Equal(new ThrustStandPoint(1.0, 24.0, 38.5), points[1]);
    }

    [Fact]
    public void Rejects_throttle_outside_zero_one()
        => Assert.Throws<InvalidDataException>(() => ThrustStand.ParseCsv("1.5,10,10"));

    [Fact]
    public void Calibration_recovers_measured_static_thrust_and_current()
    {
        var nominal = PropulsionTests.TrainerLike();
        var truth = nominal with { Propeller = nominal.Propeller.Scaled(1.2, 0.9) };
        var truthPlant = new PowerPlant(truth);
        var measured = new[] { 0.5, 0.75, 1.0 }
            .Select(t => { var s = truthPlant.SteadyState(t, 0, 1.225); return new ThrustStandPoint(t, s.Thrust, s.Current); })
            .ToArray();

        var calibrated = new PowerPlant(ThrustStand.Calibrate(nominal, measured));

        foreach (var p in measured)
        {
            var s = calibrated.SteadyState(p.Throttle, 0, 1.225);
            Assert.InRange(s.Thrust, 0.97 * p.ThrustN, 1.03 * p.ThrustN);
            Assert.InRange(s.Current, 0.95 * p.CurrentA, 1.05 * p.CurrentA);
        }
    }
}
```

`tests/Symlab.Flight.Tests/Propulsion/ApcPerformanceFileTests.cs`:
```csharp
using Symlab.Flight.Propulsion;

namespace Symlab.Flight.Tests.Propulsion;

public class ApcPerformanceFileTests
{
    const string Sample = """
         10x7E.dat

         PROP RPM =        4000

          V          J           Pe          Ct          Cp         PWR        Torque       Thrust
        (mph)     (Adv_Ratio)     -           -           -          (Hp)      (In-Lbf)      (Lbf)
         0.00      0.0000      0.0000      0.1000      0.0500      0.0100      0.1000      0.3000
         5.00      0.3000      0.4000      0.0800      0.0480      0.0100      0.1000      0.2000
        10.00      0.6000      0.6000      0.0200      0.0300      0.0100      0.1000      0.1000

         PROP RPM =        8000

          V          J           Pe          Ct          Cp         PWR        Torque       Thrust
        (mph)     (Adv_Ratio)     -           -           -          (Hp)      (In-Lbf)      (Lbf)
         0.00      0.0000      0.0000      0.1100      0.0520      0.0100      0.1000      1.3000
         5.00      0.1500      0.3000      0.1000      0.0510      0.0100      0.1000      1.2000
        10.00      0.3000      0.5000      0.0850      0.0490      0.0100      0.1000      1.0000
        15.00      0.4500      0.6500      -NaN-       0.0450      0.0100      0.1000      0.8000
        20.00      0.6000      0.7000      0.0300      0.0350      0.0100      0.1000      0.5000
        """;

    [Fact]
    public void Picks_the_block_nearest_the_target_rpm_and_skips_invalid_rows()
    {
        var p = ApcPerformanceFile.Parse(Sample, 0.254, 0.178, 7500);
        Assert.Equal(new[] { 0.0, 0.15, 0.3, 0.6 }, p.J);
        Assert.Equal(0.11, p.Ct[0], 9);
        Assert.Equal(0.035, p.Cp[^1], 9);
        Assert.Equal(0.254, p.DiameterM);
    }

    [Fact]
    public void File_without_rpm_blocks_is_rejected()
        => Assert.Throws<InvalidDataException>(() => ApcPerformanceFile.Parse("nothing here", 0.25, 0.18, 8000));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ThrustStandTests|FullyQualifiedName~ApcPerformanceFileTests"`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Propulsion/ThrustStand.cs`:
```csharp
using System.Globalization;

namespace Symlab.Flight.Propulsion;

public readonly record struct ThrustStandPoint(double Throttle, double ThrustN, double CurrentA);

/// <summary>Recalibrates a propulsion model so its static thrust and current match thrust-stand measurements.</summary>
public static class ThrustStand
{
    public static IReadOnlyList<ThrustStandPoint> ParseCsv(string text)
    {
        var points = new List<ThrustStandPoint>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(',');
            if (parts.Length < 3 || !TryParse(parts[0], out var throttle)) continue;
            if (!TryParse(parts[1], out var thrust) || !TryParse(parts[2], out var current))
                throw new InvalidDataException($"Invalid thrust-stand line: '{line}'.");
            if (throttle is < 0 or > 1)
                throw new InvalidDataException($"Throttle must be within 0..1 (line '{line}').");
            points.Add(new ThrustStandPoint(throttle, thrust, current));
        }
        return points;
    }

    public static PowerPlantSpec Calibrate(PowerPlantSpec spec, IReadOnlyList<ThrustStandPoint> points, double density = 1.225)
    {
        var usable = points.Where(p => p.Throttle > 0.05 && p.ThrustN > 0 && p.CurrentA > 0).ToArray();
        if (usable.Length == 0) throw new ArgumentException("Thrust-stand data needs at least one point above 5% throttle.");

        double ctScale = 1, cpScale = 1;
        for (int iteration = 0; iteration < 12; iteration++)
        {
            var plant = new PowerPlant(spec with { Propeller = spec.Propeller.Scaled(ctScale, cpScale) });
            double currentRatio = 0, thrustRatio = 0;
            foreach (var p in usable)
            {
                var s = plant.SteadyState(p.Throttle, 0, density);
                currentRatio += p.CurrentA / Math.Max(s.Current, 1e-3);
                thrustRatio += p.ThrustN / Math.Max(s.Thrust, 1e-3);
            }
            cpScale *= Math.Pow(currentRatio / usable.Length, 1.5);
            ctScale *= thrustRatio / usable.Length;
        }
        return spec with { Propeller = spec.Propeller.Scaled(ctScale, cpScale) };
    }

    static bool TryParse(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
```

`src/Symlab.Flight/Propulsion/ApcPerformanceFile.cs`:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Symlab.Flight.Propulsion;

/// <summary>Reads APC "PER3" performance files (columns: V, J, Pe, Ct, Cp, ...), one block per RPM.</summary>
public static class ApcPerformanceFile
{
    static readonly Regex RpmPattern = new(@"PROP\s+RPM\s*=\s*([0-9.]+)", RegexOptions.IgnoreCase);

    public static PropellerSpec Parse(string text, double diameterM, double pitchM, double targetRpm)
    {
        var blocks = new List<(double Rpm, List<(double J, double Ct, double Cp)> Rows)>();
        List<(double J, double Ct, double Cp)>? current = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var match = RpmPattern.Match(line);
            if (match.Success)
            {
                current = [];
                blocks.Add((double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), current));
                continue;
            }
            if (current is null) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            if (!TryParse(parts[0], out _) || !TryParse(parts[1], out var j) ||
                !TryParse(parts[3], out var ct) || !TryParse(parts[4], out var cp)) continue;
            current.Add((j, ct, cp));
        }

        var usable = blocks.Where(b => b.Rows.Count >= 2).ToList();
        if (usable.Count == 0) throw new InvalidDataException("No usable 'PROP RPM =' block found in the APC file.");
        var best = usable.MinBy(b => Math.Abs(b.Rpm - targetRpm));

        var rows = best.Rows.GroupBy(r => r.J).Select(g => g.First()).OrderBy(r => r.J).ToArray();
        return new PropellerSpec(
            diameterM, pitchM,
            rows.Select(r => r.J).ToArray(),
            rows.Select(r => r.Ct).ToArray(),
            rows.Select(r => r.Cp).ToArray());
    }

    static bool TryParse(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ThrustStandTests|FullyQualifiedName~ApcPerformanceFileTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(propulsion): add thrust-stand calibration and APC performance import

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Control inputs, mixing and servos

**Files:**
- Create: `src/Symlab.Flight/Controls/ControlInputs.cs`, `Servo.cs`
- Test: `tests/Symlab.Flight.Tests/Controls/ControlsTests.cs`

**Interfaces:**
- Consumes: `Angle`.
- Produces:
  - `ControlInputs(double Throttle, double Aileron, double Elevator, double Rudder, double Flap = 0)` — throttle 0..1, others −1..1 (aileron + = roll right, elevator + = pitch up, rudder + = yaw right); `Get(string channel)`; static `IsChannel(string)`; static `Mix(IReadOnlyDictionary<string,double>, in ControlInputs)` (clamped to −1..1); static `Neutral`.
  - `ControlMapping.CommandToDeflection(double command, double maxPositiveDeg, double maxNegativeDeg) : double` (radians).
  - `Servo(double secondsPer60Deg)` with `Position` (rad), `Step(double target, double dt)`, `Reset()`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Controls/ControlsTests.cs`:
```csharp
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Tests.Controls;

public class ControlsTests
{
    [Fact]
    public void Elevon_mix_combines_aileron_and_elevator()
    {
        var right = new Dictionary<string, double> { ["aileron"] = -1, ["elevator"] = -1 };
        Assert.Equal(-0.8, ControlInputs.Mix(right, new ControlInputs(0, 0.3, 0.5, 0)), 12);
    }

    [Fact]
    public void Mix_is_clamped() =>
        Assert.Equal(-1, ControlInputs.Mix(new Dictionary<string, double> { ["aileron"] = -1, ["elevator"] = -1 }, new ControlInputs(0, 1, 1, 0)));

    [Fact]
    public void Unknown_channel_throws() => Assert.Throws<ArgumentException>(() => ControlInputs.Neutral.Get("gear"));

    [Theory]
    [InlineData("throttle", true)]
    [InlineData("flap", true)]
    [InlineData("Aileron", false)]
    public void Recognizes_channel_names(string name, bool expected) => Assert.Equal(expected, ControlInputs.IsChannel(name));

    [Fact]
    public void Deflection_uses_separate_limits_per_direction()
    {
        Assert.Equal(Angle.Rad(12), ControlMapping.CommandToDeflection(1, 12, 15), 12);
        Assert.Equal(-Angle.Rad(7.5), ControlMapping.CommandToDeflection(-0.5, 12, 15), 12);
    }

    [Fact]
    public void Servo_is_rate_limited()
    {
        var servo = new Servo(secondsPer60Deg: 0.1);
        servo.Step(Angle.Rad(30), 0.02);
        Assert.Equal(Angle.Rad(12), servo.Position, 9);
        for (int i = 0; i < 10; i++) servo.Step(Angle.Rad(30), 0.02);
        Assert.Equal(Angle.Rad(30), servo.Position, 9);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ControlsTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Controls/ControlInputs.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Controls;

/// <summary>Pilot commands. Throttle 0..1; others −1..1 with aileron + = roll right, elevator + = pitch up, rudder + = yaw right.</summary>
public readonly record struct ControlInputs(double Throttle, double Aileron, double Elevator, double Rudder, double Flap = 0)
{
    public static readonly ControlInputs Neutral = new(0, 0, 0, 0);

    static readonly HashSet<string> Channels = ["throttle", "aileron", "elevator", "rudder", "flap"];

    public static bool IsChannel(string name) => Channels.Contains(name);

    public double Get(string channel) => channel switch
    {
        "throttle" => Throttle,
        "aileron" => Aileron,
        "elevator" => Elevator,
        "rudder" => Rudder,
        "flap" => Flap,
        _ => throw new ArgumentException($"Unknown control channel '{channel}'."),
    };

    public static double Mix(IReadOnlyDictionary<string, double> mix, in ControlInputs inputs)
    {
        double sum = 0;
        foreach (var (channel, weight) in mix) sum += weight * inputs.Get(channel);
        return Math.Clamp(sum, -1, 1);
    }
}

public static class ControlMapping
{
    /// <summary>Maps a −1..1 command to a deflection in radians (positive = trailing edge down).</summary>
    public static double CommandToDeflection(double command, double maxPositiveDeg, double maxNegativeDeg) =>
        command >= 0 ? command * Angle.Rad(maxPositiveDeg) : command * Angle.Rad(maxNegativeDeg);
}
```

`src/Symlab.Flight/Controls/Servo.cs`:
```csharp
namespace Symlab.Flight.Controls;

/// <summary>Rate-limited actuator, specified like a servo datasheet (seconds per 60°).</summary>
public sealed class Servo
{
    readonly double _maxRate;

    public Servo(double secondsPer60Deg)
    {
        if (secondsPer60Deg <= 0) throw new ArgumentOutOfRangeException(nameof(secondsPer60Deg));
        _maxRate = (Math.PI / 3) / secondsPer60Deg;
    }

    public double Position { get; private set; }

    public void Step(double target, double dt)
    {
        double maxStep = _maxRate * dt;
        Position += Math.Clamp(target - Position, -maxStep, maxStep);
    }

    public void Reset() => Position = 0;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ControlsTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(controls): add control inputs, mixing and rate-limited servos

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Ground contact and crash detection

**Files:**
- Create: `src/Symlab.Flight/Ground/GroundSpec.cs`, `GroundContactModel.cs`
- Test: `tests/Symlab.Flight.Tests/Ground/GroundContactTests.cs`

**Interfaces:**
- Consumes: `RigidBodyState`, `BodyLoad`, `Rk4Integrator`, `MassProperties` (Task 2), `ITerrain`, `FlatTerrain`, `CylinderObstacle` (Task 3).
- Produces:
  - `WheelSpec(string Name, Vec3 Position, double Stiffness, double Damping, double RollingFriction, double LateralFriction, double MaxSteerDeg, IReadOnlyDictionary<string, double> SteerMix)` — `Position` = tire contact point with the strut fully extended, body axes.
  - `HullPointSpec(string Name, Vec3 Position, string Tag)` — tags: `nose`, `wingtip`, `belly`, `tail`, `canopy`, anything else = generic hull.
  - `CrashLimits(double MaxGearSinkRate, double MaxHullImpactSpeed, double MaxBellyImpactSpeed)`.
  - `enum CrashCause { None, HardLanding, TreeStrike, WingtipStrike, NoseOver, HullImpact }`.
  - `GroundContactModel(IEnumerable<WheelSpec>, IEnumerable<HullPointSpec>, double mass)` with `Wheels`, `Evaluate(in RigidBodyState, ITerrain, IReadOnlyList<double> steerRad) : BodyLoad`, `WheelsInContact(in RigidBodyState, ITerrain) : int`, `DetectCrash(in RigidBodyState, ITerrain, CrashLimits) : CrashCause`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Ground/GroundContactTests.cs`:
```csharp
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Terrain;

namespace Symlab.Flight.Tests.Ground;

public class GroundContactTests
{
    static readonly Dictionary<string, double> NoSteer = new();
    static readonly WheelSpec[] Tricycle =
    [
        new("nose", new Vec3(0.2, -0.1, 0), 1000, 20, 0.03, 0.8, 0, NoSteer),
        new("left", new Vec3(-0.1, -0.1, -0.15), 1000, 20, 0.03, 0.8, 0, NoSteer),
        new("right", new Vec3(-0.1, -0.1, 0.15), 1000, 20, 0.03, 0.8, 0, NoSteer),
    ];
    static readonly MassProperties Cart = MassProperties.FromPrincipal(1.0, 0.05, 0.08, 0.05);
    static readonly CrashLimits Limits = new(MaxGearSinkRate: 3, MaxHullImpactSpeed: 1.5, MaxBellyImpactSpeed: 3);

    static RigidBodyState Simulate(GroundContactModel model, RigidBodyState s, double seconds)
    {
        var terrain = new FlatTerrain();
        WrenchFunction f = (in RigidBodyState st) =>
        {
            var load = model.Evaluate(st, terrain, []);
            return new Wrench(st.Orientation.Rotate(load.Force) + new Vec3(0, -9.81 * Cart.Mass, 0), load.Moment);
        };
        for (int i = 0; i < (int)(seconds / 0.002); i++) s = Rk4Integrator.Step(s, 0.002, Cart, f);
        return s;
    }

    [Fact]
    public void Aircraft_settles_on_its_gear_without_drifting()
    {
        var model = new GroundContactModel(Tricycle, [], Cart.Mass);
        var s = Simulate(model, new RigidBodyState(new Vec3(0, 0.1, 0), Vec3.Zero, Quat.Identity, Vec3.Zero), 5);
        Assert.InRange(s.Position.Y, 0.1 - 9.81 / 3000 - 0.002, 0.1 - 9.81 / 3000 + 0.002);
        Assert.True(s.Velocity.Length < 1e-3, $"velocity {s.Velocity}");
        Assert.True(Math.Abs(s.Position.X) < 1e-3 && Math.Abs(s.Position.Z) < 1e-3);
        Assert.Equal(3, model.WheelsInContact(s, new FlatTerrain()));
    }

    [Fact]
    public void Tires_resist_sideways_sliding()
    {
        var model = new GroundContactModel(Tricycle, [], Cart.Mass);
        var start = new RigidBodyState(new Vec3(0, 0.0967, 0), new Vec3(0, 0, 1), Quat.Identity, Vec3.Zero);
        var s = Simulate(model, start, 2);
        Assert.True(Math.Abs(s.Velocity.Z) < 0.05, $"lateral speed {s.Velocity.Z}");
    }

    [Fact]
    public void Tires_roll_forward_freely()
    {
        var model = new GroundContactModel(Tricycle, [], Cart.Mass);
        var start = new RigidBodyState(new Vec3(0, 0.0967, 0), new Vec3(3, 0, 0), Quat.Identity, Vec3.Zero);
        var s = Simulate(model, start, 1);
        Assert.InRange(s.Velocity.X, 2.5, 3.0);
    }

    [Theory]
    [InlineData("wingtip", CrashCause.WingtipStrike)]
    [InlineData("nose", CrashCause.NoseOver)]
    [InlineData("tail", CrashCause.HullImpact)]
    public void Fast_hull_impact_is_a_crash(string tag, CrashCause expected)
    {
        var model = new GroundContactModel([], [new HullPointSpec("p", Vec3.Zero, tag)], 1.0);
        var s = new RigidBodyState(new Vec3(0, -0.001, 0), new Vec3(0, -2, 0), Quat.Identity, Vec3.Zero);
        Assert.Equal(expected, model.DetectCrash(s, new FlatTerrain(), Limits));
    }

    [Fact]
    public void Gentle_belly_landing_is_not_a_crash()
    {
        var model = new GroundContactModel([], [new HullPointSpec("belly", Vec3.Zero, "belly")], 1.0);
        var s = new RigidBodyState(new Vec3(0, -0.001, 0), new Vec3(8, -2, 0), Quat.Identity, Vec3.Zero);
        Assert.Equal(CrashCause.None, model.DetectCrash(s, new FlatTerrain(), Limits));
    }

    [Fact]
    public void Hard_gear_landing_is_a_crash()
    {
        var model = new GroundContactModel(Tricycle, [], 1.0);
        var s = new RigidBodyState(new Vec3(0, 0.099, 0), new Vec3(0, -4, 0), Quat.Identity, Vec3.Zero);
        Assert.Equal(CrashCause.HardLanding, model.DetectCrash(s, new FlatTerrain(), Limits));
    }

    [Fact]
    public void Flying_into_a_tree_is_a_crash()
    {
        var model = new GroundContactModel([], [new HullPointSpec("nose", new Vec3(0.3, 0, 0), "nose")], 1.0);
        var terrain = new FlatTerrain(0, [new CylinderObstacle(10.3, 0, 1, 10)]);
        var s = new RigidBodyState(new Vec3(10, 5, 0), new Vec3(10, 0, 0), Quat.Identity, Vec3.Zero);
        Assert.Equal(CrashCause.TreeStrike, model.DetectCrash(s, terrain, Limits));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~GroundContactTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Ground/GroundSpec.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Ground;

/// <param name="Position">Tire contact point with the strut fully extended, body axes.</param>
/// <param name="SteerMix">Stick channel weights for steering; empty = fixed wheel.</param>
public sealed record WheelSpec(
    string Name,
    Vec3 Position,
    double Stiffness,
    double Damping,
    double RollingFriction,
    double LateralFriction,
    double MaxSteerDeg,
    IReadOnlyDictionary<string, double> SteerMix);

/// <param name="Tag">nose, wingtip, belly, tail, canopy; other values are generic hull points.</param>
public sealed record HullPointSpec(string Name, Vec3 Position, string Tag);

/// <param name="MaxGearSinkRate">Maximum vertical speed at wheel touchdown, m/s.</param>
/// <param name="MaxHullImpactSpeed">Maximum speed into the ground for non-belly hull points, m/s.</param>
/// <param name="MaxBellyImpactSpeed">Maximum vertical speed for belly points (belly landings), m/s.</param>
public sealed record CrashLimits(double MaxGearSinkRate, double MaxHullImpactSpeed, double MaxBellyImpactSpeed);

public enum CrashCause { None, HardLanding, TreeStrike, WingtipStrike, NoseOver, HullImpact }
```

`src/Symlab.Flight/Ground/GroundContactModel.cs`:
```csharp
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;
using Symlab.Flight.Terrain;

namespace Symlab.Flight.Ground;

/// <summary>Penalty-based contact for wheels (spring-damper + tire friction) and hull points.</summary>
public sealed class GroundContactModel
{
    const double FrictionVelocity = 0.05;
    const double HullFriction = 0.6;
    const double HullStiffnessPerKg = 3000;
    const double HullDampingPerKg = 80;

    readonly WheelSpec[] _wheels;
    readonly HullPointSpec[] _hull;
    readonly double _hullStiffness;
    readonly double _hullDamping;

    public GroundContactModel(IEnumerable<WheelSpec> wheels, IEnumerable<HullPointSpec> hull, double mass)
    {
        _wheels = wheels.ToArray();
        _hull = hull.ToArray();
        _hullStiffness = HullStiffnessPerKg * mass;
        _hullDamping = HullDampingPerKg * mass;
    }

    public IReadOnlyList<WheelSpec> Wheels => _wheels;

    readonly record struct Probe(Vec3 Point, double Depth, Vec3 Normal, Vec3 Velocity)
    {
        public double NormalSpeed => Vec3.Dot(Velocity, Normal);
    }

    public BodyLoad Evaluate(in RigidBodyState s, ITerrain terrain, IReadOnlyList<double> steerRad)
    {
        var total = BodyLoad.Zero;
        for (int i = 0; i < _wheels.Length; i++)
            total += WheelLoad(_wheels[i], s, terrain, i < steerRad.Count ? steerRad[i] : 0);
        foreach (var h in _hull)
            total += HullLoad(h, s, terrain);
        return total;
    }

    public int WheelsInContact(in RigidBodyState s, ITerrain terrain)
    {
        int count = 0;
        foreach (var w in _wheels)
            if (ProbePoint(w.Position, s, terrain).Depth > 0) count++;
        return count;
    }

    public CrashCause DetectCrash(in RigidBodyState s, ITerrain terrain, CrashLimits limits)
    {
        foreach (var w in _wheels)
        {
            var p = ProbePoint(w.Position, s, terrain);
            if (p.Depth > 0 && -p.NormalSpeed > limits.MaxGearSinkRate) return CrashCause.HardLanding;
        }
        foreach (var h in _hull)
        {
            var p = ProbePoint(h.Position, s, terrain);
            if (terrain.HitsObstacle(p.Point)) return CrashCause.TreeStrike;
            if (p.Depth <= 0) continue;
            bool belly = h.Tag == "belly";
            double limit = belly ? limits.MaxBellyImpactSpeed : limits.MaxHullImpactSpeed;
            if (-p.NormalSpeed > limit) return CauseFor(h.Tag);
        }
        return CrashCause.None;
    }

    static CrashCause CauseFor(string tag) => tag switch
    {
        "wingtip" => CrashCause.WingtipStrike,
        "nose" => CrashCause.NoseOver,
        "belly" => CrashCause.HardLanding,
        _ => CrashCause.HullImpact,
    };

    static Probe ProbePoint(Vec3 bodyPoint, in RigidBodyState s, ITerrain terrain)
    {
        var p = s.Position + s.Orientation.Rotate(bodyPoint);
        var v = s.Velocity + s.Orientation.Rotate(Vec3.Cross(s.AngularVelocity, bodyPoint));
        return new Probe(p, terrain.Height(p.X, p.Z) - p.Y, terrain.Normal(p.X, p.Z), v);
    }

    static BodyLoad WheelLoad(WheelSpec w, in RigidBodyState s, ITerrain terrain, double steer)
    {
        var c = ProbePoint(w.Position, s, terrain);
        if (c.Depth <= 0) return BodyLoad.Zero;
        double vn = c.NormalSpeed;
        double normal = Math.Max(0, w.Stiffness * c.Depth - w.Damping * vn);

        var roll = s.Orientation.Rotate(new Vec3(Math.Cos(steer), 0, Math.Sin(steer)));
        roll = (roll - c.Normal * Vec3.Dot(roll, c.Normal)).Normalized();
        var lateral = Vec3.Cross(c.Normal, roll);
        var vt = c.Velocity - c.Normal * vn;

        var force = c.Normal * normal
            - roll * (w.RollingFriction * normal * Math.Tanh(Vec3.Dot(vt, roll) / FrictionVelocity))
            - lateral * (w.LateralFriction * normal * Math.Tanh(Vec3.Dot(vt, lateral) / FrictionVelocity));
        return ToBody(s, w.Position, force);
    }

    BodyLoad HullLoad(HullPointSpec h, in RigidBodyState s, ITerrain terrain)
    {
        var c = ProbePoint(h.Position, s, terrain);
        if (c.Depth <= 0) return BodyLoad.Zero;
        double vn = c.NormalSpeed;
        double normal = Math.Max(0, _hullStiffness * c.Depth - _hullDamping * vn);
        var vt = c.Velocity - c.Normal * vn;
        double slip = vt.Length;
        var force = c.Normal * normal - vt.Normalized() * (HullFriction * normal * Math.Tanh(slip / FrictionVelocity));
        return ToBody(s, h.Position, force);
    }

    static BodyLoad ToBody(in RigidBodyState s, Vec3 point, Vec3 forceWorld)
    {
        var f = s.Orientation.InverseRotate(forceWorld);
        return new BodyLoad(f, Vec3.Cross(point, f));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~GroundContactTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(ground): add wheel and hull contact with crash detection

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Aircraft definition and JSON loader

**Files:**
- Create: `src/Symlab.Flight/Airframe/AircraftDefinition.cs`, `LoaderDtos.cs`, `Vec3JsonConverter.cs`, `AircraftLoader.cs`
- Test: `tests/Symlab.Flight.Tests/Airframe/AircraftLoaderTests.cs`

**Interfaces:**
- Consumes: every spec record from Tasks 2–9; `ThrustStand`, `ApcPerformanceFile` (Task 7).
- Produces:
  - `AircraftDefinition(string Name, string Description, string Folder, MassProperties Mass, IReadOnlyList<SurfaceSpec> Surfaces, IReadOnlyDictionary<string, Airfoil> Airfoils, IReadOnlyList<ControlSurfaceSpec> Controls, IReadOnlyList<BodySpec> Bodies, PowerPlantSpec? Power, IReadOnlyList<WheelSpec> Wheels, IReadOnlyList<HullPointSpec> Hull, CrashLimits Crash, IReadOnlyDictionary<string, string> Provenance)`.
  - `AircraftLoader.Load(string folder) : AircraftDefinition`; `AircraftLoader.LoadAirfoil(string aircraftFolder, string name) : Airfoil`; `AircraftLoader.LoadPower(string path) : PowerPlantSpec`. Errors: `FileNotFoundException` for missing files, `InvalidDataException` for malformed or inconsistent content (message starts with the file path).
  - JSON schema (camelCase; comments and trailing commas allowed; vectors are `[x, y, z]`):
    - `aircraft.json`: `name, description, mass, inertia{roll,yaw,pitch,rollYaw}, surfaces[{name, role, root, span, rootChord, tipChord, sweepDeg, dihedralDeg, incidenceDeg, twistDeg, airfoil, segments, mirror, oswald}], controls[{name, surface, side, chordFraction, spanStart, spanEnd, maxPositiveDeg, maxNegativeDeg, servoSecondsPer60Deg, mix{}}], bodies[{name, position, cdA}], power, gear[{name, position, stiffness, damping, rollingFriction, lateralFriction, maxSteerDeg, steerMix{}}], hull[{name, position, tag}], crash{maxGearSinkRate, maxHullImpactSpeed, maxBellyImpactSpeed}, provenance{}`.
    - Airfoils: `<folder>/airfoils/<name>.json`, falling back to `<folder>/../airfoils/<name>.json`; content `{name, provenance, tables[{reynolds, alphaDeg[], cl[], cd[], cm[]}]}`.
    - `power.json`: `position, thrustAxis, spinDirection, pFactor, motor{kv, resistanceOhm, noLoadCurrentA, maxCurrentA, rotorInertia}, battery{cells, capacityAh, internalResistanceOhm}, esc{throttleIn[], throttleOut[], brake}, propeller{genericDiameterIn, genericPitchIn} | {diameterM, pitchM, j[], ct[], cp[]} | {diameterM, pitchM, apcFile, apcRpm}, thrustStand` (CSV path relative to the power file).

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Airframe/AircraftLoaderTests.cs`:
```csharp
using Symlab.Flight.Aero;
using Symlab.Flight.Airframe;

namespace Symlab.Flight.Tests.Airframe;

public sealed class AircraftLoaderTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("symlab-loader-").FullName;

    const string Airfoil = """
        { "name": "flat", "tables": [ { "reynolds": 100000, "alphaDeg": [-10, 10], "cl": [-1.1, 1.1], "cd": [0.02, 0.02], "cm": [0, 0] } ] }
        """;

    const string Power = """
        {
          "position": [0.3, 0, 0], "thrustAxis": [2, 0, 0], "spinDirection": 1, "pFactor": 0.1,
          "motor": { "kv": 1000, "resistanceOhm": 0.05, "noLoadCurrentA": 1, "maxCurrentA": 40, "rotorInertia": 1e-4 },
          "battery": { "cells": 3, "capacityAh": 2.2, "internalResistanceOhm": 0.03 },
          "propeller": { "genericDiameterIn": 10, "genericPitchIn": 5 },
        }
        """;

    static string Aircraft(string controlSurface = "wing", string mixChannel = "aileron") => $$"""
        {
          // comments are allowed
          "name": "Loader test", "description": "tiny", "mass": 1.2,
          "inertia": { "roll": 0.05, "yaw": 0.08, "pitch": 0.04 },
          "surfaces": [
            { "name": "wing", "role": "wing", "root": [0, 0, 0], "span": 0.6, "rootChord": 0.2, "tipChord": 0.15,
              "dihedralDeg": 3, "airfoil": "flat", "segments": 4, "mirror": true }
          ],
          "controls": [
            { "name": "aileronRight", "surface": "{{controlSurface}}", "side": "right", "chordFraction": 0.25,
              "spanStart": 0.5, "spanEnd": 1, "maxPositiveDeg": 15, "maxNegativeDeg": 15, "mix": { "{{mixChannel}}": -1 } }
          ],
          "power": "power.json",
          "gear": [ { "name": "main", "position": [0, -0.1, 0], "stiffness": 800, "damping": 15, "steerMix": { "rudder": 1 }, "maxSteerDeg": 20 } ],
          "hull": [ { "name": "nose", "position": [0.3, 0, 0], "tag": "nose" } ],
          "provenance": { "mass": "measured" },
        }
        """;

    string Write(string aircraftJson, bool sharedAirfoil = false)
    {
        var folder = Path.Combine(_root, "plane");
        Directory.CreateDirectory(folder);
        var airfoilDir = sharedAirfoil ? Path.Combine(_root, "airfoils") : Path.Combine(folder, "airfoils");
        Directory.CreateDirectory(airfoilDir);
        File.WriteAllText(Path.Combine(airfoilDir, "flat.json"), Airfoil);
        File.WriteAllText(Path.Combine(folder, "power.json"), Power);
        File.WriteAllText(Path.Combine(folder, "aircraft.json"), aircraftJson);
        return folder;
    }

    [Fact]
    public void Loads_a_complete_definition()
    {
        var def = AircraftLoader.Load(Write(Aircraft()));
        Assert.Equal("Loader test", def.Name);
        Assert.Equal(1.2, def.Mass.Mass);
        Assert.Equal(SurfaceRole.Wing, def.Surfaces[0].Role);
        Assert.Equal(Side.Right, def.Controls[0].Side);
        Assert.Equal(-1, def.Controls[0].Mix["aileron"]);
        Assert.Equal(4, def.Surfaces[0].Segments);
        Assert.NotNull(def.Power);
        Assert.Equal(1.0, def.Power!.ThrustAxis.X, 12);
        Assert.Equal("measured", def.Provenance["mass"]);
        Assert.Equal(20, def.Wheels[0].MaxSteerDeg);
        Assert.Equal(3, def.Crash.MaxGearSinkRate);
        Assert.True(def.Airfoils.ContainsKey("flat"));
    }

    [Fact]
    public void Finds_airfoils_in_the_shared_folder()
        => Assert.True(AircraftLoader.Load(Write(Aircraft(), sharedAirfoil: true)).Airfoils.ContainsKey("flat"));

    [Fact]
    public void Control_on_unknown_surface_is_rejected()
        => Assert.Throws<InvalidDataException>(() => AircraftLoader.Load(Write(Aircraft(controlSurface: "canard"))));

    [Fact]
    public void Unknown_mix_channel_is_rejected()
        => Assert.Throws<InvalidDataException>(() => AircraftLoader.Load(Write(Aircraft(mixChannel: "gear"))));

    [Fact]
    public void Malformed_json_reports_the_file()
    {
        var ex = Assert.Throws<InvalidDataException>(() => AircraftLoader.Load(Write("{ not json")));
        Assert.Contains("aircraft.json", ex.Message);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~AircraftLoaderTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Airframe/AircraftDefinition.cs`:
```csharp
using Symlab.Flight.Aero;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Ground;
using Symlab.Flight.Propulsion;

namespace Symlab.Flight.Airframe;

/// <summary>Everything needed to build an <see cref="Aircraft"/>. Body origin is the CG.</summary>
/// <param name="Folder">Folder the definition was loaded from (holds model.glb for the game layer).</param>
/// <param name="Provenance">Parameter path → source (estimated, measured, xfoil, vspaero, cfd).</param>
public sealed record AircraftDefinition(
    string Name,
    string Description,
    string Folder,
    MassProperties Mass,
    IReadOnlyList<SurfaceSpec> Surfaces,
    IReadOnlyDictionary<string, Airfoil> Airfoils,
    IReadOnlyList<ControlSurfaceSpec> Controls,
    IReadOnlyList<BodySpec> Bodies,
    PowerPlantSpec? Power,
    IReadOnlyList<WheelSpec> Wheels,
    IReadOnlyList<HullPointSpec> Hull,
    CrashLimits Crash,
    IReadOnlyDictionary<string, string> Provenance);
```

`src/Symlab.Flight/Airframe/Vec3JsonConverter.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Airframe;

internal sealed class Vec3JsonConverter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = JsonSerializer.Deserialize<double[]>(ref reader, options);
        if (values is not { Length: 3 }) throw new JsonException("Expected a vector [x, y, z].");
        return new Vec3(values[0], values[1], values[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}
```

`src/Symlab.Flight/Airframe/LoaderDtos.cs`:
```csharp
using Symlab.Flight.Aero;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Airframe;

internal sealed class AircraftDto
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public double Mass { get; set; }
    public InertiaDto Inertia { get; set; } = new();
    public List<SurfaceDto> Surfaces { get; set; } = [];
    public List<ControlDto> Controls { get; set; } = [];
    public List<BodyDto> Bodies { get; set; } = [];
    public string? Power { get; set; }
    public List<WheelDto> Gear { get; set; } = [];
    public List<HullDto> Hull { get; set; } = [];
    public CrashDto Crash { get; set; } = new();
    public Dictionary<string, string> Provenance { get; set; } = new();
}

internal sealed class InertiaDto
{
    public double Roll { get; set; }
    public double Yaw { get; set; }
    public double Pitch { get; set; }
    public double RollYaw { get; set; }
}

internal sealed class SurfaceDto
{
    public string Name { get; set; } = "";
    public SurfaceRole Role { get; set; } = SurfaceRole.Other;
    public Vec3 Root { get; set; }
    public double Span { get; set; }
    public double RootChord { get; set; }
    public double TipChord { get; set; }
    public double SweepDeg { get; set; }
    public double DihedralDeg { get; set; }
    public double IncidenceDeg { get; set; }
    public double TwistDeg { get; set; }
    public string Airfoil { get; set; } = "";
    public int Segments { get; set; } = 4;
    public bool Mirror { get; set; }
    public double Oswald { get; set; } = 0.85;
}

internal sealed class ControlDto
{
    public string Name { get; set; } = "";
    public string Surface { get; set; } = "";
    public Side Side { get; set; } = Side.Both;
    public double ChordFraction { get; set; } = 0.25;
    public double SpanStart { get; set; }
    public double SpanEnd { get; set; } = 1;
    public double MaxPositiveDeg { get; set; } = 15;
    public double MaxNegativeDeg { get; set; } = 15;
    public double ServoSecondsPer60Deg { get; set; } = 0.12;
    public Dictionary<string, double> Mix { get; set; } = new();
}

internal sealed class BodyDto
{
    public string Name { get; set; } = "";
    public Vec3 Position { get; set; }
    public Vec3 CdA { get; set; }
}

internal sealed class WheelDto
{
    public string Name { get; set; } = "";
    public Vec3 Position { get; set; }
    public double Stiffness { get; set; }
    public double Damping { get; set; }
    public double RollingFriction { get; set; } = 0.04;
    public double LateralFriction { get; set; } = 0.8;
    public double MaxSteerDeg { get; set; }
    public Dictionary<string, double> SteerMix { get; set; } = new();
}

internal sealed class HullDto
{
    public string Name { get; set; } = "";
    public Vec3 Position { get; set; }
    public string Tag { get; set; } = "hull";
}

internal sealed class CrashDto
{
    public double MaxGearSinkRate { get; set; } = 3.0;
    public double MaxHullImpactSpeed { get; set; } = 1.5;
    public double MaxBellyImpactSpeed { get; set; } = 3.0;
}

internal sealed class AirfoilDto
{
    public string Name { get; set; } = "";
    public string Provenance { get; set; } = "estimated";
    public List<AirfoilTableDto> Tables { get; set; } = [];
}

internal sealed class AirfoilTableDto
{
    public double Reynolds { get; set; }
    public double[] AlphaDeg { get; set; } = [];
    public double[] Cl { get; set; } = [];
    public double[] Cd { get; set; } = [];
    public double[] Cm { get; set; } = [];
}

internal sealed class PowerDto
{
    public Vec3 Position { get; set; }
    public Vec3 ThrustAxis { get; set; } = Vec3.UnitX;
    public int SpinDirection { get; set; } = 1;
    public double PFactor { get; set; } = 0.1;
    public MotorDto? Motor { get; set; }
    public BatteryDto? Battery { get; set; }
    public EscDto? Esc { get; set; }
    public PropellerDto? Propeller { get; set; }
    public string? ThrustStand { get; set; }
}

internal sealed class MotorDto
{
    public double Kv { get; set; }
    public double ResistanceOhm { get; set; }
    public double NoLoadCurrentA { get; set; }
    public double MaxCurrentA { get; set; } = 100;
    public double RotorInertia { get; set; }
}

internal sealed class BatteryDto
{
    public int Cells { get; set; }
    public double CapacityAh { get; set; }
    public double InternalResistanceOhm { get; set; }
}

internal sealed class EscDto
{
    public double[] ThrottleIn { get; set; } = [0, 1];
    public double[] ThrottleOut { get; set; } = [0, 1];
    public bool Brake { get; set; }
}

internal sealed class PropellerDto
{
    public double? GenericDiameterIn { get; set; }
    public double? GenericPitchIn { get; set; }
    public double DiameterM { get; set; }
    public double PitchM { get; set; }
    public double[]? J { get; set; }
    public double[]? Ct { get; set; }
    public double[]? Cp { get; set; }
    public string? ApcFile { get; set; }
    public double ApcRpm { get; set; } = 8000;
}
```

`src/Symlab.Flight/Airframe/AircraftLoader.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Symlab.Flight.Aero;
using Symlab.Flight.Controls;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Ground;
using Symlab.Flight.Numerics;
using Symlab.Flight.Propulsion;

namespace Symlab.Flight.Airframe;

public static class AircraftLoader
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new Vec3JsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static AircraftDefinition Load(string folder)
    {
        var path = Path.Combine(folder, "aircraft.json");
        var dto = Read<AircraftDto>(path);
        if (dto.Mass <= 0) throw Invalid(path, "mass must be positive.");
        if (dto.Surfaces.Count == 0) throw Invalid(path, "at least one surface is required.");

        var surfaces = dto.Surfaces.Select(s => new SurfaceSpec(
            s.Name, s.Role, s.Root, s.Span, s.RootChord, s.TipChord, s.SweepDeg, s.DihedralDeg,
            s.IncidenceDeg, s.TwistDeg, s.Airfoil, s.Segments, s.Mirror, s.Oswald)).ToList();

        var airfoils = surfaces.Select(s => s.Airfoil).Distinct()
            .ToDictionary(name => name, name => LoadAirfoil(folder, name));

        var surfaceNames = surfaces.Select(s => s.Name).ToHashSet();
        foreach (var c in dto.Controls)
        {
            if (!surfaceNames.Contains(c.Surface)) throw Invalid(path, $"control '{c.Name}' references unknown surface '{c.Surface}'.");
            RequireChannels(path, c.Name, c.Mix.Keys);
        }
        foreach (var w in dto.Gear) RequireChannels(path, w.Name, w.SteerMix.Keys);

        var controls = dto.Controls.Select(c => new ControlSurfaceSpec(
            c.Name, c.Surface, c.Side, c.ChordFraction, c.SpanStart, c.SpanEnd,
            c.MaxPositiveDeg, c.MaxNegativeDeg, c.ServoSecondsPer60Deg, c.Mix)).ToList();

        return new AircraftDefinition(
            dto.Name,
            dto.Description,
            folder,
            MassProperties.FromPrincipal(dto.Mass, dto.Inertia.Roll, dto.Inertia.Yaw, dto.Inertia.Pitch, dto.Inertia.RollYaw),
            surfaces,
            airfoils,
            controls,
            dto.Bodies.Select(b => new BodySpec(b.Name, b.Position, b.CdA)).ToList(),
            dto.Power is null ? null : LoadPower(Path.Combine(folder, dto.Power)),
            dto.Gear.Select(w => new WheelSpec(w.Name, w.Position, w.Stiffness, w.Damping, w.RollingFriction,
                w.LateralFriction, w.MaxSteerDeg, w.SteerMix)).ToList(),
            dto.Hull.Select(h => new HullPointSpec(h.Name, h.Position, h.Tag)).ToList(),
            new CrashLimits(dto.Crash.MaxGearSinkRate, dto.Crash.MaxHullImpactSpeed, dto.Crash.MaxBellyImpactSpeed),
            dto.Provenance);
    }

    public static Airfoil LoadAirfoil(string aircraftFolder, string name)
    {
        var candidates = new[]
        {
            Path.Combine(aircraftFolder, "airfoils", name + ".json"),
            Path.Combine(aircraftFolder, "..", "airfoils", name + ".json"),
        };
        var path = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Airfoil '{name}' not found (looked in {string.Join(", ", candidates)}).");
        var dto = Read<AirfoilDto>(path);
        try
        {
            return new Airfoil(dto.Name, dto.Tables.Select(t => new AirfoilTable(t.Reynolds, t.AlphaDeg, t.Cl, t.Cd, t.Cm)));
        }
        catch (ArgumentException ex)
        {
            throw Invalid(path, ex.Message);
        }
    }

    public static PowerPlantSpec LoadPower(string path)
    {
        var dto = Read<PowerDto>(path);
        if (dto.Motor is null || dto.Battery is null || dto.Propeller is null)
            throw Invalid(path, "motor, battery and propeller are required.");
        if (dto.Motor.Kv <= 0 || dto.Motor.RotorInertia <= 0) throw Invalid(path, "motor kv and rotorInertia must be positive.");

        var esc = dto.Esc is null ? EscSpec.Linear() : new EscSpec(dto.Esc.ThrottleIn, dto.Esc.ThrottleOut, dto.Esc.Brake);
        Interpolation.RequireIncreasing(esc.ThrottleIn, "esc.throttleIn");

        var spec = new PowerPlantSpec(
            new MotorSpec(dto.Motor.Kv, dto.Motor.ResistanceOhm, dto.Motor.NoLoadCurrentA, dto.Motor.MaxCurrentA, dto.Motor.RotorInertia),
            BatterySpec.Lipo(dto.Battery.Cells, dto.Battery.CapacityAh, dto.Battery.InternalResistanceOhm),
            esc,
            Propeller(path, dto.Propeller),
            dto.Position,
            dto.ThrustAxis.Normalized(),
            dto.SpinDirection >= 0 ? 1 : -1,
            dto.PFactor);

        if (dto.ThrustStand is null) return spec;
        var csv = Path.Combine(Path.GetDirectoryName(path)!, dto.ThrustStand);
        return ThrustStand.Calibrate(spec, ThrustStand.ParseCsv(File.ReadAllText(csv)));
    }

    static PropellerSpec Propeller(string path, PropellerDto p)
    {
        if (p.GenericDiameterIn is double d && p.GenericPitchIn is double pitch) return PropellerSpec.Generic(d, pitch);
        if (p.DiameterM <= 0) throw Invalid(path, "propeller needs genericDiameterIn/genericPitchIn or diameterM.");
        if (p.ApcFile is not null)
        {
            var apc = Path.Combine(Path.GetDirectoryName(path)!, p.ApcFile);
            return ApcPerformanceFile.Parse(File.ReadAllText(apc), p.DiameterM, p.PitchM, p.ApcRpm);
        }
        if (p.J is null || p.Ct is null || p.Cp is null || p.J.Length != p.Ct.Length || p.J.Length != p.Cp.Length || p.J.Length < 2)
            throw Invalid(path, "propeller tables j, ct and cp must have the same length (at least 2).");
        Interpolation.RequireIncreasing(p.J, "propeller.j");
        return new PropellerSpec(p.DiameterM, p.PitchM, p.J, p.Ct, p.Cp);
    }

    static void RequireChannels(string path, string owner, IEnumerable<string> channels)
    {
        foreach (var ch in channels)
            if (!ControlInputs.IsChannel(ch)) throw Invalid(path, $"'{owner}' mixes unknown channel '{ch}'.");
    }

    static T Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}", path);
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw Invalid(path, "document is empty.");
        }
        catch (JsonException ex)
        {
            throw Invalid(path, ex.Message);
        }
    }

    static InvalidDataException Invalid(string path, string message) => new($"{path}: {message}");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~AircraftLoaderTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(airframe): add aircraft definition and JSON loader

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Aircraft assembly, flight environment and fixed-step simulation

**Files:**
- Create: `src/Symlab.Flight/Airframe/Aircraft.cs`, `AirData.cs`
- Create: `src/Symlab.Flight/Sim/FlightEnvironment.cs`, `Simulation.cs`, `InitialConditions.cs`
- Test: `tests/Symlab.Flight.Tests/Airframe/TestDefinitions.cs`, `tests/Symlab.Flight.Tests/Sim/SimulationTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `AirData(double Airspeed, double Alpha, double Beta)` with `From(Vec3 airVelocityBody)`.
  - `Aircraft(AircraftDefinition)` with `Definition`, `Aero` (`SurfaceAeroModel`), `Power` (`PowerPlant?`), `Ground`, `State`, `Crash`, `AirData`, `Deflections`, `Reset(RigidBodyState)`, `OverrideState(RigidBodyState)`, `Step(double dt, in ControlInputs, FlightEnvironment)`; constants `Gravity = 9.80665`, `WashFactor = 0.8`.
  - `FlightEnvironment(ITerrain terrain, WindField wind, double fieldElevationM = 0, double temperatureOffsetK = 0)` with `Terrain`, `Wind`, `Density(double worldY)`, static `Calm(ITerrain? terrain = null)`.
  - `Simulation(Aircraft, FlightEnvironment)` with const `FixedStep = 0.002`, const `MaxFrameTime = 0.25`, `Aircraft`, `Environment`, `Time`, `Previous`, `InterpolationAlpha`, `Recorder` (`FlightRecorder?`, Task 14), `Reset(RigidBodyState)`, `StepOnce(in ControlInputs)`, `Advance(double frameDt, in ControlInputs) : int`.
  - `InitialConditions.InFlight(Vec3 position, double headingDeg, double airspeed, double pitchDeg = 0, double rollDeg = 0)`, `InitialConditions.OnGround(AircraftDefinition, ITerrain, double x, double z, double headingDeg)`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Airframe/TestDefinitions.cs`:
```csharp
using Symlab.Flight.Aero;
using Symlab.Flight.Airframe;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Tests.Aero;

namespace Symlab.Flight.Tests.Airframe;

internal static class TestDefinitions
{
    /// <summary>1 kg unpowered glider, statically stable, used for simulation plumbing tests.</summary>
    public static AircraftDefinition Glider() => new(
        Name: "Test glider",
        Description: "",
        Folder: "",
        Mass: MassProperties.FromPrincipal(1.0, roll: 0.05, yaw: 0.08, pitch: 0.04),
        Surfaces:
        [
            new SurfaceSpec("wing", SurfaceRole.Wing, new Vec3(0.02, 0.05, 0), 0.75, 0.2, 0.2, 0, 4, 3, 0, "linear", 6, true),
            new SurfaceSpec("stab", SurfaceRole.HorizontalTail, new Vec3(-0.6, 0, 0), 0.2, 0.12, 0.12, 0, 0, 0, 0, "linear", 2, true),
            new SurfaceSpec("fin", SurfaceRole.VerticalTail, new Vec3(-0.6, 0, 0), 0.15, 0.12, 0.12, 0, 90, 0, 0, "linear", 2, false),
        ],
        Airfoils: TestAirfoils.Map(),
        Controls:
        [
            new ControlSurfaceSpec("elevator", "stab", Side.Both, 0.4, 0, 1, 20, 20, 0.1, new Dictionary<string, double> { ["elevator"] = -1 }),
        ],
        Bodies: [],
        Power: null,
        Wheels: [],
        Hull:
        [
            new HullPointSpec("nose", new Vec3(0.3, 0, 0), "nose"),
            new HullPointSpec("belly", new Vec3(0, -0.05, 0), "belly"),
            new HullPointSpec("tail", new Vec3(-0.65, 0, 0), "tail"),
        ],
        Crash: new CrashLimits(3, 1.5, 3),
        Provenance: new Dictionary<string, string>());
}
```

`tests/Symlab.Flight.Tests/Sim/SimulationTests.cs`:
```csharp
using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Sim;
using Symlab.Flight.Tests.Airframe;

namespace Symlab.Flight.Tests.Sim;

public class SimulationTests
{
    static Simulation Glider(double altitude = 50, double speed = 12, double pitchDeg = 0)
    {
        var sim = new Simulation(new Aircraft(TestDefinitions.Glider()), FlightEnvironment.Calm());
        sim.Reset(InitialConditions.InFlight(new Vec3(0, altitude, 0), headingDeg: 0, airspeed: speed, pitchDeg: pitchDeg));
        return sim;
    }

    [Fact]
    public void Glider_glides_forward_and_down()
    {
        var sim = Glider();
        for (int i = 0; i < 2500; i++) sim.StepOnce(ControlInputs.Neutral);
        var s = sim.Aircraft.State;
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.InRange(s.Position.Y, 30, 50);
        Assert.True(s.Position.Z < -30, $"north distance {-s.Position.Z}");
        Assert.InRange(sim.Aircraft.AirData.Airspeed, 6, 25);
        Assert.Equal(5.0, sim.Time, 9);
    }

    [Fact]
    public void Runs_are_deterministic()
    {
        var a = Glider();
        var b = Glider();
        var input = new ControlInputs(0, 0, 0.2, 0);
        for (int i = 0; i < 1000; i++) { a.StepOnce(input); b.StepOnce(input); }
        Assert.Equal(a.Aircraft.State, b.Aircraft.State);
    }

    [Fact]
    public void Advance_runs_whole_fixed_steps_and_reports_the_remainder()
    {
        var sim = Glider();
        Assert.Equal(2, sim.Advance(0.005, ControlInputs.Neutral));
        Assert.Equal(0.5, sim.InterpolationAlpha, 6);
        Assert.Equal(125, Glider().Advance(1.0, ControlInputs.Neutral));
    }

    [Fact]
    public void Previous_state_is_kept_for_interpolation()
    {
        var sim = Glider();
        sim.StepOnce(ControlInputs.Neutral);
        var before = sim.Aircraft.State;
        sim.StepOnce(ControlInputs.Neutral);
        Assert.Equal(before, sim.Previous);
    }

    [Fact]
    public void Diving_into_the_ground_crashes_and_freezes_the_aircraft()
    {
        var sim = Glider(altitude: 1.5, speed: 15, pitchDeg: -45);
        for (int i = 0; i < 1000 && sim.Aircraft.Crash == CrashCause.None; i++) sim.StepOnce(ControlInputs.Neutral);
        Assert.NotEqual(CrashCause.None, sim.Aircraft.Crash);
        var frozen = sim.Aircraft.State;
        sim.StepOnce(ControlInputs.Neutral);
        Assert.Equal(frozen, sim.Aircraft.State);
    }

    [Fact]
    public void Servo_deflection_follows_the_stick_with_rate_limit()
    {
        var sim = Glider();
        sim.StepOnce(new ControlInputs(0, 0, 1, 0));
        double first = sim.Aircraft.Deflections[0];
        Assert.True(first < 0 && first > -Angle.Rad(20));
        for (int i = 0; i < 200; i++) sim.StepOnce(new ControlInputs(0, 0, 1, 0));
        Assert.Equal(-Angle.Rad(20), sim.Aircraft.Deflections[0], 9);
    }

    [Fact]
    public void On_ground_start_puts_the_lowest_point_on_the_terrain()
    {
        var def = TestDefinitions.Glider();
        var s = InitialConditions.OnGround(def, new Symlab.Flight.Terrain.FlatTerrain(10), 0, 0, 90);
        Assert.Equal(10.051, s.Position.Y, 6);
        Assert.Equal(1.0, s.Orientation.Rotate(Vec3.UnitX).X, 9);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~SimulationTests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Airframe/AirData.cs`:
```csharp
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Airframe;

/// <summary>Airspeed (m/s), angle of attack and sideslip (rad; β positive when the air comes from the right).</summary>
public readonly record struct AirData(double Airspeed, double Alpha, double Beta)
{
    public static AirData From(Vec3 airVelocityBody)
    {
        double v = airVelocityBody.Length;
        if (v < 1e-6) return default;
        return new AirData(v, Math.Atan2(-airVelocityBody.Y, airVelocityBody.X), Math.Asin(Math.Clamp(airVelocityBody.Z / v, -1, 1)));
    }
}
```

`src/Symlab.Flight/Airframe/Aircraft.cs`:
```csharp
using Symlab.Flight.Aero;
using Symlab.Flight.Controls;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Propulsion;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Airframe;

/// <summary>An assembled, steppable aircraft: aero + propulsion + servos + ground contact on a rigid body.</summary>
public sealed class Aircraft
{
    public const double Gravity = 9.80665;

    /// <summary>Fraction of the far-wake slipstream velocity seen by surfaces behind the prop.</summary>
    public const double WashFactor = 0.8;

    readonly Servo[] _servos;
    readonly double[] _deflections;
    readonly double[] _steer;

    public Aircraft(AircraftDefinition definition)
    {
        Definition = definition;
        Aero = new SurfaceAeroModel(definition.Surfaces, definition.Airfoils, definition.Controls, definition.Bodies);
        Power = definition.Power is null ? null : new PowerPlant(definition.Power);
        Ground = new GroundContactModel(definition.Wheels, definition.Hull, definition.Mass.Mass);
        _servos = definition.Controls.Select(c => new Servo(c.ServoSecondsPer60Deg)).ToArray();
        _deflections = new double[_servos.Length];
        _steer = new double[definition.Wheels.Count];
        State = new RigidBodyState(Vec3.Zero, Vec3.Zero, Quat.Identity, Vec3.Zero);
    }

    public AircraftDefinition Definition { get; }
    public SurfaceAeroModel Aero { get; }
    public PowerPlant? Power { get; }
    public GroundContactModel Ground { get; }
    public RigidBodyState State { get; private set; }
    public CrashCause Crash { get; private set; }
    public AirData AirData { get; private set; }
    public IReadOnlyList<double> Deflections => _deflections;

    public void Reset(RigidBodyState state)
    {
        State = state;
        Crash = CrashCause.None;
        AirData = default;
        Aero.Reset();
        Power?.Reset();
        foreach (var s in _servos) s.Reset();
        Array.Clear(_deflections);
        Array.Clear(_steer);
    }

    /// <summary>Replaces the rigid-body state only (for perturbation tests and editor tools).</summary>
    public void OverrideState(RigidBodyState state) => State = state;

    public void Step(double dt, in ControlInputs input, FlightEnvironment env)
    {
        if (Crash != CrashCause.None) return;

        var controls = Definition.Controls;
        for (int i = 0; i < _servos.Length; i++)
        {
            var c = controls[i];
            _servos[i].Step(ControlMapping.CommandToDeflection(ControlInputs.Mix(c.Mix, input), c.MaxPositiveDeg, c.MaxNegativeDeg), dt);
            _deflections[i] = _servos[i].Position;
        }
        var wheels = Definition.Wheels;
        for (int i = 0; i < _steer.Length; i++)
            _steer[i] = ControlInputs.Mix(wheels[i].SteerMix, input) * Angle.Rad(wheels[i].MaxSteerDeg);

        var start = State;
        double heightAgl = start.Position.Y - env.Terrain.Height(start.Position.X, start.Position.Z);
        var wind = env.Wind.At(heightAgl);
        double density = env.Density(start.Position.Y);

        PowerTelemetry telemetry = default;
        PropWash wash = default;
        if (Power is not null)
        {
            var spec = Power.Spec;
            var air = start.Orientation.InverseRotate(start.Velocity - wind);
            double axial = Vec3.Dot(air + Vec3.Cross(start.AngularVelocity, spec.Position), spec.ThrustAxis);
            telemetry = Power.Step(dt, input.Throttle, axial, density);
            wash = new PropWash(spec.Position, spec.ThrustAxis, spec.Propeller.DiameterM / 2, telemetry.WashVelocity * WashFactor);
        }
        double propOmega = Power?.Omega ?? 0;
        double weight = Definition.Mass.Mass * Gravity;

        WrenchFunction forces = (in RigidBodyState s) =>
        {
            var air = s.Orientation.InverseRotate(s.Velocity - wind);
            double height = s.Position.Y - env.Terrain.Height(s.Position.X, s.Position.Z);
            var up = s.Orientation.InverseRotate(Vec3.UnitY);
            var load = Aero.Evaluate(new AeroContext(air, s.AngularVelocity, density, height, up, _deflections, wash));
            if (Power is not null) load += PowerPlantLoads.Compute(Power.Spec, telemetry, propOmega, air, s.AngularVelocity);
            load += Ground.Evaluate(s, env.Terrain, _steer);
            return new Wrench(s.Orientation.Rotate(load.Force) + new Vec3(0, -weight, 0), load.Moment);
        };

        State = Rk4Integrator.Step(start, dt, Definition.Mass, forces);
        Aero.Advance(dt);
        AirData = AirData.From(State.Orientation.InverseRotate(State.Velocity - wind));
        Crash = Ground.DetectCrash(State, env.Terrain, Definition.Crash);
    }
}
```

`src/Symlab.Flight/Sim/FlightEnvironment.cs`:
```csharp
using Symlab.Flight.Atmosphere;
using Symlab.Flight.Terrain;

namespace Symlab.Flight.Sim;

public sealed class FlightEnvironment
{
    public FlightEnvironment(ITerrain terrain, WindField wind, double fieldElevationM = 0, double temperatureOffsetK = 0)
    {
        Terrain = terrain;
        Wind = wind;
        FieldElevationM = fieldElevationM;
        TemperatureOffsetK = temperatureOffsetK;
    }

    public ITerrain Terrain { get; }
    public WindField Wind { get; }
    public double FieldElevationM { get; }
    public double TemperatureOffsetK { get; }

    /// <summary>Air density at a world height (world y = height above the field datum).</summary>
    public double Density(double worldY) => Isa.Density(FieldElevationM + worldY, TemperatureOffsetK);

    public static FlightEnvironment Calm(ITerrain? terrain = null) =>
        new(terrain ?? new FlatTerrain(), new WindField(new WindSettings(), seed: 1));
}
```

`src/Symlab.Flight/Sim/Simulation.cs`:
```csharp
using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Recording;

namespace Symlab.Flight.Sim;

/// <summary>Fixed-step driver: accumulates frame time and advances the aircraft in 2 ms steps.</summary>
public sealed class Simulation
{
    public const double FixedStep = 0.002;
    public const double MaxFrameTime = 0.25;

    double _accumulator;

    public Simulation(Aircraft aircraft, FlightEnvironment environment)
    {
        Aircraft = aircraft;
        Environment = environment;
        Previous = aircraft.State;
    }

    public Aircraft Aircraft { get; }
    public FlightEnvironment Environment { get; }
    public double Time { get; private set; }
    public RigidBodyState Previous { get; private set; }

    /// <summary>Fraction of a fixed step between <see cref="Previous"/> and the current state, for rendering.</summary>
    public double InterpolationAlpha => _accumulator / FixedStep;

    public FlightRecorder? Recorder { get; set; }

    public void Reset(RigidBodyState state)
    {
        Aircraft.Reset(state);
        Previous = state;
        Time = 0;
        _accumulator = 0;
    }

    public void StepOnce(in ControlInputs input)
    {
        Previous = Aircraft.State;
        var s = Aircraft.State;
        double heightAgl = s.Position.Y - Environment.Terrain.Height(s.Position.X, s.Position.Z);
        Environment.Wind.Advance(FixedStep, heightAgl, Aircraft.AirData.Airspeed);
        Aircraft.Step(FixedStep, input, Environment);
        Time += FixedStep;
        Recorder?.OnStep(this, input);
    }

    public int Advance(double frameDt, in ControlInputs input)
    {
        _accumulator += Math.Clamp(frameDt, 0, MaxFrameTime);
        int steps = 0;
        while (_accumulator >= FixedStep - 1e-12)
        {
            StepOnce(input);
            _accumulator -= FixedStep;
            steps++;
        }
        if (_accumulator < 0) _accumulator = 0;
        return steps;
    }
}
```

`src/Symlab.Flight/Sim/InitialConditions.cs`:
```csharp
using Symlab.Flight.Airframe;
using Symlab.Flight.Dynamics;
using Symlab.Flight.Geometry;
using Symlab.Flight.Terrain;

namespace Symlab.Flight.Sim;

public static class InitialConditions
{
    public static RigidBodyState InFlight(Vec3 position, double headingDeg, double airspeed, double pitchDeg = 0, double rollDeg = 0)
    {
        var q = Attitude.ToOrientation(Angle.Rad(rollDeg), Angle.Rad(pitchDeg), Angle.Rad(headingDeg));
        return new RigidBodyState(position, q.Rotate(Vec3.UnitX) * airspeed, q, Vec3.Zero);
    }

    /// <summary>Level attitude with the lowest wheel or hull point 1 mm above the terrain; the aircraft then settles.</summary>
    public static RigidBodyState OnGround(AircraftDefinition definition, ITerrain terrain, double x, double z, double headingDeg)
    {
        var q = Attitude.ToOrientation(0, 0, Angle.Rad(headingDeg));
        var lowest = definition.Wheels.Select(w => w.Position.Y)
            .Concat(definition.Hull.Select(h => h.Position.Y))
            .DefaultIfEmpty(0)
            .Min();
        return new RigidBodyState(new Vec3(x, terrain.Height(x, z) - lowest + 0.001, z), Vec3.Zero, q, Vec3.Zero);
    }
}
```

Create a placeholder-free minimal `FlightRecorder` now so `Simulation` compiles; Task 14 completes it test-first.
`src/Symlab.Flight/Recording/FlightRecorder.cs`:
```csharp
using Symlab.Flight.Controls;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Recording;

public sealed class FlightRecorder
{
    public void OnStep(Simulation simulation, in ControlInputs input) { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all PASS (whole suite, since `Aircraft` touches every module).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(sim): assemble aircraft and add fixed-step simulation driver

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 12: Airfoil data and the three aircraft definitions

**Files:**
- Create: `aircraft/airfoils/clarky.json`, `aircraft/airfoils/naca0012.json`, `aircraft/airfoils/reflex-mh45.json`
- Create: `aircraft/trainer/aircraft.json`, `aircraft/trainer/power.json`
- Create: `aircraft/sport/aircraft.json`, `aircraft/sport/power.json`
- Create: `aircraft/wing/aircraft.json`, `aircraft/wing/power.json`
- Create: `tests/Symlab.Flight.Tests/Behavior/Fleet.cs`
- Test: `tests/Symlab.Flight.Tests/Behavior/FleetDefinitionTests.cs`

**Interfaces:**
- Consumes: `AircraftLoader`, `Aircraft`, `Simulation`, `InitialConditions`, `PowerPlant.SteadyState`.
- Produces (test helper, used by Task 13):
  - `Fleet.RepoRoot`, `Fleet.Load(string id) : AircraftDefinition`
  - `Fleet.Cruise(string id) : (double Airspeed, double Throttle)` — trainer (15, 0.5), sport (18, 0.5), wing (14, 0.6)
  - `Fleet.InFlight(string id, double altitude, double airspeed, double pitchDeg = 0, double rollDeg = 0) : Simulation` (heading north)
  - `Fleet.OnGround(string id) : Simulation` (heading north, at the origin)
  - `Fleet.Fly(Simulation sim, double seconds, Func<double, ControlInputs> pilot, Action<Simulation>? observe = null)` — `pilot` receives `sim.Time`
  - `Fleet.Roll(Simulation) : double` (rad), `Fleet.WorldYawRightRate(Simulation) : double` (rad/s, + = turning right seen from above)

All numeric values below are **estimates** (tagged in `provenance`); Task 13 may tune them.

- [ ] **Step 1: Write the airfoil files**

`aircraft/airfoils/clarky.json`:
```json
{
  "name": "Clark Y (approx., Re 200k)",
  "provenance": "estimated",
  "tables": [
    {
      "reynolds": 200000,
      "alphaDeg": [-14, -10, -8, -6, -4, -2, 0, 2, 4, 6, 8, 10, 12, 14, 16, 18],
      "cl": [-0.55, -0.60, -0.45, -0.25, -0.05, 0.15, 0.35, 0.55, 0.74, 0.92, 1.08, 1.20, 1.28, 1.25, 1.05, 0.90],
      "cd": [0.100, 0.045, 0.025, 0.016, 0.012, 0.010, 0.010, 0.011, 0.013, 0.016, 0.020, 0.027, 0.038, 0.065, 0.120, 0.170],
      "cm": [-0.05, -0.06, -0.07, -0.075, -0.08, -0.08, -0.08, -0.08, -0.08, -0.08, -0.078, -0.075, -0.07, -0.07, -0.08, -0.09]
    }
  ]
}
```

`aircraft/airfoils/naca0012.json`:
```json
{
  "name": "NACA 0012 (approx., Re 200k)",
  "provenance": "estimated",
  "tables": [
    {
      "reynolds": 200000,
      "alphaDeg": [-18, -15, -13, -11, -9, -7, -5, -3, -1, 0, 1, 3, 5, 7, 9, 11, 13, 15, 18],
      "cl": [-0.70, -0.85, -1.02, -0.98, -0.88, -0.72, -0.53, -0.32, -0.11, 0.0, 0.11, 0.32, 0.53, 0.72, 0.88, 0.98, 1.02, 0.85, 0.70],
      "cd": [0.200, 0.130, 0.055, 0.030, 0.020, 0.015, 0.012, 0.010, 0.009, 0.009, 0.009, 0.010, 0.012, 0.015, 0.020, 0.030, 0.055, 0.130, 0.200],
      "cm": [0.020, 0.015, 0.010, 0.005, 0.002, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, -0.002, -0.005, -0.010, -0.015, -0.020]
    }
  ]
}
```

`aircraft/airfoils/reflex-mh45.json`:
```json
{
  "name": "Reflexed flying-wing section, MH45-like (approx., Re 150k)",
  "provenance": "estimated",
  "tables": [
    {
      "reynolds": 150000,
      "alphaDeg": [-14, -10, -8, -6, -4, -2, 0, 2, 4, 6, 8, 10, 12, 14, 16],
      "cl": [-0.70, -0.80, -0.65, -0.47, -0.28, -0.08, 0.10, 0.30, 0.50, 0.68, 0.84, 0.96, 1.00, 0.88, 0.75],
      "cd": [0.100, 0.040, 0.022, 0.015, 0.012, 0.010, 0.009, 0.010, 0.012, 0.015, 0.020, 0.028, 0.045, 0.090, 0.150],
      "cm": [-0.010, 0.0, 0.005, 0.005, 0.005, 0.005, 0.005, 0.005, 0.005, 0.005, 0.004, 0.002, -0.005, -0.020, -0.040]
    }
  ]
}
```

- [ ] **Step 2: Write the trainer**

`aircraft/trainer/aircraft.json`:
```json
{
  "name": "Trainer 1.5 m",
  "description": "High-wing trainer with generous dihedral and tricycle gear. Very forgiving.",
  "mass": 2.6,
  "inertia": { "roll": 0.14, "yaw": 0.33, "pitch": 0.22 },
  "surfaces": [
    { "name": "wing", "role": "wing", "root": [0.0175, 0.12, 0], "span": 0.75, "rootChord": 0.35, "tipChord": 0.35,
      "dihedralDeg": 3, "incidenceDeg": 1.0, "airfoil": "clarky", "segments": 6, "mirror": true },
    { "name": "stab", "role": "horizontalTail", "root": [-0.85, 0.02, 0], "span": 0.26, "rootChord": 0.22, "tipChord": 0.18,
      "incidenceDeg": 0.0, "airfoil": "naca0012", "segments": 3, "mirror": true },
    { "name": "fin", "role": "verticalTail", "root": [-0.88, 0.03, 0], "span": 0.22, "rootChord": 0.25, "tipChord": 0.14,
      "sweepDeg": 25, "dihedralDeg": 90, "airfoil": "naca0012", "segments": 3, "mirror": false }
  ],
  "controls": [
    { "name": "aileronRight", "surface": "wing", "side": "right", "chordFraction": 0.25, "spanStart": 0.4, "spanEnd": 0.95,
      "maxPositiveDeg": 12, "maxNegativeDeg": 15, "servoSecondsPer60Deg": 0.12, "mix": { "aileron": -1 } },
    { "name": "aileronLeft", "surface": "wing", "side": "left", "chordFraction": 0.25, "spanStart": 0.4, "spanEnd": 0.95,
      "maxPositiveDeg": 12, "maxNegativeDeg": 15, "servoSecondsPer60Deg": 0.12, "mix": { "aileron": 1 } },
    { "name": "elevator", "surface": "stab", "side": "both", "chordFraction": 0.35,
      "maxPositiveDeg": 20, "maxNegativeDeg": 20, "servoSecondsPer60Deg": 0.12, "mix": { "elevator": -1 } },
    { "name": "rudder", "surface": "fin", "side": "both", "chordFraction": 0.4,
      "maxPositiveDeg": 25, "maxNegativeDeg": 25, "servoSecondsPer60Deg": 0.12, "mix": { "rudder": -1 } }
  ],
  "bodies": [ { "name": "fuselage", "position": [-0.15, 0, 0], "cdA": [0.006, 0.04, 0.035] } ],
  "power": "power.json",
  "gear": [
    { "name": "nose", "position": [0.40, -0.21, 0], "stiffness": 1500, "damping": 40, "maxSteerDeg": 20, "steerMix": { "rudder": 1 } },
    { "name": "mainLeft", "position": [-0.06, -0.22, -0.20], "stiffness": 2500, "damping": 60 },
    { "name": "mainRight", "position": [-0.06, -0.22, 0.20], "stiffness": 2500, "damping": 60 }
  ],
  "hull": [
    { "name": "nose", "position": [0.50, 0.0, 0], "tag": "nose" },
    { "name": "wingtipLeft", "position": [0.0175, 0.159, -0.75], "tag": "wingtip" },
    { "name": "wingtipRight", "position": [0.0175, 0.159, 0.75], "tag": "wingtip" },
    { "name": "tail", "position": [-0.95, 0.0, 0], "tag": "tail" },
    { "name": "finTop", "position": [-0.98, 0.25, 0], "tag": "tail" },
    { "name": "belly", "position": [0.0, -0.08, 0], "tag": "belly" },
    { "name": "canopy", "position": [0.20, 0.18, 0], "tag": "canopy" }
  ],
  "crash": { "maxGearSinkRate": 2.5, "maxHullImpactSpeed": 1.5, "maxBellyImpactSpeed": 1.5 },
  "provenance": { "*": "estimated" }
}
```

`aircraft/trainer/power.json`:
```json
{
  "position": [0.45, 0.0, 0.0],
  "thrustAxis": [0.9988, -0.0349, 0.0349],
  "spinDirection": 1,
  "pFactor": 0.1,
  "motor": { "kv": 650, "resistanceOhm": 0.04, "noLoadCurrentA": 1.2, "maxCurrentA": 60, "rotorInertia": 3.0e-4 },
  "battery": { "cells": 4, "capacityAh": 3.0, "internalResistanceOhm": 0.028 },
  "propeller": { "genericDiameterIn": 12, "genericPitchIn": 6 }
}
```

- [ ] **Step 3: Write the sport aerobatic model**

`aircraft/sport/aircraft.json`:
```json
{
  "name": "Sport 1.2 m",
  "description": "Low-wing aerobatic taildragger with a symmetrical airfoil. Crisp roll rate and a clean stall break.",
  "mass": 2.2,
  "inertia": { "roll": 0.08, "yaw": 0.23, "pitch": 0.16 },
  "surfaces": [
    { "name": "wing", "role": "wing", "root": [0.014, -0.04, 0], "span": 0.6, "rootChord": 0.32, "tipChord": 0.24,
      "dihedralDeg": 1, "incidenceDeg": 0.5, "airfoil": "naca0012", "segments": 6, "mirror": true },
    { "name": "stab", "role": "horizontalTail", "root": [-0.72, 0.03, 0], "span": 0.22, "rootChord": 0.19, "tipChord": 0.15,
      "incidenceDeg": -1.0, "airfoil": "naca0012", "segments": 3, "mirror": true },
    { "name": "fin", "role": "verticalTail", "root": [-0.74, 0.04, 0], "span": 0.20, "rootChord": 0.22, "tipChord": 0.12,
      "sweepDeg": 20, "dihedralDeg": 90, "airfoil": "naca0012", "segments": 3, "mirror": false }
  ],
  "controls": [
    { "name": "aileronRight", "surface": "wing", "side": "right", "chordFraction": 0.28, "spanStart": 0.15, "spanEnd": 0.95,
      "maxPositiveDeg": 25, "maxNegativeDeg": 25, "servoSecondsPer60Deg": 0.08, "mix": { "aileron": -1 } },
    { "name": "aileronLeft", "surface": "wing", "side": "left", "chordFraction": 0.28, "spanStart": 0.15, "spanEnd": 0.95,
      "maxPositiveDeg": 25, "maxNegativeDeg": 25, "servoSecondsPer60Deg": 0.08, "mix": { "aileron": 1 } },
    { "name": "elevator", "surface": "stab", "side": "both", "chordFraction": 0.45,
      "maxPositiveDeg": 25, "maxNegativeDeg": 25, "servoSecondsPer60Deg": 0.08, "mix": { "elevator": -1 } },
    { "name": "rudder", "surface": "fin", "side": "both", "chordFraction": 0.5,
      "maxPositiveDeg": 30, "maxNegativeDeg": 30, "servoSecondsPer60Deg": 0.08, "mix": { "rudder": -1 } }
  ],
  "bodies": [ { "name": "fuselage", "position": [-0.10, 0, 0], "cdA": [0.005, 0.035, 0.03] } ],
  "power": "power.json",
  "gear": [
    { "name": "mainLeft", "position": [0.10, -0.20, -0.17], "stiffness": 2500, "damping": 55 },
    { "name": "mainRight", "position": [0.10, -0.20, 0.17], "stiffness": 2500, "damping": 55 },
    { "name": "tailwheel", "position": [-0.75, -0.07, 0], "stiffness": 800, "damping": 12,
      "rollingFriction": 0.05, "lateralFriction": 0.6, "maxSteerDeg": 25, "steerMix": { "rudder": 1 } }
  ],
  "hull": [
    { "name": "nose", "position": [0.50, 0.0, 0], "tag": "nose" },
    { "name": "wingtipLeft", "position": [0.014, -0.03, -0.60], "tag": "wingtip" },
    { "name": "wingtipRight", "position": [0.014, -0.03, 0.60], "tag": "wingtip" },
    { "name": "tail", "position": [-0.80, 0.02, 0], "tag": "tail" },
    { "name": "finTop", "position": [-0.82, 0.22, 0], "tag": "tail" },
    { "name": "canopy", "position": [0.10, 0.12, 0], "tag": "canopy" },
    { "name": "belly", "position": [0.0, -0.08, 0], "tag": "belly" }
  ],
  "crash": { "maxGearSinkRate": 2.5, "maxHullImpactSpeed": 1.5, "maxBellyImpactSpeed": 1.5 },
  "provenance": { "*": "estimated" }
}
```

`aircraft/sport/power.json`:
```json
{
  "position": [0.45, 0.0, 0.0],
  "thrustAxis": [0.9994, 0.0, 0.0349],
  "spinDirection": 1,
  "pFactor": 0.1,
  "motor": { "kv": 750, "resistanceOhm": 0.035, "noLoadCurrentA": 1.3, "maxCurrentA": 65, "rotorInertia": 2.5e-4 },
  "battery": { "cells": 4, "capacityAh": 2.6, "internalResistanceOhm": 0.03 },
  "propeller": { "genericDiameterIn": 12, "genericPitchIn": 6 }
}
```

- [ ] **Step 4: Write the FPV flying wing**

`aircraft/wing/aircraft.json`:
```json
{
  "name": "FPV Wing 1.1 m",
  "description": "Swept foam flying wing with elevons and a pusher prop. Hand launch, belly landing.",
  "mass": 1.1,
  "inertia": { "roll": 0.05, "yaw": 0.08, "pitch": 0.03 },
  "surfaces": [
    { "name": "wing", "role": "wing", "root": [0.133, 0.0, 0], "span": 0.55, "rootChord": 0.30, "tipChord": 0.15,
      "sweepDeg": 25, "incidenceDeg": 2.0, "twistDeg": -2.0, "airfoil": "reflex-mh45", "segments": 8, "mirror": true },
    { "name": "winglets", "role": "verticalTail", "root": [-0.13, 0.0, 0.55], "span": 0.12, "rootChord": 0.12, "tipChord": 0.08,
      "sweepDeg": 30, "dihedralDeg": 90, "airfoil": "naca0012", "segments": 2, "mirror": true }
  ],
  "controls": [
    { "name": "elevonRight", "surface": "wing", "side": "right", "chordFraction": 0.22, "spanStart": 0.15, "spanEnd": 0.95,
      "maxPositiveDeg": 20, "maxNegativeDeg": 20, "servoSecondsPer60Deg": 0.1, "mix": { "aileron": -1, "elevator": -1 } },
    { "name": "elevonLeft", "surface": "wing", "side": "left", "chordFraction": 0.22, "spanStart": 0.15, "spanEnd": 0.95,
      "maxPositiveDeg": 20, "maxNegativeDeg": 20, "servoSecondsPer60Deg": 0.1, "mix": { "aileron": 1, "elevator": -1 } }
  ],
  "bodies": [ { "name": "pod", "position": [0.05, 0.0, 0], "cdA": [0.004, 0.02, 0.012] } ],
  "power": "power.json",
  "gear": [],
  "hull": [
    { "name": "nose", "position": [0.32, 0.0, 0], "tag": "nose" },
    { "name": "belly", "position": [0.08, -0.04, 0], "tag": "belly" },
    { "name": "bellyAft", "position": [-0.12, -0.03, 0], "tag": "belly" },
    { "name": "wingtipLeft", "position": [-0.17, 0.0, -0.55], "tag": "wingtip" },
    { "name": "wingtipRight", "position": [-0.17, 0.0, 0.55], "tag": "wingtip" },
    { "name": "finTopLeft", "position": [-0.16, 0.12, -0.55], "tag": "wingtip" },
    { "name": "finTopRight", "position": [-0.16, 0.12, 0.55], "tag": "wingtip" },
    { "name": "top", "position": [0.05, 0.06, 0], "tag": "canopy" }
  ],
  "crash": { "maxGearSinkRate": 2.0, "maxHullImpactSpeed": 1.5, "maxBellyImpactSpeed": 3.0 },
  "provenance": { "*": "estimated" }
}
```

`aircraft/wing/power.json`:
```json
{
  "position": [-0.22, 0.0, 0.0],
  "thrustAxis": [1, 0, 0],
  "spinDirection": 1,
  "pFactor": 0.1,
  "motor": { "kv": 1400, "resistanceOhm": 0.09, "noLoadCurrentA": 0.6, "maxCurrentA": 30, "rotorInertia": 3.0e-5 },
  "battery": { "cells": 3, "capacityAh": 2.2, "internalResistanceOhm": 0.03 },
  "propeller": { "genericDiameterIn": 7, "genericPitchIn": 4 }
}
```

- [ ] **Step 5: Write the test helper and the failing definition tests**

`tests/Symlab.Flight.Tests/Behavior/Fleet.cs`:
```csharp
using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Tests.Behavior;

internal static class Fleet
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

    public static AircraftDefinition Load(string id) => AircraftLoader.Load(Path.Combine(RepoRoot, "aircraft", id));

    public static (double Airspeed, double Throttle) Cruise(string id) => id switch
    {
        "trainer" => (15, 0.5),
        "sport" => (18, 0.5),
        "wing" => (14, 0.6),
        _ => throw new ArgumentException(id),
    };

    public static Simulation InFlight(string id, double altitude, double airspeed, double pitchDeg = 0, double rollDeg = 0)
    {
        var sim = new Simulation(new Aircraft(Load(id)), FlightEnvironment.Calm());
        sim.Reset(InitialConditions.InFlight(new Vec3(0, altitude, 0), 0, airspeed, pitchDeg, rollDeg));
        return sim;
    }

    public static Simulation OnGround(string id)
    {
        var def = Load(id);
        var sim = new Simulation(new Aircraft(def), FlightEnvironment.Calm());
        sim.Reset(InitialConditions.OnGround(def, sim.Environment.Terrain, 0, 0, 0));
        return sim;
    }

    public static void Fly(Simulation sim, double seconds, Func<double, ControlInputs> pilot, Action<Simulation>? observe = null)
    {
        int steps = (int)Math.Round(seconds / Simulation.FixedStep);
        for (int i = 0; i < steps; i++)
        {
            sim.StepOnce(pilot(sim.Time));
            observe?.Invoke(sim);
        }
    }

    public static double Roll(Simulation sim) => Attitude.FromOrientation(sim.Aircraft.State.Orientation).Roll;

    public static double WorldYawRightRate(Simulation sim)
    {
        var s = sim.Aircraft.State;
        return -s.Orientation.Rotate(s.AngularVelocity).Y;
    }
}
```

`tests/Symlab.Flight.Tests/Behavior/FleetDefinitionTests.cs`:
```csharp
using Symlab.Flight.Airframe;
using Symlab.Flight.Atmosphere;
using Symlab.Flight.Propulsion;

namespace Symlab.Flight.Tests.Behavior;

public class FleetDefinitionTests
{
    [Theory]
    [InlineData("trainer", 2.6, 0.525)]
    [InlineData("sport", 2.2, 0.336)]
    [InlineData("wing", 1.1, 0.2475)]
    public void Definition_loads_with_expected_mass_and_wing_area(string id, double mass, double wingArea)
    {
        var def = Fleet.Load(id);
        Assert.Equal(mass, def.Mass.Mass);
        Assert.Equal(wingArea, new Aircraft(def).Aero.WingArea, 3);
    }

    [Theory]
    [InlineData("trainer", 0.7)]
    [InlineData("sport", 1.0)]
    [InlineData("wing", 0.5)]
    public void Static_thrust_to_weight_matches_the_aircraft_type(string id, double minimum)
    {
        var def = Fleet.Load(id);
        var full = new PowerPlant(def.Power!).SteadyState(1.0, 0, Isa.SeaLevelDensity);
        double ratio = full.Thrust / (def.Mass.Mass * Aircraft.Gravity);
        Assert.True(ratio > minimum, $"{id}: thrust/weight {ratio:F2}");
    }

    [Fact]
    public void Trainer_half_throttle_static_endurance_is_plausible()
    {
        var def = Fleet.Load("trainer");
        var half = new PowerPlant(def.Power!).SteadyState(0.5, 0, Isa.SeaLevelDensity);
        double batteryCurrent = half.Current * def.Power!.Esc.Map(0.5);
        double minutes = def.Power.Battery.CapacityAh / batteryCurrent * 60;
        Assert.InRange(minutes, 10, 45);
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~FleetDefinitionTests`
Expected: all PASS. (The data files are written before the tests; a failure here means a data typo or a loader bug. Fix the data, not the thresholds.)

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(aircraft): add airfoil polars and trainer, sport and flying-wing definitions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Behavior tests and aircraft tuning

**Files:**
- Test: `tests/Symlab.Flight.Tests/Behavior/GroundHandlingTests.cs`, `ControlResponseTests.cs`, `FlightQualityTests.cs`, `StallSpinTests.cs`, `LandingAndCrashTests.cs`
- Modify (tuning only, see rules): `aircraft/*/aircraft.json`, `aircraft/*/power.json`
- Create: `docs/tuning-log.md`

**Interfaces:**
- Consumes: `Fleet` (Task 12), `Simulation`, `Aircraft.OverrideState`, `Attitude`.

**Tuning rules** (read before touching any number):
1. A failing behavior test is first treated as a possible **physics bug**. Reproduce it with a focused unit test in the relevant module (sign, units, frame); fix the code if the unit test shows a bug.
2. Only if the physics is correct, tune **aircraft data**, using only these knobs:

| Symptom | Knob | Allowed range |
|---|---|---|
| Hands-off climbs or dives | stab `incidenceDeg` (trainer/sport); wing `twistDeg` / `incidenceDeg` (wing) | ±2° |
| Pitch too twitchy or divergent, phugoid out of range | wing `root` x (moves the aero center relative to the CG) | ±0.03 m |
| Dutch roll too weakly damped | fin `span` / `rootChord` | ±25% |
| Spiral diverges too fast | wing `dihedralDeg` | ±2° |
| Spin will not develop or will not stop | control throws `maxPositiveDeg` / `maxNegativeDeg` | ±5° |
| Ground bounce, drift or nose-over | wheel `stiffness`, `damping`, `position` | ±30%, ±0.03 m |

3. **Never** loosen a test threshold to make it pass. If a threshold is wrong, raise it with the user.
4. Log every change in `docs/tuning-log.md`: date, file, parameter, old → new value, failing test, reason.

- [ ] **Step 1: Write the behavior tests**

`tests/Symlab.Flight.Tests/Behavior/GroundHandlingTests.cs`:
```csharp
using Symlab.Flight.Controls;
using Symlab.Flight.Ground;

namespace Symlab.Flight.Tests.Behavior;

public class GroundHandlingTests
{
    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    public void Rests_on_its_gear_without_drifting(string id)
    {
        var sim = Fleet.OnGround(id);
        Fleet.Fly(sim, 5, _ => ControlInputs.Neutral);
        var settled = sim.Aircraft.State;
        Fleet.Fly(sim, 15, _ => ControlInputs.Neutral);
        var s = sim.Aircraft.State;
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True((s.Position - settled.Position).Length < 0.05, $"drift {(s.Position - settled.Position).Length:F3} m");
        Assert.True(s.AngularVelocity.Length < 0.02);
        Assert.Equal(sim.Aircraft.Definition.Wheels.Count, sim.Aircraft.Ground.WheelsInContact(s, sim.Environment.Terrain));
    }

    [Fact]
    public void Trainer_takes_off_within_a_normal_ground_roll()
    {
        var sim = Fleet.OnGround("trainer");
        Fleet.Fly(sim, 1, _ => ControlInputs.Neutral);
        double startNorth = -sim.Aircraft.State.Position.Z;
        double? liftOff = null;
        Fleet.Fly(sim, 14, t => new ControlInputs(1, 0, t > 4 ? 0.35 : 0, 0), s =>
        {
            if (liftOff is null && s.Aircraft.Ground.WheelsInContact(s.Aircraft.State, s.Environment.Terrain) == 0)
                liftOff = -s.Aircraft.State.Position.Z - startNorth;
        });
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.NotNull(liftOff);
        Assert.InRange(liftOff!.Value, 5, 80);
        Assert.True(sim.Aircraft.State.Position.Y > 10, $"altitude {sim.Aircraft.State.Position.Y:F1} m");
    }
}
```

`tests/Symlab.Flight.Tests/Behavior/ControlResponseTests.cs`:
```csharp
using Symlab.Flight.Controls;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Tests.Behavior;

public class ControlResponseTests
{
    static void AssertResponse(string id, Func<ControlInputs, ControlInputs> apply, Func<Simulation, double> measure, double minimum)
    {
        var (speed, throttle) = Fleet.Cruise(id);
        double Run(bool withInput)
        {
            var sim = Fleet.InFlight(id, 80, speed);
            var trim = new ControlInputs(throttle, 0, 0, 0);
            Fleet.Fly(sim, 1.0, _ => trim);
            Fleet.Fly(sim, 0.4, _ => withInput ? apply(trim) : trim);
            return measure(sim);
        }
        double response = Run(true) - Run(false);
        Assert.True(response > minimum, $"{id}: response {response:F3} (minimum {minimum})");
    }

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    public void Right_aileron_rolls_right(string id) =>
        AssertResponse(id, u => u with { Aileron = 0.5 }, s => s.Aircraft.State.AngularVelocity.X, 0.3);

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    public void Up_elevator_pitches_up(string id) =>
        AssertResponse(id, u => u with { Elevator = 0.5 }, s => s.Aircraft.State.AngularVelocity.Z, 0.2);

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    public void Right_rudder_yaws_right(string id) =>
        AssertResponse(id, u => u with { Rudder = 0.5 }, s => -s.Aircraft.State.AngularVelocity.Y, 0.1);
}
```

`tests/Symlab.Flight.Tests/Behavior/FlightQualityTests.cs`:
```csharp
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Ground;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Tests.Behavior;

public class FlightQualityTests
{
    static Simulation Trimmed(string id)
    {
        var (speed, throttle) = Fleet.Cruise(id);
        var sim = Fleet.InFlight(id, 150, speed);
        Fleet.Fly(sim, 20, _ => new ControlInputs(throttle, 0, 0, 0));
        return sim;
    }

    static ControlInputs Cruise(string id) => new(Fleet.Cruise(id).Throttle, 0, 0, 0);

    [Fact]
    public void Trainer_flies_hands_off_for_thirty_seconds()
    {
        var sim = Fleet.InFlight("trainer", 100, Fleet.Cruise("trainer").Airspeed);
        double minAltitude = double.MaxValue, maxAltitude = double.MinValue, maxBank = 0;
        Fleet.Fly(sim, 30, _ => Cruise("trainer"), s =>
        {
            minAltitude = Math.Min(minAltitude, s.Aircraft.State.Position.Y);
            maxAltitude = Math.Max(maxAltitude, s.Aircraft.State.Position.Y);
            maxBank = Math.Max(maxBank, Math.Abs(Fleet.Roll(s)));
        });
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.InRange(minAltitude, 40, 250);
        Assert.InRange(maxAltitude, 40, 250);
        Assert.InRange(sim.Aircraft.AirData.Airspeed, 8, 30);
        Assert.True(maxBank < Angle.Rad(45), $"max bank {Angle.Deg(maxBank):F0} deg");
    }

    [Theory]
    [InlineData("sport")]
    [InlineData("wing")]
    public void Flies_hands_off_for_ten_seconds(string id)
    {
        var sim = Fleet.InFlight(id, 100, Fleet.Cruise(id).Airspeed);
        Fleet.Fly(sim, 10, _ => Cruise(id));
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.InRange(sim.Aircraft.AirData.Airspeed, 8, 35);
    }

    [Fact]
    public void Trainer_phugoid_period_is_plausible()
    {
        var sim = Trimmed("trainer");
        var s0 = sim.Aircraft.State;
        sim.Aircraft.OverrideState(s0 with { Velocity = s0.Velocity * 1.2 });

        var samples = new List<(double T, double V)>();
        int step = 0;
        Fleet.Fly(sim, 60, _ => Cruise("trainer"), s =>
        {
            if (step++ % 50 == 0) samples.Add((s.Time, s.Aircraft.AirData.Airspeed));
        });

        double mean = samples.Average(p => p.V);
        var crossings = new List<double>();
        bool armed = false;
        foreach (var (t, v) in samples)
        {
            if (v < mean - 0.1) armed = true;
            else if (armed && v > mean + 0.1) { crossings.Add(t); armed = false; }
        }
        Assert.True(crossings.Count >= 2, $"only {crossings.Count} upward crossings");
        double period = (crossings[^1] - crossings[0]) / (crossings.Count - 1);
        Assert.InRange(period, 3, 15);
    }

    [Fact]
    public void Trainer_short_period_damps_quickly()
    {
        var sim = Trimmed("trainer");
        double start = sim.Time;
        Fleet.Fly(sim, 3.3, t => t - start < 0.3 ? Cruise("trainer") with { Elevator = 0.5 } : Cruise("trainer"));
        Assert.True(Math.Abs(sim.Aircraft.State.AngularVelocity.Z) < 0.1, $"pitch rate {sim.Aircraft.State.AngularVelocity.Z:F3}");
    }

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    public void Dutch_roll_damps_after_a_rudder_pulse(string id)
    {
        var sim = Trimmed(id);
        double start = sim.Time;
        Fleet.Fly(sim, 6.3, t => t - start < 0.3 ? Cruise(id) with { Rudder = 0.5 } : Cruise(id));
        Assert.True(Math.Abs(sim.Aircraft.State.AngularVelocity.Y) < 0.1, $"yaw rate {sim.Aircraft.State.AngularVelocity.Y:F3}");
    }

    [Fact]
    public void Trainer_spiral_mode_is_not_rapidly_divergent()
    {
        var sim = Trimmed("trainer");
        var s0 = sim.Aircraft.State;
        var banked = s0.Orientation * Quat.FromAxisAngle(Vec3.UnitX, Angle.Rad(20));
        sim.Aircraft.OverrideState(s0 with { Orientation = banked });
        Fleet.Fly(sim, 10, _ => Cruise("trainer"));
        Assert.True(Math.Abs(Fleet.Roll(sim)) < Angle.Rad(60), $"bank {Angle.Deg(Fleet.Roll(sim)):F0} deg");
    }
}
```

`tests/Symlab.Flight.Tests/Behavior/StallSpinTests.cs`:
```csharp
using Symlab.Flight.Airframe;
using Symlab.Flight.Controls;
using Symlab.Flight.Ground;

namespace Symlab.Flight.Tests.Behavior;

public class StallSpinTests
{
    [Fact]
    public void Trainer_minimum_speed_matches_its_wing_loading()
    {
        var sim = Fleet.InFlight("trainer", 150, 16);
        double minSpeed = double.MaxValue;
        Fleet.Fly(sim, 15, t => new ControlInputs(0, 0, Math.Min(1, t / 15), 0),
            s => minSpeed = Math.Min(minSpeed, s.Aircraft.AirData.Airspeed));

        var def = sim.Aircraft.Definition;
        double clMax3d = 0.9 * 1.28;
        double stallSpeed = Math.Sqrt(2 * def.Mass.Mass * Aircraft.Gravity / (1.225 * sim.Aircraft.Aero.WingArea * clMax3d));
        Assert.InRange(minSpeed, 0.7 * stallSpeed, 1.35 * stallSpeed);
    }

    [Fact]
    public void Sport_spins_with_pro_spin_controls_and_recovers_when_released()
    {
        var sim = Fleet.InFlight("sport", 250, 14);
        Fleet.Fly(sim, 1, t => new ControlInputs(0, 0, Math.Min(1, t), 0));
        var rates = new List<double>();
        double spinStart = sim.Time;
        Fleet.Fly(sim, 6, _ => new ControlInputs(0, 0, 1, 1), s =>
        {
            if (s.Time - spinStart > 4) rates.Add(Fleet.WorldYawRightRate(s));
        });
        Assert.True(rates.Average() > 1.5, $"spin rate {rates.Average():F2} rad/s");

        Fleet.Fly(sim, 6, _ => ControlInputs.Neutral);
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(Math.Abs(Fleet.WorldYawRightRate(sim)) < 0.8, $"residual yaw rate {Fleet.WorldYawRightRate(sim):F2}");
    }

    [Fact]
    public void Trainer_recovers_hands_off_from_an_incipient_spin()
    {
        var sim = Fleet.InFlight("trainer", 250, 14);
        Fleet.Fly(sim, 1, t => new ControlInputs(0, 0, Math.Min(1, t), 0));
        Fleet.Fly(sim, 4, _ => new ControlInputs(0, 0, 1, 1));
        Fleet.Fly(sim, 5, _ => ControlInputs.Neutral);
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(Math.Abs(Fleet.WorldYawRightRate(sim)) < 0.5, $"residual yaw rate {Fleet.WorldYawRightRate(sim):F2}");
    }
}
```

`tests/Symlab.Flight.Tests/Behavior/LandingAndCrashTests.cs`:
```csharp
using Symlab.Flight.Controls;
using Symlab.Flight.Ground;

namespace Symlab.Flight.Tests.Behavior;

public class LandingAndCrashTests
{
    [Fact]
    public void Steep_dive_into_the_ground_is_a_crash()
    {
        var sim = Fleet.InFlight("trainer", 5, 20, pitchDeg: -60);
        Fleet.Fly(sim, 2, _ => ControlInputs.Neutral);
        Assert.NotEqual(CrashCause.None, sim.Aircraft.Crash);
    }

    [Fact]
    public void Wing_belly_lands_and_slides_to_a_stop()
    {
        var sim = Fleet.InFlight("wing", 1.5, 11);
        Fleet.Fly(sim, 10, _ => new ControlInputs(0, 0, 0.2, 0));
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(sim.Aircraft.State.Velocity.Length < 1.0, $"speed {sim.Aircraft.State.Velocity.Length:F2}");
        Assert.True(sim.Aircraft.State.Position.Y < 0.2);
    }

    [Fact]
    public void Wing_hand_launch_climbs_away()
    {
        var sim = Fleet.InFlight("wing", 1.8, 10, pitchDeg: 10);
        Fleet.Fly(sim, 6, _ => new ControlInputs(1, 0, 0.1, 0));
        Assert.Equal(CrashCause.None, sim.Aircraft.Crash);
        Assert.True(sim.Aircraft.State.Position.Y > 5, $"altitude {sim.Aircraft.State.Position.Y:F1} m");
    }
}
```

- [ ] **Step 2: Run the behavior suite**

Run: `dotnet test --filter FullyQualifiedName~Behavior`
Expected: some tests may FAIL on the first run because the aircraft data are estimates.

- [ ] **Step 3: Diagnose and tune (loop until green)**

For each failing test, apply the tuning rules above, in order. Useful diagnostics: attach a temporary `FlightRecorder` (Task 14) to the simulation, or print `Attitude`, `AirData` and `Aero.Downwash` every 0.5 s. Remove temporary diagnostics before committing.

Create `docs/tuning-log.md` with this header, then one row per change:
```markdown
# Aircraft tuning log

All values in `aircraft/` are estimates until measured. Every change made to pass a behavior test is logged here.

| Date | File | Parameter | Old → New | Failing test | Reason |
|------|------|-----------|-----------|--------------|--------|
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test(behavior): add flight-quality tests and tune aircraft data

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Flight recorder (CSV)

**Files:**
- Modify: `src/Symlab.Flight/Recording/FlightRecorder.cs` (replace the stub from Task 11)
- Test: `tests/Symlab.Flight.Tests/Recording/FlightRecorderTests.cs`

**Interfaces:**
- Consumes: `Simulation`, `Aircraft`, `ControlInputs`, `PowerTelemetry`.
- Produces: `FlightRecorder(TextWriter writer, int decimation = 5)` implementing `IDisposable`; `FlightRecorder.Header`; `OnStep(Simulation, in ControlInputs)`. Columns: `t,throttle,aileron,elevator,rudder,x,y,z,vx,vy,vz,qx,qy,qz,qw,wx,wy,wz,airspeed,alpha_deg,beta_deg,rpm,thrust_n,battery_v,battery_a,soc,crash`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Flight.Tests/Recording/FlightRecorderTests.cs`:
```csharp
using System.Globalization;
using Symlab.Flight.Controls;
using Symlab.Flight.Recording;
using Symlab.Flight.Tests.Behavior;

namespace Symlab.Flight.Tests.Recording;

public class FlightRecorderTests
{
    [Fact]
    public void Writes_a_header_and_one_row_every_n_steps()
    {
        var writer = new StringWriter();
        var sim = Fleet.InFlight("trainer", 50, 15);
        sim.Recorder = new FlightRecorder(writer, decimation: 5);
        Fleet.Fly(sim, 0.02, _ => new ControlInputs(0.5, 0.1, 0, 0));

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(FlightRecorder.Header, lines[0].TrimEnd('\r'));
        Assert.Equal(3, lines.Length);

        var first = lines[1].TrimEnd('\r').Split(',');
        Assert.Equal(FlightRecorder.Header.Split(',').Length, first.Length);
        Assert.Equal(0.002, double.Parse(first[0], CultureInfo.InvariantCulture), 9);
        Assert.Equal(0.1, double.Parse(first[2], CultureInfo.InvariantCulture), 9);
        Assert.Equal("None", first[^1]);
    }

    [Fact]
    public void Rejects_zero_decimation()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new FlightRecorder(new StringWriter(), 0));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~FlightRecorderTests`
Expected: build FAILS (no such constructor).

- [ ] **Step 3: Implement**

`src/Symlab.Flight/Recording/FlightRecorder.cs`:
```csharp
using System.Globalization;
using Symlab.Flight.Controls;
using Symlab.Flight.Geometry;
using Symlab.Flight.Sim;

namespace Symlab.Flight.Recording;

/// <summary>Writes one CSV row every <c>decimation</c> physics steps (default 100 Hz).</summary>
public sealed class FlightRecorder : IDisposable
{
    public const string Header =
        "t,throttle,aileron,elevator,rudder,x,y,z,vx,vy,vz,qx,qy,qz,qw,wx,wy,wz," +
        "airspeed,alpha_deg,beta_deg,rpm,thrust_n,battery_v,battery_a,soc,crash";

    readonly TextWriter _writer;
    readonly int _decimation;
    long _count;

    public FlightRecorder(TextWriter writer, int decimation = 5)
    {
        if (decimation < 1) throw new ArgumentOutOfRangeException(nameof(decimation));
        _writer = writer;
        _decimation = decimation;
        _writer.WriteLine(Header);
    }

    public void OnStep(Simulation simulation, in ControlInputs input)
    {
        if (_count++ % _decimation != 0) return;
        var a = simulation.Aircraft;
        var s = a.State;
        var p = a.Power?.Telemetry ?? default;
        double[] values =
        [
            simulation.Time, input.Throttle, input.Aileron, input.Elevator, input.Rudder,
            s.Position.X, s.Position.Y, s.Position.Z, s.Velocity.X, s.Velocity.Y, s.Velocity.Z,
            s.Orientation.X, s.Orientation.Y, s.Orientation.Z, s.Orientation.W,
            s.AngularVelocity.X, s.AngularVelocity.Y, s.AngularVelocity.Z,
            a.AirData.Airspeed, Angle.Deg(a.AirData.Alpha), Angle.Deg(a.AirData.Beta),
            p.Rpm, p.Thrust, p.BatteryVoltage, p.BatteryCurrent, p.StateOfCharge,
        ];
        _writer.Write(string.Join(',', values.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))));
        _writer.Write(',');
        _writer.WriteLine(a.Crash.ToString());
    }

    public void Dispose() => _writer.Dispose();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(recording): add CSV flight recorder

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Input library scaffold and channel pipeline

**Files:**
- Create: `src/Symlab.Input/Symlab.Input.csproj`, `RawInputFrame.cs`, `AxisCalibration.cs`, `ChannelPipeline.cs`, `RadioProfile.cs`
- Create: `tests/Symlab.Input.Tests/Symlab.Input.Tests.csproj`
- Test: `tests/Symlab.Input.Tests/ChannelPipelineTests.cs`

**Interfaces:**
- Produces:
  - `RawInputFrame(double[] Axes, bool[] Buttons)` — raw values as delivered by Godot/SDL, axes in −1..1.
  - `AxisCalibration(double Min, double Center, double Max)` with `Normalize(double raw)` (piecewise linear, clamped to −1..1) and `Identity`.
  - `enum StickFunction { Throttle, Aileron, Elevator, Rudder }`.
  - `ChannelSettings(int AxisIndex, bool Reversed, AxisCalibration Calibration, double Trim = 0, double Expo = 0, double Rate = 1)`.
  - `ChannelPipeline.Process(double raw, ChannelSettings)` — calibrate → reverse → trim → expo → rate, clamped; `ChannelPipeline.ApplyExpo(double x, double expo)` (EdgeTX-style cubic, expo 0..1).
  - `StickState(double Throttle, double Aileron, double Elevator, double Rudder)` — throttle 0..1, others −1..1; `StickState.Idle`.
  - `RadioProfile` with `DeviceGuid`, `DeviceName`, `Channels` (`Dictionary<StickFunction, ChannelSettings>`), `Switches` (`List<SwitchBinding>`, Task 17), `Read(RawInputFrame) : StickState`, `ToJson()`, `static FromJson(string)`.

- [ ] **Step 1: Create the projects**

`src/Symlab.Input/Symlab.Input.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Symlab.Input</RootNamespace>
  </PropertyGroup>
</Project>
```

`tests/Symlab.Input.Tests/Symlab.Input.Tests.csproj`:
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
    <ProjectReference Include="../../src/Symlab.Input/Symlab.Input.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet add tests/Symlab.Input.Tests package Microsoft.NET.Test.Sdk
dotnet add tests/Symlab.Input.Tests package xunit
dotnet add tests/Symlab.Input.Tests package xunit.runner.visualstudio
dotnet sln add src/Symlab.Input/Symlab.Input.csproj tests/Symlab.Input.Tests/Symlab.Input.Tests.csproj
```

- [ ] **Step 2: Write the failing tests**

`tests/Symlab.Input.Tests/ChannelPipelineTests.cs`:
```csharp
namespace Symlab.Input.Tests;

public class ChannelPipelineTests
{
    static readonly AxisCalibration Asymmetric = new(-0.6, 0.0, 0.8);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.8, 1.0)]
    [InlineData(-0.6, -1.0)]
    [InlineData(0.4, 0.5)]
    [InlineData(-0.3, -0.5)]
    [InlineData(2.0, 1.0)]
    public void Calibration_normalizes_each_half_separately(double raw, double expected)
        => Assert.Equal(expected, Asymmetric.Normalize(raw), 12);

    [Fact]
    public void Reversed_channel_flips_the_sign()
        => Assert.Equal(-0.5, ChannelPipeline.Process(0.4, new ChannelSettings(0, true, Asymmetric)), 12);

    [Fact]
    public void Trim_offsets_and_output_is_clamped()
    {
        var s = new ChannelSettings(0, false, AxisCalibration.Identity, Trim: 0.1);
        Assert.Equal(0.1, ChannelPipeline.Process(0, s), 12);
        Assert.Equal(1.0, ChannelPipeline.Process(1, s), 12);
    }

    [Fact]
    public void Expo_keeps_endpoints_and_softens_the_center()
    {
        Assert.Equal(1.0, ChannelPipeline.ApplyExpo(1, 0.5), 12);
        Assert.Equal(-1.0, ChannelPipeline.ApplyExpo(-1, 0.5), 12);
        Assert.Equal(0.104, ChannelPipeline.ApplyExpo(0.2, 0.5), 12);
    }

    [Fact]
    public void Rate_scales_the_output()
        => Assert.Equal(0.6, ChannelPipeline.Process(1, new ChannelSettings(0, false, AxisCalibration.Identity, Rate: 0.6)), 12);

    [Fact]
    public void Profile_maps_throttle_to_zero_one_and_missing_channels_to_neutral()
    {
        var profile = new RadioProfile
        {
            Channels =
            {
                [StickFunction.Throttle] = new ChannelSettings(2, false, AxisCalibration.Identity),
                [StickFunction.Aileron] = new ChannelSettings(0, false, AxisCalibration.Identity),
            },
        };
        var sticks = profile.Read(new RawInputFrame([0.5, 0.9, 0.0], []));
        Assert.Equal(0.5, sticks.Throttle, 12);
        Assert.Equal(0.5, sticks.Aileron, 12);
        Assert.Equal(0.0, sticks.Elevator);
        Assert.Equal(0.0, sticks.Rudder);
    }

    [Fact]
    public void Missing_throttle_reads_as_idle()
        => Assert.Equal(0.0, new RadioProfile().Read(new RawInputFrame([], [])).Throttle);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.Input.Tests`
Expected: build FAILS.

- [ ] **Step 4: Implement**

`src/Symlab.Input/RawInputFrame.cs`:
```csharp
namespace Symlab.Input;

/// <summary>One poll of a joystick device: axes in −1..1 and button states, as reported by the host (Godot/SDL).</summary>
public readonly record struct RawInputFrame(double[] Axes, bool[] Buttons);
```

`src/Symlab.Input/AxisCalibration.cs`:
```csharp
namespace Symlab.Input;

/// <summary>Raw axis limits captured by the calibration wizard. Each half is scaled separately.</summary>
public sealed record AxisCalibration(double Min, double Center, double Max)
{
    public static readonly AxisCalibration Identity = new(-1, 0, 1);

    public double Normalize(double raw)
    {
        if (raw >= Center)
        {
            double span = Max - Center;
            return span <= 1e-9 ? 0 : Math.Clamp((raw - Center) / span, 0, 1);
        }
        double spanLow = Center - Min;
        return spanLow <= 1e-9 ? 0 : Math.Clamp((raw - Center) / spanLow, -1, 0);
    }
}
```

`src/Symlab.Input/ChannelPipeline.cs`:
```csharp
namespace Symlab.Input;

public enum StickFunction { Throttle, Aileron, Elevator, Rudder }

/// <param name="Expo">0 = linear, 1 = fully cubic (EdgeTX-style).</param>
/// <param name="Rate">Output scale (dual rate), 0..1.</param>
public sealed record ChannelSettings(
    int AxisIndex,
    bool Reversed,
    AxisCalibration Calibration,
    double Trim = 0,
    double Expo = 0,
    double Rate = 1);

/// <summary>Throttle 0..1; aileron, elevator, rudder −1..1 (right, pitch up, right positive).</summary>
public readonly record struct StickState(double Throttle, double Aileron, double Elevator, double Rudder)
{
    public static readonly StickState Idle = new(0, 0, 0, 0);
}

public static class ChannelPipeline
{
    public static double Process(double raw, ChannelSettings s)
    {
        double v = s.Calibration.Normalize(raw);
        if (s.Reversed) v = -v;
        v = Math.Clamp(v + s.Trim, -1, 1);
        v = ApplyExpo(v, s.Expo);
        return Math.Clamp(v * s.Rate, -1, 1);
    }

    public static double ApplyExpo(double x, double expo)
    {
        double k = Math.Clamp(expo, 0, 1);
        return k * x * x * x + (1 - k) * x;
    }
}
```

`src/Symlab.Input/RadioProfile.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Symlab.Input;

/// <summary>Calibration and channel assignment for one physical radio, persisted as JSON per device GUID.</summary>
public sealed class RadioProfile
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string DeviceGuid { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public Dictionary<StickFunction, ChannelSettings> Channels { get; set; } = new();
    public List<SwitchBinding> Switches { get; set; } = [];

    public StickState Read(RawInputFrame frame)
    {
        double Get(StickFunction function)
        {
            if (!Channels.TryGetValue(function, out var c) || c.AxisIndex >= frame.Axes.Length)
                return function == StickFunction.Throttle ? -1 : 0;
            return ChannelPipeline.Process(frame.Axes[c.AxisIndex], c);
        }

        return new StickState(
            (Get(StickFunction.Throttle) + 1) / 2,
            Get(StickFunction.Aileron),
            Get(StickFunction.Elevator),
            Get(StickFunction.Rudder));
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static RadioProfile FromJson(string json) =>
        JsonSerializer.Deserialize<RadioProfile>(json, Options) ?? throw new InvalidDataException("Empty radio profile.");
}
```

`RadioProfile` references `SwitchBinding`; add it now in `src/Symlab.Input/Switches.cs` (Task 17 adds the tracker test-first):
```csharp
namespace Symlab.Input;

public enum SwitchAction { Reset, Pause, ToggleWind, NextCamera }

/// <summary>Binds a radio button, or an axis above <see cref="Threshold"/>, to a simulator action.</summary>
public sealed record SwitchBinding(SwitchAction Action, int? ButtonIndex = null, int? AxisIndex = null, double Threshold = 0.5);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Symlab.Input.Tests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(input): add input library with channel pipeline and radio profile

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 16: Calibration wizard and profile persistence

**Files:**
- Create: `src/Symlab.Input/CalibrationWizard.cs`
- Test: `tests/Symlab.Input.Tests/CalibrationWizardTests.cs`, `tests/Symlab.Input.Tests/RadioProfileTests.cs`

**Interfaces:**
- Consumes: `RawInputFrame`, `AxisCalibration`, `ChannelSettings`, `RadioProfile`, `StickFunction` (Task 15).
- Produces: `CalibrationWizard(int axisCount)` with `enum Stage { Center, Extremes, Identify, Done }`, `Current`, `FunctionToIdentify` (`StickFunction?`), `Feed(RawInputFrame)`, `Next()`, `BuildProfile(string guid, string name) : RadioProfile`. Identification order: Throttle (push to full), Aileron (right), Elevator (pull back = pitch up), Rudder (right). Constants `MinDeviation = 0.5`, `MinRange = 0.2`.

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Input.Tests/CalibrationWizardTests.cs`:
```csharp
namespace Symlab.Input.Tests;

public class CalibrationWizardTests
{
    // Physical layout: axis0 = rudder, axis1 = throttle (reversed: idle = +1), axis2 = elevator, axis3 = aileron, axis4 = noisy unused.
    static RawInputFrame Frame(double a0, double a1, double a2, double a3, double a4 = 0) => new([a0, a1, a2, a3, a4], []);

    static void FeedMany(CalibrationWizard w, RawInputFrame f, int n = 10)
    {
        for (int i = 0; i < n; i++) w.Feed(f);
    }

    static CalibrationWizard Calibrated()
    {
        var w = new CalibrationWizard(5);
        FeedMany(w, Frame(0, 1, 0, 0, 0.02));
        w.Next();
        foreach (var v in new[] { -1.0, 1.0 })
        {
            FeedMany(w, Frame(v, 1, 0, 0, -0.03));
            FeedMany(w, Frame(0, v, 0, 0, 0.03));
            FeedMany(w, Frame(0, 1, v, 0));
            FeedMany(w, Frame(0, 1, 0, v));
        }
        w.Next();
        Assert.Equal(StickFunction.Throttle, w.FunctionToIdentify);
        FeedMany(w, Frame(0, -1, 0, 0)); w.Next();
        Assert.Equal(StickFunction.Aileron, w.FunctionToIdentify);
        FeedMany(w, Frame(0, -1, 0, 1)); w.Next();
        FeedMany(w, Frame(0, -1, 0.9, 0)); w.Next();
        FeedMany(w, Frame(1, -1, 0, 0)); w.Next();
        return w;
    }

    [Fact]
    public void Full_calibration_identifies_shuffled_and_reversed_axes()
    {
        var w = Calibrated();
        Assert.Equal(CalibrationWizard.Stage.Done, w.Current);
        var p = w.BuildProfile("guid-1", "TX16S");

        Assert.Equal(1, p.Channels[StickFunction.Throttle].AxisIndex);
        Assert.True(p.Channels[StickFunction.Throttle].Reversed);
        Assert.Equal(3, p.Channels[StickFunction.Aileron].AxisIndex);
        Assert.Equal(2, p.Channels[StickFunction.Elevator].AxisIndex);
        Assert.Equal(0, p.Channels[StickFunction.Rudder].AxisIndex);
        Assert.False(p.Channels[StickFunction.Rudder].Reversed);

        var sticks = p.Read(Frame(0, -1, 0, 0.5));
        Assert.Equal(1.0, sticks.Throttle, 9);
        Assert.Equal(0.5, sticks.Aileron, 9);
        Assert.Equal(0.0, p.Read(Frame(0, 1, 0, 0)).Throttle, 9);
    }

    [Fact]
    public void Identification_fails_when_no_stick_moved_enough()
    {
        var w = new CalibrationWizard(4);
        FeedMany(w, new RawInputFrame([0, 0, 0, 0], []));
        w.Next();
        FeedMany(w, new RawInputFrame([1, 1, 1, 1], []));
        FeedMany(w, new RawInputFrame([-1, -1, -1, -1], []));
        w.Next();
        FeedMany(w, new RawInputFrame([0.1, 0, 0, 0], []));
        Assert.Throws<InvalidOperationException>(() => w.Next());
    }

    [Fact]
    public void Profile_cannot_be_built_before_the_end()
        => Assert.Throws<InvalidOperationException>(() => new CalibrationWizard(4).BuildProfile("g", "n"));

    [Fact]
    public void Center_step_needs_samples()
        => Assert.Throws<InvalidOperationException>(() => new CalibrationWizard(4).Next());
}
```

`tests/Symlab.Input.Tests/RadioProfileTests.cs`:
```csharp
namespace Symlab.Input.Tests;

public class RadioProfileTests
{
    [Fact]
    public void Profile_round_trips_through_json()
    {
        var p = new RadioProfile
        {
            DeviceGuid = "030000001209000041ff000000000000",
            DeviceName = "FrSky TX16S",
            Channels = { [StickFunction.Elevator] = new ChannelSettings(1, true, new AxisCalibration(-0.9, 0.02, 0.95), Trim: 0.05, Expo: 0.3, Rate: 0.8) },
            Switches = { new SwitchBinding(SwitchAction.Reset, ButtonIndex: 3) },
        };
        var back = RadioProfile.FromJson(p.ToJson());
        Assert.Equal(p.DeviceGuid, back.DeviceGuid);
        Assert.Equal(p.Channels[StickFunction.Elevator], back.Channels[StickFunction.Elevator]);
        Assert.Equal(p.Switches[0], back.Switches[0]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.Input.Tests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

`src/Symlab.Input/CalibrationWizard.cs`:
```csharp
namespace Symlab.Input;

/// <summary>
/// Four-stage radio calibration: capture centers, capture extremes, then identify each stick function
/// by asking the pilot to move it to its positive end (full throttle, right, pull back, right).
/// </summary>
public sealed class CalibrationWizard
{
    public enum Stage { Center, Extremes, Identify, Done }

    public const double MinDeviation = 0.5;
    public const double MinRange = 0.2;

    static readonly StickFunction[] Order = [StickFunction.Throttle, StickFunction.Aileron, StickFunction.Elevator, StickFunction.Rudder];

    readonly int _axisCount;
    readonly double[] _sum, _min, _max, _center, _peak;
    readonly Dictionary<StickFunction, (int Axis, bool Reversed)> _assigned = new();
    int _samples;
    int _identifyIndex;

    public CalibrationWizard(int axisCount)
    {
        if (axisCount < 4) throw new ArgumentOutOfRangeException(nameof(axisCount), "A radio needs at least 4 axes.");
        _axisCount = axisCount;
        _sum = new double[axisCount];
        _center = new double[axisCount];
        _peak = new double[axisCount];
        _min = Enumerable.Repeat(double.MaxValue, axisCount).ToArray();
        _max = Enumerable.Repeat(double.MinValue, axisCount).ToArray();
    }

    public Stage Current { get; private set; } = Stage.Center;

    public StickFunction? FunctionToIdentify => Current == Stage.Identify ? Order[_identifyIndex] : null;

    public void Feed(RawInputFrame frame)
    {
        if (frame.Axes.Length < _axisCount) throw new ArgumentException($"Expected {_axisCount} axes, got {frame.Axes.Length}.");
        for (int i = 0; i < _axisCount; i++)
        {
            double raw = frame.Axes[i];
            switch (Current)
            {
                case Stage.Center:
                    _sum[i] += raw;
                    Track(i, raw);
                    break;
                case Stage.Extremes:
                    Track(i, raw);
                    break;
                case Stage.Identify:
                    if (IsAssigned(i) || !IsUsable(i)) break;
                    double deviation = (raw - _center[i]) / HalfRange(i);
                    if (Math.Abs(deviation) > Math.Abs(_peak[i])) _peak[i] = deviation;
                    break;
            }
        }
        if (Current == Stage.Center) _samples++;
    }

    public void Next()
    {
        switch (Current)
        {
            case Stage.Center:
                if (_samples == 0) throw new InvalidOperationException("No samples captured for the center position.");
                for (int i = 0; i < _axisCount; i++) _center[i] = _sum[i] / _samples;
                Current = Stage.Extremes;
                break;
            case Stage.Extremes:
                Array.Clear(_peak);
                Current = Stage.Identify;
                break;
            case Stage.Identify:
                var function = Order[_identifyIndex];
                int best = -1;
                for (int i = 0; i < _axisCount; i++)
                    if (!IsAssigned(i) && IsUsable(i) && (best < 0 || Math.Abs(_peak[i]) > Math.Abs(_peak[best]))) best = i;
                if (best < 0 || Math.Abs(_peak[best]) < MinDeviation)
                    throw new InvalidOperationException($"No axis moved enough to identify {function}.");
                _assigned[function] = (best, _peak[best] < 0);
                Array.Clear(_peak);
                _identifyIndex++;
                if (_identifyIndex == Order.Length) Current = Stage.Done;
                break;
            case Stage.Done:
                throw new InvalidOperationException("Calibration is already complete.");
        }
    }

    public RadioProfile BuildProfile(string guid, string name)
    {
        if (Current != Stage.Done) throw new InvalidOperationException("Calibration is not complete.");
        var profile = new RadioProfile { DeviceGuid = guid, DeviceName = name };
        foreach (var (function, (axis, reversed)) in _assigned)
        {
            double center = function == StickFunction.Throttle ? (_min[axis] + _max[axis]) / 2 : _center[axis];
            profile.Channels[function] = new ChannelSettings(axis, reversed, new AxisCalibration(_min[axis], center, _max[axis]));
        }
        return profile;
    }

    void Track(int i, double raw)
    {
        _min[i] = Math.Min(_min[i], raw);
        _max[i] = Math.Max(_max[i], raw);
    }

    bool IsUsable(int i) => _max[i] - _min[i] >= MinRange;

    bool IsAssigned(int axis) => _assigned.Values.Any(a => a.Axis == axis);

    double HalfRange(int i) => Math.Max((_max[i] - _min[i]) / 2, 1e-6);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Symlab.Input.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(input): add radio calibration wizard and JSON profile persistence

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 17: Switch actions and keyboard fallback

**Files:**
- Modify: `src/Symlab.Input/Switches.cs` (add `SwitchTracker`)
- Create: `src/Symlab.Input/Keyboard.cs`
- Test: `tests/Symlab.Input.Tests/SwitchTrackerTests.cs`, `tests/Symlab.Input.Tests/KeyboardTests.cs`

**Interfaces:**
- Consumes: `RawInputFrame`, `SwitchBinding`, `SwitchAction`, `StickState`.
- Produces:
  - `SwitchTracker(IEnumerable<SwitchBinding>)` with `Update(RawInputFrame) : IReadOnlyList<SwitchAction>` — fires on rising edges only; the first frame only primes state (a switch already on at startup does not fire).
  - `KeyboardAxis(double rate = 3.0, double returnRate = 4.0, bool selfCentering = true, double initial = 0)` with `Value`, `Update(double dt, bool negative, bool positive)`.
  - `KeyboardKeys(bool ThrottleUp, bool ThrottleDown, bool RollLeft, bool RollRight, bool PitchUp, bool PitchDown, bool YawLeft, bool YawRight)`.
  - `KeyboardStick` with `Update(double dt, KeyboardKeys keys) : StickState` (throttle starts at idle and holds its position).

- [ ] **Step 1: Write the failing tests**

`tests/Symlab.Input.Tests/SwitchTrackerTests.cs`:
```csharp
namespace Symlab.Input.Tests;

public class SwitchTrackerTests
{
    static RawInputFrame Frame(bool button, double axis = 0) => new([0, 0, 0, 0, axis], [false, false, button]);

    [Fact]
    public void Fires_once_on_the_rising_edge()
    {
        var t = new SwitchTracker([new SwitchBinding(SwitchAction.Reset, ButtonIndex: 2)]);
        Assert.Empty(t.Update(Frame(false)));
        Assert.Equal(SwitchAction.Reset, Assert.Single(t.Update(Frame(true))));
        Assert.Empty(t.Update(Frame(true)));
        Assert.Empty(t.Update(Frame(false)));
        Assert.Equal(SwitchAction.Reset, Assert.Single(t.Update(Frame(true))));
    }

    [Fact]
    public void Switch_already_on_at_startup_does_not_fire()
    {
        var t = new SwitchTracker([new SwitchBinding(SwitchAction.Reset, ButtonIndex: 2)]);
        Assert.Empty(t.Update(Frame(true)));
    }

    [Fact]
    public void Axis_binding_fires_above_threshold()
    {
        var t = new SwitchTracker([new SwitchBinding(SwitchAction.ToggleWind, AxisIndex: 4, Threshold: 0.5)]);
        t.Update(Frame(false, -1));
        Assert.Empty(t.Update(Frame(false, 0.2)));
        Assert.Equal(SwitchAction.ToggleWind, Assert.Single(t.Update(Frame(false, 0.9))));
    }

    [Fact]
    public void Out_of_range_indices_are_ignored()
    {
        var t = new SwitchTracker([new SwitchBinding(SwitchAction.Pause, ButtonIndex: 12)]);
        t.Update(Frame(false));
        Assert.Empty(t.Update(Frame(true)));
    }
}
```

`tests/Symlab.Input.Tests/KeyboardTests.cs`:
```csharp
namespace Symlab.Input.Tests;

public class KeyboardTests
{
    [Fact]
    public void Axis_ramps_toward_the_pressed_side_and_recenters()
    {
        var axis = new KeyboardAxis(rate: 2, returnRate: 4);
        axis.Update(0.25, negative: false, positive: true);
        Assert.Equal(0.5, axis.Value, 12);
        axis.Update(1.0, false, true);
        Assert.Equal(1.0, axis.Value, 12);
        axis.Update(0.1, false, false);
        Assert.Equal(0.6, axis.Value, 12);
        axis.Update(1.0, false, false);
        Assert.Equal(0.0, axis.Value, 12);
    }

    [Fact]
    public void Throttle_starts_at_idle_and_holds_its_position()
    {
        var stick = new KeyboardStick();
        Assert.Equal(0.0, stick.Update(0.01, default).Throttle, 12);
        var up = default(KeyboardKeys) with { ThrottleUp = true };
        stick.Update(1.0, up);
        double held = stick.Update(1.0, default).Throttle;
        Assert.True(held > 0.4, $"throttle {held}");
    }

    [Fact]
    public void Pitch_up_key_gives_positive_elevator()
        => Assert.True(new KeyboardStick().Update(0.2, default(KeyboardKeys) with { PitchUp = true }).Elevator > 0);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Symlab.Input.Tests`
Expected: build FAILS.

- [ ] **Step 3: Implement**

Append to `src/Symlab.Input/Switches.cs`:
```csharp
/// <summary>Turns switch bindings into one-shot actions on rising edges.</summary>
public sealed class SwitchTracker
{
    readonly SwitchBinding[] _bindings;
    readonly bool[] _previous;
    bool _primed;

    public SwitchTracker(IEnumerable<SwitchBinding> bindings)
    {
        _bindings = bindings.ToArray();
        _previous = new bool[_bindings.Length];
    }

    public IReadOnlyList<SwitchAction> Update(RawInputFrame frame)
    {
        var fired = new List<SwitchAction>();
        for (int i = 0; i < _bindings.Length; i++)
        {
            bool on = IsOn(_bindings[i], frame);
            if (_primed && on && !_previous[i]) fired.Add(_bindings[i].Action);
            _previous[i] = on;
        }
        _primed = true;
        return fired;
    }

    static bool IsOn(SwitchBinding b, RawInputFrame f)
    {
        if (b.ButtonIndex is int button) return button < f.Buttons.Length && f.Buttons[button];
        if (b.AxisIndex is int axis) return axis < f.Axes.Length && f.Axes[axis] > b.Threshold;
        return false;
    }
}
```

`src/Symlab.Input/Keyboard.cs`:
```csharp
namespace Symlab.Input;

/// <summary>Digital key pair → analog axis with ramp-up and optional spring return.</summary>
public sealed class KeyboardAxis
{
    readonly double _rate;
    readonly double _returnRate;
    readonly bool _selfCentering;

    public KeyboardAxis(double rate = 3.0, double returnRate = 4.0, bool selfCentering = true, double initial = 0)
    {
        _rate = rate;
        _returnRate = returnRate;
        _selfCentering = selfCentering;
        Value = initial;
    }

    public double Value { get; private set; }

    public double Update(double dt, bool negative, bool positive)
    {
        if (positive && !negative) Value = Math.Min(1, Value + _rate * dt);
        else if (negative && !positive) Value = Math.Max(-1, Value - _rate * dt);
        else if (_selfCentering) Value = Value > 0 ? Math.Max(0, Value - _returnRate * dt) : Math.Min(0, Value + _returnRate * dt);
        return Value;
    }
}

public readonly record struct KeyboardKeys(
    bool ThrottleUp, bool ThrottleDown,
    bool RollLeft, bool RollRight,
    bool PitchUp, bool PitchDown,
    bool YawLeft, bool YawRight);

/// <summary>Keyboard fallback for flying without a radio.</summary>
public sealed class KeyboardStick
{
    readonly KeyboardAxis _throttle = new(rate: 1.0, selfCentering: false, initial: -1);
    readonly KeyboardAxis _aileron = new();
    readonly KeyboardAxis _elevator = new();
    readonly KeyboardAxis _rudder = new();

    public StickState Update(double dt, KeyboardKeys keys) => new(
        (_throttle.Update(dt, keys.ThrottleDown, keys.ThrottleUp) + 1) / 2,
        _aileron.Update(dt, keys.RollLeft, keys.RollRight),
        _elevator.Update(dt, keys.PitchDown, keys.PitchUp),
        _rudder.Update(dt, keys.YawLeft, keys.YawRight));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all PASS (both test projects).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(input): add switch actions and keyboard fallback

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 18: README and final verification

**Files:**
- Create: `README.md`

- [ ] **Step 1: Write the README**

`README.md`:
````markdown
# Symlab

Realistic RC airplane simulator. The goal is training transfer: practice in the sim, then fly the real aircraft.

## Status

Sub-project 1 (core) — headless libraries:

- `src/Symlab.Flight` — 6DOF flight model: strip-theory aerodynamics with ±180° airfoil polars, electric
  propulsion (battery, ESC, motor, propeller, thrust-stand calibration, APC import), wheels and hull contact,
  crash detection, ISA atmosphere, wind with Dryden turbulence, fixed-step (500 Hz) simulation, CSV recorder.
- `src/Symlab.Input` — radio input: calibration wizard, channel pipeline (trim, expo, rates), switch actions,
  keyboard fallback.
- `aircraft/` — data-driven aircraft (trainer, sport, FPV wing). Values are estimates; see `docs/tuning-log.md`.

Next: the Godot game layer (field, line-of-sight camera, menus), then VSPAERO/CFD import, FPV and chase cameras,
and VTOL/drones. Design: `docs/superpowers/specs/2026-09-22-symlab-core-design.md`.

## Build and test

```bash
dotnet test
```

Requires the .NET 10 SDK (`brew install --cask dotnet-sdk`).

## Conventions

- SI units, doubles. World: y-up, x east, z south. Body: x forward, y up, z right.
- Control deflection: positive = trailing edge down. Mixing (elevons, V-tail) is declared per control surface in
  `aircraft.json`.
````

- [ ] **Step 2: Run the full suite from a clean build**

```bash
dotnet clean && dotnet test
```
Expected: all tests PASS, zero warnings (warnings are errors).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "docs: add README for the core libraries

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## After this plan

Write the next plan, **Godot game layer** (still sub-project 1), against the API produced here:
Godot 4 .NET project in `game/` referencing both libraries; SDL joystick → `RawInputFrame`; radio screen and
calibration wizard UI (French default locale); club field (terrain, runway, trees as `CylinderObstacle`s, HDR sky,
windsock); `ICameraRig` with the line-of-sight rig; body→Godot basis mapping (−Z forward, +X right) with
interpolation using `Simulation.Previous` and `InterpolationAlpha`; control-surface animation; crash screen;
optional flight-data panel; input-to-render latency overlay.
