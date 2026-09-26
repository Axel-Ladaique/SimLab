# SimLab strip model vs AVL: stability derivatives (2026-09-26)

Linear stability derivatives of the four aircraft from SimLab's `SurfaceAeroModel` (finite differences) and
from AVL 3.40 (vortex lattice, run through Waxwing's `run_avl`), at the same point: cruise airspeed, the alpha
where SimLab's lift equals the weight with zero deflections, sea level, out of ground effect, power off.

- Same geometry on both sides: AVL gets the aircraft through `waxwing` `definition_from_simlab` (no fuselage),
  SimLab is evaluated with surfaces only (bodies off). Same references (Sref, Cref = wing MAC, Bref, CG) — checked.
- Stability axes, AVL conventions: Cl' > 0 right wing down, Cm > 0 nose up, Cn' > 0 nose right, β > 0 wind
  from the right; rates per p b/2V, q c/2V, r b/2V; controls per degree of channel, surface = mix weight × channel.
- SimLab's lagged downwash is set to its quasi-steady value before each evaluation (AVL is quasi-steady).
- Scripts and full tables: `2026-09-26-avl-comparison/` (`Deriv.cs` harness, `compare_avl.py`, results for the
  main-branch polars and Waxwing's n_crit 9 / n_crit 5 polars).

## Ratios SimLab / AVL (main-branch polars)

| | trainer | sport | wing | 3d |
|---|---|---|---|---|
| CLα | 0.98 | 1.00 | 0.87 | 1.01 |
| Cmα | 1.41 | 1.57 | 1.39 | 1.85 |
| Static margin (SimLab / AVL, % MAC) | 18.5 / 12.9 | 17.7 / 11.3 | 8.2 / 5.1 | 17.8 / 9.8 |
| CYβ | 1.44 | 1.43 | 1.33 | 1.64 |
| Cnβ | 1.65 | 1.60 | 1.47 | 1.78 |
| Clβ | 0.90 | 0.50 | 0.41 | 0.63 |
| Clp | 1.79 | 1.75 | 1.30 | 1.81 |
| Cnp (SimLab / AVL) | +0.002 / −0.016 | +0.013 / −0.026 | −0.009 / −0.035 | +0.037 / −0.025 |
| Clr | 1.15 | 1.36 | 0.77 | 1.55 |
| Cnr | 1.42 | 1.44 | 1.04 | 1.48 |
| CLq | 0.34 | 0.45 | 0.12 | 0.47 |
| Cmq | 1.00 | 1.03 | 0.56 | 1.04 |
| Cl δa | 1.20 | 1.19 | 0.86 | 1.32 |
| Cn δa (SimLab / AVL, per deg) | −0.0005 / −0.0003 | −0.0005 / +0.0004 | −0.0002 / +0.0001 | −0.0006 / +0.0008 |
| Cm δe | 0.88 | 0.91 | 0.82 | 0.93 |
| Cn δr | 1.17 | 1.23 | – | 1.41 |
| Steady roll −Clδa/Clp (pb/2V per deg) | 0.67 | 0.68 | 0.67 | 0.73 |
| Spiral Clβ·Cnr/(Clr·Cnβ), > 1 stable (SimLab / AVL) | 0.95 / 1.41 | 0.39 / 1.17 | 0.27 / 0.71 | 0.34 / 1.00 |

AVL span efficiency (Trefftz plane): trainer 0.94, sport 1.01, wing 1.02, 3d 0.90 (SimLab uses 0.85 everywhere).

With Waxwing's polars (n_crit 9 or 5) the ratios move by a few percent only (static margin ratio 1.21–1.45 with
n_crit 5): the gaps come from the model, not the airfoil data.

## Reading

1. **Roll damping is too high, not too low** (Clp 1.3–1.8×), and aileron power a little high (1.2–1.3×). The
   strip model gives every strip the induced angle of the symmetric, whole-surface loading and has no tip loss;
   an antisymmetric (rolling) loading has larger induced angles and the tip strips have the longest arm. Net
   steady roll per degree of aileron is **0.67–0.73× AVL**: SimLab rolls slower than the linear vortex lattice.
   Backlog #11 (sport pb/2V 0.227 "too high") is therefore not a model over-prediction: at the same throws AVL
   predicts more. The ailerons' size and throws, or the 0.12–0.18 target, are the question.
2. **Directional stability too high** (Cnβ 1.5–1.8×, Cnr ~1.4×, CYβ ~1.4×) while the **dihedral effect is low**
   (Clβ 0.4–0.9×). The fin sees no sidewash, has a 2D-like lift slope, and its induced factor uses a doubled
   aspect ratio (reflection plane). Together these make every aircraft **spirally unstable in SimLab**
   (criterion 0.27–0.95) where AVL gives neutral to stable (0.71–1.41, only the flying wing < 1). This is the most
   likely cause of backlog #1 (hands-off cruise settling into a 20–30° bank) and fits #5 (spin recovery too easy).
   A fuselage would lower Cnβ further (it is destabilizing), so the real gap is larger.
3. **Static margin too high** by 1.4–1.8× on the tailed aircraft (tail too effective: downwash gradient
   1.6·CL/(πAR) is low, plus the same fin/tail lift slope issue). The **flying wing's** margin is 8.2% in SimLab
   but 5.1% in AVL: the CG tuned to 7.7% in SimLab (backlog #3) is about 5% by AVL.
4. **CLq is 0.1–0.5×**, and the flying wing's **Cmq is 0.56×**: strips take the velocity at the quarter chord, so
   the camber-like effect of pitch rate (the 3/4-chord point, Pistolesi) is lost. It matters little for the tailed
   aircraft (their Cmq comes from the tail and matches AVL), but the flying wing has half the pitch damping it
   should.
5. **Cnp and Cnδa have the wrong sign or size** (SimLab proverse, AVL adverse Cnp): no sidewash at the fin from the
   rolling wing's wake; small derivatives, but they set the adverse yaw a pilot feels.

AVL is inviscid and linear (no stall, no viscous drag change with deflection, no fuselage here); it is the usual
reference for these derivatives on model aircraft, typically within 10–20% of wind-tunnel data in the linear range.

## What would close the gaps

- A lifting-line / Weissinger induced-flow solve over all strips (per surface, with the wing's wake influence on
  the tail and fin) instead of one induced factor per surface: fixes Clp, Clδa, Clr, Cnp, downwash, sidewash.
- Evaluate the rotation term at the 3/4-chord point: fixes CLq and the flying wing's Cmq; cheap.
- Until then, per-surface corrections calibrated on AVL (fin and tail effectiveness factors, downwash gradient,
  Oswald from AVL's e) would bring the static and directional derivatives close with no structural change.
- Re-check backlog #1, #3, #5, #11 after each change with `compare_avl.py`.

## After the three-quarter-chord rotation term (same day)

`SurfaceAeroModel` now takes the rotation velocity at each strip's three-quarter-chord point and adds the
thin-airfoil pitch-rate camber moment −(π/4)·qc/(2V) (docs/tuning-log.md, 2026-09-26). Ratios SimLab / AVL,
main-branch polars (full table: `2026-09-26-avl-comparison/results-three-quarter-chord.txt`):

| | trainer | sport | wing | 3d |
|---|---|---|---|---|
| CLq (before → after) | 0.34 → 0.95 | 0.45 → 0.97 | 0.12 → 0.86 | 0.47 → 0.98 |
| Cmq | 1.00 → 1.12 | 1.03 → 1.12 | 0.56 → 1.20 | 1.04 → 1.16 |
| Cnr | 1.42 → 1.57 | 1.44 → 1.60 | 1.04 → 1.29 | 1.48 → 1.77 |
| CYr | 1.39 → 1.56 | 1.48 → 1.64 | 1.23 → 1.53 | 1.50 → 1.77 |

The static and roll derivatives do not change. Cmq now overshoots by the same kind of margin as Cmα (tail and
flying-wing pitch effectiveness, item 3); Cnr and CYr grow because the fin also gets the yaw-rate camber effect,
which is physical (AVL has it) but adds to the fin's over-effectiveness (item 2). Both residuals belong to the
lifting-line / sidewash work, not to this term.

## After the lifting line (same day)

`SurfaceAeroModel` now solves a Weissinger lifting line over all strips (docs/superpowers/specs/2026-09-26-lifting-line-design.md),
with the fleet re-tuned (docs/tuning-log.md, 2026-09-26) and the trailing-leg forces taken per strip end with the chord
there (the per-strip couple over-counted the dihedral effect of tapered wings). Ratios SimLab / AVL, from
`AvlDerivativeTests.Report_ratio_table`:

| | trainer | sport | wing | 3d |
|---|---|---|---|---|
| CLa | 0.95 | 0.95 | 0.97 | 0.93 |
| Clb | 1.08 | 0.92 | 1.24 | 0.90 |
| Cnb | 1.16 | 1.11 | 1.01 | 1.08 |
| Clp | 1.09 | 1.00 | 1.10 | 0.99 |
| Clr | 0.99 | 1.03 | 1.29 | 1.01 |
| Cnr | 1.13 | 1.09 | 1.04 | 1.06 |
| Cmq | 1.03 | 1.01 | 1.15 | 1.00 |
| Cl_aileron (without the 0.85 viscous flap efficiency) | 0.88 | 0.89 | 0.88 | 0.88 |
| Cm_elevator (idem) | 0.93 | 0.96 | 0.96 | 0.96 |
| Static margin, SimLab / AVL | 18.5 / 12.7 % | 15.5 / 11.2 % | 9.8 / 5.1 % | 19.1 / 16.8 % |

The fin over-effectiveness (Cnb 1.5–1.8, item 2), the low dihedral effect and Clp 1.8 are gone. Left
(docs/realism-backlog.md #16): the flying wing's winglets act too strongly on the wing tips (Clb, Clr; the wing alone
matches AVL at every sweep: Clb x1.01 unswept, x0.98 at 25°, x0.94 at 35°), and the neutral point sits about 3 % of
the chord further aft than AVL's on every planform, whatever the sweep.
