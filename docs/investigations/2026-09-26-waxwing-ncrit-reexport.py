# Run Waxwing's SimLab reexport with NeuralFoil at a given n_crit (Waxwing itself does not expose it yet).
import sys, numpy as np
import waxwing.analysis.polars as P
n_crit = float(sys.argv[1])
def _neuralfoil(self, alpha_deg):
    aero = self.airfoil.get_aero_from_neuralfoil(alpha=alpha_deg, Re=self.reynolds, model_size=self.model_size,
                                                 include_360_deg_effects=False, n_crit=n_crit)
    return {k: np.asarray(aero[k], dtype=float) for k in ("CL", "CD", "CM")}
P.Polar360._neuralfoil = _neuralfoil
from waxwing.cli import main
sys.argv = ["waxwing", "simlab", "reexport", sys.argv[2], sys.argv[3]]
main()
