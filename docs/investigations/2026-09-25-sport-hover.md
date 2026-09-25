# Sport hover / torque-roll investigation (spike, 2026-09-25)

Branch `feat/godot-game` @ 4cfd042. Nothing was committed. The throwaway code was an xUnit spike (`HoverSpike.cs`) plus
toggles in `Aircraft.cs`, `IAeroModel.cs` and `SurfaceAeroModel.cs`, all reverted afterwards. Copies are in the session
scratchpad (`HoverSpike.cs`, `spike-src.diff`).

## 1. Hover numbers (sport, ρ = 1.225, nose up, V = 0)

| Quantity | Value |
|---|---|
| Weight | 21.57 N |
| Hover throttle (linear ESC) | 0.773: 8367 rpm, T 21.6 N, prop torque 0.424 N·m, 34.6 A |
| Full throttle | 10149 rpm, 31.8 N, T/W 1.47, Q 0.624 N·m, 50 A |
| Momentum theory | v_i = 11.0 m/s, far wake 2v_i = 22.0 m/s (q = 296 Pa) |
| Wash the model applies | 0.8 × 22.0 = **17.6 m/s, q = 189 Pa**, same everywhere behind the disk, radius = R = 0.152 m |
| Inertia (roll/pitch/yaw) | 0.08 / 0.16 / 0.23 kg·m² |

Disturbing moments in hover, body axes (+Mx = roll left, the torque-roll direction). The model has no swirl.

| Source | Roll Mx | Pitch My | Yaw Mz |
|---|---|---|---|
| Motor reaction torque | +0.424 | – | – |
| 2° right thrust (thrust line 0.45 m ahead of the CG) | – | – | −0.339 |
| Aero neutral (fin at 2° in the skewed wash, stab −1° incidence, uneven coverage) | +0.062 | +0.414 | −0.358 |
| **Total bias** | **+0.50** | +0.40 | **−0.70** |
| Gyroscopic moment at a pitch rate of 2 rad/s | – | – | −0.44 |
| P-factor | 0 (switched off below 1 m/s) | | |

Control authority at full stick, with the angular acceleration each produces.

| Control | Main moment | Angular accel. | Margin over the bias | Side effects |
|---|---|---|---|---|
| Aileron ±25° | −0.54 N·m | 6.7 rad/s² | **1.07×** | pitch −0.25 |
| Elevator ±25° | 4.19 N·m | 26 rad/s² | ≫ | roll +0.37 |
| Rudder ±30° | −2.00 N·m | 8.7 rad/s² | 2.9× | roll +0.26 |

Aero damping per 1 rad/s: roll −0.086, pitch −0.95, yaw −0.46 N·m.

So holding the motor torque takes about 93% aileron in steady state. The right-thrust yaw also takes 35% rudder.

### Closed-loop hover

The pilot is a PD attitude loop (ζ 0.8, ωn 6 rad/s) on real servos with stick limits, plus altitude hold and a drift
(lean) loop. Results:
- **Baseline, soft drift loop (0.08 rad per m/s):** lost in 2.6–3.5 s. The ailerons sit at +1.0 from t ≈ 1 s and the
  roll angle keeps growing (torque roll, 41°). The aircraft then drifts sideways. The crossflow on the fin, in the wash,
  saturates the rudder and the nose tips over (tilt > 80°).
- **Same model, rudder bias removed (thrust axis along −x):** aileron authority drops to **0**. It torque-rolls
  continuously at about 4.2 rad/s, which is exactly what the user reports.
- **Baseline, tight drift loop (0.2), zero latency:** it hovers, but with 15° roll excursions and 11% of the time
  saturated. It only works for a perfect autopilot.
- **Human-like pilot (150 ms delay, ωn 3):** every variant is lost in 2–9 s, including the fixed ones. The trace shows
  the lean loop ringing, so my toy pilot is at fault. Treat these runs as inconclusive.

## 2. How the prop wash is modelled (`SurfaceAeroModel.WashAt`, `Aircraft.Step`, `PowerPlant.Step`)

- **Velocity.** `WashVelocity` = sqrt(v² + 2T/(ρA)) − v, which is the far-wake increment (2v_i when static), times
  0.8. The same velocity applies at the wing (2.9 R behind the prop) and at the tail (7.7 R, x ≈ 1.17 m).
  - At the wing, momentum theory gives (1 + x/√(R²+x²))·v_i = 21.4 m/s, so the model's 17.6 m/s is low there.
  - At the tail, 0.8 × 2v_i is plausible (the jet core is still largely intact 4 D downstream).
