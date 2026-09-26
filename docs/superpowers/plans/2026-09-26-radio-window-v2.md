# Radio Window v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the radio screen as a guided three-step setup (Connect, Calibrate, Switches) in one glass panel over the menu's live field, with virtual sticks, switch cards with state chips and a learning dialog.

**Architecture:** Pure helpers in `SimLab.App` (step logic, calibration progress, stick placement and gestures, state cycling) are unit-tested. The Godot screen is a shell (`RadioScreen`) hosting the live view (`MenuAircraftView`), a left glass panel with a step bar and one step control at a time, an aircraft card bottom right and a modal learning dialog. The switch model, routing and learning logic from the previous branch are reused unchanged.

**Tech Stack:** C# / .NET 10, xUnit, Godot 4.7 .NET (code-built UI, `_Draw` for the sticks).

**Spec:** `docs/superpowers/specs/2026-09-26-radio-window-v2-design.md` (and, for the switch model it builds on, `docs/superpowers/specs/2026-09-26-radio-window-design.md`).

## Global Constraints

- Code, comments, docs in English; UI strings in `game/translations/strings.csv`, columns `keys,fr,en`, quote fields containing commas.
- Work only in `/Users/axel.ldq/3_SYMLAB/.worktrees/radio-v2` (branch `feat/radio-window-v2`); never touch `~/3_SYMLAB` itself.
- `dotnet` may need `export PATH="/usr/local/share/dotnet:$PATH";`. Tests: `dotnet test tests/SimLab.Input.Tests && dotnet test tests/SimLab.App.Tests`; game: `dotnet build game/SimLab.Game.csproj`. Only Task 4 runs the full `dotnet test` (Flight tests take ~15 min). Baseline on this branch: 72 Input + 338 App tests passing.
- Godot: `GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot`; if `game/.godot` is missing run `"$GODOT" --headless --path game --import` once. Screenshots are taken with the in-app `--screenshot-*` modes into a `mktemp -d` dir and inspected with the Read tool; the base window is 1600×900.
- Reuse the existing look: `Ui.Glass`, `Ui.Chip`, `Ui.PrimaryButton`, `Ui.FlatButton`, `Ui.Text`; colours `Good (0.35,0.85,0.45)`, `Bad (0.95,0.40,0.35)`, accent blue `(0.22,0.54,0.87)`, chip blue fill `(0.09,0.37,0.65)`, muted text `(0.72,0.73,0.76)`.
- No icon font: glyphs only (✓ ‹ › × + ●).
- File-scoped namespaces, `///` summaries on public types, collection expressions.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- ENOSPC → stop and report.

---

### Task 1: Pure helpers

**Files:**
- Create: `src/SimLab.App/Ui/RadioSetup.cs`
- Create: `src/SimLab.App/Ui/StickLayout.cs`
- Modify: `src/SimLab.App/Ui/CalibrationPrompts.cs` (add `StepNumber`, `StepCount`)
- Modify: `src/SimLab.Input/SwitchModel.cs` (add `SwitchStates.Next`)
- Test: `tests/SimLab.App.Tests/Ui/RadioSetupTests.cs`, `tests/SimLab.App.Tests/Ui/StickLayoutTests.cs`, `tests/SimLab.Input.Tests/SwitchBoardTests.cs` (append)

**Interfaces:**
- Produces: `enum RadioStep { Connect, Calibrate, Switches }`; `RadioSetup.InitialStep(bool hasDevice, bool calibrated) → RadioStep`; `RadioSetup.IsDone(RadioStep, bool hasDevice, bool calibrated, int assignedSwitches) → bool`; `CalibrationPrompts.StepCount` (6), `CalibrationPrompts.StepNumber(CalibrationWizard.Stage, StickFunction?) → int`; `readonly record struct StickPoint(double X, double Y)`; `StickLayout.Place(StickState, StickMode) → (StickPoint Left, StickPoint Right)`; `enum StickSide { Left, Right }`; `readonly record struct StickGesture(bool Left, bool Right, bool Circle, StickPoint Target)`; `StickLayout.Gesture(CalibrationWizard.Stage, StickFunction?, StickMode) → StickGesture`; `SwitchStates.Next(SwitchFunction, int?) → int?`.

- [ ] **Step 1: Failing tests**

