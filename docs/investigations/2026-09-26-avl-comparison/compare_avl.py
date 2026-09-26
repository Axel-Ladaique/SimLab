import json, subprocess, sys, warnings
warnings.filterwarnings("ignore")
from waxwing.io.simlab_import import definition_from_simlab
from waxwing.analysis.avl_run import run_avl
S = sys.argv[1]; fleet = sys.argv[2]
speeds = {"trainer": 15, "sport": 18, "wing": 14, "3d": 14}
keys = ["CLa","Cma","CYb","Clb","Cnb","Clp","Cnp","Clr","Cnr","CYr","CLq","Cmq"]
ctl = [("aileron","Cl"),("aileron","Cn"),("elevator","Cm"),("elevator","CL"),("rudder","Cn"),("rudder","Cl")]
out = {}
for ac, v in speeds.items():
    sim = json.loads(subprocess.check_output([f"{S}/deriv/bin/Debug/net8.0/Deriv", f"{fleet}/{ac}", str(v)]))
    s = sim["surfaces"]; alpha = s["alphaDeg"]
    avl = run_avl(definition_from_simlab(f"{fleet}/{ac}"), alpha, workdir=f"{S}/avl/{ac}")
    rows = []
    for k in keys:
        rows.append((k, s[k], avl[k]))
    for ch, c in ctl:
        if ch in avl.controls and f"{c}_{ch}" in s:
            rows.append((f"{c}_{ch}/deg", s[f"{c}_{ch}"], avl.controls[ch][c]))
    xnp_sim = -s["Cma"]/s["CLa"]; xnp_avl = -avl["Cma"]/avl["CLa"]
    rows.append(("marge statique (%MAC)", 100*xnp_sim, 100*xnp_avl))
    spiral = lambda d: d["Clb"]*d["Cnr"]/(d["Clr"]*d["Cnb"])
    rows.append(("spirale Clb.Cnr/(Clr.Cnb) (>1 stable)", spiral(s), spiral(avl.values)))
    rows.append(("CL au trim", s["CL"], avl["CLtot"]))
    rows.append(("e (Oswald, AVL Trefftz)", float("nan"), avl["e"]))
    out[ac] = {"alpha": alpha, "V": v, "rows": rows, "bodies": sim["withBodies"]}
    print(f"\n=== {ac}  V {v} m/s  alpha {alpha:.2f} deg")
    print(f"{'':42s}{'SimLab':>10s}{'AVL':>10s}{'ratio':>8s}")
    for k, a, b in rows:
        r = a/b if b not in (0,) and abs(b) > 1e-9 else float('nan')
        print(f"{k:42s}{a:10.4f}{b:10.4f}{r:8.2f}")
json.dump(out, open(f"{S}/compare_{fleet.split('/')[-1]}.json","w"), indent=1)
