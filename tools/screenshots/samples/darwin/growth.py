import sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
t = np.linspace(0, 30, 300)
N0, r, K = 10, 0.3, 1000
exp = N0 * np.exp(r * t)
log = K / (1 + (K / N0 - 1) * np.exp(-r * t))
fig, ax = plt.subplots(figsize=(8, 4.2), dpi=100)
ax.plot(t, exp, color="#d0453a", lw=2.5, label="unlimited: $N_0 e^{rt}$")
ax.plot(t, log, color="#2f6db5", lw=2.5, label="limited: logistic")
ax.axhline(K, color="#888888", lw=1.2, ls="--")
ax.text(0.5, K + 25, "carrying capacity K = 1000", color="#555555", fontsize=11)
ax.set_ylim(0, 1400); ax.set_xlim(0, 30)
ax.set_xlabel("generations", fontsize=12); ax.set_ylabel("population", fontsize=12)
ax.set_title("Growth with and without a limit (r = 0.3)", fontsize=13)
ax.legend(loc="center right", fontsize=11, frameon=False)
for s in ("top", "right"): ax.spines[s].set_visible(False)
fig.tight_layout()
fig.savefig(sys.argv[1], facecolor="white")