`tests/SimLab.App.Tests/Ui/RadioSetupTests.cs`:

```csharp
using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.App.Tests.Ui;

public class RadioSetupTests
{
    [Theory]
    [InlineData(false, false, RadioStep.Connect)]
    [InlineData(true, false, RadioStep.Calibrate)]
    [InlineData(true, true, RadioStep.Switches)]
    public void Opens_on_the_first_step_left_to_do(bool device, bool calibrated, RadioStep expected)
        => Assert.Equal(expected, RadioSetup.InitialStep(device, calibrated));

    [Fact]
    public void Steps_are_done_with_a_device_a_profile_and_one_switch()
    {
        Assert.False(RadioSetup.IsDone(RadioStep.Connect, false, false, 0));
        Assert.True(RadioSetup.IsDone(RadioStep.Connect, true, false, 0));
        Assert.False(RadioSetup.IsDone(RadioStep.Calibrate, true, false, 0));
        Assert.True(RadioSetup.IsDone(RadioStep.Calibrate, true, true, 0));
        Assert.False(RadioSetup.IsDone(RadioStep.Switches, true, true, 0));
        Assert.True(RadioSetup.IsDone(RadioStep.Switches, true, true, 1));
        Assert.False(RadioSetup.IsDone(RadioStep.Calibrate, false, true, 3));
    }

    [Theory]
    [InlineData(CalibrationWizard.Stage.Center, null, 1)]
    [InlineData(CalibrationWizard.Stage.Extremes, null, 2)]
    [InlineData(CalibrationWizard.Stage.Identify, StickFunction.Throttle, 3)]
    [InlineData(CalibrationWizard.Stage.Identify, StickFunction.Aileron, 4)]
    [InlineData(CalibrationWizard.Stage.Identify, StickFunction.Elevator, 5)]
    [InlineData(CalibrationWizard.Stage.Identify, StickFunction.Rudder, 6)]
    [InlineData(CalibrationWizard.Stage.Done, null, 6)]
    public void Calibration_progress_counts_six_steps(CalibrationWizard.Stage stage, StickFunction? function, int step)
    {
        Assert.Equal(6, CalibrationPrompts.StepCount);
        Assert.Equal(step, CalibrationPrompts.StepNumber(stage, function));
    }
}
```

`tests/SimLab.App.Tests/Ui/StickLayoutTests.cs`:

```csharp
using SimLab.App.Settings;
using SimLab.App.Ui;
using SimLab.Input;

namespace SimLab.App.Tests.Ui;

public class StickLayoutTests
{
    static readonly StickState Sticks = new(Throttle: 1, Aileron: 0.5, Elevator: -0.25, Rudder: -1);

    [Fact]
    public void Mode_2_has_throttle_and_rudder_on_the_left()
    {
        var (left, right) = StickLayout.Place(Sticks, StickMode.Mode2);
        Assert.Equal(new StickPoint(-1, 1), left);
        Assert.Equal(new StickPoint(0.5, 0.25), right); // elevator −0.25 (pushed) is drawn up
    }

    [Fact]
    public void Mode_1_has_elevator_on_the_left_and_throttle_on_the_right()
    {
        var (left, right) = StickLayout.Place(Sticks, StickMode.Mode1);
        Assert.Equal(new StickPoint(-1, 0.25), left);
        Assert.Equal(new StickPoint(0.5, 1), right);
    }

    [Fact]
    public void Idle_throttle_sits_at_the_bottom()
        => Assert.Equal(-1, StickLayout.Place(new StickState(0, 0, 0, 0), StickMode.Mode2).Left.Y);

    [Fact]
    public void Center_and_extremes_show_both_sticks()
    {
        Assert.Equal(new StickGesture(true, true, false, new StickPoint(0, 0)),
            StickLayout.Gesture(CalibrationWizard.Stage.Center, null, StickMode.Mode2));
        Assert.Equal(new StickGesture(true, true, true, new StickPoint(0, 0)),
            StickLayout.Gesture(CalibrationWizard.Stage.Extremes, null, StickMode.Mode2));
    }

    [Theory]
    [InlineData(StickMode.Mode2, StickFunction.Throttle, true, 0, 1)]
    [InlineData(StickMode.Mode1, StickFunction.Throttle, false, 0, 1)]
    [InlineData(StickMode.Mode2, StickFunction.Aileron, false, 1, 0)]
    [InlineData(StickMode.Mode2, StickFunction.Elevator, false, 0, -1)]
    [InlineData(StickMode.Mode1, StickFunction.Elevator, true, 0, -1)]
    [InlineData(StickMode.Mode2, StickFunction.Rudder, true, 1, 0)]
    public void Identify_points_one_stick_to_its_positive_end(StickMode mode, StickFunction function, bool left, double x, double y)
        => Assert.Equal(new StickGesture(left, !left, false, new StickPoint(x, y)),
            StickLayout.Gesture(CalibrationWizard.Stage.Identify, function, mode));
}
```

