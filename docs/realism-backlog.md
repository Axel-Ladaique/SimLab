# Realism backlog (input for sub-project 2)

Items found while tuning the core libraries against behavior tests. None blocks sub-project 1; each should be
checked against real flight data (telemetry replay) before being "fixed".

| # | Observation | Likely cause (hypothesis) | How to check |
|---|-------------|---------------------------|--------------|
| 1 | Hands-off at trimmed cruise, trainer/sport settle into a 20–28° left bank; the wing reaches ~50° after 30 s. | No slipstream swirl on wing root/fin; motor reaction torque includes friction; no fuselage/high-wing dihedral effect; roll-due-to-yaw-rate overstated by strip model (~CL/2.9 vs DATCOM ~CL/4). | Hands-off logs from the real trainer; add swirl to the prop-wash model. |
| 2 | Trainer pitches up strongly with full power (tail download × prop wash). | Wash applied at 0.8 × far-wake velocity over the full disk radius with no contraction; stab root inside it. | Compare with real power-on pitch trim change; model wash contraction and development length. |
| 3 | Flying wing passes the behavior tests only with the wing root at x 0.106–0.109 m (static margin +2.6% MAC; real wings fly at 5–10%). The originally estimated position is unstable. | CG/geometry estimates; the hand-launch test is open-loop and ties the CG to the test. | Measure the real airframe's CG, then set it with the `cg` datum in aircraft.json; give the launch test a closed-loop pilot. |
| 4 | Trainer resists spin entry (α ≈ 8.7°, yaw rate ≈ 1.1 rad/s under pro-spin inputs). | Plausible for a real trainer; may also reflect benign post-stall polars. | Real spin attempts at altitude. |
| 5 | Sport recovers from a developed spin almost instantly, also hands-off (spec expects it not to recover hands-off). | Post-stall yaw damping too high; no fuselage side-area/strake modeling. | Real spin logs; tune post-stall polars. |
| 6 | Controls: trim is applied before expo (spec order); EdgeTX applies trim after expo/rates. | Design choice. | Only matters if sim-side trims become non-zero. |

Tuning history: `docs/tuning-log.md`.
