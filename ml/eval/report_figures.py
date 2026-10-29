"""Figures for the report: the pin reader's refinement and the pin finder, drawn from the data, never typed in.

    python -m ml.eval.report_figures                      (all figures)
    python -m ml.eval.report_figures --only ablation confusion
    python -m ml.eval.report_figures --fresh              (recompute the cached parts)

Writes docs/figures/report/<name>.pdf (vector, for LaTeX) and <name>.png (300 dpi), sized for an IEEE two-column
page (one column 3.5 in, full width 7.16 in), plus docs/figures/report/numbers.json with every number a figure
shows, so a caption can quote it. The slow parts (row-wise cross-validation of each pin-reader configuration, and
locate() on every annotated photo) are cached in ml/runs/report/<os>/ and reused; delete that folder or pass --fresh
after the data or the code change. Session 2 is never touched.

Figures
  pipeline      photo with found pins -> frame warped into the template's view with the pin windows -> patches
  locate_cdf    share of pins found within a given error (pin spacings), board and reader, stage 1 vs stage 2
  ablation      the pin-reader changes tried on session 1: pins right, missed wires, confident errors
  confusion     confusion matrices, base vs shipped (board)
  confidence    reliability before / after temperature scaling, and error vs share answered (base vs shipped)
  examples      patches the refinement fixed, and the ones still wrong (most confident first)
  augmentation  one patch and its six training copies
  importance    which features the shipped model uses, as maps of the patch and as shares
"""
import argparse
import glob
import json
import os
import sys

import cv2
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt                                            # noqa: E402
import numpy as np                                                         # noqa: E402
from matplotlib.colors import LinearSegmentedColormap                      # noqa: E402
from sklearn.metrics import confusion_matrix                               # noqa: E402
from sklearn.model_selection import GroupKFold                             # noqa: E402

from ml.eval import metrics                                                # noqa: E402
from ml.eval.train_pins import NAMES, fit_temperature, load_patches, new_model, with_copies   # noqa: E402
from ml.perception_service.locate import Template, locate                  # noqa: E402
from ml.perception_service.patches import patch_box, pin_patches, rectify, window   # noqa: E402
from ml.perception_service.pin_reader import FEATURE_SIZE, NO_WIRE, augmented, calibrated, features   # noqa: E402

OUT = os.path.join("docs", "figures", "report")
CACHE = os.path.join("ml", "runs", "report", sys.platform)   # per OS: OpenCV builds differ slightly
COL1, COL2 = 3.5, 7.16                      # IEEE column and page width, inches
THRESHOLD = 0.8                             # below this the app asks the installer

# colours: the shipped method is the one accent; everything it is compared with is neutral grey
SHIPPED, BASE, OTHER, ACCENT2 = "#2a78d6", "#7a7974", "#b9b8b2", "#eb6834"
INK, MUTED, GRID = "#1a1a19", "#52514e", "#e4e3df"
BLUES = LinearSegmentedColormap.from_list("blues", ["#ffffff", "#cde2fb", "#86b6ef", "#2a78d6", "#104281"])

# the pin-reader configurations tried on session 1 (ml/LOG.md, M3 step 2); "both" is the shipped one
CONFIGS = {
    "base":       {"label": "Base (colour grid + percentiles)", "brightness": False, "augment": False, "taller": 0},
    "taller":     {"label": "Taller window", "brightness": False, "augment": False, "taller": 4},
    "brightness": {"label": "+ brightness features", "brightness": True, "augment": False, "taller": 0},
    "augment":    {"label": "+ augmentation", "brightness": False, "augment": True, "taller": 0},
    "both":       {"label": "Brightness + augmentation (shipped)", "brightness": True, "augment": True, "taller": 0},
}
READER_CONFIGS = ["base", "both"]