(Elevator "pull back toward you" is the positive elevator command, drawn as the stick pulled down on the gimbal.)

Append to `tests/SimLab.Input.Tests/SwitchBoardTests.cs` (inside the class):

```csharp
    [Theory]
    [InlineData(SwitchFunction.Gear, 0, 1)]
    [InlineData(SwitchFunction.Gear, 1, null)]
    [InlineData(SwitchFunction.Gear, null, 0)]
    [InlineData(SwitchFunction.Flaps, 1, 2)]
    [InlineData(SwitchFunction.Reset, 0, null)]
    [InlineData(SwitchFunction.Reset, null, 0)]
    public void Next_state_cycles_through_the_states_then_no_effect(SwitchFunction f, int? state, int? next)
        => Assert.Equal(next, SwitchStates.Next(f, state));
```

- [ ] **Step 2: Run, expect build failure**

Run: `dotnet test tests/SimLab.App.Tests --filter "RadioSetupTests|StickLayoutTests"` and `dotnet test tests/SimLab.Input.Tests --filter Next_state`
Expected: build FAIL (types not defined).

- [ ] **Step 3: Implement**

`src/SimLab.App/Ui/RadioSetup.cs`:

```csharp
namespace SimLab.App.Ui;

/// <summary>The radio screen's guided steps.</summary>
public enum RadioStep { Connect, Calibrate, Switches }

/// <summary>Which step the radio screen opens on and which steps show as done.</summary>
public static class RadioSetup
{
    /// <summary>The first step left to do: connect a radio, then calibrate it, then its switches.</summary>
    public static RadioStep InitialStep(bool hasDevice, bool calibrated) =>
        !hasDevice ? RadioStep.Connect : !calibrated ? RadioStep.Calibrate : RadioStep.Switches;

    /// <summary>Connect is done with a radio, Calibrate with its profile, Switches with at least one assignment.</summary>
    public static bool IsDone(RadioStep step, bool hasDevice, bool calibrated, int assignedSwitches) => step switch
    {
        RadioStep.Connect => hasDevice,
        RadioStep.Calibrate => hasDevice && calibrated,
        _ => hasDevice && calibrated && assignedSwitches > 0,
    };
}
```

`src/SimLab.App/Ui/StickLayout.cs`:

```csharp
using SimLab.App.Settings;
using SimLab.Input;

namespace SimLab.App.Ui;

/// <summary>A point on a stick gimbal drawing, −1..1 each way: x right, y up.</summary>
public readonly record struct StickPoint(double X, double Y);

/// <summary>What the calibration drawing shows: which sticks, whether to circle them, and where to push.</summary>
public readonly record struct StickGesture(bool Left, bool Right, bool Circle, StickPoint Target);

/// <summary>Places the calibrated sticks on the two gimbals of the pilot's stick mode, and the calibration gestures.</summary>
public static class StickLayout
{
    /// <summary>Mode 2: left = rudder / throttle, right = aileron / elevator. Mode 1: left = rudder / elevator,
    /// right = aileron / throttle. Throttle 0..1 maps to bottom..top; pulling the elevator (positive) is down.</summary>
    public static (StickPoint Left, StickPoint Right) Place(StickState s, StickMode mode)
    {
        double throttle = s.Throttle * 2 - 1;
        double elevator = -s.Elevator;
        return mode == StickMode.Mode2
            ? (new StickPoint(s.Rudder, throttle), new StickPoint(s.Aileron, elevator))
            : (new StickPoint(s.Rudder, elevator), new StickPoint(s.Aileron, throttle));
    }

    public static StickGesture Gesture(CalibrationWizard.Stage stage, StickFunction? function, StickMode mode)
    {
        if (stage == CalibrationWizard.Stage.Center) return new StickGesture(true, true, false, new StickPoint(0, 0));
        if (stage != CalibrationWizard.Stage.Identify || function is not { } f)
            return new StickGesture(true, true, true, new StickPoint(0, 0));
        bool left = CalibrationPrompts.StickSideKey(f, mode) == "STICK_LEFT";
        var target = f switch
        {
            StickFunction.Throttle => new StickPoint(0, 1),
            StickFunction.Elevator => new StickPoint(0, -1),
            _ => new StickPoint(1, 0),
        };
        return new StickGesture(left, !left, false, target);
    }
}
```

