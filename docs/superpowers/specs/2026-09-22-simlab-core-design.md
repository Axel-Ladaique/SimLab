# SimLab — Sub-project 1: Core Simulator Design

Date: 2026-09-22
Status: Approved

## 1. Purpose

SimLab is a realistic RC airplane simulator. Its primary goal is **training transfer**: the pilot
(FrSky TX16S, EdgeTX) practices in the sim and then flies real aircraft. Realism and measurable
fidelity take priority over visual polish.

### Roadmap

| # | Sub-project | Content |
|---|-------------|---------|
| **1** | **Core (this spec)** | Radio input, 6DOF physics with discrete-surface aero, electric propulsion, ground contact, club field, line-of-sight camera, tests |
| 2 | Realism | VSPAERO / CFD aero table import, hybrid aero model, real telemetry replay and fidelity scoring |
| 3 | Cameras | Chase (3rd person) camera, FPV camera with OSD and video latency |
| 4 | VTOL / drones | Multirotor and VTOL on the same physics core |

### Out of scope for sub-project 1

- VSPAERO/CFD import (interface `IAeroModel` is defined now, table/hybrid models come in sub-project 2)
- FPV and chase cameras (interface `ICameraRig` is defined now)
- Real terrain reconstruction, photoscenes, thermals, multiplayer

## 2. Platform and Language

- **Godot 4 (.NET build), C#**, native desktop app (macOS first, Windows second).
- Radio input through Godot's SDL-based joystick layer: no browser Gamepad/WebHID restrictions.
- All code, comments and documentation in English. In-game UI strings go through Godot's
  translation system; French is the default shipped locale, English second.

## 3. Architecture

```
3_SYMLAB/
├─ src/SimLab.Flight/          # pure .NET library, no Godot dependency
│   ├─ Geometry/               # Vec3, Quat, Mat3, Attitude (double precision)
│   ├─ Numerics/               # table interpolation
│   ├─ Dynamics/               # rigid-body state, mass properties, RK4 fixed-step integrator
│   ├─ Aero/                   # IAeroModel, SurfaceAeroModel, surface geometry, Airfoil polars, induced flow
│   ├─ Propulsion/             # Motor, Battery, Esc, Propeller, PowerPlant, thrust-stand and APC import
│   ├─ Controls/               # ControlInputs, mixing, Servo
│   ├─ Ground/                 # Wheel (spring-damper, friction, steering), HullPoint, crash detection
│   ├─ Atmosphere/             # ISA density, WindField (steady + log profile + Dryden turbulence)
│   ├─ Terrain/                # ITerrain, FlatTerrain, obstacles
│   ├─ Airframe/               # AircraftDefinition, JSON loader, Aircraft (assembled simulation object)
│   ├─ Sim/                    # FlightEnvironment, Simulation (fixed-step accumulator), InitialConditions
│   └─ Recording/              # FlightRecorder (CSV)
├─ src/SimLab.Input/           # pure .NET: channel pipeline, calibration wizard, switches, keyboard fallback
├─ game/                       # Godot C# project: rendering, terrain, cameras, HUD, menus
├─ aircraft/                   # one folder per aircraft (see §5)
├─ tests/SimLab.Flight.Tests/  # xUnit
├─ tests/SimLab.Input.Tests/   # xUnit
└─ docs/
```

### Data flow per rendered frame

1. `game` reads raw joystick axes/buttons from Godot `Input`.
2. `SimLab.Input` converts raw values to normalized channels in [-1, 1].
3. `SimLab.Flight` advances the simulation by N fixed sub-steps of **2 ms (500 Hz)**, using an
   accumulator; leftover time is carried over.
4. `game` renders the aircraft pose interpolated between the last two physics states.

Physics never depends on frame rate. Given the same inputs and initial state, a run is
bit-for-bit reproducible on the same machine.

### Coordinate conventions

- World: right-handed **x east, y north, z up** (ENU). Heading clockwise from north. Gravity −z.
- Body: right-handed **x back (toward the tail), y right (right wing), z up** — the OpenVSP convention.
  Forward = −x. Pilot rates: roll right = −ω_x, pitch up = +ω_y, yaw right = −ω_z. Aero forces are
  computed per surface as vectors, so no stability-derivative sign conventions are needed.
- Control deflection: positive = trailing edge down relative to the surface normal.
- Godot mapping (game layer only, `SimLab.App.Mapping.GodotBasis`): world ENU → Godot `(x, z, −y)`;
  body → node local `(y, z, x)`.
- SI units, doubles throughout.

## 4. Flight Model

### 4.1 Rigid body

