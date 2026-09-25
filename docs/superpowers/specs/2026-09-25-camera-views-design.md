# Camera Views — Design

Date: 2026-09-25. Branch: `feat/camera-views` (from `main`).

## Goal

Besides the pilot's line-of-sight view from the ground, let the pilot fly from an FPV camera mounted on the aircraft
(first person) and from a chase camera behind it (third person), and switch between the three in flight.

## 1. Views

All rigs live in `SimLab.App.Cameras`, are pure C# and implement the existing `ICameraRig`
(`Update(dt, ctx)` → `CameraPose`, `Reset(ctx)`).

`CameraPose` gains an `Up` vector (world ENU), because the FPV view rolls with the aircraft. The line-of-sight and
chase rigs return world up. The game layer uses it in `LookAtFromPosition` instead of the fixed `Vector3.Up`.
It keeps the current fallback when the look direction is almost parallel to up.

`CameraContext` gains what the new rigs need: a terrain height function `Func<double, double, double>`
(east, north → ground height, m) and the viewport aspect ratio (width / height).

### 1.1 Ground (line of sight)

`LineOfSightRig`, unchanged. It is the default view.

### 1.2 FPV — `FpvRig`

- The camera is rigidly fixed to the aircraft and has no lag. Position = aircraft CG + orientation · mount position.
  The look direction is the body forward axis (−x), tilted up by the uptilt about the body y axis. Up is the body
  +z axis tilted by the same angle. The horizon rolls and pitches with the aircraft.
- The mount comes from `aircraft.json`, in an optional block:

  ```json
  "fpvCamera": { "position": [0.02, 0, 0.05], "uptiltDeg": 25, "fovDeg": 110 }
  ```

  - `position`: measured from the datum in body axes, like every other position; the loader subtracts the cg.
  - `uptiltDeg`: camera tilted up from the body forward axis, 0 to 60°.
  - `fovDeg`: **horizontal** field of view, as FPV camera specs give it, 60 to 150°.
- The loader turns it into `AircraftDefinition.FpvCamera` (`FpvCameraSpec(Vec3 Position, double UptiltDeg,
  double HorizontalFovDeg)`, nullable). Out-of-range values are load errors, like the other checks.
- Without the block, the defaults are the hull point tagged `nose`, or the most forward hull point if none is
  tagged. The uptilt defaults to 10° and the horizontal FOV to 110°. `FpvCameraSpec.For(definition)` resolves this.
- Shipped aircraft: `wing` gets an explicit block with `uptiltDeg` 25. The others use the defaults unless their
  nose point sits inside the propeller disk: then `position` puts the camera just above the spinner.
- The rig returns a vertical FOV. It converts from the horizontal FOV with the context's aspect ratio:
  `vfov = 2·atan(tan(hfov/2) / aspect)`.
- The game layer does not render the aircraft in this view. The aircraft meshes, propeller included, go on their
  own visual layer, and the camera's cull mask excludes that layer in FPV. The mount is at the nose, so the
  near plane (0.1 m) never shows the inside of the aircraft.

### 1.3 Chase — `ChaseRig`

- Distance behind = 3 × span, height above = 0.8 × span, measured from the aircraft CG.
- It follows the **horizontal heading only**. The heading is the horizontal projection of the body forward axis,
  smoothed with a time constant of 0.4 s (exponential, like the line-of-sight head).
  - When the forward axis is within 10° of vertical (vertical climb, dive, hover), the heading holds its last
    value.
- It looks at the aircraft CG. Up is world up: the horizon stays level, and the aircraft is seen rolling and pitching.
- It never goes below the ground: the camera height is at least terrain height + 0.5 m.
- It uses a fixed vertical FOV of 60°.

### 1.4 Reset

`Reset(ctx)` snaps a rig to its target with no smoothing. The chase heading snaps to the aircraft's heading.
`FlightScene` calls it on the active rig when the flight session resets (R or the radio Reset switch), which it
detects through `FlightSession.ResetCount`, and when the view changes.

## 2. Switching views

- `CameraView` enum: `Ground`, `Fpv`, `Chase`.
- `CameraDirector` (in `SimLab.App.Cameras`):
  - It holds one rig per view.
  - It exposes `Current`, `Next()` (Ground → Fpv → Chase → Ground), `Select(view)` and `Update(dt, ctx)`.
  - `Next()` and `Select()` reset the new rig.
  - It raises no events. `FlightScene` reads `Current` every frame to set the cull mask.
- Inputs:
  - **C key** (physical key, same on AZERTY/QWERTY). `KeyboardCommands` gains `NextCamera`, and `InputRouter`
    turns its rising edge into `SwitchAction.NextCamera`. V stays the wind toggle.
  - **Radio switch**: `SwitchAction.NextCamera` already exists. The radio screen gains a fourth bind button,
    `RADIO_BIND_CAMERA` ("Interrupteur « vue »" / "View switch"), next to Reset, Pause and Wind.
  - `FlightSession.Handle` keeps ignoring `NextCamera`. `FlightScene` sees it in `LastInput.Actions` and calls
    `director.Next()`.
  - The future HUD button calls `Next()` or `Select()` on the same director (HUD branch, not this work).
- `AppSettings.CameraView` (default `Ground`) remembers the last view. The next flight starts in it. An unknown
  value is sanitized to `Ground`.
- The keyboard help line (`HUD_KEYBOARD`) adds "C vue" / "C view".

## 3. Sound

The audio listener follows the active camera, as today. In FPV the motor is loud and there is no Doppler, which is
what an onboard microphone would hear. Nothing else changes.

## 4. Tests

- `FpvRig`:
  - Level flight: it looks along the heading, tilted up by the uptilt.
  - Rolled 90°: up follows the body.
  - The position follows the aircraft's rotation around the CG.
  - Horizontal → vertical FOV conversion at 16:9.
- `ChaseRig`:
  - With 60° of bank: up is still world up, and the camera stays behind at 3 spans and 0.8 span up.
  - A 90° heading change is followed with a lag: less than half done after 0.1 s, almost complete after 2 s.
  - Vertical attitude holds the last heading.
  - Terrain higher than the camera pushes it to terrain + 0.5 m.
  - `Reset` snaps.
- `CameraDirector`:
  - The cycle order.
  - `Select`.
  - The new rig is reset on each switch.
- Loader:
  - A `fpvCamera` block is shifted by the cg.
  - Without the block, the default comes from the nose hull point.
  - Out-of-range uptilt or FOV is rejected.
- `InputRouter`: C produces one `NextCamera` per press.
- `AppSettings`: `CameraView` round-trip and sanitization.
- `docs/manual-acceptance.md` gets a "Camera views" section:
  - C cycles the three views.
  - A bound radio switch does the same.
  - FPV shows no part of the aircraft and rolls with it.
  - Chase keeps the horizon level and never dips into the ground.
  - R snaps the camera back.
  - The view is remembered in the next flight.
  - The wing's FPV view looks up about 25°.

## Out of scope

- The HUD button (HUD branch).
- A mouse-orbit camera.
- An adjustable chase distance.
- Player-adjustable FPV settings.
- FPV image effects (distortion, noise).
- Head tracking.