`CalibrationPrompts` — add:

```csharp
    /// <summary>Number of calibration steps shown to the pilot: centre, extremes, then the four sticks.</summary>
    public const int StepCount = 6;

    /// <summary>1-based step of the calibration progress line.</summary>
    public static int StepNumber(CalibrationWizard.Stage stage, StickFunction? function) => stage switch
    {
        CalibrationWizard.Stage.Center => 1,
        CalibrationWizard.Stage.Extremes => 2,
        CalibrationWizard.Stage.Identify => function switch
        {
            StickFunction.Throttle => 3,
            StickFunction.Aileron => 4,
            StickFunction.Elevator => 5,
            _ => 6,
        },
        _ => StepCount,
    };
```

`SwitchStates` (in `SwitchModel.cs`) — add:

```csharp
    /// <summary>The state after <paramref name="state"/> when the pilot clicks a position: the next state, "no
    /// effect" (null) after the last one, the first state after "no effect".</summary>
    public static int? Next(SwitchFunction function, int? state) =>
        state is not int s ? 0 : s + 1 < Count(function) ? s + 1 : null;
```

- [ ] **Step 4: Run tests** — same commands, expected PASS; then the two full projects.

- [ ] **Step 5: Commit** — `feat(radio): setup steps, stick layout and state cycling helpers`.

---

### Task 2: Radio widgets

**Files:**
- Create: `game/Scripts/Radio/StepBar.cs`, `game/Scripts/Radio/SticksView.cs`, `game/Scripts/Radio/SwitchCard.cs`, `game/Scripts/Radio/LearnDialog.cs`
- Modify: `game/Scripts/Ui.cs` (small helpers only if needed: `Ui.Pill(text, color)`)
- Modify: `game/translations/strings.csv`, `tests/SimLab.App.Tests/Localization/TranslationTests.cs`

**Interfaces:**
- Consumes: Task 1 helpers; `SwitchStates`, `SwitchSummary.SourceKey/SourceNumber`, `SwitchAssignment`, `LearnedSwitch`.
- Produces (used by Task 3):
  - `StepBar : HBoxContainer` — `Init(System.Action<RadioStep> selected)`, `Show(RadioStep current, System.Func<RadioStep, bool> done)`.
  - `SticksView : Control` — `Init(Vector2 size)`, `Show(StickPoint left, StickPoint right, StickGesture? gesture)`; draws two rounded gimbal squares side by side, a cross hair, a live dot per stick (accent blue) and, when a gesture is given, the target (orange ring at `Target` on the gimbals it names, or a circle arrow when `Circle`); gimbals not named by the gesture are dimmed.
  - `SwitchCard : PanelContainer` — `Init(SwitchAssignment assignment, System.Action<int> chipClicked, System.Action clear)`, `Light(int? position)`; glass sub-panel (`Ui.Glass(0.35f, 10, 12)`): header row with the function name (18 px), the source (`RADIO_SOURCE_AXIS`/`BUTTON`, muted 14 px) and a flat `×` button; a row of chips (one per position, text = state name or `SWITCH_NO_EFFECT`); `Light` fills the chip of the current position with the chip blue, others outlined.
  - `LearnDialog : Control` — full-rect modal: dark scrim `Color(0,0,0,0.55)` catching clicks, centred glass box 560 px wide with title `RADIO_LEARN_TITLE` ("{0}"), instruction `RADIO_LEARN_PROMPT`, a row of found-position chips (`Show(IReadOnlyList<double>? positions)` — shows "Position 1", "Position 2"… with the raw value hidden; empty state `RADIO_LEARN_WAITING`), buttons *Save* (`Ui.PrimaryButton`, disabled until 2 positions) and *Cancel* (`Ui.FlatButton`); `Init(string functionName, System.Action save, System.Action cancel)`.
  - `+ Function` add-cards are built in Task 3 with `Ui.FlatButton("+ " + name, …)`.

