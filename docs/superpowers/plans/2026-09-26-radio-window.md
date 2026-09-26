# Radio Window and Positional Switches Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the pilot assign every position of a radio switch to a state (gear down, flaps landing, "no effect"…), add throttle cut and OSD switches, and rebuild the radio screen as three tabs with live switch feedback.

**Architecture:** A pure `SwitchBoard` in `SimLab.Input` resolves learned switch positions (nearest value + hysteresis) into held states and entry events; a pure `SwitchLearner` detects which switch the pilot flipped and its positions. `RadioProfile` stores `SwitchAssignment`s (old profiles are converted on load). `InputRouter` turns them into `ControlInputs` (gear, flaps, throttle cut) and `FlightCommand`s (reset, pause, wind, camera, OSD). The Godot radio screen is split into a shell plus three tab controls.

**Tech Stack:** C# / .NET 10, xUnit, Godot 4.7 .NET (code-built UI).

**Spec:** `docs/superpowers/specs/2026-09-26-radio-window-design.md`

## Global Constraints

- Code, comments, docs in English; UI strings in `game/translations/strings.csv` with `fr` then `en` columns (quote fields containing commas).
- Work only in the worktree `/Users/axel.ldq/3_SYMLAB/.worktrees/radio-window` (branch `feat/radio-window`); never touch `~/3_SYMLAB` itself.
- `dotnet` may need `export PATH="/usr/local/share/dotnet:$PATH";` first. Game build: `dotnet build game/SimLab.Game.csproj`.
- Tests: `SimLab.Flight.Tests` takes ~14 min and nothing here touches flight physics, so wherever a step says `dotnet test`, run `dotnet test tests/SimLab.Input.Tests && dotnet test tests/SimLab.App.Tests` instead (Task 3 must also build `tests/SimLab.Flight.Tests` with `dotnet build tests/SimLab.Flight.Tests` because `ControlInputs` changes). Only Task 6 runs the full `dotnet test`. Baseline: 705 passing + 1 known skip (`Sport_full_aileron_roll_helix_angle_is_realistic`).
- Godot: `GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot`; if `game/.godot` is missing run `"$GODOT" --headless --path game --import` once.
- Disk is often nearly full: if a command fails with ENOSPC, stop and report.
- Match the surrounding style: file-scoped namespaces, `///` summaries on public types, collection expressions, no `#region`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Switch model and `SwitchBoard`

**Files:**
- Create: `src/SimLab.Input/SwitchModel.cs`
- Create: `src/SimLab.Input/SwitchBoard.cs`
- Test: `tests/SimLab.Input.Tests/SwitchBoardTests.cs`

(The old `src/SimLab.Input/Switches.cs` stays untouched in this task; Task 3 removes it.)

**Interfaces:**
- Produces: `SwitchFunction`, `SwitchSource(int? AxisIndex, int? ButtonIndex)` with `double? Read(RawInputFrame)`, `SwitchPosition(double Value, int? State)`, `SwitchAssignment(SwitchFunction Function, SwitchSource Source, List<SwitchPosition> Positions)`, `SwitchStates` constants + `Names/Count/IsHeld/FunctionKey/StateKey/Defaults/All`, `SwitchEvent(SwitchFunction Function, int State)`, `SwitchBoard` with `Update(RawInputFrame) → IReadOnlyList<SwitchEvent>`, `int? Held(SwitchFunction)`, `int? Position(SwitchFunction)`, `static int Nearest(IReadOnlyList<SwitchPosition>, double value, int? current)`.

- [ ] **Step 1: Write the failing tests**

`tests/SimLab.Input.Tests/SwitchBoardTests.cs`:

```csharp
namespace SimLab.Input.Tests;

public class SwitchBoardTests
{
    static RawInputFrame Frame(double axis, bool button = false) => new([0, 0, 0, 0, axis], [false, button]);

    static SwitchAssignment Axis(SwitchFunction f, params (double Value, int? State)[] positions) =>
        new(f, new SwitchSource(AxisIndex: 4), positions.Select(p => new SwitchPosition(p.Value, p.State)).ToList());

    [Fact]
    public void Nearest_picks_the_closest_learned_position()
    {
        List<SwitchPosition> p = [new(-1, 0), new(0, 1), new(1, 2)];
        Assert.Equal(0, SwitchBoard.Nearest(p, -0.8, null));
        Assert.Equal(1, SwitchBoard.Nearest(p, 0.3, null));
        Assert.Equal(2, SwitchBoard.Nearest(p, 0.9, null));
    }

    [Fact]
    public void Nearest_keeps_the_current_position_until_another_wins_by_the_hysteresis()
    {
        List<SwitchPosition> p = [new(-1, 0), new(0, 1), new(1, 2)];
        Assert.Equal(1, SwitchBoard.Nearest(p, 0.52, 1));
        Assert.Equal(2, SwitchBoard.Nearest(p, 0.6, 1));
    }

    [Fact]
    public void Held_function_reports_the_state_of_the_current_position_and_null_on_no_effect()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Gear, (-1, SwitchStates.GearDown), (0, null), (1, SwitchStates.GearUp))]);
        board.Update(Frame(-1));
        Assert.Equal(SwitchStates.GearDown, board.Held(SwitchFunction.Gear));
        board.Update(Frame(0));
        Assert.Null(board.Held(SwitchFunction.Gear));
        board.Update(Frame(1));
        Assert.Equal(SwitchStates.GearUp, board.Held(SwitchFunction.Gear));
        Assert.Null(board.Held(SwitchFunction.Flaps));
    }

    [Fact]
    public void Held_functions_never_emit_events()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Flaps, (-1, SwitchStates.FlapsUp), (1, SwitchStates.FlapsLanding))]);
        Assert.Empty(board.Update(Frame(-1)));
        Assert.Empty(board.Update(Frame(1)));
    }

    [Fact]
    public void Entry_functions_emit_once_on_entering_a_position_including_the_first_frame()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Camera,
            (-1, SwitchStates.CameraGround), (0, SwitchStates.CameraFpv), (1, SwitchStates.CameraChase))]);
        Assert.Equal(new SwitchEvent(SwitchFunction.Camera, SwitchStates.CameraGround), Assert.Single(board.Update(Frame(-1))));
        Assert.Empty(board.Update(Frame(-1)));
        Assert.Equal(new SwitchEvent(SwitchFunction.Camera, SwitchStates.CameraFpv), Assert.Single(board.Update(Frame(0))));
        Assert.Empty(board.Update(Frame(0.05)));
    }

    [Fact]
    public void Reset_never_fires_on_the_first_frame_but_fires_on_each_later_entry()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Reset, (-1, null), (1, SwitchStates.ResetFire))]);
        Assert.Empty(board.Update(Frame(1)));
        Assert.Empty(board.Update(Frame(-1)));
        Assert.Equal(new SwitchEvent(SwitchFunction.Reset, SwitchStates.ResetFire), Assert.Single(board.Update(Frame(1))));
        Assert.Empty(board.Update(Frame(1)));
    }

    [Fact]
    public void Entering_a_no_effect_position_emits_nothing()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Wind, (-1, null), (1, SwitchStates.WindOn))]);
        Assert.Empty(board.Update(Frame(-1)));
        Assert.Single(board.Update(Frame(1)));
        Assert.Empty(board.Update(Frame(-1)));
    }

    [Fact]
    public void One_switch_can_drive_two_functions()
    {
        var board = new SwitchBoard([
            Axis(SwitchFunction.Gear, (-1, SwitchStates.GearDown), (1, SwitchStates.GearUp)),
            Axis(SwitchFunction.Flaps, (-1, SwitchStates.FlapsLanding), (1, SwitchStates.FlapsUp)),
        ]);
        board.Update(Frame(-1));
        Assert.Equal(SwitchStates.GearDown, board.Held(SwitchFunction.Gear));
        Assert.Equal(SwitchStates.FlapsLanding, board.Held(SwitchFunction.Flaps));
        board.Update(Frame(1));
        Assert.Equal(SwitchStates.GearUp, board.Held(SwitchFunction.Gear));
        Assert.Equal(SwitchStates.FlapsUp, board.Held(SwitchFunction.Flaps));
    }

    [Fact]
    public void A_button_reads_minus_one_released_and_plus_one_pressed()
    {
        var pause = new SwitchAssignment(SwitchFunction.Pause, new SwitchSource(ButtonIndex: 1),
            [new(-1, SwitchStates.PauseRunning), new(1, SwitchStates.PausePaused)]);
        var board = new SwitchBoard([pause]);
        Assert.Equal(new SwitchEvent(SwitchFunction.Pause, SwitchStates.PauseRunning), Assert.Single(board.Update(Frame(0))));
        Assert.Equal(new SwitchEvent(SwitchFunction.Pause, SwitchStates.PausePaused), Assert.Single(board.Update(Frame(0, button: true))));
        Assert.Equal(1, board.Position(SwitchFunction.Pause));
    }

    [Fact]
    public void Out_of_range_sources_are_ignored()
    {
        var board = new SwitchBoard([new SwitchAssignment(SwitchFunction.Reset, new SwitchSource(ButtonIndex: 12),
            [new(-1, null), new(1, SwitchStates.ResetFire)])]);
        Assert.Empty(board.Update(Frame(0)));
        Assert.Null(board.Position(SwitchFunction.Reset));
    }

    [Fact]
    public void Position_is_null_for_an_unassigned_function()
    {
        var board = new SwitchBoard([Axis(SwitchFunction.Gear, (-1, 0), (1, 1))]);
        board.Update(Frame(1));
        Assert.Equal(1, board.Position(SwitchFunction.Gear));
        Assert.Null(board.Position(SwitchFunction.Osd));
    }

    [Theory]
    [InlineData(SwitchFunction.Gear, 2, new[] { 0, 1 })]
    [InlineData(SwitchFunction.Flaps, 2, new[] { 0, 2 })]
    [InlineData(SwitchFunction.Flaps, 3, new[] { 0, 1, 2 })]
    [InlineData(SwitchFunction.Camera, 2, new[] { 0, 1 })]
    [InlineData(SwitchFunction.Camera, 3, new[] { 0, 1, 2 })]
    [InlineData(SwitchFunction.Gear, 3, new[] { 0, -1, 1 })]
    [InlineData(SwitchFunction.Reset, 2, new[] { -1, 0 })]
    [InlineData(SwitchFunction.Reset, 3, new[] { -1, -1, 0 })]
    public void Defaults_suggest_states_in_order_with_minus_one_for_no_effect(SwitchFunction f, int positions, int[] expected)
        => Assert.Equal(expected, SwitchStates.Defaults(f, positions).Select(s => s ?? -1));

    [Fact]
    public void Every_function_has_state_names_and_keys()
    {
        foreach (var f in SwitchStates.All)
        {
            Assert.NotEmpty(SwitchStates.Names(f));
            Assert.StartsWith("SWITCH_", SwitchStates.FunctionKey(f));
        }
        Assert.Equal("SWITCH_THROTTLE_CUT", SwitchStates.FunctionKey(SwitchFunction.ThrottleCut));
        Assert.Equal("SWITCH_THROTTLE_CUT_CUT", SwitchStates.StateKey(SwitchFunction.ThrottleCut, SwitchStates.ThrottleCut));
        Assert.Equal("SWITCH_FLAPS_TAKEOFF", SwitchStates.StateKey(SwitchFunction.Flaps, SwitchStates.FlapsTakeoff));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SimLab.Input.Tests --filter SwitchBoardTests`
Expected: build FAIL (`SwitchBoard`, `SwitchAssignment`… not defined).

- [ ] **Step 3: Implement `src/SimLab.Input/SwitchModel.cs`**

