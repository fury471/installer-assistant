"""Look inside the trained pin reader: what it is, what it looks at, how sure it is, and where it goes wrong.

    python -m ml.eval.inspect_pins --template A1610

Writes figures to ml/runs/inspect_<template>/ and prints the model's facts. The quality figures use the same
row-wise cross-validation as train_pins (predictions for rows the model was not trained on).
"""
import argparse
import csv
import os

import cv2
import joblib
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt                                           # noqa: E402
import numpy as np                                                        # noqa: E402
from sklearn.metrics import confusion_matrix                              # noqa: E402
from sklearn.model_selection import GroupKFold                            # noqa: E402

from ml.eval.train_pins import NAMES, load, new_model                     # noqa: E402
from ml.perception_service.patches import window                          # noqa: E402
from ml.perception_service.pin_reader import FEATURE_SIZE, MODELS, NO_WIRE, calibrated   # noqa: E402

COL = {NO_WIRE: "#bbbbbb", 0: "#e53935", 1: "#222222", 2: "#2e9e4f", 3: "#f2c200"}


def out_of_fold(X, y, groups):
    classes = sorted(set(y.tolist()))
    proba = np.zeros((len(y), len(classes)))
    for tr, te in GroupKFold(n_splits=min(7, len(set(groups)))).split(X, y, groups):
        m = new_model().fit(X[tr], y[tr])
        proba[np.ix_(te, [classes.index(c) for c in m.classes_])] = m.predict_proba(X[te])
    return classes, proba