def style():
    plt.rcParams.update({
        "font.family": "serif", "font.serif": ["Times New Roman", "Liberation Serif", "STIXGeneral", "DejaVu Serif"],
        "mathtext.fontset": "stix", "font.size": 8, "axes.titlesize": 8, "axes.labelsize": 8,
        "xtick.labelsize": 7, "ytick.labelsize": 7, "legend.fontsize": 7, "axes.edgecolor": MUTED,
        "axes.labelcolor": INK, "xtick.color": MUTED, "ytick.color": MUTED, "text.color": INK,
        "axes.spines.top": False, "axes.spines.right": False, "axes.linewidth": 0.6,
        "xtick.major.width": 0.6, "ytick.major.width": 0.6, "axes.titlelocation": "left",
        "axes.titleweight": "bold", "legend.frameon": False, "savefig.dpi": 300, "pdf.fonttype": 42,
    })


def save(fig, name):
    os.makedirs(OUT, exist_ok=True)
    for ext in ("pdf", "png"):
        fig.savefig(os.path.join(OUT, f"{name}.{ext}"), bbox_inches="tight", pad_inches=0.02)
    plt.close(fig)
    print("  wrote", os.path.join(OUT, name + ".pdf / .png"))


# ---------------------------------------------------------------- cached computations

def cross_validate(data, sessions, template, config, fresh=False):
    """Row-wise cross-validated predictions of one configuration (same folds and model as train_pins)."""
    path = os.path.join(CACHE, f"cv_{template}_{config}.npz")
    if os.path.exists(path) and not fresh:
        z = np.load(path, allow_pickle=False)
        return {k: z[k] for k in z.files}
    c = CONFIGS[config]
    print(f"  cross-validating {template} / {config} ...", flush=True)
    patches, y, groups, files, margins = load_patches(data, sessions, template)
    size = FEATURE_SIZE[template]
    X = np.array([features(p[m:p.shape[0] - m + c["taller"], m:p.shape[1] - m], size, c["brightness"])
                  for p, m in zip(patches, margins)])
    Xa = None
    if c["augment"]:
        x0, y0, _, _ = patch_box(template)
        Xa = np.array([[features(a, size, c["brightness"]) for a in augmented(p, m, (m - x0, m - y0))]
                       for p, m in zip(patches, margins)])
    classes = sorted(set(y.tolist()))
    proba = np.zeros((len(y), len(classes)))
    for train, test in GroupKFold(n_splits=min(7, len(set(groups)))).split(X, y, groups):
        m = new_model().fit(*with_copies(X, y, Xa, train))
        proba[np.ix_(test, [classes.index(k) for k in m.classes_])] = m.predict_proba(X[test])
    T = fit_temperature(proba, y, classes)
    out = {"proba": proba, "y": y, "classes": np.array(classes), "temperature": np.array(T),
           "files": np.array(files), "margins": np.array(margins)}
    os.makedirs(CACHE, exist_ok=True)
    np.savez_compressed(path, **out)
    return out


def importance(data, sessions, fresh=False):
    """Feature importances of the shipped board model (brightness + augmentation, trained on everything)."""
    path = os.path.join(CACHE, "importance_A1610.npy")
    if os.path.exists(path) and not fresh:
        return np.load(path)
    print("  training the shipped board model for its importances ...", flush=True)
    patches, y, _, _, margins = load_patches(data, sessions, "A1610")
    size = FEATURE_SIZE["A1610"]
    x0, y0, _, _ = patch_box("A1610")
    X = np.array([features(window(p, m), size) for p, m in zip(patches, margins)])
    Xa = np.array([[features(c, size) for c in augmented(p, m, (m - x0, m - y0))] for p, m in zip(patches, margins)])
    imp = new_model().fit(*with_copies(X, y, Xa, np.arange(len(y)))).feature_importances_
    os.makedirs(CACHE, exist_ok=True)
    np.save(path, imp)
    return imp


