"""SimLab vs AVL stability derivatives for the fleet (see ../2026-09-26-avl-comparison.md).

Usage, with the Waxwing venv (it provides waxwing and AVL):
  dotnet build Deriv.csproj -p:TreatWarningsAsErrors=false
  ~/4_WAXWING/.venv/bin/python compare_avl.py bin/Debug/net8.0/Deriv ../../../aircraft [--write-fixture <path>]

The SimLab harness finds the alpha where lift = weight at each aircraft's cruise speed; AVL runs at that alpha.
--write-fixture writes the AVL side as tests/SimLab.Flight.Tests/Behavior/Golden/avl-derivatives.json expects it.
"""
import json, subprocess, sys, tempfile, warnings
warnings.filterwarnings("ignore")
from waxwing.io.simlab_import import definition_from_simlab
from waxwing.analysis.avl_run import run_avl

harness, fleet = sys.argv[1], sys.argv[2]
fixture_path = sys.argv[sys.argv.index("--write-fixture") + 1] if "--write-fixture" in sys.argv else None
speeds = {"trainer": 15, "sport": 18, "wing": 14, "3d": 14}
keys = ["CLa", "Cma", "CYb", "Clb", "Cnb", "Clp", "Cnp", "Clr", "Cnr", "CYr", "CLq", "Cmq"]
fixture = {
    "source": "AVL 3.40b via Waxwing run_avl (definition_from_simlab, no fuselage); "
              "docs/investigations/2026-09-26-avl-comparison/compare_avl.py",
    "conventions": "stability axes; Cl'>0 right wing down, Cm>0 nose up, Cn'>0 nose right; beta>0 wind from the right; "
                   "rates per pb/2V, qc/2V, rb/2V; controls per degree of channel (surface = mix weight x channel)",
    "aircraft": {},
}
for ac, v in speeds.items():
    sim = json.loads(subprocess.check_output([harness, f"{fleet}/{ac}", str(v)]))
    s = sim["surfaces"]
    alpha = s["alphaDeg"]
    with tempfile.TemporaryDirectory() as work:
        avl = run_avl(definition_from_simlab(f"{fleet}/{ac}"), alpha, workdir=work)
    print(f"\n=== {ac}  V {v} m/s  alpha {alpha:.2f} deg")
    print(f"{'':24s}{'SimLab':>10s}{'AVL':>10s}{'ratio':>8s}")
    entry = {"airspeed": v, "alphaDeg": round(alpha, 6)}
    for k in keys:
        entry[k] = avl[k]
        print(f"{k:24s}{s[k]:10.4f}{avl[k]:10.4f}{s[k] / avl[k] if abs(avl[k]) > 1e-9 else float('nan'):8.2f}")
    for channel, coefficients in avl.controls.items():
        for c in ("CL", "Cl", "Cm", "Cn"):
            if c in coefficients:
                entry[f"{c}_{channel}"] = coefficients[c]
                if f"{c}_{channel}" in s:
                    print(f"{c + '_' + channel + '/deg':24s}{s[f'{c}_{channel}']:10.4f}{coefficients[c]:10.4f}")
    entry["CL"] = avl["CLtot"]
    print(f"{'static margin':24s}{-s['Cma'] / s['CLa']:10.4f}{-avl['Cma'] / avl['CLa']:10.4f}")
    fixture["aircraft"][ac] = entry
if fixture_path:
    with open(fixture_path, "w") as f:
        json.dump(fixture, f, indent=2)
    print(f"\nwrote {fixture_path}")