- State: position, velocity (world), orientation quaternion, angular velocity (body).
- Full inertia tensor (Ixx, Iyy, Izz, Ixz), center of gravity from the definition.
- RK4 integration, quaternion renormalized every step.

### 4.2 Aerodynamics — `IAeroModel`

```csharp
public interface IAeroModel
{
    // Returns total aerodynamic force and moment about the CG, in body axes.
    AeroResult Evaluate(in AeroContext ctx);
}
```

`AeroContext` carries body-axis airspeed (including wind and prop wash), angular rates, control
surface deflections (after servo dynamics), air density, height above ground.

**`SurfaceAeroModel`** (this sub-project): the aircraft is a set of lifting surfaces
(left/right wing panels split into several spanwise segments, horizontal stabilizer, fin,
elevons for flying wings). For each segment:

- Local velocity = body velocity + ω × r + prop wash contribution (if inside the slipstream).
- Local angle of attack and sideslip from the segment's own frame (incidence, dihedral, sweep).
- Coefficients from the segment's **airfoil polar**: Cl, Cd, Cm vs α, Reynolds number, with
  control deflection modeled as an effective camber / α shift (flap-effectiveness factor).
- Full **±180° α range**: attached polar blended into a flat-plate post-stall model, so stalls,
  asymmetric wing drops, spins, snap rolls, knife-edge and harriers emerge naturally.
- Finite-span correction through an effective aspect ratio per surface; downwash from wing to
  tail with a lag term.
- Fuselage modeled as a simple drag/side-force body.
- Ground effect: induced drag reduction and lift increase as a function of h/b.

`AeroResult` sums forces and moments of all segments.

### 4.3 Propulsion

Chain: throttle channel → `Esc` → `Motor` ↔ `Propeller`, powered by `Battery`.

- **Motor**: Kv, Rm, I0, max current. Electrical model: `I = (V_applied − RPM/Kv)/Rm`,
  torque `Q = (I − I0)/Kv_SI`. Motor + prop rotational inertia integrated as its own state
  (spool-up/spool-down lag).
- **Propeller**: CT(J), CP(J) tables (optionally per RPM). APC performance `.dat` files are
  importable; otherwise a generic model from diameter and pitch.
- **Battery**: cells, capacity, internal resistance, open-circuit voltage vs state of charge.
  Voltage sags under load; flight time is realistic.
- **ESC**: throttle curve, optional brake.
- **Thrust-stand override**: a CSV of measured static thrust and current vs throttle rescales the
  model so static values match measurements.
- Effects applied to the airframe: thrust along the thrust line (with offsets / down- and
  right-thrust), motor reaction torque, gyroscopic precession, P-factor, prop wash velocity
  (momentum theory) feeding surfaces inside the slipstream.

### 4.4 Control surfaces and servos

Each surface has a mechanical deflection range per direction and a servo speed
(e.g. 0.10 s/60°). Commanded deflection is rate-limited before reaching the aero model.

### 4.5 Ground contact

- Wheels: spring-damper along the ground normal, tire friction (rolling + lateral, regularized),
  steerable nose/tail wheel. Brakes are out of scope (RC aircraft rarely have them).
- Hull points (wingtips, nose, tail, belly): stiff contact with high friction, used for belly
  landings (flying wing hand-launch/belly land) and crash detection.
- Obstacles: trees and fences as simple collision volumes.
- **CrashDetector**: triggers when contact impulse or vertical speed exceeds per-aircraft
  thresholds or a non-hull point penetrates an obstacle; reports a cause
  (`HardLanding`, `TreeStrike`, `WingtipStrike`, `NoseOver`, …).

### 4.6 Atmosphere

- ISA density (field elevation and temperature configurable).
- Wind: steady speed/direction, **Dryden turbulence** (low-altitude MIL-F-8785C model, first-order
  filters, seeded for reproducibility) providing the gusts, and a log-profile wind gradient near the ground.

## 5. Aircraft Definition Format

One folder per aircraft, e.g. `aircraft/trainer/`:

| File | Content |
|------|---------|
| `aircraft.json` | Metadata, mass, inertia, surfaces (geometry, airfoil ref, segments), control surfaces (chord fraction, deflection limits, servo speed, channel mapping), gear, hull points, crash thresholds. All body positions are measured in body axes from a free datum (e.g. the nose tip); the required `"cg": [x, y, z]` field gives the centre of gravity in that same datum, and the loader subtracts it from every position so the CG becomes the origin. |
| `airfoils/*.json` | Polars: Cl, Cd, Cm vs α for one or more Reynolds numbers |
| `power.json` | Motor, propeller, battery, ESC, optional thrust-stand CSV reference |
| `aero.json` | *(sub-project 2)* VSPAERO/CFD coefficient tables |
| `model.glb` | Visual model with named control-surface nodes for animation |

