# Fuel engines: turbine F-18 and gas P-51 (design)

Date: 2026-09-26. Request: a large turbine-powered fighter and a piston-engine model. The user picked a 1:6 F-18/F-15
class jet and, instead of a piston A400M (commercial RC A400Ms are electric), a single-engine gas warbird.

## Reference models

**Skymaster F/A-18E Super Hornet 1:6.25 (ARF Plus)**: span 2180 mm, length 2970 mm, dry weight 21–22 kg; single turbine
180–220 N or two of 100–140 N; 11 servos, scale retracts. Engine used here: one **JetCat P220-RXi class** turbine
(220 N, idle about 33 000 rpm, max about 117 000 rpm; the P180-RXi publishes 175 N, 32 000–126 000 rpm, 610 ml/min,
1.6 kg). Planform from the Boeing F/A-18E (span 13.62 m, length 18.31 m, wing 46.45 m², aspect ratio 4, 20° quarter-chord
sweep, taper 0.35 estimated, 3° anhedral, fins canted 20°) scaled 1:6.25.

**Hangar 9 P-51D Mustang 60cc (HAN4770)**: span 2260 mm, length 1970 mm, wing area 1420 in² (0.916 m²), flying weight
11.8–13.0 kg, 1/5 scale, electric retracts, flaps. Manual: CG 171 mm behind the wing leading edge at the root; high
rates aileron 22 mm up / 17 mm down, elevator 25/25 mm, rudder 55/55 mm; flaps 25 mm (mid) / 72 mm (landing).
Engine: **Evolution 62GXi** (61.5 cc, 1.53 kg, 1 000–8 000 rpm, props 22×8 to 24×10, benchmark 23×9 at 7 300 rpm).

## Propulsion model

`PowerPlantSpec` keeps its shape; the electric parts (`motor`, `battery`, `esc`) become one of three sources:

| power.json block | Source | Drives |
|------------------|--------|--------|
| `motor` + `battery` (+ `esc`) | brushless motor on a LiPo (unchanged) | `propeller` |
| `piston` | glow/gas engine | `propeller` |
| `turbine` | kerosene micro-turbine | its own thrust at the nozzle |

**Piston.** Full-throttle brake power follows `P(x) = Pmax (x + x² − x³)`, `x = ω / ω_peak` (peak at `peakPowerRpm`),
cut by the ignition limiter above `maxRpm`; internal friction grows with speed. The throttle sets the opening
`u = u_idle + (1 − u_idle)·throttle`; `u_idle` is solved when the plant is built so the engine idles at `idleRpm` with its
propeller (throttle 0 never stops the engine). The rotor (crank + propeller) spins up against the propeller torque, the
airframe feels the net crank torque (torque roll), and propeller wash, P-factor and gyroscopic moments work as for the
electric motor. Fuel flow grows linearly from idle to full-power flow with shaft power; an empty tank stops the engine.

**Turbine.** Spool speed N follows the throttle through the ECU: target `N² = N_idle² + throttle (N_max² − N_idle²)`,
approached with a 0.25 s lag but never faster than a full idle→max sweep in `spoolUpSeconds` (down: `spoolDownSeconds`).
Gross thrust `T_idle + (T_max − T_idle)(N² − N_idle²)/(N_max² − N_idle²)`, scaled with air density, minus ram drag
`ṁ·V` (mass flow proportional to N). Fuel flow is linear in thrust between idle and max flow; an empty tank flames the
engine out (it spools down to 0). The engine starts at idle (no start sequence). No torque on the airframe; the rotor's
gyroscopic moment remains.

For both, `PowerTelemetry.StateOfCharge` is the fuel left (fraction) and the electrical fields are 0.

## Other changes

- **Wheel brakes**: optional `brakeFriction` per wheel, on while the throttle is closed. Fuel engines keep idling (about
  10 N for either aircraft), which rolling friction alone does not hold. The brake friction has a sharper onset than
  rolling friction (5 mm/s instead of 5 cm/s) so a held aircraft does not creep.
- **OSD**: fuel percentage and ml left instead of volts/amps/mAh for fuel engines.
- **Sound**: the voice follows the power source. Piston adds an exhaust pulse train at the
  firing frequency (single-cylinder two-stroke: once per revolution); turbine replaces the motor whine by the spool
  whine and adds a broadband jet roar that grows with thrust.
- **Aircraft**: `aircraft/f18` (turbine, retracts, twin canted fins, stabilators, outboard ailerons) and `aircraft/p51`
  (gas, retracts, taildragger, two-blade 23×9), each with a `visual.json`. Main wheels of both have brakes.

## Known limits

- The aircraft mass is fixed (full fuel); burning 3–4 kg of kerosene does not lighten the F-18 or move its CG.
- No engine start/stop sequence, no kill switch, no carburettor lag; the turbine never hot-starts or overheats.
- Flaps: the flap channel is not wired to any input yet, so the P-51 flies without them.
