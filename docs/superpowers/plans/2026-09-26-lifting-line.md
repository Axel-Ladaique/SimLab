# Lifting Line Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the strip model's per-surface induced angle and scalar tail downwash with a Weissinger-type nonlinear lifting line over all surfaces, so SimLab's stability derivatives match AVL.

**Architecture:** A new `LiftingLine` (horseshoe vortices, influence matrices, frozen-Jacobian Newton solve with warm start, lagged wing→tail influence) sits inside `SurfaceAeroModel`, which keeps its strip loop (polars, flaps, prop wash, section moments) but takes the section angle of attack from the lifting line's control-point induced flow and the force direction from the bound-vortex induced flow, plus a chordwise trailing-leg couple. A test utility computes stability derivatives; a fixture holds AVL's values.

**Tech Stack:** C# / .NET (net8.0 target, runs on the installed .NET 10 runtime), xUnit, pure `SimLab.Flight` library (no Godot). AVL 3.40b and Waxwing (Python, `~/4_WAXWING/.venv`) only to regenerate the AVL fixture.

**Spec:** `docs/superpowers/specs/2026-09-26-lifting-line-design.md` (read it first; the method section explains every formula below).

## Global Constraints

- Code, comments and docs in English. Match the surrounding style (XML doc comments on public members, file-scoped namespaces, `Vec3` from `SimLab.Flight.Geometry`).
- Body axes everywhere in the flight library: x back, y right, z up; origin at the CG. `AeroContext.AirVelocityBody` is the CG velocity *relative to the air* (so the flow the aircraft sees is its negative).
- `Directory.Build.props` sets `TreatWarningsAsErrors`, `Nullable`, `ImplicitUsings`: the build must stay warning-free.
- No allocation per `SurfaceAeroModel.Evaluate` call (all buffers allocated in constructors).
- Run tests with `dotnet test tests/SimLab.Flight.Tests` from the worktree root `/Users/axel.ldq/3_SYMLAB/.worktrees/lifting-line`. Never `cd` to `/Users/axel.ldq/3_SYMLAB` itself (other sessions use it) and never switch branches there.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Criteria (spec): derivatives ±15 % of AVL; static margin ±0.02 of AVL; spiral criterion on AVL's side of 1; Cnp same sign and ±0.015; Cnδa ±0.0003/deg; mean `Evaluate` below 50 µs (Release).

## File Structure

| File | Responsibility |
|---|---|
| `src/SimLab.Flight/Aero/VortexMath.cs` (new) | Biot–Savart velocity per unit circulation of a straight segment and of a semi-infinite line, Scully core. |
| `src/SimLab.Flight/Numerics/LuDecomposition.cs` (new) | Dense LU with partial pivoting; factor once, solve many times. |
| `src/SimLab.Flight/Aero/LiftingLine.cs` (new) | Horseshoe geometry, influence matrices, Jacobian, nonlinear solve, downwash lag. `StripState` input record. |
| `src/SimLab.Flight/Aero/SurfaceAeroModel.cs` | Uses `LiftingLine`; old induced angle and downwash removed; `TailDownwash`, `Line`. |
| `src/SimLab.Flight/Aero/InducedFlow.cs` | Keeps only `GroundEffectFactor`. |
| `src/SimLab.Flight/Aero/SurfaceSegment.cs`, `SurfaceGeometry.cs`, `SurfaceSpec.cs` | `InducedFactor` and `Oswald` removed. |
| `src/SimLab.Flight/Airframe/LoaderDtos.cs`, `AircraftLoader.cs` | `oswald` rejected. |
| `tests/SimLab.Flight.Tests/StabilityDerivatives.cs` (new) | Finite-difference stability derivatives in AVL conventions. |
| `tests/SimLab.Flight.Tests/Aero/VortexMathTests.cs`, `Numerics/LuDecompositionTests.cs`, `Aero/LiftingLineTests.cs`, `Aero/LiftingLineValidationTests.cs` (new) | Unit and validation tests. |
| `tests/SimLab.Flight.Tests/Behavior/AvlDerivativeTests.cs` (new), `Behavior/Golden/avl-derivatives.json` (already committed with this plan) | Fleet criteria against AVL. |
| `tests/SimLab.Flight.Tests/Behavior/StaticStability.cs` | Settles the downwash lag. |
| `docs/investigations/2026-09-26-avl-comparison/compare_avl.py` | Harness path argument, `--write-fixture`. |

---

### Task 1: Vortex math and LU decomposition

**Files:**
- Create: `src/SimLab.Flight/Aero/VortexMath.cs`
- Create: `src/SimLab.Flight/Numerics/LuDecomposition.cs`
- Test: `tests/SimLab.Flight.Tests/Aero/VortexMathTests.cs`
- Test: `tests/SimLab.Flight.Tests/Numerics/LuDecompositionTests.cs`

**Interfaces:**
- Produces: `VortexMath.Segment(Vec3 p, Vec3 a, Vec3 b, double core) : Vec3`, `VortexMath.SemiInfinite(Vec3 p, Vec3 a, Vec3 direction, double core) : Vec3` — flow velocity at `p` per unit circulation (circulation runs a→b, or from a along `direction` to infinity). `new LuDecomposition(double[,] matrix)`, `int Size`, `void Solve(ReadOnlySpan<double> rhs, Span<double> x)`; the constructor throws `InvalidOperationException` for a singular matrix.

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.Flight.Tests/Aero/VortexMathTests.cs`:

```csharp
using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Aero;

public class VortexMathTests
{
    [Fact]
    public void Long_segment_induces_the_infinite_line_velocity()
    {
        // Circulation along +x, point 0.5 m above: v = 1/(2πh), along x × z = −y.
        var v = VortexMath.Segment(new Vec3(0, 0, 0.5), new Vec3(-1000, 0, 0), new Vec3(1000, 0, 0), 0);
        Assert.Equal(0, v.X, 9);
        Assert.Equal(-1 / (2 * Math.PI * 0.5), v.Y, 6);
        Assert.Equal(0, v.Z, 9);
    }

    [Fact]
    public void Semi_infinite_line_induces_half_of_the_infinite_line_beside_its_start()
    {
        var v = VortexMath.SemiInfinite(new Vec3(0, 0, 0.5), Vec3.Zero, Vec3.UnitX, 0);
        Assert.Equal(-1 / (4 * Math.PI * 0.5), v.Y, 9);
        Assert.Equal(0, v.X, 12);
        Assert.Equal(0, v.Z, 12);
    }

    [Fact]
    public void Core_keeps_the_velocity_finite_on_and_near_the_line()
    {
        const double core = 0.1;
        var onLine = VortexMath.Segment(Vec3.Zero, new Vec3(-1, 0, 0), new Vec3(1, 0, 0), core);
        var near = VortexMath.Segment(new Vec3(0, 0, 1e-6), new Vec3(-1, 0, 0), new Vec3(1, 0, 0), core);
        var nearLeg = VortexMath.SemiInfinite(new Vec3(1, 0, 1e-6), Vec3.Zero, Vec3.UnitX, core);
        Assert.Equal(Vec3.Zero, onLine);
        Assert.True(near.Length < 1 / (2 * Math.PI * core), $"segment {near.Length}");
        Assert.True(nearLeg.Length < 1 / (2 * Math.PI * core), $"leg {nearLeg.Length}");
    }

    [Fact]
    public void Point_at_an_end_gives_zero_instead_of_nan()
    {
        Assert.Equal(Vec3.Zero, VortexMath.Segment(Vec3.Zero, Vec3.Zero, Vec3.UnitY, 0));
        Assert.Equal(Vec3.Zero, VortexMath.SemiInfinite(Vec3.Zero, Vec3.Zero, Vec3.UnitX, 0));
        Assert.Equal(Vec3.Zero, VortexMath.SemiInfinite(new Vec3(2, 0, 0), Vec3.Zero, Vec3.UnitX, 0));
    }
}
```

`tests/SimLab.Flight.Tests/Numerics/LuDecompositionTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~VortexMathTests|FullyQualifiedName~LuDecompositionTests"`
Expected: build error, `VortexMath` and `LuDecomposition` do not exist.

- [ ] **Step 3: Implement**

`src/SimLab.Flight/Aero/VortexMath.cs`:

```csharp
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

/// <summary>
/// Biot–Savart flow velocities per unit circulation of straight vortex filaments, with a Scully core of radius
/// <c>core</c>: the tangential speed Γ r / (2π (r² + core²)) of an infinite line, so it stays finite on the filament.
/// </summary>
public static class VortexMath
{
    const double FourPi = 4 * Math.PI;

    /// <summary>Velocity at <paramref name="p"/> induced by a unit circulation running from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static Vec3 Segment(Vec3 p, Vec3 a, Vec3 b, double core)
    {
        var r0 = b - a;
        var r1 = p - a;
        var r2 = p - b;
        double n1 = r1.Length, n2 = r2.Length;
        if (n1 < 1e-12 || n2 < 1e-12) return Vec3.Zero;
        var cross = Vec3.Cross(r1, r2);
        double denominator = cross.LengthSquared + core * core * r0.LengthSquared;
        if (denominator < 1e-24) return Vec3.Zero;
        return cross * (Vec3.Dot(r0, r1 / n1 - r2 / n2) / (FourPi * denominator));
    }

    /// <summary>
    /// Velocity at <paramref name="p"/> induced by a unit circulation running from <paramref name="a"/> to infinity along
    /// the unit vector <paramref name="direction"/>.
    /// </summary>
    public static Vec3 SemiInfinite(Vec3 p, Vec3 a, Vec3 direction, double core)
    {
        var r1 = p - a;
        double n1 = r1.Length;
        if (n1 < 1e-12) return Vec3.Zero;
        double along = Vec3.Dot(r1, direction);
        double distanceSquared = Math.Max(0, r1.LengthSquared - along * along) + core * core;
        if (distanceSquared < 1e-24) return Vec3.Zero;
        return Vec3.Cross(direction, r1) * ((1 + along / n1) / (FourPi * distanceSquared));
    }
}
```

`src/SimLab.Flight/Numerics/LuDecomposition.cs`:

```csharp
namespace SimLab.Flight.Numerics;

/// <summary>LU factorization with partial pivoting of a square matrix: factored once, solved for many right-hand sides.</summary>
public sealed class LuDecomposition
{
    readonly double[,] _lu;
    readonly int[] _pivot;

    public LuDecomposition(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n) throw new ArgumentException("The matrix must be square.", nameof(matrix));
        _lu = (double[,])matrix.Clone();
        _pivot = new int[n];
        for (int i = 0; i < n; i++) _pivot[i] = i;
        for (int k = 0; k < n; k++)
        {
            int row = k;
            double largest = Math.Abs(_lu[k, k]);
            for (int i = k + 1; i < n; i++)
            {
                if (Math.Abs(_lu[i, k]) > largest)
                {
                    largest = Math.Abs(_lu[i, k]);
                    row = i;
                }
            }
            if (largest < 1e-14) throw new InvalidOperationException("The matrix is singular.");
            if (row != k)
            {
                for (int c = 0; c < n; c++) (_lu[k, c], _lu[row, c]) = (_lu[row, c], _lu[k, c]);
                (_pivot[k], _pivot[row]) = (_pivot[row], _pivot[k]);
            }
            for (int i = k + 1; i < n; i++)
            {
                double factor = _lu[i, k] /= _lu[k, k];
                for (int c = k + 1; c < n; c++) _lu[i, c] -= factor * _lu[k, c];
            }
        }
        Size = n;
    }

    public int Size { get; }

    /// <summary>Solves A x = <paramref name="rhs"/>. <paramref name="x"/> must not alias <paramref name="rhs"/>.</summary>
    public void Solve(ReadOnlySpan<double> rhs, Span<double> x)
    {
        int n = Size;
        if (rhs.Length != n || x.Length != n) throw new ArgumentException("Vector lengths must match the matrix size.");
        for (int i = 0; i < n; i++)
        {
            double sum = rhs[_pivot[i]];
            for (int c = 0; c < i; c++) sum -= _lu[i, c] * x[c];
            x[i] = sum;
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = x[i];
            for (int c = i + 1; c < n; c++) sum -= _lu[i, c] * x[c];
            x[i] = sum / _lu[i, i];
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~VortexMathTests|FullyQualifiedName~LuDecompositionTests"`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.Flight/Aero/VortexMath.cs src/SimLab.Flight/Numerics/LuDecomposition.cs tests/SimLab.Flight.Tests/Aero/VortexMathTests.cs tests/SimLab.Flight.Tests/Numerics/LuDecompositionTests.cs
git commit -m "feat(aero): vortex filament velocities and a dense LU solver

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `LiftingLine`

**Files:**
- Create: `src/SimLab.Flight/Aero/LiftingLine.cs`
- Test: `tests/SimLab.Flight.Tests/Aero/LiftingLineTests.cs`