Every numeric parameter may carry a **provenance** tag: `estimated`, `measured`, `vspaero`,
`cfd`, `xfoil`. The sim can display a confidence summary per aircraft.

Aircraft shipped in sub-project 1:

1. **Trainer** — high wing, ~1.5 m span, dihedral, tricycle gear.
2. **Sport / aerobatic** — mid/low wing, symmetrical airfoil, ~1.2 m, taildragger.
3. **Flying wing FPV** — foam delta/wing type, ~1.0–1.2 m, elevons, pusher prop, hand launch,
   belly landing.

Starting values come from the Sightline mockup (mass, span, area, inertia) and typical
published data; all tagged `estimated`.

## 6. Radio Input — `SimLab.Input`

- Channel pipeline, each stage unit-tested:
  `raw → calibrated → reversed → trim → expo → rates → stick function`.
- Mixing (elevons, V-tail, flaperons) is a property of the airframe and lives in `aircraft.json`:
  each control surface declares a `mix` of stick functions. The radio model used for the sim must
  have no mixes.
- Calibration wizard (4 steps, from the mockup): center, extremes, then stick identification by
  moving one stick at a time. Stored per radio GUID in the user config.
- Mode 1 / Mode 2 only changes the wizard's instructions (UI text); identification is by function.
- Switches assignable to: reset, pause, camera change (later), wind toggle.
- Sim-side expo and rates default to **neutral** so the pilot trains with their real radio settings.
- Keyboard fallback for testing without a radio.
- Radio screen: detected devices list, live bars for all raw axes, status in green/red,
  EdgeTX USB-joystick troubleshooting text.
- Input-to-physics latency displayed in the diagnostics overlay; target < 20 ms end to end.

## 7. Game Layer (Godot)

### 7.1 Field

- ~2 × 2 km gently rolling terrain, 100 × 15 m grass runway, pilot box with fence.
- Instanced trees, tree lines, hedges as distance and height cues.
- HDR sky with configurable sun position (sun glare is a real line-of-sight hazard).
- Windsock driven by the `WindField`.

### 7.2 Cameras — `ICameraRig`

- **Line-of-sight** (this sub-project): eye height 1.7 m in the pilot box, tracks the aircraft
  like a head. Optional limited auto-zoom keeping apparent size plausible, can be disabled for
  true apparent size. Configurable FOV to match the pilot's screen size and viewing distance.
- Chase and FPV rigs: sub-project 3.

### 7.3 UI

- Main menu: aircraft selection, field conditions (wind, turbulence, sun), radio setup, settings.
- In flight: minimal by default; optional panel with airspeed, altitude, throttle, battery
  voltage, flight timer.
- Crash screen with cause and instant reset (also bindable to a radio switch).
- Control surfaces animated on the 3D model.

### 7.4 Performance

Target ≥ 120 fps on a recent Mac; physics cost independent of frame rate.

## 8. Recording

`FlightRecorder` writes a CSV per flight: time, raw and processed channels, full state, aero and
propulsion summaries, battery. Used for debugging, and in sub-project 2 for comparison with real
telemetry.

## 9. Testing and Validation

### 9.1 Unit tests (xUnit, no Godot)

- Math: quaternion ops, frame conversions, sign conventions.
- Integrator: torque-free rotation conserves energy and angular momentum; free fall matches
  analytic solution; error converges when halving the step.
- Aero: each control input produces the expected-sign rate response (right aileron → roll right,
  up elevator → pitch up, right rudder → yaw right).
- Airfoil polar: continuity across the attached/post-stall blend, ±180° coverage.
- Propulsion: static thrust and current match `power.json`/thrust-stand data within tolerance.
- Ground: aircraft at rest on its gear is stable (no jitter/drift over 60 s).
- Input pipeline: every stage, mixers, calibration.

### 9.2 Behavior tests (scripted scenarios with bounds)

- Trim: level-flight speed at a given throttle within expected range for each aircraft.
- Dynamic modes from small perturbations: phugoid, short period, Dutch roll, spiral — periods
  and damping inside typical RC ranges.
- Stall speed consistent with wing loading and CLmax; spin entry with full elevator + rudder and
  recovery when neutralized (trainer recovers hands-off, sport does not).
- Takeoff run length and landing roll plausible.
- Battery endurance at cruise throttle plausible.

### 9.3 Definition of done for sub-project 1

- TX16S detected via SDL, calibrated, all 4 primary channels move the aircraft correctly.
- The three aircraft take off, fly, stall, spin (trainer/sport) and land on the club field in
  line-of-sight view.
- All unit and behavior tests pass in CI (`dotnet test`).
- Every flight is recorded to CSV.
