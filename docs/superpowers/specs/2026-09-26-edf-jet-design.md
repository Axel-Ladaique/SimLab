# EDF jet: F-16 80 mm (design)

Date: 2026-09-26. Request: add an RC jet with a fighter look, based on a real commercial model where possible.

## Reference model

**E-flite F-16 Falcon 80mm EDF** (EFL87850 BNF Basic / EFL87870 ARF Plus), published figures:

| Item | Value | Source |
|------|-------|--------|
| Span / length | 1000 mm / 1450 mm | Horizon Hobby product page, retailer listings |
| Weight | 96 oz (2.73 kg) | retailer listings |
| Power | 80 mm 12-blade V2 fan, 3280 2100 kV 4-pole inrunner, 100 A ESC, 6S 4000–7000 mAh | manual, product page |
| CG | 95 mm (±5) behind the wing leading edge at the fuselage | manual |
| High rates | aileron 15 mm, stabilator 32 mm up / 27 mm down, rudder 21 mm | manual |
| Flight time | about 3.5 min on 5000 mAh | review (thercgeek.com) |

Bench data for 80 mm 12-blade 2100 kV fans on 6S: about 3.4 kg static thrust at 98 A.

Where the model's planform is not published, the General Dynamics F-16 is scaled 1:10.4 (length 15.03 m → 1.45 m):
reference wing 30 ft span / 300 ft², taper 0.227, 40° leading-edge sweep, 0° dihedral; stabilators with 40°
leading-edge sweep and 10° anhedral; fin 47° leading-edge sweep. Inertia from the F-16's radii of gyration.

## What changes in SimLab

1. **`aircraft/jet/`**: `aircraft.json`, `power.json`, `visual.json`. Tricycle gear with nosewheel steering, all-moving
   stabilators (a control with `chordFraction` 1.0), flaperon-style inboard ailerons.
2. **Ducted fan propulsion** without a new model: the fan is a propeller of 0.08 m with explicit Ct/Cp tables, plus
   `ductStatorRecovery` in `power.json` (0–1): the share of the rotor torque that the stator vanes take back. The airframe
   feels `ReactionTorque − recovery · PropTorque` (so the spool-up torque still reaches it) and the exhaust swirl is reduced
   by the same share. Default 0 keeps every existing aircraft unchanged.
3. **`visual.json`** (display only, `SimLab.App.Visual.AircraftVisualSpec`): lofted superellipse shapes (radome,
   fuselage, canopy, spine, intake, nozzle, wingtip missiles), flat plates (strakes, ventral fins, rails), surface and
   control colours, and `propellerDisc: false`. Shapes replace the box fuselage. Positions use the `aircraft.json` datum.
4. **Surface meshes** are drawn as trapezoids along each strip's swept quarter-chord line with chords interpolated to the
   strip ends, and control hinges rotate about the real (swept) hinge line. Before, swept surfaces showed a staircase.
5. **Sound**: `sound.blades` accepts 1–16 (fans have 5–14 blades), and the synthesizer drops blade-pass harmonics at or
   above Nyquist (a 12-blade fan passes 9.5 kHz; its 4th and 5th harmonics folded back to 6.1 and 3.4 kHz).

## Known limits

- Retracts (added the same day): `gearRetract` in aircraft.json, see aircraft/README.md. No gear doors, and the legs all fold
  forward the same way.
- Symmetric NACA 0012 polars stand in for the F-16's thin NACA 64A204 section; no strake (LEX) lift, no fuselage
  destabilization, so the CG sits 56 mm behind the manual's to give a realistic static margin (tuning log).
- The all-moving stabilator goes through the plain-flap model (τ = 1, 85% efficiency, large-deflection K'), so it is
  about 20% less effective than a true all-moving surface at the same angle.
- Like the rest of the fleet, the jet is slightly spirally unstable (realism backlog #1).
