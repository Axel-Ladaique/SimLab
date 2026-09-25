# SimLab

Realistic RC airplane simulator. The goal is training transfer: practice in the sim, then fly the real aircraft.

## Status

Sub-project 1 (core) — headless libraries:

- `src/SimLab.Flight` — 6DOF flight model: strip-theory aerodynamics with ±180° airfoil polars, electric
  propulsion (battery, ESC, motor, propeller, thrust-stand calibration, APC import), wheels and hull contact,
  crash detection, ISA atmosphere, wind with Dryden turbulence, fixed-step (500 Hz) simulation, CSV recorder.
- `src/SimLab.Input` — radio input: calibration wizard, channel pipeline (trim, expo, rates), switch actions,
  keyboard fallback.
- `aircraft/` — data-driven aircraft (trainer, sport, FPV wing). Values are estimates; see `docs/tuning-log.md`.

- `src/SimLab.App` — testable game logic: club field and terrain, three camera views (ground line-of-sight, FPV,
  chase), procedural aircraft meshes, input routing, flight session, settings, translations.
- `game/` — Godot 4 (.NET) simulator: radio setup and calibration, club field, flying from the ground, FPV or
  chase view (C key or a bound radio switch), HUD, crash screen, French/English menus. See `docs/dev-setup.md`
  to build and run, and `docs/manual-acceptance.md` for the pilot's checklist.

Next: VSPAERO/CFD import and telemetry replay (sub-project 2), VTOL/drones (sub-project 4).

## Keyboard (without a radio)

W/S (Z/S on AZERTY) throttle · arrows aileron/elevator (↓ = pull) · A/D (Q/D on AZERTY) rudder ·
R reset · P pause · V wind on/off · C camera view · H HUD · F3 diagnostics · Esc menu.

## Build and test

```bash
dotnet test
dotnet build game/SimLab.Game.csproj
```

Requires the .NET 10 SDK (`brew install --cask dotnet-sdk`).

## Conventions

- World: right-handed **x east, y north, z up** (ENU). Heading clockwise from north. Gravity −z.
- Body: right-handed **x back (toward the tail), y right (right wing), z up** — the OpenVSP convention.
  Forward = −x. Pilot rates: roll right = −ω_x, pitch up = +ω_y, yaw right = −ω_z.
- Control deflection: positive = trailing edge down relative to the surface normal.
- Godot mapping (game layer only, `SimLab.App.Mapping.GodotBasis`): world ENU → Godot `(x, z, −y)`; body → node
  local `(y, z, x)`.
- SI units, doubles. `aircraft.json` positions are in body axes from a free datum (e.g. the nose tip); the
  required `"cg": [x, y, z]` field gives the CG in that datum, and the loader subtracts it from every position so
  the CG becomes the origin. Mixing (elevons, V-tail) is declared per control surface in `aircraft.json`.
