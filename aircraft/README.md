# Entering an aircraft

This describes `aircraft.json` and `power.json` well enough to enter a real model from its
build sheet and CG measurement.

## Units and axes

All lengths are in **metres**, areas in **m²**, masses in **kg**, inertias in **kg·m²**, angles
in **degrees**.

Every position field in `aircraft.json` and `power.json` — surface `root`, body `position`,
gear `position`, hull `position`, power `position` — is measured in **body axes** (x back
toward the tail, y right, z up) **from one free datum of your choosing**. The shipped aircraft
use the nose tip (`[0, 0, 0]`) as that datum, but any fixed reference works as long as every
field uses the *same* one.

- Left side is **−y** (a left aileron, a left main wheel, a left-mounted pod all get negative y).
- Ground/below is **−z** (gear contact points, a belly line, a down-thrust component are negative z).
- Back/aft is **+x** (the tail is at larger x than the nose); forward is −x.

## `cg` (required)

```json
"cg": [x, y, z]
```

`cg` is the centre of gravity in the **same datum and axes** as every other position field.
The loader subtracts it from every position when building the aircraft, so the simulator's
body origin is always the CG regardless of which datum you picked (see
`AircraftLoader.Load`, which throws if `cg` is missing).

If you don't know the vertical CG (z), it is measured far less often than the fore-aft one:
put the fuselage reference line (a straight line along the fuselage, e.g. the top of the
fuselage sides or the thrustline) at **z = 0** and measure everything relative to that line —
gear and hull points get their z from how far above (+) or below (−) the reference line they
sit. This keeps the model geometrically consistent even with an approximate vertical CG.

## Surfaces

```json
{ "name": "wing", "role": "wing", "root": [x, y, z], "span": 0.75,
  "rootChord": 0.35, "tipChord": 0.35, "sweepDeg": 0, "dihedralDeg": 5,
  "incidenceDeg": 1.0, "twistDeg": 0, "airfoil": "clarky", "segments": 6, "mirror": true }
```

- `root` is the **root quarter-chord point** of the panel, in the same datum as `cg`.
- `span` is one panel's span (root to tip); a mirrored surface's total span is twice that.
- `sweepDeg` is quarter-chord sweep (positive sweeps the tip aft, +x).
- `dihedralDeg` rotates the panel about body +x, tip up; **90° makes a vertical fin** whose
  span points up and whose normal points **left (−y)**. This is why the shipped fins have
  `dihedralDeg: 90`.
- `incidenceDeg` / `twistDeg` keep their usual meaning (twist is tip-relative, linear along span).
- A surface is defined by its **right panel** (toward +y); `"mirror": true` adds the left one
  automatically. A one-off surface (e.g. an asymmetric fin) uses `mirror: false`.

## Bodies (fuselage, pods)

```json
"bodies": [ { "name": "fuselage", "position": [0.65, 0, 0], "cdA": [0.006, 0.035, 0.04] } ]
```

`position` is the body's reference point in the same datum as `cg`. `cdA` is the drag area in
m², **in body-axis order [x back, y right, z up] = [frontal, side, top]** — i.e. the drag area
you'd measure facing the nose, facing the side, and looking down from above.

## Gear

```json
{ "name": "mainLeft", "position": [0.56, -0.2, -0.22], "stiffness": 2500, "damping": 60,
  "rollingFriction": 0.04, "lateralFriction": 0.8, "maxSteerDeg": 0, "steerMix": {} }
```

`position` is the wheel contact point in the same datum as `cg`; a left wheel has negative y,
and a wheel is below the CG datum line so it normally has negative z. A steerable wheel
(nosewheel, tailwheel) gets a non-zero `maxSteerDeg` and a `steerMix` (see Controls below);
a free or fixed wheel uses `"steerMix": {}`.

### Wheel brakes (optional)

`"brakeFriction": 0.5` on a wheel fits it with a brake: its rolling friction coefficient when fully braked. Brakes are
mixed to the throttle stick, like a common jet radio setup: on with the throttle closed (≤ 2 %), off as soon as it
opens. Aircraft with a fuel engine need them, because the engine keeps idling and pushing on the ground.

## Retractable gear (optional)

```json
"gearRetract": { "seconds": 5.0, "cdA": [0.004, 0.003, 0.002] }
```