def summary(r):
    classes = list(r["classes"])
    cal = calibrated(r["proba"], float(r["temperature"]))
    pred = np.array(classes)[cal.argmax(1)]
    conf = cal.max(1)
    y = r["y"]
    ok = pred == y
    wired = y != NO_WIRE
    keep = conf >= THRESHOLD
    return {"pins": int(len(y)), "wires": int(wired.sum()), "pins_right": float(ok.mean()),
            "missed_wires": int(np.sum(wired & (pred == NO_WIRE))),
            "false_wires": int(np.sum(~wired & (pred != NO_WIRE))),
            "colour_right_on_wired_pins": float(ok[wired].mean()) if wired.any() else None,
            "answered_at_threshold": float(keep.mean()), "errors_at_threshold": int(np.sum(keep & ~ok)),
            "ece_before": float(metrics.ece(r["proba"].max(1), r["proba"].argmax(1) ==
                                            np.array([classes.index(c) for c in y]))),
            "ece_after": float(metrics.ece(conf, ok)), "temperature": float(r["temperature"])}, pred, conf


def locate_errors(data, session, end, fresh=False):
    """Per-pin errors (pin spacings) of locate() on every annotated photo of one end, stage 1 and final."""
    path = os.path.join(CACHE, f"locate_{session}_{end}.json")
    if os.path.exists(path) and not fresh:
        return json.load(open(path, encoding="utf-8"))
    from ml.eval.eval_locate import score_photo
    print(f"  locating every {end} photo ...", flush=True)
    tpl = Template.load("A1610" if end == "controller" else "A4020E")
    out = {"final": [], "stage1": [], "photos": 0, "found": 0, "ms": []}
    for f in sorted(glob.glob(os.path.join(data, "annotations", session, "*.json"))):
        ann = json.loads(open(f, encoding="utf-8").read())
        if ann["end"] != end:
            continue
        res, ms, _, err, err1, _ = score_photo(tpl, ann, data, 1920)
        out["photos"] += 1
        out["found"] += int(res.found)
        out["final"] += [float(e) for e in err]
        out["stage1"] += [float(e) for e in err1]
        out["ms"].append(ms)
    os.makedirs(CACHE, exist_ok=True)
    json.dump(out, open(path, "w", encoding="utf-8"))
    return out


# ---------------------------------------------------------------- figures

def fig_pipeline(a, numbers):
    """One board photo through the pipeline."""
    ann = json.loads(open(os.path.join(a.data, "annotations", a.session, a.photo + ".json"), encoding="utf-8").read())
    rgb = cv2.cvtColor(cv2.imread(os.path.join(a.data, ann["image"])), cv2.COLOR_BGR2RGB)
    s = 1920 / max(rgb.shape[:2])
    rgb = cv2.resize(rgb, None, fx=s, fy=s, interpolation=cv2.INTER_AREA) if s < 1 else rgb
    tpl = Template.load("A1610")
    res = locate(tpl, rgb)
    if not res.found:
        print("  pipeline: board not found in", a.photo); return
    view = rectify(tpl, res, rgb)
    pts = np.array([p for m in res.pins.values() for p in m])
    tp = np.array([(p["x"] + res.shifts.get(m, (0, 0))[0], p["y"] + res.shifts.get(m, (0, 0))[1])
                   for m, pins in tpl.modules.items() for p in pins])
    x0, y0, x1, y1 = patch_box(tpl)

    pad = 0.6 * np.ptp(pts[:, 1])
    cx0, cx1 = int(max(0, pts[:, 0].min() - pad / 4)), int(min(rgb.shape[1], pts[:, 0].max() + pad / 1.6))
    cy0, cy1 = int(max(0, pts[:, 1].min() - pad / 8)), int(min(rgb.shape[0], pts[:, 1].max() + pad / 8))
    vx0, vx1 = int(tp[:, 0].min() + x0 - 25), int(tp[:, 0].max() + x1 + 60)
    vy0, vy1 = int(tp[:, 1].min() - 30), int(tp[:, 1].max() + 30)
    asp_a, asp_b = (cx1 - cx0) / (cy1 - cy0), (vx1 - vx0) / (vy1 - vy0)
    ratios = [asp_a, asp_b, 0.55]
    height = 3.4
    fig = plt.figure(figsize=(min(COL2, height * sum(ratios) * 1.12), height))
    gs = fig.add_gridspec(1, 3, width_ratios=ratios, wspace=0.06)
    # (a) the photo with the found pins
    ax = fig.add_subplot(gs[0])
    ax.imshow(rgb[cy0:cy1, cx0:cx1])
    ax.scatter(pts[:, 0] - cx0, pts[:, 1] - cy0, s=7, c=SHIPPED, edgecolors="white", linewidths=0.5)
    ax.set_title(f"(a) Photo: {res.inliers} matches"); ax.axis("off")
    # (b) warped into the template's view, with the pin windows
    ax = fig.add_subplot(gs[1])
    ax.imshow(view[vy0:vy1, vx0:vx1])
    for x, y in tp:
        ax.add_patch(plt.Rectangle((x + x0 - vx0, y + y0 - vy0), x1 - x0, y1 - y0, fill=False, ec=SHIPPED, lw=0.6))
    ax.set_title("(b) Template view"); ax.axis("off")
    # (c) the patches of one module
    module = "A1610.READER1" if "A1610.READER1" in tpl.modules else next(iter(tpl.modules))
    ps = pin_patches(tpl, res, rgb, view)[module]
    order = np.argsort([p["y"] for p in tpl.modules[module]])          # top to bottom, as in the photo
    sub = gs[2].subgridspec(len(ps), 1, hspace=0.3)
    for row, i in enumerate(order):
        p = ps[i]
        ax = fig.add_subplot(sub[row]); ax.imshow(p); ax.set_xticks([]); ax.set_yticks([])
        for sp in ax.spines.values():
            sp.set_visible(True); sp.set_color(GRID)
        ax.set_ylabel(tpl.modules[module][i].get("name", str(i + 1)), rotation=0, ha="right", va="center", fontsize=6)
        if row == 0:
            ax.set_title("(c) " + module.split(".")[1].title().replace("Reader", "Reader ") + " patches")
    save(fig, "pipeline")
    numbers["pipeline"] = {"photo": a.photo, "inliers": int(res.inliers), "pins": int(len(pts))}


