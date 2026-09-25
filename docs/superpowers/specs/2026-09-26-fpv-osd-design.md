# FPV OSD HUD — Design

Date: 2026-09-26. Branch: `feat/fpv-osd` (from `main` @ fef68c9).

## Goal

Replace the flight HUD with an OSD in the style of an FPV flight controller (Betaflight):
- white monospace figures with a black outline around the edges of the screen;
- an artificial horizon in the FPV view.

Add two mouse-clickable buttons in flight (HUD on/off, camera view) and a mouse cursor that hides itself when idle.

## 1. Data — `OsdData` (SimLab.App.Ui)

A pure, tested record computed every frame:

```csharp
public sealed record OsdData(
    double AirspeedKmh, double HeightM, double VarioMs,
    double HeadingDeg, double RollDeg, double PitchDeg,
    double HomeDistanceM, double HomeRelativeBearingDeg,
    double ThrottlePercent, double? BatteryVolts, double? CurrentAmps, double? ConsumedMah,
    double FlightTimeSeconds);
```

`OsdData.From(Aircraft aircraft, RigidBodyState display, double heightAgl, double flightTimeSeconds, double throttle,
Vec3 pilot)` computes each field as follows.

| Field | Source |
|---|---|
| `AirspeedKmh` | `aircraft.AirData.Airspeed · 3.6` |
| `HeightM` | `max(0, heightAgl)` |
| `VarioMs` | `display.Velocity.Z` (world up, m/s) |
| `HeadingDeg`, `RollDeg`, `PitchDeg` | `Attitude.FromOrientation(display.Orientation)` in degrees. Heading is 0–360, clockwise from north. |
| `HomeDistanceM` | Horizontal distance from the aircraft to `pilot`. |
| `HomeRelativeBearingDeg` | Compass bearing to the pilot minus the heading, wrapped to (−180, 180]. Positive means the pilot is to the right. |
| `ThrottlePercent` | `clamp(throttle, 0, 1) · 100` |
| `BatteryVolts` | Battery telemetry voltage, or the open-circuit voltage at the current state of charge before the first step (same fallback as today). Null without a power plant. |
| `CurrentAmps` | `Telemetry.BatteryCurrent`. Null without a power plant. |
| `ConsumedMah` | `(1 − StateOfCharge) · CapacityAh · 1000`. It returns to 0 on a reset, because the power plant resets its charge. Null without a power plant. |
| `FlightTimeSeconds` | `flightTimeSeconds` |

`FlightDataFormatter.Format`, its keys `HUD_AIRSPEED`…`HUD_TIMER` and their tests are removed: the OSD replaces them.
`FlightDataFormatter.CrashKey` stays.

## 2. Display — `FlightHud` rewrite (game)

### 2.1 Style

- **Font:** a `SystemFont` with the names `Menlo`, `Consolas`, `DejaVu Sans Mono` and `monospace`, size 22. The
  small help line uses 15.
- **Colour:** white text with a black outline (`LabelSettings`, outline size 5).
- **Numbers:** invariant culture, no labels, only units or short symbols, as on a real OSD.

### 2.2 Layout (1600×900 reference; anchored so wider screens keep the corners)

| Place | Content |
|---|---|
| Top centre | Home arrow (drawn, rotated by `HomeRelativeBearingDeg`) and `45 m` under it; heading `275°` below |
| Middle left | Airspeed `52 km/h` |
| Middle right | Height `24 m`; vario `↑ 2.1 m/s` / `↓ 0.4 m/s` under it |
| Bottom left | `11.8 V  14.2 A  312 mAh` (`— V` when there is no power plant) |
| Bottom right | Throttle `GAZ 65 %` / `THR 65 %` (key `OSD_THROTTLE` "GAZ"/"THR") and the timer `02:14` |
| Bottom line | Input source and the keyboard help (existing `HUD_INPUT`/`HUD_KEYBOARD` text), size 15 |
| Centre (FPV only) | Artificial horizon, pitch ladder and crosshair, see below |

### 2.3 Artificial horizon (FPV view only)