def reliability(ax, conf, ok, title):
    edges = np.linspace(0, 1, 11)
    mids, accs, counts = [], [], []
    for lo, hi in zip(edges[:-1], edges[1:]):
        sel = (conf > lo) & (conf <= hi)
        if sel.sum():
            mids.append(conf[sel].mean()); accs.append(ok[sel].mean()); counts.append(sel.sum())
    ax.plot([0, 1], [0, 1], "--", color="#999", lw=1)
    ax.scatter(mids, accs, s=[20 + c / 4 for c in counts], color="#1565c0", zorder=3)
    ax.plot(mids, accs, color="#1565c0")
    ax.set(xlim=(0, 1.02), ylim=(0, 1.02), xlabel="confidence", ylabel="share right", title=title)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--template", default="A1610")
    ap.add_argument("--data", default="data")
    ap.add_argument("--sessions", nargs="+", default=["session1"])
    a = ap.parse_args()
    out = os.path.join("ml", "runs", "inspect_" + a.template)
    os.makedirs(out, exist_ok=True)

    path = MODELS / f"pins_{a.template}.joblib"
    if not path.exists():
        raise SystemExit("inspect_pins.py describes the first, per-device pin reader (pins_<template>.joblib, before "
                         "1 Nov 2026). The general reader's numbers are in ml/runs/train_pins.json (train_pins.py).")
    bundle = joblib.load(path)
    model, T = bundle["model"], bundle["temperature"]
    w, h = FEATURE_SIZE[a.template]
    depths = [e.get_depth() for e in model.estimators_]
    leaves = [e.get_n_leaves() for e in model.estimators_]
    print(f"model: {type(model).__name__}, {len(model.estimators_)} trees, classes "
          f"{[NAMES[c] for c in model.classes_]}")
    brightness = bool(bundle.get("brightness", False))
    print(f"input: {model.n_features_in_} numbers per pin = the patch shrunk to {w} x {h} pixels x (L, a, b) "
          f"+ 9 percentiles" + (f" + {w * h} relative L + {w // 2 * h} edges + 3 dark thirds" if brightness else ""))
    print(f"trees: depth median {int(np.median(depths))} (max {max(depths)}), leaves median {int(np.median(leaves))}")
    print(f"temperature: {T:.3f} (below 1 = the raw votes were too modest and are sharpened)")
    print("trained:", bundle.get("info", {}))

    X, y, groups = load(a.data, a.sessions, a.template, brightness)
    files, margins = [], []
    for session in a.sessions:
        folder = os.path.join(a.data, "patches", session)
        for r in csv.DictReader(open(os.path.join(folder, "labels.csv"), encoding="utf-8")):
            if r["located"] == "True" and (r["module"] == "A4020E") == (a.template == "A4020E"):
                files.append(os.path.join(folder, r["file"]))
                margins.append(int(r.get("margin") or 0))
    show = lambda i: window(cv2.cvtColor(cv2.imread(files[i]), cv2.COLOR_BGR2RGB), margins[i])   # noqa: E731

    # 1. what the model looks at: importance of each feature, drawn where it sits in the patch
    imp = model.feature_importances_
    grid = imp[:w * h * 3].reshape(h, w, 3)
    fig, axes = plt.subplots(1, 4, figsize=(15, 2.8), gridspec_kw={"width_ratios": [1, 1, 1, 0.8]})
    for k, name in enumerate(["L (brightness)", "a (green - red)", "b (blue - yellow)"]):
        im = axes[k].imshow(grid[:, :, k], cmap="magma", vmin=0, vmax=grid.max())
        axes[k].set_title(name); axes[k].set_xticks([]); axes[k].set_yticks([])
    n = w * h * 3
    groups_imp = {"L, a, b grid": imp[:n].sum(), "percentiles": imp[n:n + 9].sum()}
    if brightness:
        groups_imp.update({"relative L": imp[n + 9:n + 9 + w * h].sum(),
                           "edges": imp[n + 9 + w * h:-3].sum(), "dark thirds": imp[-3:].sum()})
    axes[3].barh(list(groups_imp), list(groups_imp.values()), color="#1565c0")
    axes[3].invert_yaxis()
    axes[3].set_title("share of importance")
    fig.colorbar(im, ax=axes[:3], shrink=0.8, label="importance")
    fig.suptitle(f"What the {a.template} pin reader looks at (wire leaves the pin to the right)", y=1.02)
    fig.savefig(os.path.join(out, "1_importance.png"), dpi=130, bbox_inches="tight"); plt.close(fig)

    # 2. calibration and 3. confusion, from out-of-fold predictions
    classes, proba = out_of_fold(X, y, groups)
    idx = np.array([classes.index(c) for c in y])
    cal = calibrated(proba, T)
    fig, axes = plt.subplots(1, 3, figsize=(15, 4.4))
    reliability(axes[0], proba.max(1), proba.argmax(1) == idx, "Confidence before temperature scaling")
    reliability(axes[1], cal.max(1), cal.argmax(1) == idx, f"After (T = {T:.2f})")
    pred = np.array(classes)[cal.argmax(1)]
    cm = confusion_matrix(y, pred, labels=classes)
    axes[2].imshow(np.log1p(cm), cmap="Blues")
    for i in range(len(classes)):
        for j in range(len(classes)):
            axes[2].text(j, i, cm[i, j], ha="center", va="center", color="white" if cm[i, j] > cm.max() / 3 else "black")
    names = [NAMES[c] for c in classes]
    axes[2].set(xticks=range(len(classes)), yticks=range(len(classes)), xticklabels=names, yticklabels=names,
                xlabel="read as", ylabel="true", title="Confusion (rows held out)")
    fig.savefig(os.path.join(out, "2_calibration_confusion.png"), dpi=130, bbox_inches="tight"); plt.close(fig)

    # 4. the mistakes, most confident first: the dangerous ones
    conf = cal.max(1)
    wrong = np.where(pred != y)[0]
    wrong = wrong[np.argsort(-conf[wrong])][:24]
    cols = 6
    fig, axes = plt.subplots(int(np.ceil(len(wrong) / cols)) or 1, cols, figsize=(15, 1.3 * np.ceil(len(wrong) / cols) + 0.5))
    for ax in np.ravel(axes):
        ax.axis("off")
    for ax, i in zip(np.ravel(axes), wrong):
        ax.imshow(show(i))
        ax.set_title(f"{NAMES[y[i]]} read {NAMES[pred[i]]} {conf[i]:.2f}\n{os.path.basename(files[i])[3:30]}", fontsize=7)
    fig.suptitle("Mistakes, most confident first (true, read as, confidence)")
    fig.savefig(os.path.join(out, "3_mistakes.png"), dpi=130, bbox_inches="tight"); plt.close(fig)

    # 5. one pin, many trees: how the 400 trees vote
    i = int(np.where(y == 1)[0][0]) if (y == 1).any() else 0
    votes = np.array([e.predict_proba(X[i:i + 1])[0] for e in model.estimators_])
    fig, axes = plt.subplots(1, 2, figsize=(10, 2.8), gridspec_kw={"width_ratios": [1, 2]})
    axes[0].imshow(show(i)); axes[0].axis("off")
    axes[0].set_title(f"true: {NAMES[y[i]]}")
    share = votes.argmax(1)
    counts = np.bincount(share, minlength=len(model.classes_))
    axes[1].bar([NAMES[c] for c in model.classes_], counts, color=[COL.get(int(c), "#888") for c in model.classes_])
    axes[1].set_title(f"votes of the {len(model.estimators_)} trees (a pin seen in training, so it is easy)")
    fig.savefig(os.path.join(out, "4_votes.png"), dpi=130, bbox_inches="tight"); plt.close(fig)
    print("figures in", out)


if __name__ == "__main__":
    main()