**Interfaces:**
- Consumes: Task 1 (`VortexMath`, `LuDecomposition`); existing `SurfaceSegment` (`Position`, `HalfSpan`, `ChordAxis`, `NormalAxis`, `FlowChordAxis`, `FlowNormalAxis`, `Chord`, `Area`, `Role`, `SurfaceName`, `Airfoil`), `SurfaceGeometry.Build(SurfaceSpec, Airfoil)`, `Isa.DynamicViscosity`.
- Produces:
  - `public readonly record struct StripState(Vec3 Velocity, double AlphaOffset, double GroundFactor)`
  - `public sealed class LiftingLine` with `LiftingLine(IReadOnlyList<SurfaceSegment> segments)`, constants `CoreFraction` (0.05), `MaxIterations` (8), `Tolerance` (1e-9), members `int Count`, `double Circulation(int i)`, `Vec3 InducedAtControlPoint(int i)`, `Vec3 InducedAtBoundVortex(int i)`, `Vec3 BoundVector(int i)`, `double NormalChord(int i)`, `int LastIterations`, `long NonConvergedSolves`, `void Solve(ReadOnlySpan<StripState> strips, double density)`, `void Advance(double dt, double airspeed)`, `void Reset()`, `static Vec3 ControlPoint(SurfaceSegment s)`.
  - Induced velocities are *flow* velocities (the air's motion); `SurfaceAeroModel` subtracts them from the strip's velocity relative to the air.

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.Flight.Tests/Aero/LiftingLineTests.cs`:

```csharp
using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests.Aero;

public class LiftingLineTests
{
    static readonly SurfaceSpec Rectangular = new("wing", SurfaceRole.Wing, Vec3.Zero, 0.75, 0.25, 0.25, 0, 0, 0, 0, "linear", 12, true);
    static readonly SurfaceSpec Stab = new("stab", SurfaceRole.HorizontalTail, new Vec3(0.8, 0, 0.1), 0.25, 0.15, 0.15, 0, 0, 0, 0, "linear", 4, true);
    static readonly SurfaceSpec Fin = new("fin", SurfaceRole.VerticalTail, new Vec3(0.8, 0, 0.05), 0.2, 0.18, 0.12, 20, 90, 0, 0, "linear", 6, false);

    static List<SurfaceSegment> Strips(params SurfaceSpec[] specs) =>
        specs.SelectMany(s => SurfaceGeometry.Build(s, TestAirfoils.Linear())).ToList();

    static StripState[] Uniform(int count, double speed, double alphaDeg, double betaDeg = 0)
    {
        double a = Angle.Rad(alphaDeg), b = Angle.Rad(betaDeg);
        var air = new Vec3(-speed * Math.Cos(a) * Math.Cos(b), speed * Math.Sin(b), -speed * Math.Sin(a) * Math.Cos(b));
        return Enumerable.Repeat(new StripState(air, 0, 1), count).ToArray();
    }

    [Fact]
    public void Bound_vortices_run_so_that_the_flow_lifts_along_each_strip_normal()
    {
        var strips = Strips(Rectangular, Fin);
        var line = new LiftingLine(strips);
        for (int i = 0; i < strips.Count; i++)
            Assert.True(Vec3.Dot(Vec3.Cross(Vec3.UnitX, line.BoundVector(i)), strips[i].NormalAxis) > 0, $"strip {i}");
    }

    [Fact]
    public void Very_long_wing_tends_to_the_two_dimensional_circulation()
    {
        var longWing = Rectangular with { Span = 50, Segments = 40 };
        var strips = Strips(longWing);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, 15, 3), 1.225);
        // Mid-span strip (index 0 is the right root strip): Γ = ½ V c a α with a = 2π.
        double expected = 0.5 * 15 * 0.25 * 2 * Math.PI * Math.Sin(Angle.Rad(3));
        Assert.InRange(line.Circulation(0), 0.97 * expected, 1.01 * expected);
    }

    [Fact]
    public void Linear_polar_converges_in_a_few_newton_steps()
    {
        var strips = Strips(Rectangular, Stab, Fin);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, 15, 2), 1.225);
        Assert.True(line.LastIterations <= 3, $"iterations {line.LastIterations}");
        Assert.Equal(0, line.NonConvergedSolves);
    }

    [Fact]
    public void Symmetric_flight_gives_symmetric_circulation()
    {
        var strips = Strips(Rectangular, Stab);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, 15, 4), 1.225);
        // SurfaceGeometry emits each right strip followed by its left mirror.
        for (int i = 0; i < strips.Count; i += 2)
            Assert.Equal(line.Circulation(i), line.Circulation(i + 1), 9);
    }

    [Fact]
    public void Tail_feels_the_wing_downwash_after_the_lag()
    {
        var strips = Strips(Rectangular, Stab);
        var line = new LiftingLine(strips);
        var states = Uniform(strips.Count, 15, 5);
        int tail = strips.FindIndex(s => s.Role == SurfaceRole.HorizontalTail);
        // Lag time constant: stab mean quarter-chord x (0.8) minus the wing's (0), over the airspeed.
        double tau = 0.8 / 15;

        line.Solve(states, 1.225);
        double before = line.Circulation(tail);
        line.Advance(1000, 15);
        line.Solve(states, 1.225);
        double settled = line.Circulation(tail);
        Assert.True(settled < before, "the wing's downwash lowers the tail's circulation");

        line.Reset();
        line.Solve(states, 1.225);
        for (int k = 0; k < 100; k++)
        {
            line.Advance(tau / 100, 15);
            line.Solve(states, 1.225);
        }
        double fraction = (line.Circulation(tail) - before) / (settled - before);
        Assert.InRange(fraction, 0.57, 0.70);
    }

    [Theory]
    [InlineData(15, 40)]
    [InlineData(15, 180)]
    [InlineData(0.05, 5)]
    public void Stays_finite_far_outside_the_linear_range(double speed, double alphaDeg)
    {
        var strips = Strips(Rectangular, Stab, Fin);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, speed, alphaDeg, 10), 1.225);
        Assert.True(line.LastIterations <= LiftingLine.MaxIterations);
        for (int i = 0; i < strips.Count; i++)
        {
            Assert.True(double.IsFinite(line.Circulation(i)), $"Γ {i}");
            Assert.True(double.IsFinite(line.InducedAtBoundVortex(i).Length), $"induced {i}");
        }
    }

    [Fact]
    public void Reset_clears_the_circulation()
    {
        var strips = Strips(Rectangular);
        var line = new LiftingLine(strips);
        line.Solve(Uniform(strips.Count, 15, 4), 1.225);
        Assert.NotEqual(0, line.Circulation(0));
        line.Reset();
        Assert.Equal(0, line.Circulation(0));
        Assert.Equal(Vec3.Zero, line.InducedAtBoundVortex(0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~LiftingLineTests"`
Expected: build error, `LiftingLine` and `StripState` do not exist.

- [ ] **Step 3: Implement**

`src/SimLab.Flight/Aero/LiftingLine.cs`:

```csharp
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Geometry;
using SimLab.Flight.Numerics;

namespace SimLab.Flight.Aero;

/// <param name="Velocity">
/// Velocity of the strip's three-quarter-chord point relative to the air, body axes (m/s): freestream, rotation and prop
/// wash, without the lifting line's own induced flow (the <see cref="AeroContext"/> convention).
/// </param>
/// <param name="AlphaOffset">Angle added to the section angle of attack (rad): the flap term.</param>
/// <param name="GroundFactor">Factor on the induced flow the strip receives (McCormick ground effect, 1 out of ground effect).</param>
public readonly record struct StripState(Vec3 Velocity, double AlphaOffset, double GroundFactor);

/// <summary>
/// Weissinger-type lifting line over all strips of all surfaces, with each strip's own section polar
/// (docs/superpowers/specs/2026-09-26-lifting-line-design.md, section 1).
/// </summary>
/// <remarks>
/// Each strip is a horseshoe vortex: a bound segment on its quarter-chord line and two trailing legs to +∞ along body +x.
/// The section angle of attack is taken at the three-quarter-chord control point, where every horseshoe's flow is summed
/// and the two-dimensional self-induction of the strip's own bound vortex, a normal flow Γ/(π cₙ), is removed because the
/// polar already contains it. Γᵢ = ½ vᵢ cₙᵢ clᵢ with cₙ = c·cosΛ, the chord normal to the bound vortex. The equations are
/// solved by Newton's method with a constant Jacobian, I − diag(½ cₙ a)·N, factored once. Tail strips see the wing
/// strips' circulation through a first-order lag of time constant (tail x − wing x) / V: the downwash takes that long to
/// travel to the tail.
/// </remarks>
public sealed class LiftingLine
{
    /// <summary>Scully core radius as a fraction of the source strip's bound-segment length.</summary>
    public const double CoreFraction = 0.05;

    public const int MaxIterations = 8;

    /// <summary>Convergence when max |residual| ≤ Tolerance · max(1, max |Γ|).</summary>
    public const double Tolerance = 1e-9;

    const double MinSpeed = 0.1;
    static readonly double SlopeProbe = Angle.Rad(2);

    readonly SurfaceSegment[] _segments;
    readonly int _n;
    readonly Vec3[] _start;
    readonly Vec3[] _end;
    readonly double[] _normalChord;
    readonly double[] _slope;
    readonly Vec3[] _wControl;
    readonly Vec3[] _wBound;
    readonly bool[] _isWing;
    readonly int[] _group;
    readonly double[] _groupLead;
    readonly double[][] _lagged;
    readonly LuDecomposition _jacobian;
    readonly double[] _gamma;
    readonly double[] _residual;
    readonly double[] _step;
    readonly double[] _groundFactor;
    readonly bool[] _still;
    readonly Vec3[] _inducedControl;
    readonly Vec3[] _inducedBound;

    public LiftingLine(IReadOnlyList<SurfaceSegment> segments)
    {
        _segments = segments.ToArray();
        _n = _segments.Length;
        _start = new Vec3[_n];
        _end = new Vec3[_n];
        _normalChord = new double[_n];
        _slope = new double[_n];
        for (int i = 0; i < _n; i++)
        {
            var s = _segments[i];
            var half = s.HalfSpan;
            if (half.Length < 1e-9) throw new ArgumentException($"Strip {i} of '{s.SurfaceName}' has no span.");
            // Circulation runs start → end, oriented so that the flow along +x over it lifts along the strip normal.
            if (Vec3.Dot(Vec3.Cross(Vec3.UnitX, half), s.NormalAxis) < 0) half = -half;
            _start[i] = s.Position - half;
            _end[i] = s.Position + half;
            _normalChord[i] = s.Area / (2 * half.Length);
            // Attached lift slope at the lowest-Reynolds table (Reynolds 0 clamps to it).
            double slope = (s.Airfoil.Evaluate(SlopeProbe, 0).Cl - s.Airfoil.Evaluate(-SlopeProbe, 0).Cl) / (2 * SlopeProbe);
            _slope[i] = Math.Max(slope, 1.0);
        }

        _wControl = new Vec3[_n * _n];
        _wBound = new Vec3[_n * _n];
        for (int i = 0; i < _n; i++)
        {
            var control = ControlPoint(_segments[i]);
            var bound = _segments[i].Position;
            for (int j = 0; j < _n; j++)
            {
                double core = CoreFraction * (_end[j] - _start[j]).Length;
                _wControl[i * _n + j] = Horseshoe(control, j, core, includeBound: true);
                _wBound[i * _n + j] = Horseshoe(bound, j, core, includeBound: i != j);
            }
        }

        _isWing = _segments.Select(s => s.Role == SurfaceRole.Wing).ToArray();
        _group = new int[_n];
        Array.Fill(_group, -1);
        var leads = new List<double>();
        if (_isWing.Any(w => w))
        {
            double wingX = Enumerable.Range(0, _n).Where(i => _isWing[i]).Average(i => _segments[i].Position.X);
            var groups = new Dictionary<string, int>();
            for (int i = 0; i < _n; i++)
            {
                var s = _segments[i];
                if (s.Role is not (SurfaceRole.HorizontalTail or SurfaceRole.VerticalTail)) continue;
                if (!groups.TryGetValue(s.SurfaceName, out int g))
                {
                    g = groups.Count;
                    groups[s.SurfaceName] = g;
                    double surfaceX = _segments.Where(o => o.SurfaceName == s.SurfaceName).Average(o => o.Position.X);
                    leads.Add(Math.Max(0, surfaceX - wingX));
                }
                _group[i] = g;
            }
        }
        _groupLead = leads.ToArray();
        _lagged = _groupLead.Select(_ => new double[_n]).ToArray();

        var jacobian = new double[_n, _n];
        for (int i = 0; i < _n; i++)
        {
            var normal = _segments[i].FlowNormalAxis;
            for (int j = 0; j < _n; j++)
            {
                bool lagged = _group[i] >= 0 && _isWing[j];
                double coupling = lagged ? 0 : Vec3.Dot(normal, _wControl[i * _n + j]) + (i == j ? 1 / (Math.PI * _normalChord[i]) : 0);
                jacobian[i, j] = (i == j ? 1 : 0) - 0.5 * _normalChord[i] * _slope[i] * coupling;
            }
        }
        _jacobian = new LuDecomposition(jacobian);

        _gamma = new double[_n];
        _residual = new double[_n];
        _step = new double[_n];
        _groundFactor = new double[_n];
        _still = new bool[_n];
        _inducedControl = new Vec3[_n];
        _inducedBound = new Vec3[_n];
    }

    public int Count => _n;

    /// <summary>Iterations of the last <see cref="Solve"/> (0 when the warm start already met the tolerance).</summary>
    public int LastIterations { get; private set; }

    /// <summary>Solves that stopped at <see cref="MaxIterations"/> without meeting the tolerance (not cleared by <see cref="Reset"/>).</summary>
    public long NonConvergedSolves { get; private set; }

    /// <summary>Circulation of strip i (m²/s), positive when the strip lifts along its normal.</summary>
    public double Circulation(int i) => _gamma[i];

    /// <summary>Flow induced at strip i's control point without the strip's 2D self-induction, ground factor applied.</summary>
    public Vec3 InducedAtControlPoint(int i) => _inducedControl[i];

    /// <summary>Flow induced at the middle of strip i's bound vortex (its own bound segment excluded), ground factor applied.</summary>
    public Vec3 InducedAtBoundVortex(int i) => _inducedBound[i];

    /// <summary>Strip i's bound vortex from start to end (m, body axes), in the direction of positive circulation.</summary>
    public Vec3 BoundVector(int i) => _end[i] - _start[i];

    /// <summary>Chord normal to strip i's bound vortex, c·cosΛ (m).</summary>
    public double NormalChord(int i) => _normalChord[i];

    /// <summary>The three-quarter-chord point of a strip, where its angle of attack and rotation velocity are taken.</summary>
    public static Vec3 ControlPoint(SurfaceSegment s) => s.Position - s.ChordAxis * (0.5 * s.Chord);

    public void Solve(ReadOnlySpan<StripState> strips, double density)
    {
        if (strips.Length != _n) throw new ArgumentException($"Expected {_n} strip states, got {strips.Length}.", nameof(strips));
        for (int i = 0; i < _n; i++)
        {
            var s = _segments[i];
            _groundFactor[i] = strips[i].GroundFactor;
            double uc = Vec3.Dot(strips[i].Velocity, s.FlowChordAxis), un = Vec3.Dot(strips[i].Velocity, s.FlowNormalAxis);
            _still[i] = uc * uc + un * un < MinSpeed * MinSpeed;
            if (_still[i]) _gamma[i] = 0;
        }

        double lambda = 1, previous = double.PositiveInfinity;
        bool converged = false;
        int iteration = 0;
        for (; ; iteration++)
        {
            double worst = Residuals(strips, density);
            if (worst <= Tolerance * Math.Max(1, MaxAbsGamma()))
            {
                converged = true;
                break;
            }
            if (iteration == MaxIterations) break;
            // Damping for stall, where the constant Jacobian's lift slope has the wrong sign.
            lambda = worst > previous ? Math.Max(lambda / 2, 0.125) : 1;
            previous = worst;
            _jacobian.Solve(_residual, _step);
            for (int i = 0; i < _n; i++) _gamma[i] = _still[i] ? 0 : _gamma[i] + lambda * _step[i];
        }
        LastIterations = iteration;
        if (!converged) NonConvergedSolves++;
        for (int i = 0; i < _n; i++) _inducedBound[i] = Induced(_wBound, i) * _groundFactor[i];
    }

    /// <summary>Advances the lagged wing circulation seen by each tail surface (not called by <see cref="Solve"/>).</summary>
    public void Advance(double dt, double airspeed)
    {
        double speed = Math.Max(airspeed, 1.0);
        for (int g = 0; g < _groupLead.Length; g++)
        {
            double tau = _groupLead[g] / speed;
            double k = tau > 1e-9 ? Math.Min(1, dt / tau) : 1;
            var lagged = _lagged[g];
            for (int j = 0; j < _n; j++)
                if (_isWing[j]) lagged[j] += (_gamma[j] - lagged[j]) * k;
        }
    }

    public void Reset()
    {
        Array.Clear(_gamma);
        foreach (var lagged in _lagged) Array.Clear(lagged);
        Array.Clear(_inducedControl);
        Array.Clear(_inducedBound);
        LastIterations = 0;
    }

    Vec3 Horseshoe(Vec3 p, int j, double core, bool includeBound)
    {
        var v = VortexMath.SemiInfinite(p, _end[j], Vec3.UnitX, core) - VortexMath.SemiInfinite(p, _start[j], Vec3.UnitX, core);
        return includeBound ? v + VortexMath.Segment(p, _start[j], _end[j], core) : v;
    }

    double Residuals(ReadOnlySpan<StripState> strips, double density)
    {
        double worst = 0;
        for (int i = 0; i < _n; i++)
        {
            if (_still[i])
            {
                _residual[i] = 0;
                _inducedControl[i] = Vec3.Zero;
                continue;
            }
            var s = _segments[i];
            var w = (Induced(_wControl, i) + s.FlowNormalAxis * (_gamma[i] / (Math.PI * _normalChord[i]))) * _groundFactor[i];
            _inducedControl[i] = w;
            var u = strips[i].Velocity - w;
            double uc = Vec3.Dot(u, s.FlowChordAxis), un = Vec3.Dot(u, s.FlowNormalAxis);
            double v = Math.Sqrt(uc * uc + un * un);
            double alpha = Math.Atan2(-un, uc) + strips[i].AlphaOffset;
            double reynolds = density * v * s.Chord / Isa.DynamicViscosity;
            double cl = s.Airfoil.Evaluate(alpha, reynolds).Cl;
            _residual[i] = 0.5 * v * _normalChord[i] * cl - _gamma[i];
            worst = Math.Max(worst, Math.Abs(_residual[i]));
        }
        return worst;
    }

    Vec3 Induced(Vec3[] influence, int i)
    {
        var sum = Vec3.Zero;
        int row = i * _n, g = _group[i];
        for (int j = 0; j < _n; j++)
        {
            double gamma = g >= 0 && _isWing[j] ? _lagged[g][j] : _gamma[j];
            if (gamma != 0) sum += influence[row + j] * gamma;
        }
        return sum;
    }

    double MaxAbsGamma()
    {
        double m = 0;
        for (int i = 0; i < _n; i++) m = Math.Max(m, Math.Abs(_gamma[i]));
        return m;
    }
}
```

Note: `SurfaceGeometry.Build` still takes the `Oswald`-derived induced factor at this point; that is removed in Task 3. The tests above construct `SurfaceSpec` without the `Oswald` argument (it has a default), so they compile before and after Task 3.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~LiftingLineTests"`
Expected: 9 passed (the theory counts 3). If `Tail_feels_the_wing_downwash_after_the_lag` lands just outside 0.57–0.70, print `fraction` and check the lag formula before touching the bounds (a discrete lag of 100 steps of τ/100 gives 1 − 0.99¹⁰⁰ = 0.634).

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.Flight/Aero/LiftingLine.cs tests/SimLab.Flight.Tests/Aero/LiftingLineTests.cs
git commit -m "feat(aero): Weissinger lifting line with section polars and a lagged tail downwash

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Use the lifting line in `SurfaceAeroModel`; remove the old induced flow and `oswald`

**Files:**
- Modify: `src/SimLab.Flight/Aero/SurfaceAeroModel.cs` (whole file below)
- Modify: `src/SimLab.Flight/Aero/InducedFlow.cs`, `SurfaceSegment.cs`, `SurfaceGeometry.cs`, `SurfaceSpec.cs`
- Modify: `src/SimLab.Flight/Airframe/LoaderDtos.cs`, `AircraftLoader.cs`
- Modify: `tests/SimLab.Flight.Tests/Behavior/StaticStability.cs`
- Create: `tests/SimLab.Flight.Tests/StabilityDerivatives.cs`
- Create: `tests/SimLab.Flight.Tests/Aero/LiftingLineValidationTests.cs`
- Modify: `tests/SimLab.Flight.Tests/Aero/SurfaceAeroModelTests.cs`, `InducedFlowTests.cs`, `tests/SimLab.Flight.Tests/Airframe/AircraftLoaderTests.cs` (the `oswald` test)

**Interfaces:**
- Consumes: Task 2 (`LiftingLine`, `StripState`).
- Produces: `SurfaceAeroModel.TailDownwash` (double, rad), `SurfaceAeroModel.Line` (`LiftingLine`); `StabilityDerivatives.Reference(double Area, double Span, double Chord)`, `StabilityDerivatives.ReferenceOf(AircraftDefinition)`, `StabilityDerivatives.Evaluate(SurfaceAeroModel, Reference, double airspeed, double alpha, IReadOnlyList<double> deflections, double beta = 0, double pHat = 0, double qHat = 0, double rHat = 0) : double[]` (CL, CD, CY, Cl, Cm, Cn), `StabilityDerivatives.Compute(SurfaceAeroModel, Reference, double airspeed, double alpha, IReadOnlyList<ControlSurfaceSpec> controls) : Dictionary<string, double>` with keys `CL`, `CD`, `CY`, `Cl`, `Cm`, `Cn`, each with suffixes `a`, `b`, `p`, `q`, `r` (per rad / per hat rate) and `_aileron`, `_elevator`, `_rudder` (per degree).
- Behavior tests (`tests/SimLab.Flight.Tests/Behavior/*`) are expected to change with the new physics. This task does not make them pass; it records which fail (step 9). Tasks 5–6 deal with them.

- [ ] **Step 1: Write the validation tests and the derivative utility (they fail: the model still uses the old induced angle)**

`tests/SimLab.Flight.Tests/StabilityDerivatives.cs`:

```csharp
using SimLab.Flight.Aero;
using SimLab.Flight.Airframe;
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Tests;

/// <summary>
/// Stability-axis derivatives of an aero model by central differences, in AVL's conventions: Cl' &gt; 0 right wing down,
/// Cm &gt; 0 nose up, Cn' &gt; 0 nose right; β &gt; 0 wind from the right; rates per p b/2V, q c/2V, r b/2V; controls per
/// degree of channel with each surface deflected by its mix weight. Sea level, far from the ground, downwash lag settled,
/// moments about the model origin (the CG). Same harness as docs/investigations/2026-09-26-avl-comparison/Deriv.cs.
/// </summary>
internal static class StabilityDerivatives
{
    public sealed record Reference(double Area, double Span, double Chord);

    public static readonly string[] Names = ["CL", "CD", "CY", "Cl", "Cm", "Cn"];

    /// <summary>Wing area and span, and the first wing surface's mean aerodynamic chord (AVL's Sref, Bref, Cref from Waxwing).</summary>
    public static Reference ReferenceOf(AircraftDefinition def)
    {
        var wings = def.Surfaces.Where(s => s.Role == SurfaceRole.Wing).ToArray();
        var first = wings[0];
        double taper = first.TipChord / first.RootChord;
        double mac = 2.0 / 3.0 * first.RootChord * (1 + taper + taper * taper) / (1 + taper);
        return new Reference(wings.Sum(s => s.TotalArea), wings.Max(s => s.TotalSpan), mac);
    }

    /// <summary>[CL, CD, CY, Cl, Cm, Cn] at this condition.</summary>
    public static double[] Evaluate(SurfaceAeroModel aero, Reference r, double airspeed, double alpha, IReadOnlyList<double> deflections,
        double beta = 0, double pHat = 0, double qHat = 0, double rHat = 0)
    {
        var air = new Vec3(-airspeed * Math.Cos(alpha) * Math.Cos(beta), airspeed * Math.Sin(beta), -airspeed * Math.Sin(alpha) * Math.Cos(beta));
        double p = pHat * 2 * airspeed / r.Span, q = qHat * 2 * airspeed / r.Chord, rate = rHat * 2 * airspeed / r.Span;
        // Stability-axis rates → body FRD → SimLab body axes (x back, y right, z up).
        double pf = p * Math.Cos(alpha) - rate * Math.Sin(alpha), rf = p * Math.Sin(alpha) + rate * Math.Cos(alpha);
        var ctx = new AeroContext(air, new Vec3(-pf, q, -rf), Isa.SeaLevelDensity, 1000, Vec3.UnitZ, deflections, default);
        aero.Reset();
        for (int i = 0; i < 3; i++)
        {
            aero.Evaluate(ctx);
            aero.Advance(100); // the tail's downwash straight to its steady value
        }
        var load = aero.Evaluate(ctx);
        // SimLab body → FRD → stability axes.
        var f = new Vec3(-load.Force.X, load.Force.Y, -load.Force.Z);
        var m = new Vec3(-load.Moment.X, load.Moment.Y, -load.Moment.Z);
        double ca = Math.Cos(alpha), sa = Math.Sin(alpha);
        double xs = f.X * ca + f.Z * sa, zs = -f.X * sa + f.Z * ca;
        double ls = m.X * ca + m.Z * sa, ns = -m.X * sa + m.Z * ca;
        double qS = 0.5 * Isa.SeaLevelDensity * airspeed * airspeed * r.Area;
        return [-zs / qS, -xs / qS, f.Y / qS, ls / (qS * r.Span), m.Y / (qS * r.Chord), ns / (qS * r.Span)];
    }

    public static Dictionary<string, double> Compute(SurfaceAeroModel aero, Reference r, double airspeed, double alpha,
        IReadOnlyList<ControlSurfaceSpec> controls)
    {
        var zero = new double[controls.Count];
        var d = new Dictionary<string, double>();
        var basis = Evaluate(aero, r, airspeed, alpha, zero);
        for (int k = 0; k < Names.Length; k++) d[Names[k]] = basis[k];

        void Central(string suffix, Func<double, double[]> at, double h)
        {
            var up = at(h);
            var down = at(-h);
            for (int k = 0; k < Names.Length; k++) d[Names[k] + suffix] = (up[k] - down[k]) / (2 * h);
        }
        double da = Angle.Rad(0.5);
        Central("a", h => Evaluate(aero, r, airspeed, alpha + h, zero), da);
        Central("b", h => Evaluate(aero, r, airspeed, alpha, zero, beta: h), da);
        Central("p", h => Evaluate(aero, r, airspeed, alpha, zero, pHat: h), 0.01);
        Central("q", h => Evaluate(aero, r, airspeed, alpha, zero, qHat: h), 0.01);
        Central("r", h => Evaluate(aero, r, airspeed, alpha, zero, rHat: h), 0.01);
        foreach (var channel in new[] { "aileron", "elevator", "rudder" })
        {
            var weights = controls.Select(c => c.Mix.TryGetValue(channel, out var w) ? w : 0).ToArray();
            if (weights.All(w => w == 0)) continue;
            Central("_" + channel, h => Evaluate(aero, r, airspeed, alpha, weights.Select(w => w * Angle.Rad(h)).ToArray()), 1.0);
        }
        return d;
    }
}
```

`tests/SimLab.Flight.Tests/Aero/LiftingLineValidationTests.cs`:

```csharp
using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;
using Xunit.Abstractions;

namespace SimLab.Flight.Tests.Aero;

/// <summary>
/// The lifting line against AVL 3.40 (flat plate, 12 chordwise panels) on simple planforms, with the linear 2π test polar.
/// AVL values from the 2026-09-26 prototype runs (docs/superpowers/specs/2026-09-26-lifting-line-design.md, section 1).
/// </summary>
public class LiftingLineValidationTests(ITestOutputHelper output)
{
    const double Speed = 15;
    static readonly StabilityDerivatives.Reference Ar6 = new(0.375, 1.5, 0.25);
    static readonly SurfaceSpec Rectangular = new("wing", SurfaceRole.Wing, Vec3.Zero, 0.75, 0.25, 0.25, 0, 0, 0, 0, "linear", 12, true);
    static readonly SurfaceSpec Fin = new("fin", SurfaceRole.VerticalTail, new Vec3(0.8, 0, 0.05), 0.2, 0.18, 0.12, 20, 90, 0, 0, "linear", 16, false);

    Dictionary<string, double> Derivatives(StabilityDerivatives.Reference r, params SurfaceSpec[] surfaces)
    {
        var d = StabilityDerivatives.Compute(new SurfaceAeroModel(surfaces, TestAirfoils.Map(), [], []), r, Speed, Angle.Rad(4), []);
        output.WriteLine(string.Join(", ", new[] { "CL", "CD", "CLa", "Clp", "CYb", "Clb", "Cnb" }.Select(k => $"{k} {d[k]:F4}")));
        return d;
    }

    static void Near(double expected, double actual, double fraction, string what) =>
        Assert.True(Math.Abs(actual - expected) <= fraction * Math.Abs(expected),
            $"{what}: {actual:F4}, AVL {expected:F4} (±{fraction:P0})");

    [Fact]
    public void Rectangular_wing_lift_slope_and_roll_damping_match_avl()
    {
        var d = Derivatives(Ar6, Rectangular);
        Near(4.190, d["CLa"], 0.04, "CLα");
        Near(-0.4376, d["Clp"], 0.08, "Clp");
        Near(-0.0372, d["Clb"], 0.10, "Clβ (lift-dependent part, chordwise trailing legs)");
    }

    [Fact]
    public void Lift_slope_converges_with_the_strip_count()
    {
        double coarse = Derivatives(Ar6, Rectangular)["CLa"];
        double fine = Derivatives(Ar6, Rectangular with { Segments = 24 })["CLa"];
        Near(fine, coarse, 0.02, "CLα 12 vs 24 strips");
    }

    [Fact]
    public void Rectangular_wing_span_efficiency_is_close_to_one()
    {
        var d = Derivatives(Ar6, Rectangular);
        double induced = d["CD"] - 0.01; // the test polar's profile drag
        double e = d["CL"] * d["CL"] / (Math.PI * 6 * induced);
        Assert.InRange(e, 0.9, 1.1);
    }

    [Fact]
    public void Dihedral_effect_matches_avl()
    {
        var d = Derivatives(Ar6, Rectangular with { DihedralDeg = 5 });
        Near(-0.1007, d["Clb"], 0.10, "Clβ");
    }

    [Fact]
    public void Fin_behind_a_dihedral_wing_matches_avl_in_sideslip()
    {
        var d = Derivatives(Ar6, Rectangular with { DihedralDeg = 5 }, Fin);
        Near(-0.1579, d["CYb"], 0.10, "CYβ");
        Near(0.0764, d["Cnb"], 0.10, "Cnβ");
        Near(-0.1067, d["Clb"], 0.10, "Clβ");
    }

    [Fact]
    public void Swept_tapered_wing_lift_slope_matches_avl()
    {
        var swept = new SurfaceSpec("wing", SurfaceRole.Wing, new Vec3(0.213, 0, 0), 0.55, 0.30, 0.15, 25, 0, 0, 0, "linear", 8, true);
        var d = Derivatives(new StabilityDerivatives.Reference(0.2475, 1.1, 0.23333), swept);
        Near(3.854, d["CLa"], 0.06, "CLα");
    }
}
```

- [ ] **Step 2: Run the validation tests to verify they fail**

Run: `dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~LiftingLineValidationTests"`
Expected: FAIL (the old model: Clp about −0.6, Clβ about 0 on the flat wing).

- [ ] **Step 3: Remove `Oswald` and `InducedFactor`**

In `src/SimLab.Flight/Aero/SurfaceSpec.cs`, delete the parameter `double Oswald = 0.85` (the record's last parameter becomes `bool Mirror)`); keep `AspectRatio`.

In `src/SimLab.Flight/Aero/SurfaceSegment.cs`, delete:

```csharp
    /// <summary>1 / (π e AR) of the parent surface.</summary>
    public required double InducedFactor { get; init; }
```

In `src/SimLab.Flight/Aero/SurfaceGeometry.cs`: delete the line `double inducedFactor = 1.0 / (Math.PI * spec.Oswald * spec.AspectRatio);`, remove the `inducedFactor` argument from both `Make(...)` calls and the `double inducedFactor` parameter from `Make`, and delete `InducedFactor = inducedFactor,` from the object initializer.

In `src/SimLab.Flight/Aero/InducedFlow.cs`, delete `SolveInducedAngle` and its summary so only `GroundEffectFactor` remains.

In `src/SimLab.Flight/Airframe/LoaderDtos.cs`, replace `public double Oswald { get; set; } = 0.85;` with:

```csharp
    /// <summary>No longer used (the lifting line computes the span efficiency); kept only to reject old files with a clear message.</summary>
    public double? Oswald { get; set; }
```

In `src/SimLab.Flight/Airframe/AircraftLoader.cs`, replace `if (s.Oswald <= 0) throw Invalid(path, $"surface '{s.Name}' oswald must be positive.");` with:

```csharp
            if (s.Oswald is not null)
                throw Invalid(path, $"surface '{s.Name}': oswald is no longer used: the lifting line computes the span efficiency.");
```

and remove `, s.Oswald` from the `new SurfaceSpec(...)` call.

Then: `grep -rn "Oswald\|InducedFactor\|SolveInducedAngle" src tests game --include=*.cs` must only list the DTO property, the loader check and `SurfaceAeroModelTests` (fixed in step 6).

- [ ] **Step 4: Rewrite `SurfaceAeroModel`**

Replace the whole of `src/SimLab.Flight/Aero/SurfaceAeroModel.cs` with:

```csharp
using SimLab.Flight.Atmosphere;
using SimLab.Flight.Dynamics;
using SimLab.Flight.Geometry;

namespace SimLab.Flight.Aero;

/// <summary>
/// Strip-theory aerodynamics on a lifting line: every surface is cut into spanwise strips, each with its own local flow
/// (rotation, prop wash, ground effect) and airfoil polar; the induced flow of all strips on each other (span loading,
/// the wing's downwash on the tail, sidewash on the fin) comes from <see cref="LiftingLine"/>.
/// </summary>
public sealed class SurfaceAeroModel : IAeroModel
{
    const double FlapEfficiency = 0.85;

    /// <summary>Points per strip at which the prop wash is sampled (overlap weighting).</summary>
    const int WashSamples = 8;

    /// <summary>Length of the trailing legs over the chord, quarter chord to trailing edge, as a fraction of the chord.</summary>
    const double ChordwiseLegFraction = 0.75;

    readonly SurfaceSegment[] _segments;
    readonly bool[] _inWash;
    readonly Vec3[] _washAir;
    PropWash _cachedWash;
    bool _washValid;
    readonly BodySpec[] _bodies;
    readonly List<ControlSurfaceSpec> _assigned = [];
    readonly LiftingLine _line;
    readonly StripState[] _states;
    readonly double[] _meanSquare;
    double _lastAirspeed;

    public SurfaceAeroModel(
        IEnumerable<SurfaceSpec> surfaces,
        IReadOnlyDictionary<string, Airfoil> airfoils,
        IReadOnlyList<ControlSurfaceSpec> controls,
        IEnumerable<BodySpec> bodies)
    {
        var specs = surfaces.ToArray();
        _segments = specs.SelectMany(s => SurfaceGeometry.Build(s, Lookup(airfoils, s))).ToArray();
        _inWash = new bool[_segments.Length];
        _washAir = new Vec3[_segments.Length * WashSamples];
        _bodies = bodies.ToArray();

        var wings = specs.Where(s => s.Role == SurfaceRole.Wing).ToArray();
        WingSpan = wings.Length > 0 ? wings.Max(s => s.TotalSpan) : 0;
        WingArea = wings.Sum(s => s.TotalArea);

        for (int i = 0; i < controls.Count; i++) AssignControl(controls[i], i);

        _line = new LiftingLine(_segments);
        _states = new StripState[_segments.Length];
        _meanSquare = new double[_segments.Length];
    }

    public IReadOnlyList<SurfaceSegment> Segments => _segments;
    public double WingSpan { get; }
    public double WingArea { get; }

    /// <summary>The lifting line (read it; <see cref="Evaluate"/>, <see cref="Advance"/> and <see cref="Reset"/> drive it).</summary>
    public LiftingLine Line => _line;

    /// <summary>
    /// Mean reduction of the horizontal-tail strips' angle of attack by the induced flow in the last <see cref="Evaluate"/>
    /// (rad; 0 without a horizontal tail). For tests and diagnostics.
    /// </summary>
    public double TailDownwash { get; private set; }

    public BodyLoad Evaluate(in AeroContext ctx)
    {
        var force = Vec3.Zero;
        var moment = Vec3.Zero;

        bool blown = ctx.Wash.IsActive;
        if (blown) UpdateWashSamples(ctx.Wash);

        for (int i = 0; i < _segments.Length; i++)
        {
            var seg = _segments[i];
            // The rotation is taken at the three-quarter-chord point (Pistolesi): a section pitching about its quarter
            // chord lifts as if at the angle of attack seen there. It is also the lifting line's control point.
            var u = ctx.AirVelocityBody + Vec3.Cross(ctx.AngularVelocityBody, LiftingLine.ControlPoint(seg));
            // Mean in-plane speed squared over the strip; differs from v² only where the prop wash varies across it.
            double meanSquare = -1;
            if (blown) u = Blow(i, u, seg.FlowChordAxis, seg.FlowNormalAxis, out meanSquare);
            _meanSquare[i] = meanSquare;
            double height = ctx.HeightAboveGround + Vec3.Dot(seg.Position, ctx.UpBody);
            double flap = Flap(seg, ctx, out _);
            _states[i] = new StripState(u, seg.FlapEffectiveness * flap, InducedFlow.GroundEffectFactor(height, WingSpan));
        }

        _line.Solve(_states, ctx.Density);

        double downwash = 0;
        int tailStrips = 0;
        for (int i = 0; i < _segments.Length; i++)
        {
            var seg = _segments[i];
            var c = seg.FlowChordAxis;
            var n = seg.FlowNormalAxis;
            var u = _states[i].Velocity;

            // Section angle of attack at the control point, with the lifting line's induced flow.
            var uSection = u - _line.InducedAtControlPoint(i);
            double sc = Vec3.Dot(uSection, c), sn = Vec3.Dot(uSection, n);
            double vSection = Math.Sqrt(sc * sc + sn * sn);
            if (vSection < 0.1) continue;
            double alphaSection = Math.Atan2(-sn, sc);
            double reynolds = ctx.Density * vSection * seg.Chord / Isa.DynamicViscosity;
            var coeff = seg.Airfoil.Evaluate(alphaSection + _states[i].AlphaOffset, reynolds);
            if (seg.Role == SurfaceRole.HorizontalTail)
            {
                downwash += Math.Atan2(-Vec3.Dot(u, n), Vec3.Dot(u, c)) - alphaSection;
                tailStrips++;
            }

            // Lift and drag directions and the dynamic pressure from the flow at the bound vortex.
            var uForce = u - _line.InducedAtBoundVortex(i);
            double uc = Vec3.Dot(uForce, c), un = Vec3.Dot(uForce, n);
            double v = Math.Sqrt(uc * uc + un * un);
            if (v < 0.1) continue;
            double q = 0.5 * ctx.Density * Math.Max(v * v, _meanSquare[i]);
            double flap = Flap(seg, ctx, out double delta);
            double sinDelta = Math.Sin(delta);
            double cl = coeff.Cl;
            double cd = coeff.Cd + seg.ControlCoverage * seg.ControlChordFraction * sinDelta * sinDelta;

            var liftDir = (c * -un + n * uc) / v;
            var dragDir = (c * uc + n * un) / -v;
            double qa = q * seg.Area;
            var f = (liftDir * cl + dragDir * cd) * qa;
            force += f;
            // The linear normal wash of a pitching section is a parabolic camber: ΔCm_c/4 = −(π/4)·q c / (2V) (thin airfoil).
            double pitchRate = Vec3.Dot(ctx.AngularVelocityBody, seg.PitchAxis);
            double cm = coeff.Cm + seg.FlapMomentEffectiveness * flap - Math.PI / 4 * pitchRate * seg.Chord / (2 * v);
            moment += Vec3.Cross(seg.Position, f) + seg.PitchAxis * (qa * seg.Chord * cm);
            // The trailing legs over the chord carry the circulation in the local flow: equal and opposite forces at the
            // strip's two ends, a couple. In sideslip it is the lift-dependent part of the dihedral effect.
            double legs = ctx.Density * _line.Circulation(i) * ChordwiseLegFraction * seg.Chord;
            moment += Vec3.Cross(_line.BoundVector(i), Vec3.Cross(-uForce, Vec3.UnitX)) * legs;
        }
        TailDownwash = tailStrips > 0 ? downwash / tailStrips : 0;

        foreach (var body in _bodies)
        {
            var u = ctx.AirVelocityBody + Vec3.Cross(ctx.AngularVelocityBody, body.Position);
            double k = -0.5 * ctx.Density * u.Length;
            var f = new Vec3(u.X * body.CdA.X, u.Y * body.CdA.Y, u.Z * body.CdA.Z) * k;
            force += f;
            moment += Vec3.Cross(body.Position, f);
        }

        _lastAirspeed = ctx.AirVelocityBody.Length;
        return new BodyLoad(force, moment);
    }

    public void Advance(double dt) => _line.Advance(dt, _lastAirspeed);

    public void Reset()
    {
        _line.Reset();
        _lastAirspeed = 0;
        TailDownwash = 0;
    }

    /// <summary>Effective flap angle of a strip (rad; 0 without a control) and its raw deflection.</summary>
    static double Flap(SurfaceSegment seg, in AeroContext ctx, out double delta)
    {
        delta = seg.ControlIndex >= 0 ? ctx.Deflections[seg.ControlIndex] : 0;
        // Plain flaps lose effectiveness at large deflections as the flow separates on the flap.
        return delta == 0 ? 0 : seg.ControlCoverage * FlapEfficiency * SurfaceGeometry.LargeDeflectionFactor(delta, seg.ControlChordFraction) * delta;
    }
```

…followed, unchanged, by the existing members `Blow`, `UpdateWashSamples`, `AssignControl` and `Lookup` (copy them verbatim from the current file, from `/// <summary>` of `Blow` to the end of the class). Delete `DownwashGain`, `MaxDownwash`, `_wingAspectRatio`, `_tailArm`, `_lastWingCl`, `Downwash`.

- [ ] **Step 5: `StaticStability` settles the downwash lag**

In `tests/SimLab.Flight.Tests/Behavior/StaticStability.cs`, replace the class summary's sentences from "Tailless aircraft only:" to the end of that paragraph with "The lifting line's downwash lag is settled before each load evaluation, so tailed aircraft are valid too.", and in `Loads` replace

```csharp
        aircraft.Aero.Reset();
        var load = aircraft.Aero.Evaluate(new AeroContext(air, Vec3.Zero, Isa.SeaLevelDensity, FarFromGround, up, deflections, default));
```

with

```csharp
        var ctx = new AeroContext(air, Vec3.Zero, Isa.SeaLevelDensity, FarFromGround, up, deflections, default);
        aircraft.Aero.Reset();
        for (int i = 0; i < 3; i++)
        {
            aircraft.Aero.Evaluate(ctx);
            aircraft.Aero.Advance(100); // the tail's downwash straight to its steady value
        }
        var load = aircraft.Aero.Evaluate(ctx);
```

- [ ] **Step 6: Adapt the existing unit tests that encoded the old model**

In `tests/SimLab.Flight.Tests/Aero/InducedFlowTests.cs`, delete the test `Finite_wing_lift_slope_matches_lifting_line_estimate` (keep the ground-effect test).

In `tests/SimLab.Flight.Tests/Aero/SurfaceAeroModelTests.cs`:

Replace the body of `Lift_matches_finite_wing_estimate` with:

```csharp
        var load = WingOnly().Evaluate(Context(Flow(15, 4)));
        double q = 0.5 * 1.225 * 15 * 15;
        double ar = Wing.AspectRatio;
        // Helmbold's lifting-surface estimate; the lifting line lands a few per cent below it with 6 strips per side.
        double a = 2 * Math.PI * ar / (2 + Math.Sqrt(ar * ar + 4));
        double expectedLift = q * Wing.TotalArea * a * Angle.Rad(4);
        double lift = load.Force.Z * Math.Cos(Angle.Rad(4)) - load.Force.X * Math.Sin(Angle.Rad(4));
        Assert.InRange(lift, 0.93 * expectedLift, 1.03 * expectedLift);
```

Replace the body of `Pitch_rate_about_the_quarter_chord_lifts_like_the_angle_at_the_three_quarter_chord` with:

```csharp
        // Pistolesi: a thin section pitching at q about its quarter chord lifts as if at alpha = q (c/2) / V.
        const double speed = 15, pitchRate = 1;
        var pitching = WingOnly().Evaluate(Context(Flow(speed, 0), omega: new Vec3(0, pitchRate, 0)));
        double equivalent = Math.Atan(pitchRate * (Wing.RootChord / 2) / speed);
        var steady = WingOnly().Evaluate(Context(Flow(speed, Angle.Deg(equivalent))));
        double steadyLift = steady.Force.Z * Math.Cos(equivalent) - steady.Force.X * Math.Sin(equivalent);
        Assert.InRange(pitching.Force.Z, 0.97 * steadyLift, 1.03 * steadyLift);
```

In `Wing_lift_builds_downwash_at_the_tail`, replace `model.Downwash` by `model.TailDownwash` (three places).

In `Swept_elevon_section_moment_adds_no_roll_or_yaw`, insert before `var withoutFlapMoment = model.Evaluate(ctx);`:

```csharp
        // Same starting point for the lifting line's iteration as the first evaluation, so only the flap moment differs.
        model.Reset();
```

In `tests/SimLab.Flight.Tests/Airframe/AircraftLoaderTests.cs`, replace the test `Zero_oswald_factor_is_rejected` with:

```csharp
    [Fact]
    public void Removed_oswald_factor_is_rejected()
    {
        var json = Edit(Aircraft(), "\"segments\": 4,", "\"segments\": 4, \"oswald\": 0.9,");
        var ex = Assert.Throws<InvalidDataException>(() => AircraftLoader.Load(Write(json)));
        Assert.Contains("oswald is no longer used", ex.Message);
    }
```

- [ ] **Step 7: Run the aero and loader tests**

Run: `dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~SimLab.Flight.Tests.Aero|FullyQualifiedName~AircraftLoaderTests|FullyQualifiedName~Numerics"`
Expected: all pass. If a `SurfaceAeroModelTests` test other than the four adapted ones fails, it encodes an old-model magnitude: report it with its message instead of loosening it.

If a `LiftingLineValidationTests` case fails, compare with the spec's prototype table: the prototype is `…/scratchpad/llt/gen.py` of the planning session (not in the repo), so re-derive from the spec's formulas; the usual culprits are the sign of `Horseshoe` legs, `NormalChord`, or using the control-point instead of the bound-vortex induced flow for the force.

- [ ] **Step 8: Build everything**

Run: `dotnet build SimLab.slnx` and `dotnet test tests/SimLab.App.Tests`
Expected: build succeeds without warnings; app tests pass (they do not depend on the aero magnitudes; if one does, report it).

- [ ] **Step 9: Record the behavior tests' state**

Run: `dotnet test tests/SimLab.Flight.Tests --logger "console;verbosity=detailed" > /tmp/lifting-line-task3.txt 2>&1; grep "\[FAIL\]" /tmp/lifting-line-task3.txt`
Expected: some `Behavior` tests fail (new physics). Put the list in the commit message body. No test outside `Behavior` may fail.

- [ ] **Step 10: Commit**

```bash
git add -A src tests
git commit -m "feat(aero): strips on the lifting line; oswald and the scalar downwash removed

Behavior tests failing with the new physics (handled in the re-tuning step):
<paste the [FAIL] list>

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Fleet derivatives against AVL, timing, and the measurement stop

**Files:**
- Create: `tests/SimLab.Flight.Tests/Behavior/AvlDerivativeTests.cs`
- Uses: `tests/SimLab.Flight.Tests/Behavior/Golden/avl-derivatives.json` (committed with this plan: AVL values per aircraft with `airspeed`, `alphaDeg`, `CLa`, `Cma`, `CYb`, `Clb`, `Cnb`, `Clp`, `Cnp`, `Clr`, `Cnr`, `CYr`, `CLq`, `Cmq`, and `Cl_aileron`, `Cn_aileron`, `Cm_elevator`, `CL_elevator`, `Cn_rudder`, `Cl_rudder` where the aircraft has them)
- Modify: `docs/investigations/2026-09-26-avl-comparison/compare_avl.py`

**Interfaces:**
- Consumes: `StabilityDerivatives` (Task 3), `Fleet.Load`, `Fleet.RepoRoot`, `Fleet.Cruise`.

- [ ] **Step 1: Write the tests**

`tests/SimLab.Flight.Tests/Behavior/AvlDerivativeTests.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using SimLab.Flight.Aero;
using SimLab.Flight.Geometry;
using Xunit.Abstractions;

namespace SimLab.Flight.Tests.Behavior;

/// <summary>
/// The fleet's stability derivatives against AVL (docs/superpowers/specs/2026-09-26-lifting-line-design.md, success
/// criteria 1–4 and 6). AVL values: Golden/avl-derivatives.json, regenerated with
/// docs/investigations/2026-09-26-avl-comparison/compare_avl.py --write-fixture when an aircraft's geometry changes.
/// </summary>
public class AvlDerivativeTests(ITestOutputHelper output)
{
    static readonly string FixturePath = Path.Combine(Fleet.RepoRoot, "tests", "SimLab.Flight.Tests", "Behavior", "Golden", "avl-derivatives.json");

    /// <summary>Derivatives held to ±15 % of AVL (criterion 1).</summary>
    static readonly string[] Relative = ["CLa", "CYb", "Cnb", "Clb", "Clp", "Cnr", "Clr", "Cl_aileron", "Cm_elevator", "Cn_rudder"];

    static readonly string[] Reported = ["CLa", "Cma", "CYb", "Clb", "Cnb", "Clp", "Cnp", "Clr", "Cnr", "CYr", "CLq", "Cmq",
        "Cl_aileron", "Cn_aileron", "Cm_elevator", "CL_elevator", "Cn_rudder", "Cl_rudder"];

    static JsonElement Avl(string id) =>
        JsonDocument.Parse(File.ReadAllText(FixturePath)).RootElement.GetProperty("aircraft").GetProperty(id);

    static Dictionary<string, double> Sim(string id, JsonElement avl)
    {
        var def = Fleet.Load(id);
        var aero = new SurfaceAeroModel(def.Surfaces, def.Airfoils, def.Controls, []);
        return StabilityDerivatives.Compute(aero, StabilityDerivatives.ReferenceOf(def), avl.GetProperty("airspeed").GetDouble(),
            Angle.Rad(avl.GetProperty("alphaDeg").GetDouble()), def.Controls);
    }

    static double Spiral(Func<string, double> d) => d("Clb") * d("Cnr") / (d("Clr") * d("Cnb"));

    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    public void Derivatives_match_avl(string id)
    {
        var avl = Avl(id);
        var sim = Sim(id, avl);
        double A(string key) => avl.GetProperty(key).GetDouble();
        var failures = new List<string>();

        foreach (var key in Relative)
        {
            if (!avl.TryGetProperty(key, out var value) || !sim.TryGetValue(key, out double s)) continue;
            double ratio = s / value.GetDouble();
            if (ratio is < 0.85 or > 1.15) failures.Add($"{key} {s:F4} vs AVL {value.GetDouble():F4} (x{ratio:F2})");
        }

        double marginSim = -sim["Cma"] / sim["CLa"], marginAvl = -A("Cma") / A("CLa");
        if (Math.Abs(marginSim - marginAvl) > 0.02) failures.Add($"static margin {marginSim:P1} vs AVL {marginAvl:P1}");

        double spiralSim = Spiral(k => sim[k]), spiralAvl = Spiral(A);
        if ((spiralSim > 1) != (spiralAvl > 1)) failures.Add($"spiral criterion {spiralSim:F2} vs AVL {spiralAvl:F2}");

        if (Math.Sign(sim["Cnp"]) != Math.Sign(A("Cnp")) || Math.Abs(sim["Cnp"] - A("Cnp")) > 0.015)
            failures.Add($"Cnp {sim["Cnp"]:F4} vs AVL {A("Cnp"):F4}");
        if (avl.TryGetProperty("Cn_aileron", out var cnda) && sim.TryGetValue("Cn_aileron", out double cndaSim)
            && Math.Abs(cndaSim - cnda.GetDouble()) > 0.0003)
            failures.Add($"Cn_aileron {cndaSim:F5} vs AVL {cnda.GetDouble():F5}");

        output.WriteLine($"{id}: static margin {marginSim:P1} (AVL {marginAvl:P1}), spiral {spiralSim:F2} (AVL {spiralAvl:F2})");
        Assert.True(failures.Count == 0, $"{id}: " + string.Join("; ", failures));
    }

    [Fact]
    public void Report_ratio_table()
    {
        foreach (var id in new[] { "trainer", "sport", "wing", "3d" })
        {
            var avl = Avl(id);
            var sim = Sim(id, avl);
            output.WriteLine($"== {id} (V {avl.GetProperty("airspeed").GetDouble()} m/s, alpha {avl.GetProperty("alphaDeg").GetDouble():F2} deg)");
            foreach (var key in Reported)
            {
                if (!avl.TryGetProperty(key, out var a) || !sim.TryGetValue(key, out double s)) continue;
                output.WriteLine($"  {key,-12} SimLab {s,10:F4}  AVL {a.GetDouble(),10:F4}  ratio {s / a.GetDouble(),6:F2}");
            }
        }
    }

    /// <summary>
    /// Criterion 6 is 50 µs per evaluation in a Release build
    /// (dotnet test tests/SimLab.Flight.Tests -c Release --filter FullyQualifiedName~Aero_evaluation_is_cheap);
    /// the assertion here is a wide guard against an algorithmic regression in any build.
    /// </summary>
    [Theory]
    [InlineData("trainer")]
    [InlineData("sport")]
    [InlineData("wing")]
    [InlineData("3d")]
    public void Aero_evaluation_is_cheap(string id)
    {
        var def = Fleet.Load(id);
        var aero = new SurfaceAeroModel(def.Surfaces, def.Airfoils, def.Controls, def.Bodies);
        var deflections = new double[def.Controls.Count];
        double speed = Fleet.Cruise(id).Airspeed;
        AeroContext Context(int i)
        {
            double alpha = Angle.Rad(3 + 0.01 * (i % 10));
            return new AeroContext(new Vec3(-speed * Math.Cos(alpha), 0, -speed * Math.Sin(alpha)), new Vec3(0.1, 0.05, 0.02),
                1.225, 50, Vec3.UnitZ, deflections, default);
        }
        for (int i = 0; i < 500; i++) aero.Evaluate(Context(i));
        const int count = 4000;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < count; i++) aero.Evaluate(Context(i));
        double micro = watch.Elapsed.TotalMilliseconds * 1000 / count;
        output.WriteLine($"{id}: {aero.Segments.Count} strips, {micro:F1} µs per evaluation, last solve {aero.Line.LastIterations} iterations, " +
                         $"{aero.Line.NonConvergedSolves} non-converged");
        Assert.True(micro < 200, $"{micro:F1} µs per evaluation");
    }
}
```

- [ ] **Step 2: Run them**

Run: `dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~AvlDerivativeTests" --logger "console;verbosity=detailed" > /tmp/lifting-line-task4.txt 2>&1; grep -E "FAIL|ratio|static margin|µs|Message" /tmp/lifting-line-task4.txt`
Then: `dotnet test tests/SimLab.Flight.Tests -c Release --filter "FullyQualifiedName~Aero_evaluation_is_cheap" --logger "console;verbosity=detailed" | grep "µs"`
Expected: the report and the timings print. `Derivatives_match_avl` may fail for some aircraft at this point (tail strip counts, the swept wing's Clβ); that is what the stop below is for. Do not change tolerances, aircraft data or the fixture.

- [ ] **Step 3: Make `compare_avl.py` able to regenerate the fixture**

Replace `docs/investigations/2026-09-26-avl-comparison/compare_avl.py` with:

```python
"""SimLab vs AVL stability derivatives for the fleet (see ../2026-09-26-avl-comparison.md).

Usage, with the Waxwing venv (it provides waxwing and AVL):
  dotnet build Deriv.csproj -p:TreatWarningsAsErrors=false
  ~/4_WAXWING/.venv/bin/python compare_avl.py bin/Debug/net8.0/Deriv ../../../aircraft [--write-fixture <path>]

The SimLab harness finds the alpha where lift = weight at each aircraft's cruise speed; AVL runs at that alpha.
--write-fixture writes the AVL side as tests/SimLab.Flight.Tests/Behavior/Golden/avl-derivatives.json expects it.
"""
import json, subprocess, sys, tempfile, warnings
warnings.filterwarnings("ignore")
from waxwing.io.simlab_import import definition_from_simlab
from waxwing.analysis.avl_run import run_avl

harness, fleet = sys.argv[1], sys.argv[2]
fixture_path = sys.argv[sys.argv.index("--write-fixture") + 1] if "--write-fixture" in sys.argv else None
speeds = {"trainer": 15, "sport": 18, "wing": 14, "3d": 14}
keys = ["CLa", "Cma", "CYb", "Clb", "Cnb", "Clp", "Cnp", "Clr", "Cnr", "CYr", "CLq", "Cmq"]
fixture = {
    "source": "AVL 3.40b via Waxwing run_avl (definition_from_simlab, no fuselage); "
              "docs/investigations/2026-09-26-avl-comparison/compare_avl.py",
    "conventions": "stability axes; Cl'>0 right wing down, Cm>0 nose up, Cn'>0 nose right; beta>0 wind from the right; "
                   "rates per pb/2V, qc/2V, rb/2V; controls per degree of channel (surface = mix weight x channel)",
    "aircraft": {},
}
for ac, v in speeds.items():
    sim = json.loads(subprocess.check_output([harness, f"{fleet}/{ac}", str(v)]))
    s = sim["surfaces"]
    alpha = s["alphaDeg"]
    with tempfile.TemporaryDirectory() as work:
        avl = run_avl(definition_from_simlab(f"{fleet}/{ac}"), alpha, workdir=work)
    print(f"\n=== {ac}  V {v} m/s  alpha {alpha:.2f} deg")
    print(f"{'':24s}{'SimLab':>10s}{'AVL':>10s}{'ratio':>8s}")
    entry = {"airspeed": v, "alphaDeg": round(alpha, 6)}
    for k in keys:
        entry[k] = avl[k]
        print(f"{k:24s}{s[k]:10.4f}{avl[k]:10.4f}{s[k] / avl[k] if abs(avl[k]) > 1e-9 else float('nan'):8.2f}")
    for channel, coefficients in avl.controls.items():
        for c in ("CL", "Cl", "Cm", "Cn"):
            if c in coefficients:
                entry[f"{c}_{channel}"] = coefficients[c]
                if f"{c}_{channel}" in s:
                    print(f"{c + '_' + channel + '/deg':24s}{s[f'{c}_{channel}']:10.4f}{coefficients[c]:10.4f}")
    entry["CL"] = avl["CLtot"]
    print(f"{'static margin':24s}{-s['Cma'] / s['CLa']:10.4f}{-avl['Cma'] / avl['CLa']:10.4f}")
    fixture["aircraft"][ac] = entry
if fixture_path:
    with open(fixture_path, "w") as f:
        json.dump(fixture, f, indent=2)
    print(f"\nwrote {fixture_path}")
```

Check it runs (from `docs/investigations/2026-09-26-avl-comparison/`): `dotnet build Deriv.csproj -p:TreatWarningsAsErrors=false && ~/4_WAXWING/.venv/bin/python compare_avl.py bin/Debug/net8.0/Deriv ../../../aircraft --write-fixture /tmp/avl-fixture-check.json`. Its AVL column must equal the committed fixture's values for the current data (same geometry, alpha within 0.3° of the fixture's because the SimLab trim changed; the derivatives move by at most a few per cent). Do not overwrite the committed fixture in this task. Delete `bin/` and `obj/` afterwards (`git status` must show only `compare_avl.py` in that folder).

- [ ] **Step 4: Commit**

```bash
git add tests/SimLab.Flight.Tests/Behavior/AvlDerivativeTests.cs docs/investigations/2026-09-26-avl-comparison/compare_avl.py
git commit -m "test(aero): fleet stability derivatives against AVL, evaluation timing

<paste the ratio table's summary: per aircraft, the keys outside ±15 %, margin and spiral>

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: STOP — report to the user (controller)**

Report in French: per aircraft, criteria 1–4 (which derivatives pass, ratios of those that do not), the Release timing, and the behavior tests failing since Task 3. If the flying wing misses criteria 1–3 because of the swept planform (Clβ, margin), say so and ask before any method change. Do not start Task 5 without the user's go.

---

### Task 5: Revert the compensating tunings and propose a re-tuning (controller, with the user)

**Files:**
- Modify: `aircraft/trainer/aircraft.json`, `aircraft/wing/aircraft.json`, `aircraft/3d/aircraft.json`
- Modify: `tests/SimLab.Flight.Tests/Behavior/Golden/avl-derivatives.json` (regenerated)

- [ ] **Step 1: Revert (exact edits; values in the current body-axis datum frame)**

`aircraft/trainer/aircraft.json`: wing `"dihedralDeg": 5` → `3`; hull `wingtipLeft` / `wingtipRight` z `0.185` → `0.159` (0.12 + 0.75·sin 3°).

`aircraft/wing/aircraft.json`: `"cg": [0.31, 0, 0]` → `[0.32, 0, 0]`; wing `"root": [0.213, 0, 0]` → `[0.187, 0, 0]` (the original 0.133 m ahead of the CG in the pre-2026-09-24 frame, with the CG at 0.32); wing `"twistDeg": -6.0` → `-2.0`; winglets `"root": [0.48, 0.55, 0]` → `[0.45, 0.55, 0]` and `"span": 0.135` → `0.12`; hull `finTopLeft` / `finTopRight` `[0.56, ±0.55, 0.135]` → `[0.48, ±0.55, 0.12]`; elevons `"aileron": -0.75` / `0.75` → `-1` / `1`.

`aircraft/3d/aircraft.json`: `"cg": [0.52, 0, 0]` → `[0.50, 0, 0]`; stab `"incidenceDeg": -1.5` → `0`.

Run `dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~FleetDefinitionTests"`: the hull-geometry tests must pass (they check the hull points against the surfaces).

- [ ] **Step 2: Regenerate the AVL fixture**

From `docs/investigations/2026-09-26-avl-comparison/`: `dotnet build Deriv.csproj -p:TreatWarningsAsErrors=false && ~/4_WAXWING/.venv/bin/python compare_avl.py bin/Debug/net8.0/Deriv ../../../aircraft --write-fixture ../../../tests/SimLab.Flight.Tests/Behavior/Golden/avl-derivatives.json`; then remove `bin/` and `obj/`.

- [ ] **Step 3: Run everything and gather the failures**

`dotnet test tests/SimLab.Flight.Tests --logger "console;verbosity=detailed" > /tmp/lifting-line-task5.txt 2>&1`. List the failing tests with their messages, and the `AvlDerivativeTests` results.

- [ ] **Step 4: Build the re-tuning proposal**

For each failing behavior test, find the smallest data change that makes it pass, using only estimated parameters (CG, incidences, throws, mixes, segment counts, surface sizes and positions listed as estimated in `provenance`). Prefer, in order: tail-surface `segments` (resolution, not tuning: fins and stabs up to 16 strips, as the spec's convergence table shows), then parameters from the section 5 revert list, then others. Scan each candidate over a small range and record the scan (value → test metric), like the existing entries in `docs/tuning-log.md`. Do not change test tolerances or test setups.

- [ ] **Step 5: STOP — submit the proposal to the user**

One message in French: the table of proposed changes (aircraft, parameter, value, which test it fixes, the scan), what still fails without them, and the AVL criteria after them. Apply nothing before the user approves.

---

### Task 6: Apply the approved tuning, regenerate the golden file, document

**Files:**
- Modify: `aircraft/*/aircraft.json` (approved changes only), `tests/SimLab.Flight.Tests/Behavior/Golden/avl-derivatives.json` (regenerated if geometry changed), `tests/SimLab.Flight.Tests/Behavior/Golden/frame-invariance.json` (regenerated)
- Modify: `docs/tuning-log.md`, `docs/realism-backlog.md`, `docs/investigations/2026-09-26-avl-comparison.md`

- [ ] **Step 1: Apply the approved changes and regenerate the AVL fixture** (Task 5 step 2 command) if any geometry changed.

- [ ] **Step 2: Regenerate the golden file** (intended physics change): `SIMLAB_WRITE_GOLDEN=1 dotnet test tests/SimLab.Flight.Tests --filter "FullyQualifiedName~FrameInvarianceGolden"`, then run it again without the variable: it must pass.

- [ ] **Step 3: Full test run**

`dotnet test tests/SimLab.Flight.Tests` and `dotnet test tests/SimLab.App.Tests`: all pass (the known skip `Sport_full_aileron_roll_helix_angle_is_realistic` stays skipped unless its reason changed; if the lifting line brings the sport's pb/2V into 0.12–0.18, report it rather than un-skipping silently).

- [ ] **Step 4: Document**

- `docs/tuning-log.md`: one row for the physics change (lifting line, what it replaced, AVL ratios before/after), one row for the section 5 revert, one row per approved re-tuning change with its scan.
- `docs/realism-backlog.md`: update #1 (hands-off bank: new values from `HoverTests.Report_slipstream_effects`), #3 (flying wing margin and stall roll with the reverted data), #5 (spin recovery), #11 (sport pb/2V and the AVL steady-roll comparison), #14 (wing yaw: Cnβ, Cnr, Dutch roll from `WingYawTests.Report_wing_yaw_characteristics`).
- `docs/investigations/2026-09-26-avl-comparison.md`: a section "After the lifting line" with the ratio table from `AvlDerivativeTests.Report_ratio_table`.

- [ ] **Step 5: Commit**

```bash
git add -A aircraft tests docs
git commit -m "feat(aircraft): fleet re-tuned for the lifting line; golden and docs updated

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
