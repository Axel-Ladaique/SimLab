# Radio window redesign and positional switch assignment

Date: 2026-09-26 · Branch: `feat/radio-window`

## Problem

- Switch assignment records only "the first control that moves up". The pilot cannot say which position means
  gear down, cannot reverse the flap switch (hard-coded low = up, high = landing), and cannot leave a position
  without effect.
- The radio screen shows neither the current assignments nor live switch states; it is one long column with six
  bind buttons in a row.

## Goals (approved 2026-09-26)

- **P1** Each function is assigned to a switch position by position.
- **P2** A position may have no effect ("—").
- **P3** One physical switch may drive several functions.
- **P4** Functions and their states:

  | Function | States | Applies |
  |---|---|---|
  | Gear | Down, Up | held |
  | Flaps | Up, Takeoff, Landing | held |
  | Throttle cut (new) | Armed, Cut | held |
  | Camera | Ground, FPV, Chase | on entering the position |
  | OSD (new) | On, Off | on entering the position |
  | Wind | On, Off | on entering the position |
  | Pause | Running, Paused | on entering the position |
  | Reset | Reset | on entering the position |

- **P5** New functions: throttle cut and OSD (see table).
- **P6** New radio screen: three tabs (Radio, Channels, Switches), 3D control check kept on the right, live
  position highlight.
- **P7** Old profiles are converted on load; nothing is lost.

## Model (`SimLab.Input`)

```csharp
public enum SwitchFunction { Gear, Flaps, ThrottleCut, Camera, Osd, Wind, Pause, Reset }

/// A physical switch: an axis or a button.
public readonly record struct SwitchSource(int? AxisIndex, int? ButtonIndex);

/// One learned position: the raw value it sits at and the state it selects (null = no effect).
public sealed record SwitchPosition(double Value, int? State);

public sealed record SwitchAssignment(SwitchFunction Function, SwitchSource Source, IReadOnlyList<SwitchPosition> Positions);
```

- `SwitchStates.Of(function)` lists the state count and translation keys (`SWITCH_STATE_GEAR_DOWN`, ...). States are
  indices into that list, stored as ints in JSON next to the function name.
- `RadioProfile.Switches` becomes `List<SwitchAssignment>`; at most one assignment per function.
- A button source has two positions at raw values −1 (released) and +1 (pressed).

### Resolving the current position

- The current position is the learned position nearest to the raw value.
- Hysteresis: the resolved position changes only when the raw value is nearer to the new position than to the
  current one by more than 0.1 (raw units), so noise on a 3-position switch never chatters.

### `SwitchBoard` (replaces `SwitchTracker`)

Pure, unit-tested. `Update(RawInputFrame)` returns:

- **Held states** for gear, flaps and throttle cut: the state of the current position; when that position is "—",
  the last held state is kept (so the keyboard can still change it).
- **Events** for camera, OSD, wind, pause and reset: `SwitchEvent(Function, State)` when the resolved position
  changes to a position with a state.
- On the first frame (priming) it sets the held states and emits the entry events for camera, OSD, wind and pause,
  so the aircraft starts as the radio says. Reset never fires on priming.

## Routing (`SimLab.App.Session`)

- `InputRouter` keeps one state per held function. The radio sets it on every frame it is on a stateful position; the
  keyboard (G gear, F flaps) changes it on a key press. "Last event wins" while the switch is on "—" or unbound.
- Throttle cut: when Cut, `ControlInputs.Throttle` is forced to 0 and `ControlInputs.ThrottleCut` is true (for the OSD
  and the control check). Starts Armed on a new flight unless the radio says Cut. No keyboard key.
- `RouterOutput.Actions` (`SwitchAction` list) becomes `Commands`, a list of `FlightCommand`:
  `Reset`, `TogglePause`, `SetPause(bool)`, `ToggleWind`, `SetWind(bool)`, `NextCamera`, `SelectCamera(view)`,
  `SetOsd(bool)`. Keyboard keys produce the toggles, radio events the setters; the H key keeps toggling the OSD in `FlightScene`.
- `FlightSession.Handle` handles reset, pause and wind; `FlightScene` handles camera and OSD (it owns both). OSD
  from the radio updates `Settings.ShowFlightData` like the H key.
- A reset always unpauses the sim, even if a pause switch sits on Paused: the pause setter only fires again once
  the switch is flipped away and back.