- [ ] **Step 1: Strings.** Add rows (fr, en):

```csv
RADIO_STEP_CONNECT,Connecter,Connect
RADIO_STEP_CALIBRATE,Calibrer,Calibrate
RADIO_STEP_SWITCHES,Interrupteurs,Switches
RADIO_BACK,‹ Retour,‹ Back
RADIO_PILL_READY,Calibrée,Calibrated
RADIO_PILL_TODO,À calibrer,To calibrate
RADIO_EMPTY_TITLE,Aucune radio branchée,No radio connected
RADIO_EMPTY_BODY,"Branchez la radio en USB et choisissez « USB Joystick (HID) » sur la radio. Elle apparaîtra ici.","Plug the radio in over USB and choose “USB Joystick (HID)” on the radio. It will show up here."
RADIO_CONTINUE,Continuer,Continue
RADIO_START_CALIBRATION,Lancer la calibration,Start calibration
RADIO_CAL_PROGRESS,Étape {0}/{1},Step {0}/{1}
RADIO_DETAILS,Détails,Details
RADIO_SAVE,Enregistrer,Save
RADIO_LEARN_TITLE,Apprendre « {0} »,Learn “{0}”
RADIO_LEARN_WAITING,Aucune position détectée pour l'instant.,No position found yet.
RADIO_POSITION,Position {0},Position {0}
RADIO_ADD_SWITCH,Ajouter un interrupteur,Add a switch
RADIO_CALIBRATED_HINT,"Radio calibrée. Vérifiez les manches ci-dessous, ou relancez la calibration.","Radio calibrated. Check the sticks below, or calibrate again."
```

and change `RADIO_LEARN_PROMPT` to `"Basculez l'interrupteur dans toutes ses positions en marquant un temps sur chacune.","Flip the switch through all its positions, pausing on each."` (no `{0}` any more; the title names the function). Add every new key to `TranslationTests.Every_key_used_by_the_app_exists`. Remove `RADIO_TAB_RADIO`, `RADIO_TAB_CHANNELS`, `RADIO_TAB_SWITCHES`, `RADIO_LEARN_DONE`, `RADIO_SWITCHES_HELP`, `RADIO_LEARN`, `RADIO_CLEAR` rows **in Task 3** (when their last users go), not here.

- [ ] **Step 2: Widgets.** Write the four classes per the interface above. Guidance:
  - `StepBar`: three `Button`s styled like `Ui.Chip` but not in a `ButtonGroup` (selection is driven by `Show`); text `"1  Connect"`, or `"✓  Connect"` when done; current step = blue fill, done = green outline `Good`, others outlined; separators `"—"` muted between chips; `FocusMode.None`.
  - `SticksView._Draw`: gimbal size = min(height, (width − gap)/2); rounded rect via `DrawStyleBox` with a `StyleBoxFlat` (bg `(1,1,1,0.05)`, border `(1,1,1,0.25)`, radius 12); cross hair lines `(1,1,1,0.12)`; dot radius 9 accent blue; target: ring radius 13 orange `(0.85,0.35,0.19)` width 3; circle gesture: `DrawArc` radius 0.8·half-size with a small arrow head; dim a gimbal the gesture doesn't name with `Modulate`-like alpha 0.35 on its strokes. Call `QueueRedraw()` in `Show` only when a value changed.
  - `SwitchCard` chips: `Button` with `ToggleMode=false`, `FocusMode.None`, styles like `Ui.Chip` (reuse `Ui` fill helpers — make `Ui.Fill`/`Ui.Style` `internal static` or add `Ui.ChipStyles(Button, bool on)` so no StyleBox code is duplicated).
  - `LearnDialog`: `MouseFilter = Stop` on the scrim; `Visible` toggled by Task 3.

- [ ] **Step 3: Verify** — `dotnet test tests/SimLab.App.Tests --filter TranslationTests` PASS; `dotnet build game/SimLab.Game.csproj` 0 errors 0 warnings. (Widgets are exercised on screen in Task 3.)

