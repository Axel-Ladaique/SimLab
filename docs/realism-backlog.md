# Realism backlog (input for sub-project 2)

Items found while tuning the core libraries against behavior tests. None blocks sub-project 1; each should be
checked against real flight data (telemetry replay) before being "fixed".

| # | Observation | Likely cause (hypothesis) | How to check |
|---|-------------|---------------------------|--------------|
| 1 | Hands-off at trimmed cruise, trainer/sport settle into a 20–28° left bank; the wing reaches ~50° after 30 s. | No slipstream swirl on wing root/fin; motor reaction torque includes friction; no fuselage/high-wing dihedral effect; roll-due-to-yaw-rate overstated by strip model (~CL/2.9 vs DATCOM ~CL/4). | Hands-off logs from the real trainer; add swirl to the prop-wash model. |
| 2 | Trainer pitches up strongly with full power (tail download × prop wash). | Wash applied at 0.8 × far-wake velocity over the full disk radius with no contraction; stab root inside it. | Compare with real power-on pitch trim change; model wash contraction and development length. |
| 3 | Flying wing passes the behavior tests only with the wing root quarter-chord 0.211–0.214 m behind the nose (cg at 0.32 m behind the nose; static margin +2.6% MAC; real wings fly at 5–10%). The originally estimated position is unstable. | CG/geometry estimates; the hand-launch test is open-loop and ties the CG to the test. | Measure the real airframe's CG, then set it with the `cg` datum in aircraft.json; give the launch test a closed-loop pilot. |
| 4 | Trainer resists spin entry (α ≈ 8.7°, yaw rate ≈ 1.1 rad/s under pro-spin inputs). | Plausible for a real trainer; may also reflect benign post-stall polars. | Real spin attempts at altitude. |
| 5 | Sport recovers from a developed spin almost instantly, also hands-off (spec expects it not to recover hands-off). | Post-stall yaw damping too high; no fuselage side-area/strake modeling. | Real spin logs; tune post-stall polars. |
| 6 | Controls: trim is applied before expo (spec order); EdgeTX applies trim after expo/rates. | Design choice. | Only matters if sim-side trims become non-zero. |
| 7 | Trainer drifts ~12 m sideways during an 8 s full-throttle ground roll with no rudder input. | P-factor/slipstream swirl and motor torque on the gear not balanced by nose-wheel steering; tire side-force model. | Real takeoff roll hands-off on the rudder; compare lateral drift. |
| 8 | Sport noses over in the scripted takeoff (full throttle, near-neutral elevator). | Taildragger ground attitude, CG vs main-gear position, prop-wash pitch-down on the stab. | Real takeoff with neutral elevator; check CG and main-gear position. |
| 9 | Sport drifts off the runway during the takeoff roll without rudder. | Taildragger yaw instability on the ground, tail-wheel steering/friction model. | Real takeoff roll; measure the rudder needed to track straight. |
| 10 | Flying wing rolls to knife-edge at full throttle hands-off. | Motor reaction torque with no fin/slipstream counter-moment; low roll damping. | Real full-throttle hands-off logs; check motor torque and roll damping. |
| 11 | Sport full-aileron roll helix angle pb/2V is 0.245 at 18 m/s (421°/s) with the DATCOM plain-flap large-deflection correction (0.361 without it); RC aerobatic models reach about 0.12–0.18. Throw is no longer the lever: K'·δ plateaus above ~18°, and 15° still gives 0.218. `RollRateTests.Sport_full_aileron_roll_helix_angle_is_realistic` is skipped until this is resolved. | Ailerons span 15–95% of the half-span (large for a sport model); thin-airfoil flap effectiveness × 0.85 may be optimistic for a thick section at low Reynolds number (DATCOM's cl_δ/cl_δ,theory correction, Figure 6.1.1.1-39B, is not modeled); roll damping from the strip model may be low. | Real roll-rate log at full throw; measure the real aileron span and chord; compare Cl_p and Cl_δ with VSPAERO. |

Tuning history: `docs/tuning-log.md`.
