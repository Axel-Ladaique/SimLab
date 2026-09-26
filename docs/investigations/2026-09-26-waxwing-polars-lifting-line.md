# Waxwing polars on the lifting line (2026-09-26)

Merged 2026-09-26 for the trainer, sport and 3D, after the local-slope Jacobian (see "Resolution"). Follow-up of `exp/waxwing-fleet` (docs/investigations/2026-09-26-waxwing-polars.md
there), redone on the lifting line: only the polars are taken from Waxwing, the aircraft keep main's tuning.

Polars regenerated with `2026-09-26-waxwing-ncrit-reexport.py` (Waxwing's `simlab reexport` with NeuralFoil's `n_crit`
forced; Waxwing does not expose it).

## Results (flight tests, lifting line)

| | n_crit 5 | n_crit 7 |
|---|---|---|
| Trainer, sport, 3D | all pass | trainer Dutch roll, sport hover torque, AVL Cnb/Cnr/Clp x1.16-1.27 fail |
| Flying wing (MH45) | trim elevator 0.30 (max 0.25), hands-off NoseOver, belly HardLanding; twist -5 deg fixes those, but Clb goes to x1.46 of AVL (backlog #16) | same |

This branch carries n_crit 5 for the trainer, sport and 3D; the flying wing keeps `reflex-mh45`.

## Why it was not merged at first

The low-Reynolds tables keep a laminar-bubble kink around alpha 0 even at n_crit 5 (NACA 0012 at Re 80k: lift slope
9.3/rad between 0 and 2 deg, 10.6 at n_crit 9, 2pi = 6.3). The lifting line's Newton solve uses a constant Jacobian
with the lift slope probed at +-2 deg, so with these polars it needs about 6 iterations instead of 2 and some solves
stop unconverged: the aero evaluation goes from 60-80 us to 110-650 us (3D, sport, trainer), over the 100 us budget,
and unconverged solves make the loads noisy.

## Resolution

`LiftingLine` now refactors its Jacobian with each strip's local lift slope when a solve is not converged after four
steps (central difference of +-0.1 deg, floored at half the attached slope; `Reset` restores the construction
Jacobian). With these polars: no unconverged solve in the timing test, two iterations per solve and one refresh in
4500 evaluations.

## Still open

- Waxwing: expose `n_crit` (5 for RC outdoors) instead of the script here.
- Flying wing: keeps `reflex-mh45` until backlog #16 (winglet-on-wing interaction); the MH45 n_crit 5 polar also needs
  about -5 deg of twist to trim.