Every wheel in `gear` retracts together, over `seconds` each way. The wheels carry load only when the gear is fully down
and locked; up or travelling, the hull points (belly, nose…) meet the ground instead. `cdA` is the extended gear's drag
area (m², body order [frontal, side, top] like a body's), applied at the wheels' centroid and scaled by how far the gear
is down. The pilot raises it with the G key or a radio gear switch (learned on the radio screen's Switches tab,
which position means gear up and which means gear down); after a start or reset, a switch left up is ignored until
it has been seen down.

## Hull points

```json
{ "name": "wingtipLeft", "position": [0.4825, -0.75, 0.185], "tag": "wingtip" }
```

Hull points are collision/crash-detection points (nose, wingtips, tail, belly, canopy), each
with a `tag` used by the crash logic (`nose`, `wingtip`, `tail`, `belly`, `canopy`, …). Same
datum and axes as everything else — a left wingtip has negative y, a belly point has negative z.

## FPV camera (optional)

```json
"fpvCamera": { "position": [x, y, z], "uptiltDeg": 25, "fovDeg": 120 }
```

`position` from the datum in body axes like every other position, `uptiltDeg` (0–60) tilts it up from the body
forward axis, `fovDeg` (60–150) is its **horizontal** field of view, as FPV camera specs give it. Without the block,
the camera sits on the hull point tagged `nose` (else the most forward hull point) with 10° uptilt and 110°.

## Display shape: `visual.json` (optional)

Without it an aircraft is drawn from its aero surfaces plus a box fuselage spanning the hull points. A `visual.json`
next to `aircraft.json` adds display-only geometry; it never changes the physics. Positions use the same datum and body
axes as `aircraft.json`, colours are `[r, g, b]` in 0–1.

```json
{
  "surfaceColor": [0.60, 0.63, 0.66],      // wing and tail panels (default cream)
  "controlColor": [0.53, 0.56, 0.59],      // control surfaces (default orange)
  "propellerDisc": false,                  // hide the spinning prop disc (ducted fans)
  "shapes": [                              // lofted bodies; any shape replaces the box fuselage
    { "name": "fuselage", "color": [0.55, 0.58, 0.61], "sides": 24, "roundness": 2.6, "mirror": false,
      "stations": [ { "x": 0.0, "width": 0 }, { "x": 0.3, "y": 0, "z": 0.01, "width": 0.10, "height": 0.11 } ] }
  ],
  "plates": [                              // flat convex polygons: strakes, ventral fins, rails
    { "name": "strakes", "color": [0.6, 0.63, 0.66], "mirror": true, "points": [ [0.28, 0.05, -0.01], [0.45, 0.085, -0.01], [0.70, 0.05, -0.01] ] }
  ]
}
```

- A shape is a list of cross-sections (at least 2), each a superellipse `width` (along y) by `height` (along z, defaults
  to `width`) centred on (`y`, `z`) at body `x`. `roundness` 2 is an ellipse, higher values square the section off.
  A zero-size station closes the shape to a point (a nose, a tail cone). Shapes are smooth-shaded.
- `mirror: true` also draws the shape or plate reflected to −y (wingtip missiles, strakes).
- `sides` is 3–64 (default 16).

See `jet/visual.json` for a complete example (the F-16).

## Inertia

```json
"inertia": { "roll": 0.14, "yaw": 0.33, "pitch": 0.22, "rollYaw": 0 }
```

About the **CG**, in body axes:

- `roll` = I_xx, `pitch` = I_yy, `yaw` = I_zz (kg·m²).
- `rollYaw` (optional, defaults to 0) is the roll–yaw product of inertia. It equals the
  classical FRD-axes I_xz, so a value from a literature table or CAD tool using the usual
  aircraft (forward-right-down) convention can be **entered as-is**, with no sign flip.
- The loader requires `roll`, `yaw`, `pitch` > 0 and `roll·yaw > rollYaw²` (positive-definite).

## `power.json`

```json
{ "position": [0.05, 0, 0], "thrustAxis": [-0.9988, 0.0349, -0.0349], ... }
```

- `position` is the prop disc / motor mount, same datum as `cg`.
- `thrustAxis` is the thrust direction in body axes (need not be unit-length; it's normalized
  on load):
  - straight-ahead thrust: `[-1, 0, 0]` (the wing and sport aircraft use this, or nearly so).
  - a **right** thrust offset (common on single-engine props to counter torque/P-factor) adds a
    small **+y** component.
  - a **down** thrust offset adds a small **−z** component.
  - the trainer combines both: `[-0.9988, 0.0349, -0.0349]` (about 2° right, 2° down thrust).

### Fuel engines: `piston` or `turbine`

`power.json` holds exactly one power source: `motor` + `battery` (electric, above), `piston` or `turbine`. Both fuel
engines start already running at idle (no start sequence); throttle 0 is idle, not off; an empty tank stops them. The
OSD then shows the fuel left instead of the battery.

```json
"piston": { "maxPowerW": 5000, "peakPowerRpm": 8300, "idleRpm": 1800, "maxRpm": 9000, "rotorInertia": 0.008,
            "tankMl": 700, "fuelFlowMaxMlMin": 75, "fuelFlowIdleMlMin": 8 }
```

A glow or gas engine turning the `propeller` (required). Full-throttle power follows `maxPowerW · (x + x² − x³)`,
`x = rpm / peakPowerRpm`, with the ignition cut above `maxRpm`; the idle throttle opening is worked out so the engine
idles at `idleRpm` on its propeller. `rotorInertia` is crank plus propeller. Torque roll, prop wash and P-factor act as
for an electric motor. Fuel flow grows linearly with power from idle to max. See `p51/power.json`.

```json
"turbine": { "maxThrustN": 220, "idleThrustN": 9, "maxRpm": 117000, "idleRpm": 33000,
             "spoolUpSeconds": 4.5, "spoolDownSeconds": 3.0, "massFlowKgS": 0.45, "nozzleDiameterM": 0.1,
             "rotorInertia": 6e-5, "tankMl": 4500, "fuelFlowMaxMlMin": 750, "fuelFlowIdleMlMin": 120 }
```

A kerosene micro-turbine, no `propeller`; `position` is the nozzle exit. The throttle sets the share of the idle-to-max
thrust range; the spool follows it no faster than a full idle→max sweep in `spoolUpSeconds` (down: `spoolDownSeconds`).
Thrust drops with airspeed by the ram drag `massFlowKgS · V`. No torque roll; the spool's gyroscopic moment remains.
See `f18/power.json` and docs/superpowers/specs/2026-09-26-fuel-engines-design.md.

### Ducted fans (EDF)

A ducted fan is entered as a propeller of the fan's diameter with explicit `j` / `ct` / `cp` tables (the generic
propeller estimate is far too weak for a 12-blade fan), plus:

```json
"pFactor": 0,
"ductStatorRecovery": 0.9
```

`ductStatorRecovery` (0–1, default 0) is the share of the rotor's aerodynamic torque that the stator vanes behind the
fan take back by straightening the swirl: the airframe keeps only the rest (and the torque that spins the rotor up), and
the exhaust leaves with that much less swirl. Put `position` at the nozzle exit so no surface sits in the exhaust.
See `jet/power.json`.

### Optional `sound` block (power.json)

```json
"sound": { "blades": 2, "polePairs": 7, "sample": "motor.ogg", "sampleRpm": 9000 }
```

| Field | Default | Meaning |
|-------|---------|---------|
| `blades` | 2 | Propeller or fan blade count (1–16); sets the blade-pass frequency rpm × blades / 60 (a turbine's spool whine) |
| `polePairs` | 7 | Motor magnetic pole pairs (1–20); sets the motor whine frequency rpm × polePairs / 60 |
| `sample`, `sampleRpm` | none | Recorded motor loop (relative to the aircraft folder) and the rpm it was recorded at; when given, the loop replaces the synthesized motor and propeller, pitched by rpm / sampleRpm. Give both or neither. |

## Controls

```json
{ "name": "rudder", "surface": "fin", "side": "both", "chordFraction": 0.4,
  "maxPositiveDeg": 25, "maxNegativeDeg": 25, "servoSecondsPer60Deg": 0.12,
  "mix": { "rudder": 1 } }
```

Positive deflection is always **trailing edge down relative to the surface's own normal**
(`maxPositiveDeg`/`maxNegativeDeg` are the magnitudes of the down/up throws). `side` selects
which panel of a mirrored surface the control lives on: `right`, `left`, or `both` (a
single-panel surface, like the fin, always uses `both`).

`mix` maps pilot stick channels (`aileron`, `elevator`, `rudder`, `flap`; positive aileron =
roll right, positive elevator = pitch up, positive rudder = yaw right) to a deflection command:
`deflection = clamp(Σ weight · channel, −1, 1)` × the appropriate max throw.

The `flap` channel carries the flap setting: 0 (up), 0.35 (half) or 1 (landing), from the F key (which steps through
them) or a radio flap switch (learned on the radio screen's Switches tab; each position is assigned Up, Takeoff or
Landing, or "—" for no effect). A control mixed from `flap` is a high-lift flap: besides the usual alpha shift, 60% of its effect is a
lift increment, so it raises the maximum lift instead of only stalling the section earlier. A flap normally has
`maxNegativeDeg: 0` and a slow servo; an elevator can take a small negative `flap` weight for the pitch compensation
(see `p51/aircraft.json`: flaps 13° / 40°, elevator `"flap": -0.2`).

Because the fin has `dihedralDeg: 90`, its normal points **left (−y)**, not up. So "trailing
edge down relative to the normal" for the rudder means the trailing edge moves toward −normal,
i.e. to the **right (+y)** — which pushes the tail right and yaws the nose **right**. That is
why the shipped rudder uses `"mix": { "rudder": 1 }` (positive, not negative): a positive
rudder command (yaw right) needs a positive (trailing-edge-down) deflection, because of the
fin's left-pointing normal. Getting this sign backwards is a classic bug, not a tuning choice —
it was caught by `FleetControlSignTests` and is logged in `docs/tuning-log.md` (2026-09-22).

The other mixes follow the same reasoning:
- `aileronRight: { "aileron": -1 }`, `aileronLeft: { "aileron": 1 }` — a roll-right command
  (`aileron` > 0) deflects the right aileron **up** (negative) and the left aileron **down**
  (positive), increasing left-wing lift and decreasing right-wing lift so the aircraft rolls
  right (right wing drops).
- `elevator: { "elevator": -1 }` — a pitch-up command deflects the elevator **up** (negative),
  reducing (or reversing) tail lift so the tail drops and the nose rises.
- A tailwheel/nosewheel `steerMix` follows the same yaw-right-positive convention, but its sign
  also depends on whether the wheel is ahead of or behind the CG (compare the trainer's
  nosewheel, `{ "rudder": 1 }`, with the sport's tailwheel, `{ "rudder": -1 }`).

## Annotated excerpt (trimmed trainer)

```json
{
  "mass": 2.6,
  "cg": [0.5, 0, 0],                          // datum = nose tip; CG is 0.5 m aft, on the fuselage reference line
  "inertia": { "roll": 0.14, "yaw": 0.33, "pitch": 0.22 },   // kg·m² about the CG; rollYaw omitted (0)

  "surfaces": [
    { "name": "wing", "role": "wing", "root": [0.4825, 0, 0.12],  // root quarter-chord, 0.12 m above the ref. line
      "span": 0.75, "rootChord": 0.35, "tipChord": 0.35,
      "dihedralDeg": 5, "incidenceDeg": 1.0, "airfoil": "clarky", "segments": 6, "mirror": true },
    { "name": "fin", "role": "verticalTail", "root": [1.38, 0, 0.03],
      "span": 0.22, "rootChord": 0.25, "tipChord": 0.14,
      "sweepDeg": 25, "dihedralDeg": 90,           // 90° dihedral = vertical fin, normal points left
      "airfoil": "naca0012", "segments": 3, "mirror": false }
  ],

  "controls": [
    { "name": "rudder", "surface": "fin", "side": "both", "chordFraction": 0.4,
      "maxPositiveDeg": 25, "maxNegativeDeg": 25, "servoSecondsPer60Deg": 0.12,
      "mix": { "rudder": 1 } }                     // + rudder command -> TE right -> yaw right
  ],

  "bodies": [
    { "name": "fuselage", "position": [0.65, 0, 0], "cdA": [0.006, 0.035, 0.04] }
    // cdA = [frontal, side, top] drag area in m²
  ],

  "gear": [
    { "name": "mainLeft", "position": [0.56, -0.2, -0.22], "stiffness": 2500, "damping": 60 }
    // left main wheel: -y; below the reference line: -z
  ],

  "hull": [
    { "name": "wingtipLeft", "position": [0.4825, -0.75, 0.185], "tag": "wingtip" }
    // left wingtip: -y; above the reference line by the dihedral rise: +z
  ]
}
```