- Gear arming in `FlightSession.Tick` (gear up only counts once seen down) is unchanged.

## Migration (P7)

`RadioProfile.FromJson` reads the old `Switches` shape (`Action` + button/axis + `Threshold`) and converts it:

| Old action | New assignment |
|---|---|
| GearUp | Gear: `Threshold−0.5` → Down, `Threshold+0.5` → Up |
| Flaps | Flaps: −1 → Up, 0 → Takeoff, +1 → Landing (switch points at ±0.5 instead of the old ±1/3; identical for real 3-position switches) |
| Pause | Pause: low → Running, high → Paused |
| ToggleWind | Wind: low → Off, high → On |
| Reset | Reset: low → —, high → Reset |
| NextCamera | dropped (no positional equivalent); the Switches tab shows it unassigned |

Buttons use −1/+1 in place of `Threshold∓0.5`. The next save writes the new shape. Nearest-position resolution
with these values switches at the old threshold (±0.05 hysteresis). A button bound to Pause or Wind used to toggle
on each press; it now pauses / turns wind on while held.

## Learning a switch (Switches tab)

1. Click **Learn** on a function row. Prompt: "Move the switch for <function> through all its positions, then
   Next."
2. `SwitchLearner` (pure, tested) watches every axis and button: the source is the one with the largest travel;
   its positions are the plateaus it rested on (value stable within 0.05 for 0.25 s, merged when closer than 0.2).
   2 or 3 positions; a button always gives 2. Fewer than 2 → "No switch moved" and the step repeats.
3. The row shows the detected positions (sorted by raw value, labelled by the live highlight, not up/down, since
   the radio's direction is unknown) with a state picker each, including "—". Defaults: states in order (gear:
   Down/Up; flaps 2 positions: Up/Landing; 3: Up/Takeoff/Landing; camera 3: Ground/FPV/Chase, 2: Ground/FPV;
   Reset: —/Reset; others: first/second state). Flipping the switch highlights the matching position live.
4. **Next** stores the learned assignment at once (no separate Save step); **Clear** removes it. Changing a picker
   also saves at once.

Any switch can be learned for several functions (P3).

## Screen layout (P6)

Left: a `TabContainer` with the menu's glass style (`Ui.Glass`, chips for tabs). Right, always visible: aircraft
picker, 3D control check (gear, flaps and throttle cut driven by the switch states), and a status line showing
gear, flaps and throttle cut ("Gear down · Flaps 0 · Throttle armed"; camera is not shown).

- **Radio**: device list, status (name + calibrated/not), stick mode, calibration wizard (prompt, Calibrate / Next
  / Cancel), EdgeTX help.
- **Channels**: one row per raw axis: index, live bar, what it drives (Throttle, Aileron, ... or the switch
  functions on it), and for the four stick channels the reverse toggle and the control-check readout.
- **Switches**: one row per function: name, positions with their states (active one highlighted), Learn and Clear
  buttons; the learning prompt shows under the table.
- A header line keeps the device status visible on every tab. Back button at the bottom.

All new strings in `game/translations/strings.csv` (fr, en). Code and docs in English.

## Flight side

- OSD: a "THR CUT" / "GAZ COUPÉS" warning while throttle cut is Cut.
- Keyboard unchanged (R, P, W, C, G, F, H).

## Testing

- `SwitchBoard`: nearest position, hysteresis, "—" keeps the held state, entry events, priming (no reset), one
  switch for two functions, button source.
- `SwitchLearner`: 2- and 3-position axis, button, noise, no movement, largest-travel wins.
- `RadioProfile`: new JSON round-trip, validation (unknown state index, fewer than 2 positions, duplicate
  function), migration of every old action including NextCamera dropped.
- `InputRouter`: throttle cut forces 0, keyboard vs radio on "—", commands from keys and radio, new-flight reset.
- `FlightSession` / scene: set/toggle pause and wind.
- Screenshot mode for the radio screen (existing `--screenshot-*` pattern) for the Switches tab.
- `docs/manual-acceptance.md`: learn gear on a 2-position switch, flaps on a 3-position one, camera and OSD, throttle
  cut, and an old profile still working.

## Out of scope

Dual rates, per-aircraft switch assignments, keyboard key for throttle cut, mixing.