- [ ] **Step 4: Commit** — `feat(radio): step bar, stick view, switch card and learning dialog widgets`.

---

### Task 3: Guided radio screen over the live field

**Files:**
- Rewrite: `game/Scripts/Radio/RadioScreen.cs`
- Create: `game/Scripts/Radio/ConnectStep.cs`, `game/Scripts/Radio/CalibrateStep.cs`, `game/Scripts/Radio/SwitchesStep.cs`
- Delete: `game/Scripts/Radio/RadioTab.cs`, `game/Scripts/Radio/ChannelsTab.cs`, `game/Scripts/Radio/SwitchesTab.cs`
- Modify: `game/Scripts/Main.cs` (screenshot modes), `game/translations/strings.csv` (remove obsolete keys listed in Task 2 Step 1), `tests/SimLab.App.Tests/Localization/TranslationTests.cs`

**Interfaces:**
- Consumes: Task 1 and Task 2 types; `MenuAircraftView` (`Init(FlightConditions, FieldMap)`, `ShowAircraft(AircraftDefinition, SoundSpec)`, `Step(dt, inputs)`, `Aircraft`); `InputRouter`, `SwitchBoard.Position`, `SwitchLearner`, `CalibrationWizard`, `ControlCheck`, `SwitchSummary`, `RadioProfile.SetSwitch/ClearSwitch`, `Services.Radios/Router/Settings`.
- Produces: `RadioScreen.SelectStep(RadioStep)`, `RadioScreen.ShowDemo(bool learning)`, `RadioScreen.ForceControlCheck(string aircraftId, ControlInputs inputs)` (kept).

- [ ] **Step 1: Shell (`RadioScreen`).**
  - Full-rect `MenuAircraftView` first (`Init(services.Settings.Conditions, FieldCatalog.Load(services.Settings.LastField))`), then the left panel exactly like `MainMenu` (anchors LeftWide, margin 32, width 620, `Ui.Glass()`), containing: header row (`Ui.FlatButton(Ui.T("RADIO_BACK"), back)`, title `RADIO_TITLE` 26 px, spacer, status pill — device name + `RADIO_PILL_READY` green / `RADIO_PILL_TODO` red / `RADIO_NO_DEVICE` red), the `StepBar`, a vertical `ScrollContainer` (horizontal scroll disabled, expand) hosting the current step control, and the primary button slot at the bottom.
  - Bottom-right aircraft card (anchors BottomRight, margin 32, width 420, `Ui.Glass(0.55f, 12, 14)`): `‹ name ›` carousel (`Ui.FlatButton`) over `AircraftCatalog.List`, selecting calls `ShowAircraft(definition, SoundSpecLoader.Load(folder))`, remembers nothing; below it a row of three small pills from `SwitchSummary.Status(inputs)`.
  - `LearnDialog` added last (on top), hidden.
  - `_Process`: poll pads, selected pad (from `ConnectStep.SelectedGuid`, else first), reload profile when stale or pad changed (as today), private `InputRouter` for live controls (`_forcedInputs` overrides), `_view.Step(delta, inputs)`, refresh the step bar (`RadioSetup.IsDone`), the status pill, the current step (`Refresh(...)`), the card pills, and the learning dialog.
  - Initial step: `RadioSetup.InitialStep` on the first frame that polled pads (not in `Init`), unless `SelectStep` was called.
  - Primary button per spec §Layout; it is rebuilt when the step or the step's own state changes (each step exposes `PrimaryText` (null = hidden) and `Primary()`).