- **Geometry.**
  - The wash is a hard-edged cylinder of radius R: no contraction (the theoretical value is R/√2 ≈ 0.108 m), no
    spreading, no radial profile.
  - Each segment is either fully in or fully out, tested at its centre point.
  - Momentum flux through the modelled wash is ρ·17.6²·πR² = 27.7 N, **1.28 × T**. The wash is not too weak overall.
  - It follows the thrust axis, so at the tail it sits 4.1 cm to the left because of the 2° right thrust.
- **Segments inside the wash:**
  - Wing: the two root strips (no aileron) and **only the left inboard aileron strip**.
    - Its centre is at y = 0.15, z = −0.037, and its radial distance is 0.140 on the left versus 0.169 on the right.
    - With a straight thrust axis both sides are at 0.1545 > 0.1524 and both drop out: a 2 mm knife edge.
  - Stab: 1 of 3 strips on the right, 3 of 3 on the left. Hence the elevator→roll and aileron→pitch coupling.
  - Fin: the lower 2 of 3 strips.
- **Direction and frames.** The wash is added along `ThrustAxis` in body axes, with the correct sign: it raises the
  chordwise flow like forward speed does. The only lateral component is the 2° right-thrust skew. **No swirl
  (tangential) component is modelled** (backlog #1).
- **Angle of attack in the axial wash:**
  - Stab −1° (its incidence); the downwash lag is negligible.
  - Wing +0.5°.
  - Fin +2° (from the skewed wash).
  - Control deflections add τ·0.85·K'·δ, then the induced angle is solved.
- **Effective lift in the wash, from the moments:**
  - Elevator: Cl ≈ 0.60 on 0.051 m² in the wash. The alpha shift is 10.2°, and the induced angle of the low aspect
    ratio stab (AR ≈ 2.6) removes about 40% of it.
  - Rudder: Cl ≈ 0.55.
  - Aileron: Cl ≈ 0.63 on the one strip in the wash.
- **Stall.** The flap is modelled as an alpha shift on the clean polar. ΔCl is therefore capped at the clean Clmax of
  about 1.0 (NACA 0012, Re 200k). Past about 13° effective, a bigger throw *loses* lift. The large-deflection factor K'
  reduces the shift first, so at 45° the model gives roughly half of the DATCOM ΔCl.

## 3. K', FlapEfficiency and aileron coverage

| Control | δ | K' | τ | α shift (× FlapEfficiency 0.85) |
|---|---|---|---|---|
| Aileron | 25° | 0.686 | 0.641 | 9.3° |
| Elevator | 25° | 0.615 | 0.785 | 10.2° |
| Rudder | 30° | 0.542 | 0.818 | 11.3° |

Going to 3D throws (ailerons 40° with a 35% chord from 2% of the span, elevator and rudder 45°) raises authority:

| | Baseline | 3D throws | Gain |
|---|---|---|---|
| Aileron | 0.54 | 1.19 N·m | ×2.2 (mostly from the extra span in the wash) |
| Elevator | 4.19 | 5.76 N·m | ×1.37 for +80% throw |
| Rudder | 2.00 | 2.54 N·m | ×1.27 for +50% throw |

The small gains on elevator and rudder show the K' × clean-Clmax saturation.

A spike that adds the flap as a lift increment on the clean polar (raising Clmax, with the induced angle included) gave
+4% at 25° throws and +8% at 40–45°. That is a secondary effect.

The aileron fraction in the wash is tiny: about 0.057 m of aileron span per side lies geometrically inside R, and
≈ 0.01 m inside the contracted R/√2. The model rounds this to 0.10 m on one side and 0 on the other.

## 4. Comparison with real practice

- 3D and aerobatic RC setups typically have:
  - ailerons of 25–35% chord running almost to the root, with ±35–45° throws;
  - elevator and rudder of 40–50% chord with ±40–50° throws;
  - a big rudder;
  - 0–1.5° right thrust and 0/0 incidences.
- Hover throttle:
  - With a linear ESC, thrust grows roughly as throttle², so T/W 1.5 gives ≈ 0.8. The model's 0.77 is consistent.
  - The quoted 50–70% hover throttle is for T/W ≥ 2 3D planes.
- The sport as defined (25° throws, ailerons from 15% of the half span, 2° right thrust, −1° stab) is a
  sport-aerobatic model, not a 3D one. Real models like it do hover, but they torque-roll unless the pilot works hard.
  **So the geometry and throws are part of the limit.**
- The model makes it worse than reality in two ways:
  1. There is **no swirl counter-torque**. Real slipstream swirl on the wing root, fuselage, stab and fin acts as a
     stator and recovers a good part of the motor torque. The same swirl on the fin is what the 2° right thrust exists
     to cancel. Without it, the right thrust gives a pure 0.70 N·m yaw bias in hover.
  2. The aileron authority depends on a **hard-edged, segment-centre wash test**, a 2 mm knife edge that the right
     thrust tips to one side.

## Root cause

In hover the sport's roll control margin over the motor reaction torque is **1.07** (0.54 versus 0.50 N·m). It rests on
one aileron strip that the binary wash test counts as "in" only because the 2° right thrust shifts the wash 1.5 cm.
Nothing in the airframe counters the torque, because slipstream swirl is not modelled. Any disturbance therefore
becomes a torque roll. A straight thrust line would give zero aileron authority and a pure torque roll.

Secondary: the uncompensated right-thrust yaw (0.70 N·m, 35% rudder) plus crossflow from drift on the fin in the wash
saturates the rudder and tips the aircraft over.

## Ranked fixes (predicted effects from the spikes)

1. **Model: slipstream swirl in `PropWash`.**
   - What: tangential velocity carrying the motor torque's angular momentum. The spike used a solid-body swirl
     Ω = 2Q/(ρ(V+w)πR⁴) × efficiency, applied in `WashAt` with sign `−(Ω·axis)×r`.
   - Hover effect (efficiency is the free parameter):

     | Efficiency | Airframe recovers | Roll bias | Yaw bias | Roll margin | Perfect-pilot hover |
     |---|---|---|---|---|---|
     | 0 | – | +0.50 | −0.70 | 1.07 | lost |
     | 0.15 | ≈ 50% of Q | +0.29 | −0.47 | 1.79 | lost at 5 s (soft drift loop) / holds (tight loop) |
     | 0.30 | ≈ 100% of Q | +0.08 | −0.24 | 5.9 | holds, 0% saturation |

   - Calibration: the strip model over-extracts (efficiency 1 recovers 3.2 Q, which is nonphysical). A real
     implementation needs calibration, or a cap on the recovered torque at about 0.3–0.6 Q.
   - Risk at 0.15 and 0.3 (after using V + w in the swirl formula):
     - Only the 3 golden scenarios move: trainer_wind, trainer_takeoff and sport_rolls, by 0.01–1%. Regenerate them.
     - All 219 other tests pass.
     - The hands-off cruise bank (backlog #1) is unchanged: sport −28.0° → −27.8°, trainer −21.7° → −21.1°.
2. **Model: a physical wash footprint.**
   - What: contraction toward R/√2 plus downstream spreading and a smooth radial profile. Momentum flux should equal T
     rather than 1.28 T. Use an area-weighted segment overlap instead of the centre-point test.
   - Evidence of the discretization problem: tripling the segment count changes aileron authority 0.54 → 0.46, and
     with a straight thrust line 0 → 0.28.
   - Predicted effect:
     - Removes the knife edge and the left/right asymmetry (elevator→roll 0.37 → 0, aileron→pitch 0.25 → 0.10).
     - Honestly *reduces* the sport's in-wash aileron authority, so it must ship with fix 1 or fix 3.
     - Also the right lever for backlog #2 (trainer power pitch-up).
   - Risk: every powered scenario and the goldens change. Behavior tests on takeoff and trim need re-checking.
3. **Data: a 3D setup.** Preferably a new `aircraft/3d` (or a "3D rates" profile) rather than changing the sport.
   - What: ailerons from ~5% span at 30–35% chord and ±40°; elevator and rudder ±45°; 0° right thrust; 0/0 incidence.
   - Predicted: aileron 1.19 N·m (margin 2.4 without swirl), elevator 5.8, rudder 2.5. With a straight thrust line and
     swirl 0.15 it holds a hover with 0% saturation.
   - Risk:
     - Changing the sport itself pushes the cruise roll rate further past backlog #11 (pb/2V already 0.245).
     - It would break `RollRateTests` / fleet expectations and move the goldens.
   - Optional follow-up: flap lift as a ΔCl increment with its own Clmax, so 40–50° throws are not capped by the clean
     polar (+8% at 45° in the spike; wing tests failed with the crude hack, so it needs care).

## Uncertainties

- The real de-swirl fraction for a low-wing sport airframe in hover is unknown, estimated at 30–60% of Q. The spike's
  swirl model is solid-body and not momentum-bounded.
- The toy pilots are crude. The zero-latency results support the authority and margin argument; the human-latency
  runs failed for every variant (lean-loop ringing) and prove nothing either way.
- Motor torque includes no-load friction, so it slightly overstates the aerodynamic reaction.
- The polars are estimated (Re 200k), and in-wash Reynolds numbers are about 200–250k.
- Hover throttle depends on the linear ESC map.
