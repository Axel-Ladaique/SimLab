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

- `src/Symlab.App` — testable game logic: club field and terrain, line-of-sight camera, procedural aircraft
  meshes, input routing, flight session, settings, translations.
- `game/` — Godot 4 (.NET) simulator: radio setup and calibration, club field, line-of-sight flying, HUD,
  crash screen, French/English menus. See `docs/dev-setup.md` to build and run, and
  `docs/manual-acceptance.md` for the pilot's checklist.

Next: VSPAERO/CFD import and telemetry replay (sub-project 2), FPV and chase cameras (sub-project 3),
VTOL/drones (sub-project 4).

## Keyboard (without a radio)

W/S (Z/S on AZERTY) throttle · arrows aileron/elevator (↓ = pull) · A/D (Q/D on AZERTY) rudder ·
R reset · P pause · V wind on/off · F3 diagnostics · Esc menu.

## Build and test

```bash
dotnet test
dotnet build game/Symlab.Game.csproj
```

Requires the .NET 10 SDK (`brew install --cask dotnet-sdk`).

## Conventions

- SI units, doubles. World: y-up, x east, z south. Body: x forward, y up, z right.
- Body origin is the CG. `aircraft.json` may set an optional `"cg": [x, y, z]` datum (default `[0, 0, 0]`); the loader
  subtracts it from every position in the file (surface roots, bodies, gear, hull points, power position), so
  positions can be measured from any convenient reference such as the firewall or the wing leading edge.
- Control deflection: positive = trailing edge down. Mixing (elevons, V-tail) is declared per control surface in
  `aircraft.json`.