- [ ] **Step 2: `ConnectStep`.** One card per pad (glass sub-panel, name 20 px, pill ready/todo, whole card clickable → select; selected card has the accent border). No pad: empty-state card (`RADIO_EMPTY_TITLE`, `RADIO_EMPTY_BODY`, then `RADIO_HELP` muted). Stick mode row (`RADIO_MODE`, OptionButton, saves settings) under the cards. Primary: `RADIO_CALIBRATE` when the selected pad has no profile, else `RADIO_CONTINUE` (→ Switches); hidden without a pad.
- [ ] **Step 3: `CalibrateStep`.** `SticksView` (≈ 520×240) at the top, fed every frame with `StickLayout.Place(profile.Read(frame), mode)` when a profile exists, else centred dots; during a run `StickLayout.Gesture(stage, function, mode)`. Under it: during a run the progress line `RADIO_CAL_PROGRESS` (`StepNumber`, `StepCount`) and the prompt (`CalibrationPrompts` as today), a flat *Cancel*; otherwise `RADIO_CALIBRATED_HINT` or nothing. A folded *Details* section (flat toggle `RADIO_DETAILS ▸/▾`) containing the raw axes (index, bar, what it drives — port from `ChannelsTab`) and the four channel-direction rows (name, reverse toggle, readout — port from `ChannelsTab`). Primary: `RADIO_START_CALIBRATION` when idle (calibrated or not), `RADIO_NEXT` during a run, and after a successful run switch the primary to `RADIO_CONTINUE` (→ Switches). Port the wizard logic (pinning by guid, disconnect cancels, `CAL_FAILED`, keep switches on save, invalidate routers) from `RadioTab` unchanged.
- [ ] **Step 4: `SwitchesStep`.** A `SwitchCard` per assigned function (in `SwitchStates.All` order); chip click → `SetState(function, position, SwitchStates.Next(function, current))` saving at once; × → `ClearSwitch`; `Light(board?.Position(function))` every frame. Then `RADIO_ADD_SWITCH` label and a flow of `+ Name` flat buttons (use `HFlowContainer`) for unassigned functions; click → open the `LearnDialog` for that function with a fresh `SwitchLearner` pinned to the selected pad. While the dialog is open, feed the learner every frame and `Show(learner.Result()?.Positions)`; *Save* → build the assignment with `SwitchStates.Defaults` and `SetSwitch`, close; *Cancel* or pad change → close. Port save/reload logic from `SwitchesTab` (load fresh, edit, save, invalidate both routers, reload).
- [ ] **Step 5: Screenshot modes (`Main.cs`).** `--screenshot-radio <step> <png>` → `SelectStep((RadioStep)step)` (TryParse, clamp 0..2). New `--screenshot-radio-demo <png>` → `ShowDemo(false)`; `--screenshot-radio-learn <png>` → `ShowDemo(true)`. `ShowDemo` injects a pad `new JoypadSnapshot("demo", "Demo TX16S", new RawInputFrame([0,0,-1,0,0,1,0,0], new bool[8]))` and an in-memory profile (identity calibration on axes 0–3 as aileron, elevator, throttle, rudder; gear on axis 5 two positions (−1 Down, 1 Up); flaps on axis 4 three positions (−1 Up, 0 Takeoff, 1 Landing)) used instead of polling and the store; with `learning`, opens the dialog for Throttle cut and shows two found positions (−1, 1). Nothing is saved in demo mode.
- [ ] **Step 6: Remove** the three old tab files and the obsolete strings; `grep` that nothing references them.
- [ ] **Step 7: Verify.** `dotnet test tests/SimLab.Input.Tests && dotnet test tests/SimLab.App.Tests`, `dotnet build game/SimLab.Game.csproj`. Screenshots into a `mktemp -d` dir: `--screenshot-radio 0|1|2`, `--screenshot-radio-demo`, `--screenshot-radio-learn`, `--screenshot-radio-preview p51`. Read each: panel readable over the field, no clipping, no horizontal scroll, aircraft visible right of the panel and not hidden by the bottom-right card, chips/dialog legible. Fix layout until they are.
- [ ] **Step 8: Commit** — `feat(radio): guided radio setup over the live field`.

---

### Task 4: Docs and final verification

**Files:** `docs/dev-setup.md` (screenshot modes), `docs/manual-acceptance.md` (radio section: steps, cards, dialog), `README.md`/`aircraft/README.md` only where they describe the radio screen's tabs.

- [ ] **Step 1:** Update the docs (steps instead of tabs; chips instead of drop-downs; demo/learn screenshot flags).
- [ ] **Step 2:** Full `dotnet test` (expect all passing + the 1 known skip `Sport_full_aileron_roll_helix_angle_is_realistic`), `dotnet build game/SimLab.Game.csproj`, `"$GODOT" --headless --path game -- --smoke-boot` (`SIMLAB_BOOT_OK`), `--smoke-flight p51 5` (`SIMLAB_SMOKE_OK`; a rare exit 134 after it is a known teardown race, rerun once).
- [ ] **Step 3: Commit** — `docs(radio): guided radio setup`.