```csharp
namespace SimLab.Input;

/// <summary>
/// What a radio switch can drive. <see cref="Gear"/>, <see cref="Flaps"/> and <see cref="ThrottleCut"/> follow the
/// switch position (held); the others act when the switch enters a position.
/// </summary>
public enum SwitchFunction { Gear, Flaps, ThrottleCut, Camera, Osd, Wind, Pause, Reset }

/// <summary>A physical switch: one axis or one button of the radio.</summary>
public readonly record struct SwitchSource(int? AxisIndex = null, int? ButtonIndex = null)
{
    /// <summary>The raw value: the axis value, or −1 / +1 for a released / pressed button; null when out of range.</summary>
    public double? Read(RawInputFrame frame)
    {
        if (ButtonIndex is int b) return b >= 0 && b < frame.Buttons.Length ? (frame.Buttons[b] ? 1 : -1) : null;
        if (AxisIndex is int a) return a >= 0 && a < frame.Axes.Length ? frame.Axes[a] : null;
        return null;
    }
}

/// <summary>One learned switch position: the raw value it rests at and the state it selects (null: no effect).</summary>
public sealed record SwitchPosition(double Value, int? State);

/// <summary>A function bound to a physical switch, position by position (2 or 3 positions).</summary>
public sealed record SwitchAssignment(SwitchFunction Function, SwitchSource Source, List<SwitchPosition> Positions);

/// <summary>The states each <see cref="SwitchFunction"/> can be put in, as indices, with their translation keys.</summary>
public static class SwitchStates
{
    public const int GearDown = 0, GearUp = 1;
    public const int FlapsUp = 0, FlapsTakeoff = 1, FlapsLanding = 2;
    public const int ThrottleArmed = 0, ThrottleCut = 1;
    public const int CameraGround = 0, CameraFpv = 1, CameraChase = 2;
    public const int OsdOn = 0, OsdOff = 1;
    public const int WindOn = 0, WindOff = 1;
    public const int PauseRunning = 0, PausePaused = 1;
    public const int ResetFire = 0;

    public static readonly SwitchFunction[] All = Enum.GetValues<SwitchFunction>();

    /// <summary>State names in index order (the last part of their translation keys).</summary>
    public static IReadOnlyList<string> Names(SwitchFunction function) => function switch
    {
        SwitchFunction.Gear => ["DOWN", "UP"],
        SwitchFunction.Flaps => ["UP", "TAKEOFF", "LANDING"],
        SwitchFunction.ThrottleCut => ["ARMED", "CUT"],
        SwitchFunction.Camera => ["GROUND", "FPV", "CHASE"],
        SwitchFunction.Osd => ["ON", "OFF"],
        SwitchFunction.Wind => ["ON", "OFF"],
        SwitchFunction.Pause => ["RUNNING", "PAUSED"],
        SwitchFunction.Reset => ["RESET"],
        _ => throw new ArgumentOutOfRangeException(nameof(function)),
    };

    public static int Count(SwitchFunction function) => Names(function).Count;

    public static bool IsHeld(SwitchFunction function) =>
        function is SwitchFunction.Gear or SwitchFunction.Flaps or SwitchFunction.ThrottleCut;

    public static string FunctionKey(SwitchFunction function) => "SWITCH_" + Upper(function);

    public static string StateKey(SwitchFunction function, int state) => $"SWITCH_{Upper(function)}_{Names(function)[state]}";

    static string Upper(SwitchFunction function) =>
        function == SwitchFunction.ThrottleCut ? "THROTTLE_CUT" : function.ToString().ToUpperInvariant();

    /// <summary>Suggested states for a freshly learned switch, lowest raw value first (null: no effect).</summary>
    public static int?[] Defaults(SwitchFunction function, int positions)
    {
        if (function == SwitchFunction.Reset) return positions == 3 ? [null, null, ResetFire] : [null, ResetFire];
        if (function == SwitchFunction.Camera && positions == 2) return [CameraGround, CameraFpv];
        bool three = Count(function) == 3;
        if (positions == 3) return three ? [0, 1, 2] : [0, null, 1];
        return three ? [0, 2] : [0, 1];
    }
}
```

- [ ] **Step 4: Implement `src/SimLab.Input/SwitchBoard.cs`**

```csharp
namespace SimLab.Input;

/// <summary>An entry function (camera, OSD, wind, pause, reset) whose switch just entered a position with a state.</summary>
public readonly record struct SwitchEvent(SwitchFunction Function, int State);

/// <summary>
/// Reads the assigned switches every frame. A switch sits on its learned position nearest to the raw value (with
/// hysteresis, so a noisy 3-position switch never chatters). Held functions (gear, flaps, throttle cut) report the
/// state of that position; the others emit a <see cref="SwitchEvent"/> when the switch enters a position that has a
/// state. The first frame emits those entry events too, so a flight starts as the radio says, except reset.
/// </summary>
public sealed class SwitchBoard
{
    /// <summary>Raw-value margin by which a new position must be nearer than the current one to take over.</summary>
    public const double Hysteresis = 0.1;

    readonly SwitchAssignment[] _assignments;
    readonly int?[] _current;
    readonly Dictionary<SwitchFunction, int> _held = new();
    bool _primed;

    public SwitchBoard(IEnumerable<SwitchAssignment> assignments)
    {
        _assignments = assignments.Where(a => a.Positions.Count > 0).ToArray();
        _current = new int?[_assignments.Length];
    }

    public IReadOnlyList<SwitchEvent> Update(RawInputFrame frame)
    {
        var events = new List<SwitchEvent>();
        _held.Clear();
        for (int i = 0; i < _assignments.Length; i++)
        {
            var assignment = _assignments[i];
            if (assignment.Source.Read(frame) is not double value) continue;
            int position = Nearest(assignment.Positions, value, _current[i]);
            bool entered = _current[i] != position;
            _current[i] = position;
            if (assignment.Positions[position].State is not int state) continue;
            if (SwitchStates.IsHeld(assignment.Function)) _held[assignment.Function] = state;
            else if (entered && (_primed || assignment.Function != SwitchFunction.Reset))
                events.Add(new SwitchEvent(assignment.Function, state));
        }
        _primed = true;
        return events;
    }

    /// <summary>State of a held function this frame; null when unassigned or on a no-effect position.</summary>
    public int? Held(SwitchFunction function) => _held.TryGetValue(function, out var state) ? state : null;

    /// <summary>Index of the position the function's switch is on; null when unassigned or not read yet.</summary>
    public int? Position(SwitchFunction function)
    {
        for (int i = 0; i < _assignments.Length; i++)
            if (_assignments[i].Function == function) return _current[i];
        return null;
    }

    public static int Nearest(IReadOnlyList<SwitchPosition> positions, double value, int? current)
    {
        int best = 0;
        for (int i = 1; i < positions.Count; i++)
            if (Math.Abs(value - positions[i].Value) < Math.Abs(value - positions[best].Value)) best = i;
        if (current is int c && c < positions.Count && c != best
            && Math.Abs(value - positions[c].Value) - Math.Abs(value - positions[best].Value) <= Hysteresis)
            return c;
        return best;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SimLab.Input.Tests --filter SwitchBoardTests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SimLab.Input/SwitchModel.cs src/SimLab.Input/SwitchBoard.cs tests/SimLab.Input.Tests/SwitchBoardTests.cs
git commit -m "feat(input): positional switch model and SwitchBoard"
```

---

### Task 2: `SwitchLearner`

**Files:**
- Create: `src/SimLab.Input/SwitchLearner.cs`
- Test: `tests/SimLab.Input.Tests/SwitchLearnerTests.cs`

**Interfaces:**
- Consumes: `SwitchSource` (Task 1).
- Produces: `LearnedSwitch(SwitchSource Source, IReadOnlyList<double> Positions)` (positions sorted ascending), `SwitchLearner` with `void Feed(RawInputFrame frame, double dt)` and `LearnedSwitch? Result()`.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace SimLab.Input.Tests;

public class SwitchLearnerTests
{
    const double Dt = 0.02;

    static RawInputFrame F(double a0, double a1 = 0, bool b0 = false) => new([a0, a1], [b0]);

    static void Hold(SwitchLearner l, RawInputFrame frame, double seconds)
    {
        for (double t = 0; t < seconds; t += Dt) l.Feed(frame, Dt);
    }

    [Fact]
    public void Finds_a_three_position_axis_switch()
    {
        var l = new SwitchLearner();
        Hold(l, F(-1), 0.3);
        Hold(l, F(0), 0.3);
        Hold(l, F(1), 0.3);
        var r = l.Result()!;
        Assert.Equal(new SwitchSource(AxisIndex: 0), r.Source);
        Assert.Equal([-1.0, 0.0, 1.0], r.Positions);
    }

    [Fact]
    public void Positions_are_sorted_and_a_two_position_switch_gives_two()
    {
        var l = new SwitchLearner();
        Hold(l, F(0, 1), 0.3);
        Hold(l, F(0, -1), 0.3);
        Hold(l, F(0, 1), 0.3);
        var r = l.Result()!;
        Assert.Equal(new SwitchSource(AxisIndex: 1), r.Source);
        Assert.Equal([-1.0, 1.0], r.Positions);
    }

    [Fact]
    public void Passing_quickly_through_the_middle_is_not_a_position()
    {
        var l = new SwitchLearner();
        Hold(l, F(-1), 0.3);
        Hold(l, F(0), 0.1);
        Hold(l, F(1), 0.3);
        Assert.Equal([-1.0, 1.0], l.Result()!.Positions);
    }

    [Fact]
    public void Noise_within_the_band_still_counts_as_resting()
    {
        var l = new SwitchLearner();
        Hold(l, F(-1), 0.3);
        for (int i = 0; i < 20; i++) l.Feed(F(1 - (i % 2) * 0.03), Dt);
        Assert.Equal(2, l.Result()!.Positions.Count);
    }

    [Fact]
    public void Nothing_moved_gives_no_result()
    {
        var l = new SwitchLearner();
        Hold(l, F(0.3, -0.2), 1);
        Assert.Null(l.Result());
        Assert.Null(new SwitchLearner().Result());
    }

    [Fact]
    public void A_button_pressed_and_released_gives_minus_one_and_plus_one()
    {
        var l = new SwitchLearner();
        Hold(l, F(0), 0.1);
        Hold(l, F(0, b0: true), 0.1);
        Hold(l, F(0), 0.1);
        var r = l.Result()!;
        Assert.Equal(new SwitchSource(ButtonIndex: 0), r.Source);
        Assert.Equal([-1.0, 1.0], r.Positions);
    }

    [Fact]
    public void The_widest_travel_wins()
    {
        var l = new SwitchLearner();
        Hold(l, F(0, -1), 0.3);
        Hold(l, F(0.4, 1), 0.3);
        Assert.Equal(new SwitchSource(AxisIndex: 1), l.Result()!.Source);
    }