It is drawn by an `OsdHorizon : Control` in `_Draw`, as a synthetic indicator the way Betaflight draws it. It is
not aligned with the camera image, which has its own uptilt.

- **Crosshair:** a small fixed `—(+)—` at the screen centre.
- **Horizon line:** 360 px wide with a centre gap. It is moved by the pitch (8 px per degree) and rotated by −roll
  around the screen centre.
- **Pitch ladder:** short dashed lines every 10° from −30° to +30°, labelled `10`, `20`, `30` (negative labels
  below the horizon). It moves and rotates with the horizon line.
  - Everything is clipped to a 420×300 px central box, so the ladder never reaches the side figures.

In the ground and chase views the horizon, ladder and crosshair are hidden; everything else is identical.

### 2.4 Unchanged

The PAUSE banner, the crash panel and the F3 diagnostics overlay keep their behaviour. The PAUSE banner stays
visible when the HUD is off.

## 3. Buttons and mouse

- Two `Ui.FlatButton`s at the top right:
  - `HUD` toggles the HUD.
  - `VUE : FPV` / `VIEW: FPV` calls `FlightScene.NextCamera()`. Its text follows the current view:
    `VIEW_GROUND` "Sol"/"Ground", `VIEW_FPV` "FPV", `VIEW_CHASE` "3e pers."/"Chase", in the format
    `HUD_VIEW_BUTTON` "Vue : {0}"/"View: {0}".
- The buttons never take focus (`FocusMode.None`, already set by `Ui.FlatButton`), so Space, Enter, the arrows and
  the radio never trigger them. A click on them never reaches the flight.
- **Auto-hide:**
  - Any mouse motion shows the cursor (`Input.MouseMode = Visible`) and the two buttons.
  - After 2.5 s with no mouse motion, both hide (`MouseMode = Hidden`).
  - Leaving the flight (the node leaves the tree) restores `Visible`, so the menu always has a cursor.
  - Scripted runs (smoke tests, screenshots) never touch the mouse mode.

## 4. HUD on/off

- The **H key** (physical key) and the `HUD` button toggle the HUD.
- The state lives in the existing `AppSettings.ShowFlightData`, which is saved on each toggle.
  - Its default becomes `true`, so new installs show the HUD. Existing saved settings keep their value.
  - The Settings screen check box keeps editing it, with a new label: `SET_FLIGHT_DATA`
    "Afficher le HUD (H)"/"Show the HUD (H)".
- When off, the whole OSD is hidden (figures, horizon and help line). The buttons still appear with the mouse.
- Scripted runs always show the HUD and never save.
- The keyboard help text `HUD_KEYBOARD` gains "H HUD".

## 5. Tests

- `OsdData`:
  - Heading, roll and pitch from an attitude.
  - Vario from the vertical velocity.
  - Home distance and relative bearing in the four quadrants: pilot behind, to the right, to the left and ahead,
    including the ±180° wrap.
  - Throttle clamp.
  - Consumed mAh grows like the integrated battery current, and returns to 0 after a power-plant reset.
  - Voltage fallback before the first step.
  - No power plant gives null battery fields.
- The translation test is updated for the removed `HUD_AIRSPEED`…`HUD_TIMER` keys and the new `VIEW_*`,
  `HUD_VIEW_BUTTON` and `OSD_THROTTLE` keys.
- Screenshots:
  - `--screenshot-flight trainer 8 <png> --view fpv` shows the horizon and ladder.
  - The same command without `--view` shows the corner figures only.
- `docs/manual-acceptance.md` gets a "HUD" section:
  - H and the button toggle the HUD, which is remembered.
  - The mouse and buttons appear on motion and hide after 2.5 s.
  - The view button cycles and names the view.
  - The horizon follows roll and pitch in FPV only.
  - The home arrow points at the pilot.
  - mAh resets with R.
  - The menu has its cursor back.

## Out of scope

- Custom OSD layout editing.
- Betaflight glyph font.
- RSSI or link quality.
- GPS speed or ground speed.
- Warnings (low battery, stall).
- A clickable pause/reset/wind/menu.
