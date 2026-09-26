# Radio window v2: guided setup over the live field

Date: 2026-09-26 · Branch: `feat/radio-window-v2` · Follows `2026-09-26-radio-window-design.md` (switch model, routing
and learning are unchanged; this is the screen only).

## Problem

The tabbed radio screen works but reads as a form: an empty-looking device list, Next/Cancel greyed out all the time,
ten raw bars, drop-downs for switch states, a grey 3D box, a full-width Back bar, and nothing saying where to start.

## Goals (approved 2026-09-26, proposals V1–V7)

- **V1 Guided steps.** A step bar *1 Connect → 2 Calibrate → 3 Switches*; a done step shows a green check; every step
  stays clickable. Each step shows only what it needs and one orange primary action (`Ui.PrimaryButton`).
- **V2 Connect.** One card per detected radio (name, pill *Calibrated* / *To calibrate*, click to select). With no
  radio, an empty-state card explains what to do (USB cable, EdgeTX joystick mode).
- **V3 Calibrate.** Next and Cancel exist only while a calibration runs; a two-stick drawing shows the gesture of the
  current stage and a progress line *Step n/6*.
- **V4 Virtual sticks.** Two gimbal squares with a dot following the calibrated sticks live (stick mode aware). The
  raw axes (bar + what each drives) and the channel directions (reverse + control-check readout) move into a folded
  *Details* section of the Calibrate step.
- **V5 Switch cards.** One card per assigned function: name, switch (*Channel 6* / *Button 3*), one chip per position
  showing its state, the chip of the current position lit blue live; clicking a chip steps its state through the
  function's states and "—". A small × clears the card. Unassigned functions are compact "+ Gear" cards below; clicking
  one opens the learning dialog: a centred glass dialog "Flip the Gear switch through all its positions", the detected
  positions appear as chips as they are found, *Save* (enabled from 2 positions) and *Cancel*.
- **V6 Live field backdrop.** The screen uses the menu's full-window live view (`MenuAircraftView`: field, sun,
  windsock, aircraft flying in place on the right and reacting to the sticks through its mixing and servos). A small
  glass card bottom right holds the aircraft carousel (‹ name ›) and the state chips (gear, flaps, throttle cut).
- **V7 Layout.** One left glass panel like the menu (`Ui.Glass`), a flat *‹ Back* button top left of it, a title, the
  device status pill, the step bar, then the step content (scrolls vertically when needed). No full-width bars.

## Layout (1600×900 base window, scales up)

```
┌ glass panel (620 px) ────────────────┐                                  
│ ‹ Back   Radio        ● TX16S ready  │        live field, aircraft      
│ (1 Connect)─(2 Calibrate)─(3 Switch.)│        flying in place, right    
│                                      │                                  
│  step content                        │                                  
│                                      │              ┌ glass card ─────┐ 
│                                      │              │ ‹ F-16 EDF ›    │ 
│ [      primary action (orange)     ] │              │ Down·Up·Armed   │ 
└──────────────────────────────────────┘              └─────────────────┘ 
```

Primary action per step: Connect → *Calibrate* (or *Continue* when already calibrated); Calibrate → *Start
calibration* / during a run *Next* (Cancel as a flat button beside it) / after it *Continue*; Switches → none (cards
act directly).

Initial step: no radio → Connect; radio without profile → Calibrate; calibrated → Switches.

## Pure helpers (`SimLab.App`, unit-tested)

- `RadioSetup.InitialStep(bool hasDevice, bool calibrated)` and `RadioSetup.IsDone(step, hasDevice, calibrated,
  int assignedSwitches)` (Connect done with a device, Calibrate with a profile, Switches with ≥ 1 assignment).
- `CalibrationPrompts.StepNumber(stage, function)` → 1..6 (center 1, extremes 2, throttle 3, aileron 4, elevator 5,
  rudder 6) and `StepCount = 6`.
- `StickLayout.Place(StickState, StickMode)` → left and right gimbal points in −1..1 (x right, y up): Mode 2 left =
  (rudder, throttle·2−1), right = (aileron, elevator); Mode 1 left = (rudder, elevator), right = (aileron,
  throttle·2−1).
- `StickLayout.Gesture(stage, function, StickMode)` → which gimbal(s) and target point the drawing shows: center →
  both at (0,0); extremes → both, circle; identify → one gimbal, target at its positive end (full throttle up, right,
  pull back = down, right).
- `SwitchStates.Next(function, int? state)` → the next state in order, "—" after the last, first after "—".

## Godot pieces (`game/Scripts/Radio`)

- `RadioScreen` — shell: live view, left panel (back, title, status pill, step bar, step host, primary button), bottom
  right aircraft card, learning dialog host; polls pads, keeps the selected profile and a private `InputRouter` for
  the live controls (as today).
- `StepBar` — three chips with number/check, click to switch step.
- `ConnectStep`, `CalibrateStep`, `SwitchesStep` — step contents (replace `RadioTab`, `ChannelsTab`, `SwitchesTab`).
- `SticksView` — draws the two gimbals, live dots and the gesture target (custom `_Draw`).
- `SwitchCard`, `LearnDialog` — a function card with chips; the modal learning dialog over the whole screen.
- `ControlPreview` stays for the flight-free `--screenshot-radio-preview`; the radio screen now uses
  `MenuAircraftView`.

Existing behaviour kept: calibration wizard logic and saving (keeps switches), reverse toggles, control-check
readouts, switch learning (`SwitchLearner`) and saving (`SetSwitch`/`ClearSwitch`, invalidate routers), live position
from the preview router's `SwitchBoard`, `ForceControlCheck` for screenshots.

## Screenshots

- `--screenshot-radio <step 0-2> <png>` (was tabs) opens that step.
- `--screenshot-radio-demo <png>` shows the Switches step with a built-in demo profile (gear on a 2-position axis,
  flaps on a 3-position axis, flaps on the middle position) and the learning dialog closed, so the cards can be checked
  without a radio. `--screenshot-radio-learn <png>` shows the learning dialog with two demo positions found.

## Strings

New keys (fr, en) for steps, empty state, pills, *Details*, *Save*, dialog text, *Start calibration*, *Continue*,
*Step {0}/{1}*, *+ {0}* is built in code. Obsolete tab keys (`RADIO_TAB_*`) are removed.

## Testing

Unit tests for every pure helper above; translation-key test extended; screenshots of the three steps, the demo and
the learning dialog at 1600×900 checked by eye (no clipping, no horizontal scroll, readable over the field); full
`dotnet test`, smoke boot and smoke flight at the end.

## Out of scope

Real icons (no icon font in the project: glyphs like ✓ ‹ × + only), animations beyond the live dots, gamepad
navigation of the screen.
