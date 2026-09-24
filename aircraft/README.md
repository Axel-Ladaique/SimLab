# Entering an aircraft

Positions are in body axes **x back, y right, z up**, in metres, measured from any datum you like
(the nose tip is used for the shipped aircraft). Give the centre of gravity with `"cg": [x, y, z]` in the
same datum — it is required. The simulator moves everything so the CG is the origin.

- Surfaces are defined by their right panel (`"mirror": true` makes the left one). `root` is the root
  quarter-chord point; `span` is the panel span; `sweepDeg`, `dihedralDeg`, `incidenceDeg`, `twistDeg`
  keep their usual meaning; a fin is a surface with `dihedralDeg: 90`.
- `power.json`: `position` of the prop disc (same datum), `thrustAxis` is the thrust direction
  (`[-1, 0, 0]` = straight ahead).
- Inertia: `roll` = I_xx, `pitch` = I_yy, `yaw` = I_zz (kg·m², about the CG).