    [Fact]
    public void An_axis_resting_in_more_than_three_places_is_not_a_switch()
    {
        var l = new SwitchLearner();
        foreach (var v in new[] { -1.0, -0.5, 0, 0.5, 1 }) Hold(l, F(v), 0.3);
        Assert.Null(l.Result());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SimLab.Input.Tests --filter SwitchLearnerTests`
Expected: build FAIL (`SwitchLearner` not defined).

- [ ] **Step 3: Implement `src/SimLab.Input/SwitchLearner.cs`**

```csharp
namespace SimLab.Input;

/// <summary>The switch found by <see cref="SwitchLearner"/> and the raw values of its positions, lowest first.</summary>
public sealed record LearnedSwitch(SwitchSource Source, IReadOnlyList<double> Positions);

/// <summary>
/// "Move the switch through all its positions": watches every axis and button and finds the one the pilot flipped.
/// An axis position is a value held within <see cref="StableBand"/> for <see cref="StableSeconds"/> (the value on the
/// first frame counts at once, the switch was resting there); positions closer than <see cref="MergeDistance"/> are
/// one. The switch is the source with the widest travel among axes with 2 or 3 positions and buttons that were both
/// pressed and released (travel 2, positions −1 and +1).
/// </summary>
public sealed class SwitchLearner
{
    public const double StableBand = 0.05;
    public const double StableSeconds = 0.25;
    public const double MergeDistance = 0.2;

    double[] _anchor = [], _stable = [], _min = [], _max = [];
    List<double>[] _plateaus = [];
    bool[] _pressed = [], _released = [];
    bool _started;

    public void Feed(RawInputFrame frame, double dt)
    {
        if (!_started)
        {
            _started = true;
            _anchor = (double[])frame.Axes.Clone();
            _min = (double[])frame.Axes.Clone();
            _max = (double[])frame.Axes.Clone();
            _stable = new double[frame.Axes.Length];
            _plateaus = frame.Axes.Select(v => new List<double> { v }).ToArray();
            _pressed = (bool[])frame.Buttons.Clone();
            _released = frame.Buttons.Select(b => !b).ToArray();
            return;
        }

        for (int i = 0; i < Math.Min(frame.Axes.Length, _anchor.Length); i++)
        {
            double v = frame.Axes[i];
            _min[i] = Math.Min(_min[i], v);
            _max[i] = Math.Max(_max[i], v);
            if (Math.Abs(v - _anchor[i]) <= StableBand)
            {
                _stable[i] += dt;
                if (_stable[i] >= StableSeconds) AddPlateau(_plateaus[i], _anchor[i]);
            }
            else
            {
                _anchor[i] = v;
                _stable[i] = 0;
            }
        }

        for (int i = 0; i < Math.Min(frame.Buttons.Length, _pressed.Length); i++)
        {
            if (frame.Buttons[i]) _pressed[i] = true;
            else _released[i] = true;
        }
    }

    public LearnedSwitch? Result()
    {
        LearnedSwitch? best = null;
        double widest = 0;
        for (int i = 0; i < _plateaus.Length; i++)
        {
            if (_plateaus[i].Count is < 2 or > 3) continue;
            double travel = _max[i] - _min[i];
            if (travel <= widest) continue;
            widest = travel;
            best = new LearnedSwitch(new SwitchSource(AxisIndex: i), _plateaus[i].Order().ToArray());
        }
        for (int i = 0; i < _pressed.Length; i++)
        {
            if (!(_pressed[i] && _released[i]) || 2 <= widest) continue;
            widest = 2;
            best = new LearnedSwitch(new SwitchSource(ButtonIndex: i), [-1.0, 1.0]);
        }
        return best;
    }

    static void AddPlateau(List<double> plateaus, double value)
    {
        foreach (var p in plateaus)
            if (Math.Abs(p - value) < MergeDistance) return;
        plateaus.Add(value);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SimLab.Input.Tests --filter SwitchLearnerTests`
Expected: all PASS. (If `A_button_pressed_and_released…` fails because an axis also qualifies, it must not: the axes never move in that test.)

- [ ] **Step 5: Commit**

```bash
git add src/SimLab.Input/SwitchLearner.cs tests/SimLab.Input.Tests/SwitchLearnerTests.cs
git commit -m "feat(input): SwitchLearner finds the flipped switch and its positions"
```

---

### Task 3: Profiles, migration, routing and flight commands (cutover)

Replaces the old `SwitchAction`/`SwitchBinding`/`SwitchTracker`/`SwitchCapture` everywhere. Throttle cut becomes a real control.

**Files:**
- Create: `src/SimLab.Input/LegacySwitches.cs`
- Modify: `src/SimLab.Input/RadioProfile.cs`
- Delete: `src/SimLab.Input/Switches.cs`, `src/SimLab.App/Session/SwitchCapture.cs`, `tests/SimLab.Input.Tests/SwitchTrackerTests.cs`
- Create: `src/SimLab.App/Session/FlightCommand.cs`
- Modify: `src/SimLab.App/Session/InputRouter.cs`
- Modify: `src/SimLab.Flight/Controls/ControlInputs.cs`
- Modify: `src/SimLab.App/Session/FlightSession.cs:81-92` (`Handle`)
- Modify: `game/Scripts/Flight/FlightScene.cs` (command loop, `SetHud`)
- Modify: `game/Scripts/Radio/RadioScreen.cs` (remove the old bind buttons so the game compiles; Task 5 rebuilds the screen)
- Tests: `tests/SimLab.Input.Tests/RadioProfileTests.cs`, `tests/SimLab.App.Tests/Session/InputTests.cs`, `tests/SimLab.App.Tests/Session/FlightSessionTests.cs`, `tests/SimLab.App.Tests/Settings/SettingsTests.cs`, `tests/SimLab.App.Tests/Audio/AircraftSoundTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces:
  - `RadioProfile.Switches : List<SwitchAssignment>`, `RadioProfile.SetSwitch(SwitchAssignment)`, `RadioProfile.ClearSwitch(SwitchFunction)`.
  - `ControlInputs.ThrottleCut { get; init; }` (bool).
  - `FlightCommandKind { Reset, TogglePause, SetPause, ToggleWind, SetWind, NextCamera, SelectCamera, SetOsd }`, `FlightCommand(FlightCommandKind Kind, bool On = false, CameraView View = CameraView.Ground)`, `static FlightCommand? FlightCommand.FromSwitch(SwitchEvent)`.
  - `RouterOutput(ControlInputs Controls, IReadOnlyList<FlightCommand> Commands, InputSource Source, string DeviceName, RawInputFrame? RawFrame = null)`.
  - `InputRouter.Switches : SwitchBoard?` (board of the active radio).
  - `FlapSetting.FromState(int state)`.
  - `FlightSession.Handle(FlightCommand)`; `FlightScene.SetHud(bool)`.

- [ ] **Step 1: Write the failing profile tests**

In `tests/SimLab.Input.Tests/RadioProfileTests.cs`, replace the existing round-trip test's switch line `Switches = { new SwitchBinding(SwitchAction.Reset, ButtonIndex: 3) },` and its assertion `Assert.Equal(p.Switches[0], back.Switches[0]);` with the new shape, and add the tests below (keep the other existing tests):

```csharp
    static SwitchAssignment Gear(int axis = 5) => new(SwitchFunction.Gear, new SwitchSource(AxisIndex: axis),
        [new(-1, SwitchStates.GearDown), new(0, null), new(1, SwitchStates.GearUp)]);

    static string WithSwitches(string switches) =>
        $$"""{ "DeviceGuid": "g", "DeviceName": "n", "Channels": {}, "Switches": {{switches}} }""";

    [Fact]
    public void Switch_assignments_round_trip()
    {
        var p = new RadioProfile { DeviceGuid = "g", Switches = { Gear() } };
        var back = RadioProfile.FromJson(p.ToJson()).Switches.Single();
        Assert.Equal(SwitchFunction.Gear, back.Function);
        Assert.Equal(new SwitchSource(AxisIndex: 5), back.Source);
        Assert.Equal(Gear().Positions, back.Positions);
    }

    [Fact]
    public void Set_switch_replaces_the_function_and_clear_removes_it()
    {
        var p = new RadioProfile();
        p.SetSwitch(Gear(5));
        p.SetSwitch(Gear(6));
        Assert.Equal(6, p.Switches.Single().Source.AxisIndex);
        p.ClearSwitch(SwitchFunction.Gear);
        Assert.Empty(p.Switches);
    }

    [Theory]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] }, { "Function": "Gear", "Source": { "AxisIndex": 2 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 2 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": 1, "ButtonIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    [InlineData("""[ { "Function": "Gear", "Source": { "AxisIndex": -1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    [InlineData("""[ { "Function": "Warp", "Source": { "AxisIndex": 1 }, "Positions": [ { "Value": -1, "State": 0 }, { "Value": 1, "State": 1 } ] } ]""")]
    public void Invalid_switch_assignments_are_rejected(string switches)
        => Assert.Throws<InvalidDataException>(() => RadioProfile.FromJson(WithSwitches(switches)));

    [Fact]
    public void Old_gear_axis_binding_becomes_down_below_and_up_above_the_threshold()
    {
        var s = RadioProfile.FromJson(WithSwitches("""[ { "Action": "GearUp", "AxisIndex": 4, "Threshold": 0.2 } ]""")).Switches.Single();
        Assert.Equal(SwitchFunction.Gear, s.Function);
        Assert.Equal(new SwitchSource(AxisIndex: 4), s.Source);
        Assert.Equal([new SwitchPosition(-0.3, SwitchStates.GearDown), new SwitchPosition(0.7, SwitchStates.GearUp)], s.Positions);
    }

    [Fact]
    public void Old_flap_axis_binding_becomes_three_positions_and_a_flap_button_two()
    {
        var axis = RadioProfile.FromJson(WithSwitches("""[ { "Action": "Flaps", "AxisIndex": 4, "Threshold": 0 } ]""")).Switches.Single();
        Assert.Equal([new SwitchPosition(-1, SwitchStates.FlapsUp), new SwitchPosition(0, SwitchStates.FlapsTakeoff),
            new SwitchPosition(1, SwitchStates.FlapsLanding)], axis.Positions);
        var button = RadioProfile.FromJson(WithSwitches("""[ { "Action": "Flaps", "ButtonIndex": 2 } ]""")).Switches.Single();
        Assert.Equal(new SwitchSource(ButtonIndex: 2), button.Source);
        Assert.Equal([new SwitchPosition(-1, SwitchStates.FlapsUp), new SwitchPosition(1, SwitchStates.FlapsLanding)], button.Positions);
    }

    [Fact]
    public void Old_reset_pause_and_wind_bindings_fire_or_turn_on_in_the_high_position_and_next_camera_is_dropped()
    {
        var profile = RadioProfile.FromJson(WithSwitches("""
            [ { "Action": "Reset", "ButtonIndex": 3 },
              { "Action": "Pause", "ButtonIndex": 4 },
              { "Action": "ToggleWind", "AxisIndex": 6, "Threshold": 0 },
              { "Action": "NextCamera", "ButtonIndex": 5 } ]
            """));
        Assert.Equal([SwitchFunction.Reset, SwitchFunction.Pause, SwitchFunction.Wind], profile.Switches.Select(s => s.Function));
        Assert.Equal([new SwitchPosition(-1, null), new SwitchPosition(1, SwitchStates.ResetFire)], profile.Switches[0].Positions);
        Assert.Equal([new SwitchPosition(-1, SwitchStates.PauseRunning), new SwitchPosition(1, SwitchStates.PausePaused)], profile.Switches[1].Positions);
        Assert.Equal([new SwitchPosition(-0.5, SwitchStates.WindOff), new SwitchPosition(0.5, SwitchStates.WindOn)], profile.Switches[2].Positions);
    }

    [Fact]
    public void A_converted_profile_saves_in_the_new_shape()
    {
        var json = RadioProfile.FromJson(WithSwitches("""[ { "Action": "GearUp", "ButtonIndex": 1 } ]""")).ToJson();
        Assert.DoesNotContain("\"Action\"", json);
        Assert.Contains("\"Positions\"", json);
    }
```

(`Assert.Equal` on `List<SwitchPosition>` compares element-wise; `SwitchPosition` is a record, so values compare by value. If the `-0.3`/`0.7` doubles differ in the last bit, compare with `Assert.Equal(expected, actual.Value, 12)` per element instead.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/SimLab.Input.Tests --filter RadioProfileTests`
Expected: build FAIL (`SetSwitch`, `SwitchAssignment` list type mismatch).

- [ ] **Step 3: Implement the profile side**

Delete `src/SimLab.Input/Switches.cs` and `tests/SimLab.Input.Tests/SwitchTrackerTests.cs` (`git rm`).

Create `src/SimLab.Input/LegacySwitches.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimLab.Input;

/// <summary>
/// Reads the switch bindings saved before 2026-09-26 (one action per switch, "on" above a threshold) as positional
/// assignments. The old "next camera" switch has no positional equivalent and is dropped.
/// </summary>
static class LegacySwitches
{
    internal enum LegacyAction { Reset, Pause, ToggleWind, NextCamera, GearUp, Flaps }

    internal sealed record LegacyBinding(LegacyAction Action, int? ButtonIndex = null, int? AxisIndex = null, double Threshold = 0.5);

    public static bool IsLegacy(JsonArray switches) => switches.Any(s => s is JsonObject o && o.ContainsKey("Action"));

    public static List<SwitchAssignment> Convert(JsonArray switches, JsonSerializerOptions options)
    {
        var result = new List<SwitchAssignment>();
        foreach (var b in switches.Deserialize<List<LegacyBinding>>(options) ?? [])
        {
            var source = new SwitchSource(b.AxisIndex, b.ButtonIndex);
            bool button = b.ButtonIndex is not null;
            double low = button ? -1 : b.Threshold - 0.5;
            double high = button ? 1 : b.Threshold + 0.5;
            List<SwitchPosition> LowHigh(int? lowState, int? highState) => [new(low, lowState), new(high, highState)];
            SwitchAssignment? converted = b.Action switch
            {
                LegacyAction.GearUp => new(SwitchFunction.Gear, source, LowHigh(SwitchStates.GearDown, SwitchStates.GearUp)),
                LegacyAction.Flaps => new(SwitchFunction.Flaps, source, button
                    ? [new(-1, SwitchStates.FlapsUp), new(1, SwitchStates.FlapsLanding)]
                    : [new(-1, SwitchStates.FlapsUp), new(0, SwitchStates.FlapsTakeoff), new(1, SwitchStates.FlapsLanding)]),
                LegacyAction.Pause => new(SwitchFunction.Pause, source, LowHigh(SwitchStates.PauseRunning, SwitchStates.PausePaused)),
                LegacyAction.ToggleWind => new(SwitchFunction.Wind, source, LowHigh(SwitchStates.WindOff, SwitchStates.WindOn)),
                LegacyAction.Reset => new(SwitchFunction.Reset, source, LowHigh(null, SwitchStates.ResetFire)),
                _ => null,
            };
            if (converted is not null) result.Add(converted);
        }
        return result;
    }
}
```

In `src/SimLab.Input/RadioProfile.cs`:
- add `using System.Text.Json.Nodes;`
- change `public List<SwitchBinding> Switches { get; set; } = [];` to `public List<SwitchAssignment> Switches { get; set; } = [];`
- add after `SetReversed`:

```csharp
    /// <summary>Assigns a switch to its function, replacing any earlier assignment of that function.</summary>
    public void SetSwitch(SwitchAssignment assignment)
    {
        ClearSwitch(assignment.Function);
        Switches.Add(assignment);
    }

    public void ClearSwitch(SwitchFunction function) => Switches.RemoveAll(s => s.Function == function);
```

- in `FromJson`, replace the `try` body with:

```csharp
            var node = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Empty radio profile.");
            if (node["Switches"] is JsonArray switches && LegacySwitches.IsLegacy(switches))
                node["Switches"] = JsonSerializer.SerializeToNode(LegacySwitches.Convert(switches, Options), Options);
            profile = node.Deserialize<RadioProfile>(Options) ?? throw new InvalidDataException("Empty radio profile.");
```

- before `return profile;` add:

```csharp
        var assigned = new HashSet<SwitchFunction>();
        foreach (var s in profile.Switches)
        {
            if (s is null) throw new InvalidDataException("Empty switch assignment.");
            if (!assigned.Add(s.Function)) throw new InvalidDataException($"Switch {s.Function} is assigned twice.");
            if ((s.Source.AxisIndex is null) == (s.Source.ButtonIndex is null))
                throw new InvalidDataException($"Switch {s.Function} needs exactly one axis or one button.");
            if (s.Source.AxisIndex < 0 || s.Source.ButtonIndex < 0) throw new InvalidDataException($"Switch {s.Function}: negative index.");
            if (s.Positions is null || s.Positions.Count is < 2 or > 3)
                throw new InvalidDataException($"Switch {s.Function} needs 2 or 3 positions.");
            foreach (var p in s.Positions)
            {
                if (p is null) throw new InvalidDataException($"Switch {s.Function} has an empty position.");
                if (p.State is int state && (state < 0 || state >= SwitchStates.Count(s.Function)))
                    throw new InvalidDataException($"Switch {s.Function}: unknown state {state}.");
            }
        }
```

(`JsonNode.Parse` throws `JsonException` on malformed JSON, already caught and wrapped. An unknown enum name like `"Warp"` throws `JsonException` in `Deserialize`, also wrapped.)

- [ ] **Step 4: Run the profile tests**

Run: `dotnet test tests/SimLab.Input.Tests`
Expected: all PASS.

- [ ] **Step 5: Write the failing routing tests**

In `tests/SimLab.App.Tests/Session/InputTests.cs`:
- `Profile(params SwitchBinding[] switches)` → `Profile(params SwitchAssignment[] switches)`.
- Delete `Switch_capture_binds_a_new_button_or_a_rising_axis`, `Switch_capture_ignores_an_axis_moving_down`, `A_radio_flap_switch_sets_up_half_or_landing_by_position`, `A_flap_button_gives_landing_flaps_while_held`, `A_radio_gear_switch_sets_the_gear_by_its_position_otherwise_the_g_key_does`, `Radio_switches_and_keyboard_commands_fire_once_per_press`.
- In the remaining tests replace `.Actions` with `.Commands`, `SwitchAction.NextCamera` with `new FlightCommand(FlightCommandKind.NextCamera)`, and `Assert.Contains(SwitchAction.Reset, output.Actions)` with `Assert.Contains(new FlightCommand(FlightCommandKind.Reset), output.Commands)`.
- Add `using SimLab.App.Cameras;` and these tests:

```csharp
    static SwitchAssignment OnAxis4(SwitchFunction f, params (double Value, int? State)[] positions) =>
        new(f, new SwitchSource(AxisIndex: 4), positions.Select(p => new SwitchPosition(p.Value, p.State)).ToList());

    static JoypadSnapshot Radio(double axis4, bool button1 = false) =>
        Pad("radio-1", [0, 0, -1, 0, axis4], [false, button1, false, false]);

    [Fact]
    public void Keyboard_commands_fire_once_per_press()
    {
        var router = new InputRouter(_ => null);
        var pause = new KeyboardCommands(false, true, false);
        Assert.Equal(new FlightCommand(FlightCommandKind.TogglePause), Assert.Single(router.Update(0.016, [], default, pause).Commands));
        Assert.Empty(router.Update(0.016, [], default, pause).Commands);
        var wind = new KeyboardCommands(false, false, true);
        Assert.Equal(new FlightCommand(FlightCommandKind.ToggleWind), Assert.Single(router.Update(0.016, [], default, wind).Commands));
    }

    [Fact]
    public void A_reset_button_fires_once_per_press_but_not_at_startup()
    {
        var reset = new SwitchAssignment(SwitchFunction.Reset, new SwitchSource(ButtonIndex: 1), [new(-1, null), new(1, SwitchStates.ResetFire)]);
        var router = new InputRouter(_ => Profile(reset));
        Assert.Empty(router.Update(0.016, [Radio(0, button1: true)], default, default).Commands);
        Assert.Empty(router.Update(0.016, [Radio(0)], default, default).Commands);
        Assert.Equal(new FlightCommand(FlightCommandKind.Reset), Assert.Single(router.Update(0.016, [Radio(0, button1: true)], default, default).Commands));
        Assert.Empty(router.Update(0.016, [Radio(0, button1: true)], default, default).Commands);
    }

    [Fact]
    public void A_gear_switch_holds_the_gear_and_the_g_key_only_acts_on_a_no_effect_position()
    {
        var gear = OnAxis4(SwitchFunction.Gear, (-1, SwitchStates.GearDown), (0, null), (1, SwitchStates.GearUp));
        var router = new InputRouter(_ => Profile(gear));
        var g = new KeyboardCommands(false, false, false, ToggleGear: true);
        Assert.False(router.Update(0.016, [Radio(-1)], default, g).Controls.GearUp);
        Assert.True(router.Update(0.016, [Radio(1)], default, default).Controls.GearUp);
        Assert.True(router.Update(0.016, [Radio(0)], default, default).Controls.GearUp);
        Assert.False(router.Update(0.016, [Radio(0)], default, g).Controls.GearUp);
    }

    [Fact]
    public void A_radio_without_a_gear_switch_leaves_the_gear_to_the_g_key()
    {
        var router = new InputRouter(_ => Profile());
        var g = new KeyboardCommands(false, false, false, ToggleGear: true);
        Assert.True(router.Update(0.016, [Radio(1)], default, g).Controls.GearUp);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(-0.6, 0.0)]
    [InlineData(0.0, FlapSetting.Half)]
    [InlineData(0.2, FlapSetting.Half)]
    [InlineData(1.0, FlapSetting.Landing)]
    public void A_three_position_flap_switch_gives_up_takeoff_or_landing(double axis, double flap)
    {
        var flaps = OnAxis4(SwitchFunction.Flaps, (-1, SwitchStates.FlapsUp), (0, SwitchStates.FlapsTakeoff), (1, SwitchStates.FlapsLanding));
        var router = new InputRouter(_ => Profile(flaps));
        Assert.Equal(flap, router.Update(0.016, [Radio(axis)], default, default).Controls.Flap);
    }

    [Fact]
    public void A_reversed_flap_switch_follows_its_learned_states()
    {
        var flaps = OnAxis4(SwitchFunction.Flaps, (-1, SwitchStates.FlapsLanding), (1, SwitchStates.FlapsUp));
        var router = new InputRouter(_ => Profile(flaps));
        Assert.Equal(FlapSetting.Landing, router.Update(0.016, [Radio(-1)], default, default).Controls.Flap);
        Assert.Equal(0, router.Update(0.016, [Radio(1)], default, default).Controls.Flap);
    }

    [Fact]
    public void Throttle_cut_forces_the_throttle_to_zero_and_flags_it()
    {
        var cut = OnAxis4(SwitchFunction.ThrottleCut, (-1, SwitchStates.ThrottleArmed), (1, SwitchStates.ThrottleCut));
        var router = new InputRouter(_ => Profile(cut));
        var full = Pad("radio-1", [0, 0, 1, 0, 1]);
        var output = router.Update(0.016, [full], default, default);
        Assert.Equal(0, output.Controls.Throttle);
        Assert.True(output.Controls.ThrottleCut);
        output = router.Update(0.016, [Pad("radio-1", [0, 0, 1, 0, -1])], default, default);
        Assert.Equal(1, output.Controls.Throttle, 9);
        Assert.False(output.Controls.ThrottleCut);
    }

    [Fact]
    public void A_new_flight_arms_the_throttle_again_unless_the_radio_says_cut()
    {
        var cut = OnAxis4(SwitchFunction.ThrottleCut, (-1, null), (1, SwitchStates.ThrottleCut));
        var router = new InputRouter(_ => Profile(cut));
        Assert.True(router.Update(0.016, [Radio(1)], default, default).Controls.ThrottleCut);
        router.ResetForNewFlight();
        Assert.False(router.Update(0.016, [Radio(-1)], default, default).Controls.ThrottleCut);
    }

    [Fact]
    public void Camera_osd_wind_and_pause_switches_set_their_state_at_startup_and_on_each_move()
    {
        var camera = OnAxis4(SwitchFunction.Camera, (-1, SwitchStates.CameraGround), (0, SwitchStates.CameraFpv), (1, SwitchStates.CameraChase));
        var osd = new SwitchAssignment(SwitchFunction.Osd, new SwitchSource(ButtonIndex: 1), [new(-1, SwitchStates.OsdOff), new(1, SwitchStates.OsdOn)]);
        var router = new InputRouter(_ => Profile(camera, osd));
        Assert.Equal([new FlightCommand(FlightCommandKind.SelectCamera, View: CameraView.Fpv), new FlightCommand(FlightCommandKind.SetOsd, On: false)],
            router.Update(0.016, [Radio(0)], default, default).Commands);
        Assert.Equal([new FlightCommand(FlightCommandKind.SelectCamera, View: CameraView.Chase), new FlightCommand(FlightCommandKind.SetOsd, On: true)],
            router.Update(0.016, [Radio(1, button1: true)], default, default).Commands);
    }

    [Theory]
    [InlineData(SwitchFunction.Wind, SwitchStates.WindOn, FlightCommandKind.SetWind, true)]
    [InlineData(SwitchFunction.Wind, SwitchStates.WindOff, FlightCommandKind.SetWind, false)]
    [InlineData(SwitchFunction.Pause, SwitchStates.PausePaused, FlightCommandKind.SetPause, true)]
    [InlineData(SwitchFunction.Pause, SwitchStates.PauseRunning, FlightCommandKind.SetPause, false)]
    [InlineData(SwitchFunction.Osd, SwitchStates.OsdOn, FlightCommandKind.SetOsd, true)]
    public void Switch_events_map_to_setters(SwitchFunction f, int state, FlightCommandKind kind, bool on)
        => Assert.Equal(new FlightCommand(kind, On: on), FlightCommand.FromSwitch(new SwitchEvent(f, state)));

    [Fact]
    public void The_router_exposes_the_active_radio_switch_board()
    {
        var router = new InputRouter(_ => Profile(OnAxis4(SwitchFunction.Gear, (-1, 0), (1, 1))));
        Assert.Null(router.Switches);
        router.Update(0.016, [Radio(1)], default, default);
        Assert.Equal(1, router.Switches!.Position(SwitchFunction.Gear));
    }
```

In `tests/SimLab.App.Tests/Session/FlightSessionTests.cs` and `tests/SimLab.App.Tests/Audio/AircraftSoundTests.cs` replace `session.Handle(SwitchAction.X)` with `session.Handle(new FlightCommand(FlightCommandKind.K))` where Pause→TogglePause, ToggleWind→ToggleWind, Reset→Reset (add `using SimLab.App.Session;` if missing). Add to `FlightSessionTests`:

```csharp
    [Fact]
    public void Set_pause_and_set_wind_put_the_session_in_that_state()
    {
        using var session = NewSession(); // use the helper the other tests in this file use to build a session with wind
        session.Handle(new FlightCommand(FlightCommandKind.SetPause, On: true));
        session.Handle(new FlightCommand(FlightCommandKind.SetPause, On: true));
        Assert.True(session.Paused);
        session.Handle(new FlightCommand(FlightCommandKind.SetPause, On: false));
        Assert.False(session.Paused);
        session.Handle(new FlightCommand(FlightCommandKind.SetWind, On: false));
        Assert.False(session.WindEnabled);
        session.Handle(new FlightCommand(FlightCommandKind.SetWind, On: true));
        Assert.True(session.WindEnabled);
    }
```

(Look at how the existing wind test at `FlightSessionTests.cs:~70-82` builds its session and reuse exactly that construction instead of `NewSession()` if no helper exists.)

In `tests/SimLab.App.Tests/Settings/SettingsTests.cs:136` replace the binding with
`Switches = { new SwitchAssignment(SwitchFunction.Reset, new SwitchSource(ButtonIndex: 3), [new(-1, null), new(1, SwitchStates.ResetFire)]) },`.

- [ ] **Step 6: Implement routing**

`src/SimLab.Flight/Controls/ControlInputs.cs`: below `GearUp` add

```csharp
    /// <summary>The pilot's throttle-cut switch is on (the throttle is then already 0).</summary>
    public bool ThrottleCut { get; init; }
```

and extend the summary with "<see cref="ThrottleCut"/> is shown by the OSD.".

Create `src/SimLab.App/Session/FlightCommand.cs`:

```csharp
using SimLab.App.Cameras;
using SimLab.Input;

namespace SimLab.App.Session;

public enum FlightCommandKind { Reset, TogglePause, SetPause, ToggleWind, SetWind, NextCamera, SelectCamera, SetOsd }

/// <summary>
/// A one-off command from a key (toggles) or a radio switch (setters). <see cref="On"/> is the setting for
/// <see cref="FlightCommandKind.SetPause"/>, <see cref="FlightCommandKind.SetWind"/> and
/// <see cref="FlightCommandKind.SetOsd"/>; <see cref="View"/> the view for <see cref="FlightCommandKind.SelectCamera"/>.
/// </summary>
public readonly record struct FlightCommand(FlightCommandKind Kind, bool On = false, CameraView View = CameraView.Ground)
{
    /// <summary>The command a switch event stands for; null for held functions, which never emit events.</summary>
    public static FlightCommand? FromSwitch(SwitchEvent e) => e.Function switch
    {
        SwitchFunction.Reset => new FlightCommand(FlightCommandKind.Reset),
        SwitchFunction.Pause => new FlightCommand(FlightCommandKind.SetPause, On: e.State == SwitchStates.PausePaused),
        SwitchFunction.Wind => new FlightCommand(FlightCommandKind.SetWind, On: e.State == SwitchStates.WindOn),
        SwitchFunction.Osd => new FlightCommand(FlightCommandKind.SetOsd, On: e.State == SwitchStates.OsdOn),
        SwitchFunction.Camera => new FlightCommand(FlightCommandKind.SelectCamera, View: e.State switch
        {
            SwitchStates.CameraFpv => CameraView.Fpv,
            SwitchStates.CameraChase => CameraView.Chase,
            _ => CameraView.Ground,
        }),
        _ => null,
    };
}
```

`src/SimLab.App/Session/InputRouter.cs`:
- In `FlapSetting`, replace `FromSwitch` with:

```csharp
    /// <summary>Flap command for a flap switch state (<see cref="SwitchStates.FlapsUp"/>, takeoff, landing).</summary>
    public static double FromState(int state) => state switch
    {
        SwitchStates.FlapsTakeoff => Half,
        SwitchStates.FlapsLanding => Landing,
        _ => 0,
    };
```

- `RouterOutput`: rename `IReadOnlyList<SwitchAction> Actions` to `IReadOnlyList<FlightCommand> Commands`.
- Replace the class summary and body of `InputRouter` with:

```csharp
/// <summary>
/// Chooses the calibrated radio when one is connected, else the keyboard, and turns switches and keys into commands.
/// Gear, flaps and throttle cut are one state each: a radio switch sets it on every frame it sits on a position with a
/// state; G and F change it on a key press, so the keyboard wins while the switch is unbound or on a no-effect
/// position. Throttle cut forces the throttle to 0.
/// </summary>
public sealed class InputRouter
{
    readonly Func<string, RadioProfile?> _loadProfile;
    readonly Dictionary<string, RadioProfile?> _profiles = new();
    KeyboardStick _keyboard = new();
    SwitchBoard? _switches;
    string? _activeGuid;
    KeyboardCommands _previousCommands;
    bool _gearUp;
    double _flap;
    bool _throttleCut;

    public InputRouter(Func<string, RadioProfile?> loadProfile) => _loadProfile = loadProfile;

    /// <summary>Switch board of the active radio (the radio screen shows its positions); null on the keyboard.</summary>
    public SwitchBoard? Switches => _switches;

    public void InvalidateProfiles()
    {
        _profiles.Clear();
        _activeGuid = null;
        _switches = null;
    }

    /// <summary>Forgets cached profiles, the active radio and the keyboard stick (throttle back to idle); gear down,
    /// flaps up, throttle armed.</summary>
    public void ResetForNewFlight()
    {
        InvalidateProfiles();
        _keyboard = new KeyboardStick();
        _previousCommands = default;
        (_gearUp, _flap, _throttleCut) = (false, 0, false);
    }

    public RouterOutput Update(double dt, IReadOnlyList<JoypadSnapshot> pads, KeyboardKeys keys, KeyboardCommands commands)
    {
        var output = new List<FlightCommand>();
        if (commands.Reset && !_previousCommands.Reset) output.Add(new FlightCommand(FlightCommandKind.Reset));
        if (commands.Pause && !_previousCommands.Pause) output.Add(new FlightCommand(FlightCommandKind.TogglePause));
        if (commands.ToggleWind && !_previousCommands.ToggleWind) output.Add(new FlightCommand(FlightCommandKind.ToggleWind));
        if (commands.NextCamera && !_previousCommands.NextCamera) output.Add(new FlightCommand(FlightCommandKind.NextCamera));
        if (commands.ToggleGear && !_previousCommands.ToggleGear) _gearUp = !_gearUp;
        if (commands.ToggleFlaps && !_previousCommands.ToggleFlaps) _flap = FlapSetting.Next(_flap);
        _previousCommands = commands;

        var keyboardSticks = _keyboard.Update(dt, keys);

        foreach (var pad in pads)
        {
            if (!_profiles.TryGetValue(pad.Guid, out var profile)) _profiles[pad.Guid] = profile = _loadProfile(pad.Guid);
            if (profile is null) continue;
            if (_activeGuid != pad.Guid)
            {
                _activeGuid = pad.Guid;
                _switches = new SwitchBoard(profile.Switches);
            }
            foreach (var e in _switches!.Update(pad.Frame))
                if (FlightCommand.FromSwitch(e) is { } command) output.Add(command);
            ApplyReset(output);
            if (_switches.Held(SwitchFunction.Gear) is int gear) _gearUp = gear == SwitchStates.GearUp;
            if (_switches.Held(SwitchFunction.Flaps) is int flaps) _flap = FlapSetting.FromState(flaps);
            if (_switches.Held(SwitchFunction.ThrottleCut) is int cut) _throttleCut = cut == SwitchStates.ThrottleCut;
            return new RouterOutput(Command(ToControls(profile.Read(pad.Frame))), output, InputSource.Radio, pad.Name, pad.Frame);
        }

        _activeGuid = null;
        _switches = null;
        ApplyReset(output);
        return new RouterOutput(Command(ToControls(keyboardSticks)), output, InputSource.Keyboard, "");
    }

    /// <summary>A reset puts the gear down and the flaps up (a switch holding them elsewhere takes them back at once).</summary>
    void ApplyReset(List<FlightCommand> commands)
    {
        if (commands.Contains(new FlightCommand(FlightCommandKind.Reset))) (_gearUp, _flap) = (false, 0);
    }

    ControlInputs Command(ControlInputs sticks) => sticks with
    {
        Throttle = _throttleCut ? 0 : sticks.Throttle,
        GearUp = _gearUp,
        Flap = _flap,
        ThrottleCut = _throttleCut,
    };

    /// <summary>Calibrated sticks to simulator commands (same signs: right, pitch up, right positive).</summary>
    public static ControlInputs ToControls(StickState s) => new(s.Throttle, s.Aileron, s.Elevator, s.Rudder);
}
```

Delete `src/SimLab.App/Session/SwitchCapture.cs` (`git rm`).

`src/SimLab.App/Session/FlightSession.cs` — replace `Handle`:

```csharp
    /// <summary>Reset, pause and wind commands; camera and OSD belong to the flight scene.</summary>
    public void Handle(FlightCommand command)
    {
        switch (command.Kind)
        {
            case FlightCommandKind.Reset: Reset(); break;
            case FlightCommandKind.TogglePause: Paused = !Paused; break;
            case FlightCommandKind.SetPause: Paused = command.On; break;
            case FlightCommandKind.ToggleWind: SetWind(!WindEnabled); break;
            case FlightCommandKind.SetWind: SetWind(command.On); break;
        }
    }
```

`game/Scripts/Flight/FlightScene.cs`:
- replace the `foreach (var action in LastInput.Actions) { … }` loop with:

```csharp
        foreach (var command in LastInput.Commands)
        {
            switch (command.Kind)
            {
                case FlightCommandKind.NextCamera: if (_script is null) NextCamera(); break;
                case FlightCommandKind.SelectCamera: if (_script is null) SelectCamera(command.View); break;
                case FlightCommandKind.SetOsd: SetHud(command.On); break;
                default: _session.Handle(command); break;
            }
        }
```

- replace `ToggleHud` with:

```csharp
    /// <summary>Shows or hides the OSD (H key, HUD button). Scripted runs keep it on.</summary>
    public void ToggleHud() => SetHud(!_services.Settings.ShowFlightData);

    /// <summary>Shows or hides the OSD and remembers it (radio OSD switch). Scripted runs keep it on.</summary>
    public void SetHud(bool on)
    {
        if (_script is not null || _services.Settings.ShowFlightData == on) return;
        _services.Settings = _services.Settings with { ShowFlightData = on };
        _services.SaveSettings();
    }
```

`game/Scripts/Radio/RadioScreen.cs` (temporary, Task 5 rewrites it): remove the `Ui.Row(Ui.Button(Ui.T("RADIO_BIND_RESET")…RADIO_BIND_FLAPS…))` row, the fields `_capture` and `_captureAction`, the methods `StartCapture` and `SaveBinding`, and every `_capture` reference (`_Process` keeps only the wizard branch: `if (_wizard is not null) { if (Pinned is not { } active) {…cancel…} else { _wizard.Feed(active.Frame); _prompt.Text = PromptText(_wizard); } }`; `Cancel`/`StartCalibration` drop `_capture = null`; `SetBusy` becomes `_next.Disabled = !busy; _cancel.Disabled = !busy;`).

Run `grep -rn "SwitchAction\|SwitchBinding\|SwitchTracker\|SwitchCapture\|\.Actions\b\|FromSwitch(double" src game tests --include='*.cs'` — expected: no output.

- [ ] **Step 7: Run everything**

Run: `dotnet test` then `dotnet build game/SimLab.Game.csproj`
Expected: all tests PASS (the known skip stays skipped); game builds with 0 errors.

- [ ] **Step 8: Commit**

```bash
git add -A src game tests
git commit -m "feat(input): positional switch assignments, throttle cut, flight commands; convert old profiles"
```

---

### Task 4: Throttle-cut warning on the OSD

**Files:**
- Modify: `src/SimLab.App/Ui/OsdData.cs`
- Modify: `game/Scripts/Flight/FlightScene.cs` (`OsdData.From` call)
- Modify: `game/Scripts/Flight/FlightHud.cs:~178` (throttle label)
- Modify: `game/translations/strings.csv`
- Test: `tests/SimLab.App.Tests/Ui/OsdDataTests.cs`, `tests/SimLab.App.Tests/Localization/TranslationTests.cs`

**Interfaces:**
- Consumes: `ControlInputs.ThrottleCut` (Task 3).
- Produces: `OsdData.ThrottleCut` (bool, last record parameter, default false); `OsdData.From(…, double flap = 0, bool throttleCut = false)`.

- [ ] **Step 1: Failing tests**

In `OsdDataTests.cs`, add a test next to the existing flap test, building the aircraft the same way that test does:

```csharp
    [Fact]
    public void Throttle_cut_is_reported()
    {
        // build `aircraft`, `state`, `pilot` exactly as the neighbouring flap test does
        Assert.True(OsdData.From(aircraft, state, 10, 0, 0, pilot, 0, throttleCut: true).ThrottleCut);
        Assert.False(OsdData.From(aircraft, state, 10, 0, 0, pilot).ThrottleCut);
    }
```

In `TranslationTests.Every_key_used_by_the_app_exists`, add `"OSD_THROTTLE_CUT"` to the key list.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/SimLab.App.Tests --filter "OsdDataTests|TranslationTests"`
Expected: FAIL (no `throttleCut` parameter; key missing).

- [ ] **Step 3: Implement**

- `OsdData`: add record parameter `bool ThrottleCut = false` after `FlapIndicator? Flaps = null`, a `<param name="ThrottleCut">The throttle-cut switch is on.</param>` doc line, the `bool throttleCut = false` parameter on `From`, and pass `throttleCut` as the last constructor argument.
- `FlightScene._Process`: `OsdData.From(…, LastInput.Controls.Flap, LastInput.Controls.ThrottleCut)`.
- `FlightHud`: where `_throttle.Text` is set:

```csharp
        _throttle.Text = osd.ThrottleCut
            ? Ui.T("OSD_THROTTLE_CUT")
            : $"{Ui.T("OSD_THROTTLE")} {osd.ThrottlePercent.ToString("0", Inv)} %";
        _throttle.Modulate = osd.ThrottleCut ? new Color(0.95f, 0.40f, 0.35f) : Colors.White;
```

- `strings.csv`, after `OSD_THROTTLE`: `OSD_THROTTLE_CUT,GAZ COUPÉS,THROTTLE CUT`

- [ ] **Step 4: Run tests and build**

Run: `dotnet test` and `dotnet build game/SimLab.Game.csproj` — Expected: PASS, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src game tests
git commit -m "feat(osd): throttle-cut warning"
```

---

### Task 5: Radio screen in three tabs with switch learning

**Files:**
- Rewrite: `game/Scripts/Radio/RadioScreen.cs` (shell: header, tabs, right column, polling)
- Create: `game/Scripts/Radio/RadioTab.cs` (devices, stick mode, calibration wizard, help)
- Create: `game/Scripts/Radio/ChannelsTab.cs` (raw axes with what they drive, reverse toggles, control-check readouts)
- Create: `game/Scripts/Radio/SwitchesTab.cs` (function rows, live positions, learning)
- Create: `src/SimLab.App/Ui/SwitchSummary.cs` + test `tests/SimLab.App.Tests/Ui/SwitchSummaryTests.cs` (pure text helpers, unit-tested)
- Modify: `game/Scripts/Main.cs` (`--screenshot-radio <tab> <png>`)
- Modify: `game/translations/strings.csv`
- Test: `tests/SimLab.App.Tests/Localization/TranslationTests.cs`

**Interfaces:**
- Consumes: `SwitchLearner`, `LearnedSwitch`, `SwitchStates`, `SwitchAssignment`, `RadioProfile.SetSwitch/ClearSwitch`, `InputRouter.Switches`, `ControlInputs.GearUp/Flap/ThrottleCut`, `FlapSetting`.
- Produces: `RadioScreen.SelectTab(int)`, `RadioScreen.ForceControlCheck(string, ControlInputs)` (kept), `SwitchSummary.SourceKey/SourceNumber/DrivenBy/Status`.

- [ ] **Step 1: Failing tests for the pure helpers**

`tests/SimLab.App.Tests/Ui/SwitchSummaryTests.cs`:

```csharp
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.App.Tests.Ui;

public class SwitchSummaryTests
{
    [Fact]
    public void Source_is_named_by_kind_and_one_based_number()
    {
        Assert.Equal(("RADIO_SOURCE_AXIS", 6), (SwitchSummary.SourceKey(new SwitchSource(AxisIndex: 5)), SwitchSummary.SourceNumber(new SwitchSource(AxisIndex: 5))));
        Assert.Equal(("RADIO_SOURCE_BUTTON", 1), (SwitchSummary.SourceKey(new SwitchSource(ButtonIndex: 0)), SwitchSummary.SourceNumber(new SwitchSource(ButtonIndex: 0))));
    }

    [Fact]
    public void Driven_by_lists_stick_channels_then_switch_functions_on_an_axis()
    {
        var p = new RadioProfile();
        p.Channels[StickFunction.Throttle] = new ChannelSettings(2, false, AxisCalibration.Identity);
        p.SetSwitch(new SwitchAssignment(SwitchFunction.Gear, new SwitchSource(AxisIndex: 5), [new(-1, 0), new(1, 1)]));
        p.SetSwitch(new SwitchAssignment(SwitchFunction.Flaps, new SwitchSource(AxisIndex: 5), [new(-1, 0), new(1, 2)]));
        Assert.Equal(["STICK_THROTTLE"], SwitchSummary.DrivenBy(p, 2));
        Assert.Equal(["SWITCH_GEAR", "SWITCH_FLAPS"], SwitchSummary.DrivenBy(p, 5));
        Assert.Empty(SwitchSummary.DrivenBy(p, 0));
    }

    [Fact]
    public void Status_keys_follow_gear_flaps_and_throttle_cut()
    {
        var controls = new ControlInputs(0, 0, 0, 0, FlapSetting.Landing) { GearUp = true, ThrottleCut = true };
        Assert.Equal(["SWITCH_GEAR_UP", "SWITCH_FLAPS_LANDING", "SWITCH_THROTTLE_CUT_CUT"], SwitchSummary.Status(controls));
        Assert.Equal(["SWITCH_GEAR_DOWN", "SWITCH_FLAPS_TAKEOFF", "SWITCH_THROTTLE_CUT_ARMED"],
            SwitchSummary.Status(new ControlInputs(0, 0, 0, 0, FlapSetting.Half)));
    }
}
```

Also add to `TranslationTests.Every_key_used_by_the_app_exists`:

```csharp
        foreach (var f in SwitchStates.All)
        {
            Assert.Contains(SwitchStates.FunctionKey(f), keys);
            for (int s = 0; s < SwitchStates.Count(f); s++) Assert.Contains(SwitchStates.StateKey(f, s), keys);
        }
        foreach (var k in new[] { "RADIO_TAB_RADIO", "RADIO_TAB_CHANNELS", "RADIO_TAB_SWITCHES", "RADIO_LEARN", "RADIO_CLEAR",
                     "RADIO_LEARN_PROMPT", "RADIO_LEARN_NONE", "RADIO_LEARN_DONE", "RADIO_SWITCH_NONE", "RADIO_SOURCE_AXIS",
                     "RADIO_SOURCE_BUTTON", "RADIO_SWITCHES_HELP", "SWITCH_NO_EFFECT", "STICK_THROTTLE", "STICK_AILERON",
                     "STICK_ELEVATOR", "STICK_RUDDER", "RADIO_CHANNEL_FREE" })
            Assert.Contains(k, keys);
```

(If `STICK_*` keys already exist under another name, e.g. `CHANNEL_*`, reuse those names in `SwitchSummary` and in this list rather than adding duplicates — check with `grep -n "^CHANNEL_\|^STICK_" game/translations/strings.csv`.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/SimLab.App.Tests --filter "SwitchSummaryTests|TranslationTests"` — Expected: FAIL.

- [ ] **Step 3: Implement `src/SimLab.App/Ui/SwitchSummary.cs`**

```csharp
using SimLab.App.Session;
using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.App.Ui;

/// <summary>Translation keys the radio screen shows for switches, axes and the switch-driven controls.</summary>
public static class SwitchSummary
{
    public static string SourceKey(SwitchSource source) => source.ButtonIndex is not null ? "RADIO_SOURCE_BUTTON" : "RADIO_SOURCE_AXIS";

    /// <summary>One-based axis or button number, as radios and the raw-axes list count them.</summary>
    public static int SourceNumber(SwitchSource source) => (source.ButtonIndex ?? source.AxisIndex ?? 0) + 1;

    /// <summary>What a raw axis drives: stick channels (STICK_*) then switch functions (SWITCH_*).</summary>
    public static IReadOnlyList<string> DrivenBy(RadioProfile profile, int axis)
    {
        var keys = new List<string>();
        foreach (var (function, channel) in profile.Channels)
            if (channel.AxisIndex == axis) keys.Add("STICK_" + function.ToString().ToUpperInvariant());
        foreach (var s in profile.Switches)
            if (s.Source.AxisIndex == axis) keys.Add(SwitchStates.FunctionKey(s.Function));
        return keys;
    }

    /// <summary>Gear, flaps and throttle-cut state keys for the control-check status line.</summary>
    public static IReadOnlyList<string> Status(in ControlInputs controls) =>
    [
        SwitchStates.StateKey(SwitchFunction.Gear, controls.GearUp ? SwitchStates.GearUp : SwitchStates.GearDown),
        SwitchStates.StateKey(SwitchFunction.Flaps, controls.Flap >= FlapSetting.Landing ? SwitchStates.FlapsLanding
            : controls.Flap >= FlapSetting.Half ? SwitchStates.FlapsTakeoff : SwitchStates.FlapsUp),
        SwitchStates.StateKey(SwitchFunction.ThrottleCut, controls.ThrottleCut ? SwitchStates.ThrottleCut : SwitchStates.ThrottleArmed),
    ];
}
```

(`profile.Channels` is a `Dictionary`, whose enumeration order is insertion order for a never-removed-from dictionary; the test inserts only throttle on axis 2, so order is not at stake.)

- [ ] **Step 4: Translations**

Remove the obsolete rows `RADIO_BIND_RESET`, `RADIO_BIND_PAUSE`, `RADIO_BIND_WIND`, `RADIO_BIND_CAMERA`, `RADIO_BIND_FLAPS`, `RADIO_BIND_GEAR`, `RADIO_BIND_WAIT`, `RADIO_BIND_DONE` (check with grep that no code uses them). Add:

```csv
RADIO_TAB_RADIO,Radio,Radio
RADIO_TAB_CHANNELS,Voies,Channels
RADIO_TAB_SWITCHES,Interrupteurs,Switches
RADIO_LEARN,Apprendre,Learn
RADIO_CLEAR,Effacer,Clear
RADIO_LEARN_PROMPT,"« {0} » : basculez l'interrupteur dans toutes ses positions en marquant un temps sur chacune, puis Suivant.","{0}: flip the switch through all its positions, pausing on each, then Next."
RADIO_LEARN_NONE,"Aucun interrupteur n'a bougé. Recommencez en marquant un temps sur chaque position.",No switch moved. Try again and pause on each position.
RADIO_LEARN_DONE,"Interrupteur appris. Choisissez l'effet de chaque position.",Switch learned. Choose what each position does.
RADIO_SWITCH_NONE,non assigné,not assigned
RADIO_SOURCE_AXIS,Voie {0},Channel {0}
RADIO_SOURCE_BUTTON,Bouton {0},Button {0}
RADIO_SWITCHES_HELP,"Basculez un interrupteur : sa position active s'allume. « — » = sans effet.","Flip a switch: its active position lights up. “—” = no effect."
RADIO_CHANNEL_FREE,libre,free
SWITCH_NO_EFFECT,—,—
STICK_THROTTLE,Gaz,Throttle
STICK_AILERON,Ailerons,Aileron
STICK_ELEVATOR,Profondeur,Elevator
STICK_RUDDER,Dérive,Rudder
SWITCH_GEAR,Train,Gear
SWITCH_GEAR_DOWN,Sorti,Down
SWITCH_GEAR_UP,Rentré,Up
SWITCH_FLAPS,Volets,Flaps
SWITCH_FLAPS_UP,Rentrés,Up
SWITCH_FLAPS_TAKEOFF,Décollage,Takeoff
SWITCH_FLAPS_LANDING,Atterrissage,Landing
SWITCH_THROTTLE_CUT,Coupure gaz,Throttle cut
SWITCH_THROTTLE_CUT_ARMED,Gaz armés,Armed
SWITCH_THROTTLE_CUT_CUT,Gaz coupés,Cut
SWITCH_CAMERA,Caméra,Camera
SWITCH_CAMERA_GROUND,Sol,Ground
SWITCH_CAMERA_FPV,FPV,FPV
SWITCH_CAMERA_CHASE,Poursuite,Chase
SWITCH_OSD,OSD,OSD
SWITCH_OSD_ON,Affiché,Shown
SWITCH_OSD_OFF,Masqué,Hidden
SWITCH_WIND,Vent,Wind
SWITCH_WIND_ON,Actif,On
SWITCH_WIND_OFF,Coupé,Off
SWITCH_PAUSE,Pause,Pause
SWITCH_PAUSE_RUNNING,En vol,Running
SWITCH_PAUSE_PAUSED,En pause,Paused
SWITCH_RESET,Reset,Reset
SWITCH_RESET_RESET,Reset,Reset
```

(Keep the "(STICK_* already exist?)" check from Step 1 in mind. `RADIO_REVERSE_TITLE`, `RADIO_REVERSE`, `RADIO_AXES`, `RADIO_MODE`, `RADIO_HELP`, `RADIO_PREVIEW_*`, `RADIO_CALIBRATE/NEXT/CANCEL/SAVED`, `RADIO_NO_DEVICE`, `RADIO_DEVICE_*` stay.)

Run: `dotnet test tests/SimLab.App.Tests --filter "SwitchSummaryTests|TranslationTests"` — Expected: PASS.

- [ ] **Step 5: `RadioTab.cs`** — move the device/mode/calibration part of the current `RadioScreen` here, unchanged in behaviour:

```csharp
using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>Radio tab: detected devices, stick mode, calibration wizard and the EdgeTX help.</summary>
public partial class RadioTab : VBoxContainer
{
    Services _services = null!;
    System.Action _saved = null!;
    ItemList _devices = null!;
    Label _prompt = null!;
    Button _next = null!;
    Button _cancel = null!;
    CalibrationWizard? _wizard;
    string? _wizardGuid;
    List<string> _deviceGuids = [];

    /// <summary>Guid of the device picked in the list; null means "the first one".</summary>
    public string? SelectedGuid { get; private set; }

    public void Init(Services services, System.Action saved)
    {
        _services = services;
        _saved = saved;
        Name = Ui.T("RADIO_TAB_RADIO");
        AddThemeConstantOverride("separation", 12);

        _devices = new ItemList { CustomMinimumSize = new Vector2(0, 90) };
        _devices.ItemSelected += index => SelectedGuid = index < _deviceGuids.Count ? _deviceGuids[(int)index] : null;
        AddChild(_devices);

        var mode = new OptionButton();
        mode.AddItem("Mode 1", 1);
        mode.AddItem("Mode 2", 2);
        mode.Selected = services.Settings.StickMode == StickMode.Mode1 ? 0 : 1;
        mode.ItemSelected += index =>
        {
            _services.Settings = _services.Settings with { StickMode = index == 0 ? StickMode.Mode1 : StickMode.Mode2 };
            _services.SaveSettings();
        };
        AddChild(Ui.Row(Ui.RowLabel(Ui.T("RADIO_MODE")), mode));

        _prompt = Ui.Text("", 20);
        AddChild(_prompt);
        _next = Ui.Button(Ui.T("RADIO_NEXT"), Next);
        _cancel = Ui.Button(Ui.T("RADIO_CANCEL"), Cancel);
        AddChild(Ui.Row(Ui.Button(Ui.T("RADIO_CALIBRATE"), StartCalibration), _next, _cancel));
        AddChild(Ui.Text(Ui.T("RADIO_HELP"), 16));
        SetBusy(false);
    }

    JoypadSnapshot? _selected;
    IReadOnlyList<JoypadSnapshot> _pads = [];

    /// <summary>Called every frame by the screen with the current poll and the selected device.</summary>
    public void Refresh(IReadOnlyList<JoypadSnapshot> pads, JoypadSnapshot? selected)
    {
        _pads = pads;
        _selected = selected;
        var guids = pads.Select(p => p.Guid).ToList();
        if (!guids.SequenceEqual(_deviceGuids))
        {
            _deviceGuids = guids;
            _devices.Clear();
            foreach (var pad in pads) _devices.AddItem(pad.Name);
            if (SelectedGuid is not null && _deviceGuids.IndexOf(SelectedGuid) is var i and >= 0) _devices.Select(i);
        }

        if (_wizard is null) return;
        var active = pads.FirstOrDefault(p => p.Guid == _wizardGuid);
        if (active.Guid is null)
        {
            _wizard = null;
            _prompt.Text = Ui.T("RADIO_CANCEL") + " — " + Ui.T("RADIO_NO_DEVICE");
            SetBusy(false);
            return;
        }
        _wizard.Feed(active.Frame);
        _prompt.Text = PromptText(_wizard);
    }

    // Move PromptText, StartCalibration, Next and Cancel here from the old RadioScreen, with these changes:
    // - StartCalibration: `if (_selected is not { } pad) return; _wizardGuid = pad.Guid; _wizard = new CalibrationWizard(pad.Frame.Axes.Length); SetBusy(true);`
    // - Next: the pinned pad is `_pads.FirstOrDefault(p => p.Guid == _wizardGuid)` (return if its Guid is null); after
    //   `_services.Radios.Save(profile); _services.Router.InvalidateProfiles();` call `_saved();` instead of touching
    //   `_profileStale`/`_calibrated`; then `_wizard = null; _prompt.Text = Ui.T("RADIO_SAVED"); SetBusy(false);`.
    // - Cancel: `_wizard = null; _prompt.Text = ""; SetBusy(false);`
    // - SetBusy(bool busy): `_next.Disabled = !busy; _cancel.Disabled = !busy;`
}
```

(The comment block above lists the exact moves; write the methods out in full — they are the existing ones from `RadioScreen.cs` with the listed edits. Do not leave the comment in the final file.)

- [ ] **Step 6: `ChannelsTab.cs`**

```csharp
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.App.Visual;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>Channels tab: every raw axis with what it drives, then the stick directions with the control-check readout.</summary>
public partial class ChannelsTab : VBoxContainer
{
    static readonly Color Bad = new(0.95f, 0.40f, 0.35f);
    static readonly StickFunction[] Functions = [StickFunction.Throttle, StickFunction.Aileron, StickFunction.Elevator, StickFunction.Rudder];

    readonly List<(ProgressBar Bar, Label Drives)> _axes = [];
    readonly Dictionary<StickFunction, (CheckBox Reverse, Label Readout)> _channels = new();
    System.Action<StickFunction, bool> _setReversed = null!;
    RadioProfile? _shown;

    public void Init(System.Action<StickFunction, bool> setReversed)
    {
        _setReversed = setReversed;
        Name = Ui.T("RADIO_TAB_CHANNELS");
        AddThemeConstantOverride("separation", 8);
        AddChild(Ui.Text(Ui.T("RADIO_AXES"), 20));
        for (int i = 0; i < JoypadReader.MaxAxes; i++)
        {
            var index = Ui.RowLabel($"{i + 1}", 16);
            index.CustomMinimumSize = new Vector2(32, 0);
            var bar = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(320, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var drives = Ui.Text("", 16);
            drives.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _axes.Add((bar, drives));
            AddChild(Ui.Row(index, bar, drives));
        }

        AddChild(Ui.Text(Ui.T("RADIO_REVERSE_TITLE"), 20));
        foreach (var function in Functions)
        {
            var name = Ui.RowLabel(Ui.T("STICK_" + function.ToString().ToUpperInvariant()), 16);
            name.CustomMinimumSize = new Vector2(120, 0);
            var reverse = Ui.Check(Ui.T("RADIO_REVERSE"), false, on => _setReversed(function, on));
            reverse.CustomMinimumSize = new Vector2(130, 0);
            var readout = Ui.Text("", 16);
            readout.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _channels[function] = (reverse, readout);
            AddChild(Ui.Row(name, reverse, readout));
        }
    }

    public void Refresh(JoypadSnapshot? pad, RadioProfile? profile, in ControlInputs inputs, Aircraft? aircraft)
    {
        for (int i = 0; i < _axes.Count; i++)
        {
            var (bar, drives) = _axes[i];
            bar.Value = pad is { } p && i < p.Frame.Axes.Length ? (p.Frame.Axes[i] + 1) * 50 : 50;
            IReadOnlyList<string> keys = profile is null ? [] : SwitchSummary.DrivenBy(profile, i);
            drives.Text = keys.Count == 0 ? Ui.T("RADIO_CHANNEL_FREE") : string.Join(", ", keys.Select(Ui.T));
            drives.Modulate = keys.Count == 0 ? new Color(1, 1, 1, 0.45f) : Colors.White;
        }

        if (!ReferenceEquals(profile, _shown))
        {
            _shown = profile;
            foreach (var (function, (reverse, _)) in _channels)
            {
                var channel = profile is not null && profile.Channels.TryGetValue(function, out var c) ? c : null;
                reverse.Visible = channel is not null;
                reverse.SetPressedNoSignal(channel?.Reversed ?? false);
            }
        }

        _channels[StickFunction.Throttle].Readout.Text = ControlCheck.FormatThrottle(inputs.Throttle, Ui.T);
        if (aircraft is null) return;
        foreach (var function in Functions.Skip(1))
        {
            var check = ControlCheck.Describe(aircraft, function, inputs);
            var readout = _channels[function].Readout;
            readout.Text = ControlCheck.Format(check, Ui.T);
            readout.Modulate = check.Consistent ? Colors.White : Bad;
        }
    }
}
```

(Add `using System.Linq;`. `ControlCheck` is in whichever namespace the old `RadioScreen` imported it from — keep those `using`s.)

- [ ] **Step 7: `SwitchesTab.cs`**

```csharp
using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>
/// Switches tab: one row per function with its switch, each learned position and the state it selects; the position
/// the switch is on lights up. Learn runs a <see cref="SwitchLearner"/> on the selected radio.
/// </summary>
public partial class SwitchesTab : VBoxContainer
{
    static readonly Color Active = new(0.22f, 0.54f, 0.87f);
    static readonly Color Idle = new(1, 1, 1, 0.35f);

    sealed class Row
    {
        public required Label Source;
        public required HBoxContainer Positions;
        public required Button Clear;
        public readonly List<Label> Markers = [];
    }

    Services _services = null!;
    System.Action _saved = null!;
    readonly Dictionary<SwitchFunction, Row> _rows = new();
    Label _prompt = null!;
    Button _next = null!;
    Button _cancel = null!;
    SwitchLearner? _learner;
    SwitchFunction _learning;
    string? _learnGuid;
    RadioProfile? _shown;
    RadioProfile? _profile;
    JoypadSnapshot? _pad;

    public void Init(Services services, System.Action saved)
    {
        _services = services;
        _saved = saved;
        Name = Ui.T("RADIO_TAB_SWITCHES");
        AddThemeConstantOverride("separation", 6);

        foreach (var function in SwitchStates.All)
        {
            var name = Ui.RowLabel(Ui.T(SwitchStates.FunctionKey(function)), 18);
            name.CustomMinimumSize = new Vector2(150, 0);
            var source = Ui.Text("", 16);
            source.CustomMinimumSize = new Vector2(110, 0);
            var positions = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            positions.AddThemeConstantOverride("separation", 10);
            var learn = Ui.FlatButton(Ui.T("RADIO_LEARN"), () => StartLearn(function));
            var clear = Ui.FlatButton(Ui.T("RADIO_CLEAR"), () => Clear(function));
            _rows[function] = new Row { Source = source, Positions = positions, Clear = clear };
            var row = Ui.Row(name, source, positions, learn, clear);
            row.CustomMinimumSize = new Vector2(0, 44);
            AddChild(row);
            AddChild(new HSeparator());
        }

        _prompt = Ui.Text(Ui.T("RADIO_SWITCHES_HELP"), 18);
        AddChild(_prompt);
        _next = Ui.Button(Ui.T("RADIO_NEXT"), FinishLearn);
        _cancel = Ui.Button(Ui.T("RADIO_CANCEL"), CancelLearn);
        AddChild(Ui.Row(_next, _cancel));
        SetLearning(false);
    }

    /// <summary>Called every frame: rebuilds rows when the profile changed, feeds the learner, lights the positions.</summary>
    public void Refresh(double dt, JoypadSnapshot? pad, RadioProfile? profile, SwitchBoard? board)
    {
        _pad = pad;
        _profile = profile;
        if (!ReferenceEquals(profile, _shown))
        {
            _shown = profile;
            foreach (var function in SwitchStates.All) Rebuild(function);
        }

        if (_learner is not null)
        {
            if (pad is not { } p || p.Guid != _learnGuid)
            {
                CancelLearn();
                _prompt.Text = Ui.T("RADIO_CANCEL") + " — " + Ui.T("RADIO_NO_DEVICE");
            }
            else _learner.Feed(p.Frame, dt);
        }

        foreach (var (function, row) in _rows)
        {
            int? at = board?.Position(function);
            for (int i = 0; i < row.Markers.Count; i++) row.Markers[i].Modulate = at == i ? Active : Idle;
        }
    }

    void Rebuild(SwitchFunction function)
    {
        var row = _rows[function];
        foreach (var child in row.Positions.GetChildren()) child.QueueFree();
        row.Markers.Clear();
        var assignment = _profile?.Switches.FirstOrDefault(s => s.Function == function);
        row.Clear.Visible = assignment is not null;
        if (assignment is null)
        {
            row.Source.Text = Ui.T("RADIO_SWITCH_NONE");
            row.Source.Modulate = Idle;
            return;
        }
        row.Source.Text = string.Format(Ui.T(SwitchSummary.SourceKey(assignment.Source)), SwitchSummary.SourceNumber(assignment.Source));
        row.Source.Modulate = Colors.White;
        for (int i = 0; i < assignment.Positions.Count; i++)
        {
            int index = i;
            var marker = Ui.Text("●", 18);
            marker.Modulate = Idle;
            row.Markers.Add(marker);
            var picker = new OptionButton { FocusMode = FocusModeEnum.None };
            picker.AddItem(Ui.T("SWITCH_NO_EFFECT"), 0);
            for (int s = 0; s < SwitchStates.Count(function); s++) picker.AddItem(Ui.T(SwitchStates.StateKey(function, s)), s + 1);
            picker.Selected = (assignment.Positions[i].State ?? -1) + 1;
            picker.ItemSelected += item => SetState(function, index, item == 0 ? null : (int)item - 1);
            row.Positions.AddChild(Ui.Row(marker, picker));
        }
    }

    void StartLearn(SwitchFunction function)
    {
        if (_pad is not { } pad || _profile is null)
        {
            _prompt.Text = Ui.T("RADIO_DEVICE_UNCALIBRATED");
            return;
        }
        _learner = new SwitchLearner();
        _learning = function;
        _learnGuid = pad.Guid;
        _prompt.Text = string.Format(Ui.T("RADIO_LEARN_PROMPT"), Ui.T(SwitchStates.FunctionKey(function)));
        SetLearning(true);
    }

    void FinishLearn()
    {
        if (_learner is null || _learnGuid is null) return;
        if (_learner.Result() is not { } learned)
        {
            _learner = new SwitchLearner();
            _prompt.Text = Ui.T("RADIO_LEARN_NONE");
            return;
        }
        var defaults = SwitchStates.Defaults(_learning, learned.Positions.Count);
        var assignment = new SwitchAssignment(_learning, learned.Source,
            learned.Positions.Select((value, i) => new SwitchPosition(value, defaults[i])).ToList());
        Save(_learnGuid, p => p.SetSwitch(assignment));
        _learner = null;
        _prompt.Text = Ui.T("RADIO_LEARN_DONE");
        SetLearning(false);
    }

    void CancelLearn()
    {
        _learner = null;
        _prompt.Text = Ui.T("RADIO_SWITCHES_HELP");
        SetLearning(false);
    }

    void Clear(SwitchFunction function)
    {
        if (_pad is { } pad) Save(pad.Guid, p => p.ClearSwitch(function));
    }

    void SetState(SwitchFunction function, int position, int? state)
    {
        if (_pad is not { } pad) return;
        Save(pad.Guid, p =>
        {
            var assignment = p.Switches.FirstOrDefault(s => s.Function == function);
            if (assignment is null || position >= assignment.Positions.Count) return;
            assignment.Positions[position] = assignment.Positions[position] with { State = state };
        });
    }

    /// <summary>Loads the device's profile fresh, edits it, saves it and lets the screen reload it everywhere.</summary>
    void Save(string guid, System.Action<RadioProfile> edit)
    {
        var profile = _services.Radios.Load(guid, out _);
        if (profile is null) return;
        edit(profile);
        _services.Radios.Save(profile);
        _services.Router.InvalidateProfiles();
        _saved();
    }

    void SetLearning(bool learning)
    {
        _next.Visible = learning;
        _cancel.Visible = learning;
    }
}
```

- [ ] **Step 8: Rewrite `RadioScreen.cs` as the shell**

```csharp
using System.Linq;
using Godot;
using SimLab.App.Session;
using SimLab.App.Ui;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Input;
using SimLab.Game;

namespace SimLab.Game.Radio;

/// <summary>
/// Radio setup: a header with the device status, three tabs on the left (radio, channels, switches) and, always on the
/// right, the live control check (3D model driven by the calibrated sticks and the gear, flap and throttle-cut switches).
/// </summary>
public partial class RadioScreen : Control
{
    static readonly Color Good = new(0.35f, 0.85f, 0.45f);
    static readonly Color Bad = new(0.95f, 0.40f, 0.35f);

    Services _services = null!;
    Label _status = null!;
    TabContainer _tabs = null!;
    RadioTab _radio = null!;
    ChannelsTab _channels = null!;
    SwitchesTab _switches = null!;
    ControlPreview _preview = null!;
    Label _previewError = null!;
    Label _switchStatus = null!;
    OptionButton _picker = null!;
    IReadOnlyList<AircraftEntry> _aircraft = [];
    IReadOnlyList<JoypadSnapshot> _pads = [];
    InputRouter _router = null!;
    RadioProfile? _profile;
    string? _profileGuid;
    bool _profileStale = true;
    ControlInputs? _forcedInputs;

    public void Init(Services services, System.Action back)
    {
        _services = services;
        _router = new InputRouter(guid => _services.Radios.Load(guid, out _));
        var screen = Ui.Screen(this, Ui.T("RADIO_TITLE"));
        _status = Ui.Text("", 20);
        screen.AddChild(_status);

        var columns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 32);
        screen.AddChild(columns);

        _tabs = new TabContainer { CustomMinimumSize = new Vector2(860, 640), FocusMode = FocusModeEnum.None };
        _tabs.AddThemeStyleboxOverride("panel", Ui.Glass(0.55f, 12, 18));
        _tabs.AddThemeFontSizeOverride("font_size", 18);
        columns.AddChild(_tabs);
        _radio = new RadioTab();
        _radio.Init(services, Reload);
        _tabs.AddChild(_radio);
        _channels = new ChannelsTab();
        _channels.Init(SetReversed);
        _tabs.AddChild(_channels);
        _switches = new SwitchesTab();
        _switches.Init(services, Reload);
        _tabs.AddChild(_switches);

        BuildControlCheck(columns);
        screen.AddChild(Ui.Button(Ui.T("BACK"), back));
    }

    /// <summary>Opens a tab (0 radio, 1 channels, 2 switches); used by the screenshot mode.</summary>
    public void SelectTab(int index) => _tabs.CurrentTab = Mathf.Clamp(index, 0, _tabs.GetTabCount() - 1);

    void BuildControlCheck(HBoxContainer columns)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        columns.AddChild(column);
        column.AddChild(Ui.Text(Ui.T("RADIO_PREVIEW_TITLE"), 22));

        _aircraft = AircraftCatalog.List(AppPaths.AircraftRoot, out _);
        _picker = new OptionButton();
        int selected = 0;
        for (int i = 0; i < _aircraft.Count; i++)
        {
            _picker.AddItem(_aircraft[i].Name, i);
            if (_aircraft[i].Id == _services.Settings.LastAircraft) selected = i;
        }
        _picker.ItemSelected += index => ShowAircraft(_aircraft[(int)index].Id);
        column.AddChild(Ui.Row(Ui.RowLabel(Ui.T("RADIO_PREVIEW_AIRCRAFT")), _picker));

        _preview = new ControlPreview();
        _preview.Init(new Vector2(720, 420));
        column.AddChild(_preview);
        _switchStatus = Ui.Text("", 18);
        column.AddChild(_switchStatus);
        _previewError = Ui.Text("", 14);
        column.AddChild(_previewError);

        if (_aircraft.Count > 0)
        {
            _picker.Selected = selected;
            ShowAircraft(_aircraft[selected].Id);
        }
    }

    // Keep ShowAircraft and ForceControlCheck exactly as they are in the current RadioScreen.

    void SetReversed(StickFunction function, bool reversed)
    {
        if (_profileGuid is not { } guid || !_services.Radios.SetReversed(guid, function, reversed)) return;
        _services.Router.InvalidateProfiles();
        Reload();
    }

    /// <summary>A tab saved the profile: reload it here, in the tabs and in the preview router.</summary>
    void Reload()
    {
        _profileStale = true;
        _router.InvalidateProfiles();
    }

    JoypadSnapshot? Selected
    {
        get
        {
            foreach (var pad in _pads)
                if (pad.Guid == _radio.SelectedGuid) return pad;
            return _pads.Count > 0 ? _pads[0] : null;
        }
    }

    public override void _Process(double delta)
    {
        _pads = JoypadReader.Poll();
        var pad = Selected;
        _radio.Refresh(_pads, pad);

        if (_profileStale || pad?.Guid != _profileGuid)
        {
            _profileStale = false;
            _profileGuid = pad?.Guid;
            _profile = pad is { } p ? _services.Radios.Load(p.Guid, out _) : null;
        }

        var output = pad is { } selected ? _router.Update(delta, [selected], default, default) : default;
        var inputs = _forcedInputs ?? (output.Source == InputSource.Radio ? output.Controls : ControlInputs.Neutral);
        _preview.Step(delta, inputs);
        _channels.Refresh(pad, _profile, inputs, _preview.Aircraft);
        _switches.Refresh(delta, pad, _profile, output.Source == InputSource.Radio ? _router.Switches : null);
        _switchStatus.Text = string.Join(" · ", SwitchSummary.Status(inputs).Select(Ui.T));

        if (pad is not { } shown)
        {
            _status.Text = Ui.T("RADIO_NO_DEVICE");
            _status.Modulate = Bad;
            return;
        }
        bool calibrated = _profile is not null;
        _status.Text = $"{shown.Name} — {Ui.T(calibrated ? "RADIO_DEVICE_READY" : "RADIO_DEVICE_UNCALIBRATED")}";
        _status.Modulate = calibrated ? Good : Bad;
    }
}
```

Notes for the implementer:
- `default(RouterOutput)` has `Source == InputSource.Keyboard` (enum 0) and `Commands == null`; the code above only reads `Source` and `Controls` from it, so that is safe.
- The preview router gets `default` keyboard input, so its keyboard gear/flap state never changes.
- Write `ShowAircraft` and `ForceControlCheck` out in full (copy from the current file; `ForceControlCheck` uses `_picker`, `_aircraft`, `_forcedInputs`).
- Tab titles come from each tab's `Name` (set in their `Init`, which runs before `AddChild`).

- [ ] **Step 9: Screenshot mode in `Main.cs`**

Next to the `--screenshot-radio-preview` block add:

```csharp
        int radioShot = System.Array.IndexOf(args, "--screenshot-radio");
        if (radioShot >= 0 && radioShot + 2 < args.Length)
        {
            ShowRadio();
            ((RadioScreen)_current!).SelectTab(int.Parse(args[radioShot + 1], CultureInfo.InvariantCulture));
            CaptureAfterFrames(30, args[radioShot + 2]);
            return true;
        }
```

(`--screenshot-radio-preview` contains `--screenshot-radio` only as a prefix; `Array.IndexOf` matches whole elements, so they don't clash.)

- [ ] **Step 10: Build, test, look**

```bash
dotnet test
dotnet build game/SimLab.Game.csproj
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
SCRATCH=$(mktemp -d)
for t in 0 1 2; do "$GODOT" --path game -- --screenshot-radio $t "$SCRATCH/radio-$t.png"; done
"$GODOT" --path game -- --screenshot-radio-preview p51 "$SCRATCH/radio-preview.png"
```

Expected: tests PASS, 0 build errors, four PNGs. Open each with the Read tool and check: three tabs with translated titles, no overlapping or clipped text at 1920×1080, the Switches tab lists all 8 functions (each "not assigned" when no radio is connected), the preview is on the right. Fix layout issues before committing.

- [ ] **Step 11: Commit**

```bash
git add src game tests
git commit -m "feat(radio): tabbed radio screen with positional switch learning"
```

---

### Task 6: Docs and final verification

**Files:**
- Modify: `docs/manual-acceptance.md` (radio section)
- Modify: `docs/dev-setup.md` (command-line table)
- Modify: `README.md` and `aircraft/README.md` only where they describe radio switch binding (grep `-in "switch\|interrupteur"`)

- [ ] **Step 1: Docs**

- `docs/dev-setup.md`, table: add `| \`--screenshot-radio <tab> <png>\` | Radio screen on tab 0 (radio), 1 (channels) or 2 (switches) |`.
- `docs/manual-acceptance.md`, radio section: replace the old switch-binding checks with:
  - Learn **Gear** on a 2-position switch: the row shows two positions; flipping lights each; set Down/Up; in flight the gear follows the switch, and G works only when the gear switch is cleared.
  - Learn **Flaps** on a 3-position switch; set one position to "—"; in flight that position keeps the last flap setting.
  - Learn **Camera** (3 positions) and **OSD**: each flip selects that view / shows or hides the OSD; the flight starts in the view the switch is on.
  - Learn **Throttle cut**: on "Cut" the motor stops whatever the stick and the OSD shows GAZ COUPÉS.
  - Put gear and flaps on the same switch: both follow it.
  - An old profile (bound before this change) still drives gear/flaps/reset/pause/wind; the old view switch shows "not assigned".
- README(s): update any sentence that says a switch is bound by "flipping it up".

- [ ] **Step 2: Full verification**

```bash
dotnet test
dotnet build game/SimLab.Game.csproj
"$GODOT" --headless --path game -- --smoke-boot
"$GODOT" --headless --path game -- --smoke-flight p51 5
```

Expected: all tests PASS; `SIMLAB_BOOT_OK`; `SIMLAB_SMOKE_OK` (a rare exit 134 after `SIMLAB_SMOKE_OK` is a known teardown race — rerun once).

- [ ] **Step 3: Commit**

```bash
git add docs README.md aircraft/README.md
git commit -m "docs(radio): positional switches, radio screenshot mode, acceptance checks"
```
