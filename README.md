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