def fig_locate_cdf(a, numbers):
    board = locate_errors(a.data, a.session, "controller", a.fresh)
    reader = locate_errors(a.data, a.session, "device", a.fresh)
    fig, ax = plt.subplots(figsize=(COL1, 2.2))
    xs = np.linspace(0, 1.5, 301)
    series = [("Board, stage 1", board["stage1"], BASE, "--", board),
              ("Board, final", board["final"], SHIPPED, "-", board),
              ("Reader", reader["final"], ACCENT2, "-", reader)]
    missing = reader["photos"] - reader["found"]
    if missing:
        series[2] = (f"Reader ({missing}/{reader['photos']} photos not found)", *series[2][1:])
    for label, err, col, ls, src in series:
        err = np.asarray(err)
        total = sum(1 for _ in err) + (src["photos"] - src["found"]) * (len(err) // max(src["found"], 1))
        ax.plot(xs, [np.sum(err <= x) / total for x in xs], color=col, ls=ls, lw=1.5, label=label)
    ax.axvline(0.5, color=MUTED, lw=0.6, ls=":")
    ax.text(0.5, 1.02, "half a spacing", fontsize=6, color=MUTED, ha="center", va="bottom")
    ax.set(xlabel="pin error (pin spacings)", ylabel="share of pins within that error", xlim=(0, 1.5), ylim=(0, 1.0))
    ax.grid(True, color=GRID, lw=0.5); ax.set_axisbelow(True)
    ax.legend(loc="lower right", bbox_to_anchor=(1.0, 0.02), handlelength=2.2)
    save(fig, "locate_cdf")
    numbers["locate"] = {k: {"photos": v["photos"], "found": v["found"],
                             "within_half": float(np.mean(np.asarray(v["final"]) < 0.5)) if v["final"] else 0,
                             "stage1_within_half": float(np.mean(np.asarray(v["stage1"]) < 0.5)) if v["stage1"] else 0,
                             "median_error": float(np.median(v["final"])) if v["final"] else None,
                             "median_ms": float(np.median(v["ms"]))}
                         for k, v in (("board", board), ("reader", reader))}
    numbers["locate"]["note"] = "within_half counts found photos only; the photo-level score is in eval_locate"


def fig_ablation(a, numbers, results):
    rows = [(CONFIGS[k]["label"], summary(results[k])[0], k) for k in CONFIGS]
    panels = [("Pins read right (%)", lambda s: 100 * s["pins_right"], "{:.1f}"),
              (f"Missed wires (of {rows[0][1]['wires']})", lambda s: s["missed_wires"], "{:.0f}"),
              (f"Wrong but sure (conf. $\\geq${THRESHOLD})", lambda s: s["errors_at_threshold"], "{:.0f}")]
    fig, axes = plt.subplots(1, 3, figsize=(COL2, 1.55), sharey=True, gridspec_kw={"wspace": 0.12})
    ypos = np.arange(len(rows))[::-1]
    for ax, (title, get, fmt) in zip(axes, panels):
        vals = np.array([get(s) for _, s, _ in rows], float)
        cols = [SHIPPED if k == "both" else BASE if k == "base" else OTHER for _, _, k in rows]
        lo, hi = vals.min(), vals.max()
        span = (hi - lo) or 1
        ax.hlines(ypos, lo - 0.25 * span, vals, color=GRID, lw=1)
        ax.scatter(vals, ypos, s=34, c=cols, zorder=3, edgecolors="white", linewidths=1)
        for v, yp, (_, _, k) in zip(vals, ypos, rows):
            ax.text(v + 0.06 * span, yp, fmt.format(v), va="center", fontsize=7,
                    color=INK if k == "both" else MUTED, fontweight="bold" if k == "both" else "normal")
        ax.set_xlim(lo - 0.25 * span, hi + 0.3 * span)
        ax.set_title(title, fontweight="normal")
        ax.spines["left"].set_visible(False); ax.tick_params(axis="y", length=0)
        ax.grid(True, axis="x", color=GRID, lw=0.5); ax.set_axisbelow(True)
    axes[0].set_yticks(ypos, [r[0] for r in rows])
    for lbl, (_, _, k) in zip(axes[0].get_yticklabels(), rows):
        if k == "both":
            lbl.set_fontweight("bold"); lbl.set_color(INK)
    save(fig, "ablation")
    numbers["ablation_board"] = {k: s for _, s, k in rows}
    numbers["ablation_board"]["note"] = ("session 1, cross-validated with whole wiring rows held out (7 folds); "
                                         "augmentation on the training side only")


def fig_confusion(a, numbers, results):
    fig, axes = plt.subplots(1, 2, figsize=(COL2 * 0.72, 2.3), gridspec_kw={"wspace": 0.35})
    out = {}
    for ax, key, title in zip(axes, ["base", "both"], ["(a) Base", "(b) Shipped"]):
        r = results[key]
        _, pred, _ = summary(r)
        classes = list(r["classes"])
        cm = confusion_matrix(r["y"], pred, labels=classes)
        share = cm / np.maximum(cm.sum(1, keepdims=True), 1)
        ax.imshow(share, cmap=BLUES, vmin=0, vmax=1)
        for i in range(len(classes)):
            for j in range(len(classes)):
                if cm[i, j]:
                    ax.text(j, i, cm[i, j], ha="center", va="center", fontsize=7,
                            color="white" if share[i, j] > 0.55 else INK)
        names = ["no wire" if c == NO_WIRE else NAMES[c] for c in classes]
        ax.set_xticks(range(len(classes)), names, rotation=35, ha="right")
        ax.set_yticks(range(len(classes)), names)
        ax.set(xlabel="read as", ylabel="true" if key == "base" else "")
        ax.set_title(title)
        for sp in ax.spines.values():
            sp.set_visible(False)
        ax.tick_params(length=0)
        out[key] = {"labels": names, "matrix": cm.tolist()}
    save(fig, "confusion")
    numbers["confusion_board"] = out


def risk_coverage(conf, ok):
    order = np.argsort(-conf)
    errs = np.cumsum(~ok[order])
    n = np.arange(1, len(ok) + 1)
    return n / len(ok), errs / n


def fig_confidence(a, numbers, results):
    fig, axes = plt.subplots(1, 2, figsize=(COL2, 2.3), gridspec_kw={"wspace": 0.3})
    r = results["both"]
    classes = list(r["classes"])
    idx = np.array([classes.index(c) for c in r["y"]])
    cal = calibrated(r["proba"], float(r["temperature"]))
    s, _, _ = summary(r)
    ax = axes[0]
    ax.plot([0, 1], [0, 1], color=MUTED, lw=0.6, ls=":")
    for conf, ok, col, label in [(r["proba"].max(1), r["proba"].argmax(1) == idx, BASE,
                                  f"raw votes (ECE {s['ece_before']:.3f})"),
                                 (cal.max(1), cal.argmax(1) == idx, SHIPPED,
                                  f"T = {s['temperature']:.2f} (ECE {s['ece_after']:.3f})")]:
        edges = np.linspace(0, 1, 11)
        mx, my, mn = [], [], []
        for lo, hi in zip(edges[:-1], edges[1:]):
            sel = (conf > lo) & (conf <= hi)
            if sel.sum():
                mx.append(conf[sel].mean()); my.append(ok[sel].mean()); mn.append(sel.sum())
        ax.plot(mx, my, color=col, lw=1.5)
        ax.scatter(mx, my, s=[8 + 40 * np.sqrt(k / max(mn)) for k in mn], color=col, edgecolors="white",
                   linewidths=0.6, zorder=3, label=label)
    ax.set(xlabel="confidence", ylabel="share read right", xlim=(0, 1.02), ylim=(0, 1.02))
    ax.set_title("(a) Calibration, shipped (dot size = pins)")
    ax.legend(loc="upper left"); ax.grid(True, color=GRID, lw=0.5); ax.set_axisbelow(True)

    ax = axes[1]
    out = {}
    for key, col, label in [("base", BASE, "Base"), ("both", SHIPPED, "Shipped")]:
        s, pred, conf = summary(results[key])
        ok = pred == results[key]["y"]
        cov, risk = risk_coverage(conf, ok)
        ax.plot(100 * cov, 100 * risk, color=col, lw=1.5, label=label)
        k = np.searchsorted(-np.sort(-conf), THRESHOLD, side="left")
        cut = int(np.sum(conf >= THRESHOLD))
        ax.scatter([100 * cov[cut - 1]], [100 * risk[cut - 1]], color=col, s=22, zorder=3, edgecolors="white")
        out[key] = {"answered": float(cov[cut - 1]), "error_rate_answered": float(risk[cut - 1])}
    ax.annotate(f"conf. $\\geq$ {THRESHOLD}", (100 * out["both"]["answered"], 100 * out["both"]["error_rate_answered"]),
                xytext=(-48, 14), textcoords="offset points", fontsize=6.5, color=MUTED,
                arrowprops={"arrowstyle": "-", "color": MUTED, "lw": 0.5})
    ax.set(xlabel="pins answered without asking (%)", ylabel="wrong among answered (%)", xlim=(75, 100.5))
    ax.set_ylim(0, None)
    ax.set_title("(b) Error vs. share answered")
    ax.legend(loc="upper left"); ax.grid(True, color=GRID, lw=0.5); ax.set_axisbelow(True)
    save(fig, "confidence")
    numbers["confidence_board"] = out


def _tile(ax, r, i, text):
    p = cv2.cvtColor(cv2.imread(str(r["files"][i])), cv2.COLOR_BGR2RGB)
    ax.imshow(window(p, int(r["margins"][i])), interpolation="nearest")
    ax.set_xticks([]); ax.set_yticks([])
    for sp in ax.spines.values():
        sp.set_visible(True); sp.set_color(GRID)
    ax.set_title(text, fontsize=6, fontweight="normal", loc="center", pad=2)


def _name(c):
    return "none" if c == NO_WIRE else NAMES[int(c)]


def fig_examples(a, numbers, results):
    base, both = results["base"], results["both"]
    _, pb, _ = summary(base)
    _, ps, cs = summary(both)
    y = both["y"]
    fixed = np.where((pb != y) & (ps == y))[0]
    still = np.where(ps != y)[0]
    still = still[np.argsort(-cs[still])]
    cols = 6
    rng = np.random.default_rng(0)
    pick = np.sort(rng.choice(fixed, size=min(2 * cols, len(fixed)), replace=False)) if len(fixed) else []
    fig = plt.figure(figsize=(COL2, 3.0))
    top, bottom = fig.subfigures(2, 1, hspace=0.04)
    for sf, idx, title, text in [
            (top, pick, f"(a) Fixed by the refinement ({len(fixed)} pins; {len(pick)} shown at random)",
             lambda i: f"{_name(y[i])}; base read {_name(pb[i])}"),
            (bottom, still[:2 * cols], f"(b) Still wrong ({len(still)} pins; most confident first)",
             lambda i: f"{_name(y[i])} read {_name(ps[i])} ({cs[i]:.2f})")]:
        axes = sf.subplots(2, cols, gridspec_kw={"hspace": 0.55, "wspace": 0.08})
        for ax in axes.ravel():
            ax.axis("off")
        for ax, i in zip(axes.ravel(), idx):
            ax.axis("on"); _tile(ax, both, i, text(i))
        sf.suptitle(title, x=0.0, ha="left", fontsize=8, fontweight="bold")
    save(fig, "examples")
    numbers["examples_board"] = {"fixed": int(len(fixed)), "broken": int(np.sum((pb == y) & (ps != y))),
                                 "still_wrong": int(len(still)),
                                 "still_wrong_shown": [os.path.basename(str(both["files"][i])) for i in still[:2 * cols]]}


def fig_augmentation(a, numbers):
    patches, y, _, files, margins = load_patches(a.data, a.sessions, "A1610")
    pick = [k for k in range(len(y)) if y[k] == 3 and "room_front" in files[k]] or list(np.where(y != NO_WIRE)[0])
    i = pick[len(pick) // 2]                                   # a yellow wire in room light: easy to see
    big, m = patches[i], margins[i]
    x0, y0, x1, y1 = patch_box("A1610")
    copies = augmented(big, m, (m - x0, m - y0))
    names = ["2 px right", "2 px left", "2 px down", "2 px up", "turned +6°", "turned −6°"]
    fig, axes = plt.subplots(4, 2, figsize=(COL1, 1.9), gridspec_kw={"hspace": 0.6, "wspace": 0.08})
    tiles = [(big, f"cut with {m} px margin; window and pin"), (window(big, m), "original window")] + list(zip(copies, names))
    for ax, (img, title) in zip(axes.ravel(), tiles):
        ax.imshow(img, interpolation="nearest"); ax.set_xticks([]); ax.set_yticks([])
        for sp in ax.spines.values():
            sp.set_visible(True); sp.set_color(GRID)
        ax.set_title(title, fontsize=6, fontweight="normal", loc="center", pad=2)
    axes[0, 0].add_patch(plt.Rectangle((m - 0.5, m - 0.5), x1 - x0, y1 - y0, fill=False, ec=SHIPPED, lw=0.8))
    axes[0, 0].scatter([m - x0], [m - y0], s=6, c=SHIPPED)
    save(fig, "augmentation")
    numbers["augmentation"] = {"example": os.path.basename(files[i]), "copies": names, "margin_px": m}


def fig_importance(a, numbers, results):
    imp = importance(a.data, a.sessions, a.fresh)
    w, h = FEATURE_SIZE["A1610"]
    n = w * h * 3
    maps = [("L* (brightness)", imp[:n].reshape(h, w, 3)[..., 0]), ("a* (green–red)", imp[:n].reshape(h, w, 3)[..., 1]),
            ("b* (blue–yellow)", imp[:n].reshape(h, w, 3)[..., 2]),
            ("L* relative to patch", imp[n + 9:n + 9 + w * h].reshape(h, w)),
            ("horizontal edges", imp[n + 9 + w * h:n + 9 + w * h + (w // 2) * h].reshape(h, w // 2))]
    groups = {"L*a*b* grid": imp[:n].sum(), "percentiles": imp[n:n + 9].sum(),
              "relative L*": imp[n + 9:n + 9 + w * h].sum(), "edges": imp[n + 9 + w * h:-3].sum(),
              "dark share per third": imp[-3:].sum()}
    fig = plt.figure(figsize=(COL2, 2.1))
    gs = fig.add_gridspec(3, 4, width_ratios=[1, 1, 0.42, 0.95], hspace=0.75, wspace=0.12)
    vmax = max(mp.max() for _, mp in maps)
    for k, (title, mp) in enumerate(maps):
        ax = fig.add_subplot(gs[k % 3, k // 3])
        im = ax.imshow(mp, cmap=BLUES, vmin=0, vmax=vmax, aspect="auto", interpolation="nearest")
        ax.set_xticks([]); ax.set_yticks([])
        for sp in ax.spines.values():
            sp.set_visible(True); sp.set_color(GRID)
        ax.set_title(title, fontsize=7, fontweight="normal", pad=2)
    holder = fig.add_subplot(gs[2, 1]); holder.axis("off")
    cax = holder.inset_axes([0.0, 0.45, 1.0, 0.22])
    cb = fig.colorbar(im, cax=cax, orientation="horizontal")
    cb.outline.set_visible(False); cb.ax.tick_params(labelsize=6, length=2)
    cb.set_label("importance of one number", fontsize=7)
    ax = fig.add_subplot(gs[:, 3])
    names, vals = list(groups), 100 * np.array(list(groups.values()))
    ax.barh(names, vals, color=SHIPPED, height=0.6)
    for yv, v in enumerate(vals):
        ax.text(v + 1.5, yv, f"{v:.0f} %" if v >= 0.5 else "< 1 %", va="center", fontsize=7, color=MUTED)
    ax.invert_yaxis(); ax.set_xlim(0, vals.max() * 1.3)
    ax.set_xlabel("share of importance (%)")
    ax.spines["left"].set_visible(False); ax.tick_params(axis="y", length=0)
    ax.set_title("(b) Feature groups")
    fig.text(0.0, 1.0, "(a) Where in the patch (opening at the left; the wire leaves to the right)", fontsize=8,
             fontweight="bold")
    save(fig, "importance")
    numbers["importance_board"] = {k: float(v) for k, v in groups.items()}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="data")
    ap.add_argument("--session", default="session1")
    ap.add_argument("--photo", default="s1_ctrl_c05_room_front", help="board photo for the pipeline figure")
    ap.add_argument("--only", nargs="+", default=[], help="figure names (see the top of this file)")
    ap.add_argument("--fresh", action="store_true", help="recompute the cached cross-validation and locate runs")
    a = ap.parse_args()
    a.sessions = [a.session]
    style()
    want = lambda name: not a.only or name in a.only                       # noqa: E731
    path = os.path.join(OUT, "numbers.json")
    numbers = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}

    needs_cv = [f for f in ("ablation", "confusion", "confidence", "examples", "importance") if want(f)]
    results = {}
    if needs_cv:
        configs = list(CONFIGS) if want("ablation") else ["base", "both"]
        for k in configs:
            results[k] = cross_validate(a.data, a.sessions, "A1610", k, a.fresh)
        reader = {k: summary(cross_validate(a.data, a.sessions, "A4020E", k, a.fresh))[0] for k in READER_CONFIGS}
        numbers["ablation_reader"] = reader
    for name, fn in [("pipeline", fig_pipeline), ("locate_cdf", fig_locate_cdf), ("augmentation", fig_augmentation)]:
        if want(name):
            fn(a, numbers)
    for name, fn in [("ablation", fig_ablation), ("confusion", fig_confusion), ("confidence", fig_confidence),
                     ("examples", fig_examples), ("importance", fig_importance)]:
        if want(name):
            fn(a, numbers, results)
    import sklearn
    numbers["made_with"] = {"os": sys.platform, "opencv": cv2.__version__, "scikit-learn": sklearn.__version__,
                            "session": a.session}
    os.makedirs(OUT, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(numbers, f, indent=1, ensure_ascii=False)
    print("numbers:", path)


if __name__ == "__main__":
    main()
