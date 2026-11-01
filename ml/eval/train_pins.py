"""Train the general wire reader (no wire / red / black / green / yellow) on the labelled pin windows of every
device, measure it honestly, and save it for the service (M3).

    python -m ml.eval.build_patches --session session1        (once: cuts the windows)
    python -m ml.eval.train_pins --sessions session1

One reader for every device (pin_reader.py): wire or not from how each window differs from the device's empty
reference photo, the colour from the wire's own pixels. Measured by cross-validation with whole wiring rows held
out (a row's photos never sit on both sides), so the numbers say how it does on wirings it has not seen; the
temperature for calibrated confidences is fitted on those held-out predictions (Guo et al., 2017). Then two checks
of how well it carries over (leave-one-domain-out, Gulrajani & Lopez-Paz, 2021):
  unseen background   board: trained on the blue blocks (Reader 1, Door 1), tested on the green ones (Reader 2, Door 2)
  unseen device       trained on the board only, tested on the A4020-E reader
Augmentation (shifted and turned copies, pin_reader.augmented) is added to the training side only, never to the
held-out side. Then the model is trained on everything and saved to ml/models/pins.joblib. Session 2 is never used
here: it is the final test.
"""
import argparse
import csv
import json
import os
from concurrent.futures import ProcessPoolExecutor
from datetime import date
from functools import lru_cache

import cv2
import joblib
import numpy as np
import sklearn
from scipy.optimize import minimize_scalar
from sklearn.ensemble import ExtraTreesClassifier
from sklearn.metrics import confusion_matrix
from sklearn.model_selection import GroupKFold

from ml.eval import metrics
from ml.perception_service.locate import Template
from ml.perception_service.patches import patch_box, reference_patches, window
from ml.perception_service.pin_reader import (MODEL, NO_WIRE, SLACK, augmented, calibrated, combine, compare,
                                              decide, presence_features, window_features)

NAMES = {NO_WIRE: "none", 0: "red", 1: "black", 2: "green", 3: "yellow", 4: "white", 5: "brown", 6: "blue", 7: "orange"}
BLUE = ("A1610.READER1", "A1610.DOOR1")           # the board's blue blocks (the green ones: Reader 2, Door 2)


def load_patches(data, sessions, template):
    """The located windows of one template as cut by build_patches (window + margin): windows, colours,
    wiring-row groups, files and margins."""
    patches, y, groups, files, margins = [], [], [], [], []
    for session in sessions:
        folder = os.path.join(data, "patches", session)
        for r in csv.DictReader(open(os.path.join(folder, "labels.csv"), encoding="utf-8")):
            if r["located"] != "True" or r["module"].split(".")[0] != template:
                continue
            files.append(os.path.join(folder, r["file"]))
            patches.append(cv2.cvtColor(cv2.imread(files[-1]), cv2.COLOR_BGR2RGB))
            y.append(int(r["colour"]))
            groups.append(session + "/" + r["row"])
            margins.append(int(r.get("margin") or 0))
    return patches, np.array(y), np.array(groups), files, margins


def load(data, sessions, template, brightness=True):
    """Colour-grid features of every located window of one template (the first pin reader's features; used by
    report_figures.py and inspect_pins.py for the session-1 history), colours and wiring-row groups."""
    from ml.perception_service.pin_reader import features
    patches, y, groups, _, margins = load_patches(data, sessions, template)
    X = np.array([features(window(p, m), brightness=brightness) for p, m in zip(patches, margins)])
    return X, y, groups


def with_copies(X, y, Xa, idx):
    """Training set of the windows 'idx' plus, if given, their augmented copies (report_figures.py)."""
    if Xa is None:
        return X[idx], y[idx]
    return (np.concatenate([X[idx], Xa[idx].reshape(-1, X.shape[1])]),
            np.concatenate([y[idx], np.repeat(y[idx], Xa.shape[1])]))


FEATURE_VERSION = 1      # bump when pin_reader's features change, so the cache below is rebuilt


def load_all(data, sessions, templates, augment=True):
    """Features of every located window of every template, and its augmented copies (presence only). Cached in
    ml/runs/ (a few minutes to compute); the cache is rebuilt when a labels.csv or FEATURE_VERSION changes."""
    stamp = [FEATURE_VERSION, augment, list(templates)] + [
        os.path.getmtime(os.path.join(data, "patches", s, "labels.csv")) for s in sessions]
    cache = os.path.join("ml", "runs", "pin_features_%s.npz" % "_".join(sessions))
    if os.path.exists(cache):
        z = np.load(cache, allow_pickle=False)
        if json.loads(str(z["stamp"])) == stamp:
            return (z["P"], z["C"], z["PA"] if augment else None, z["y"], z["groups"], z["modules"], list(z["files"]))
    out = _compute_all(data, sessions, templates, augment)
    os.makedirs(os.path.dirname(cache), exist_ok=True)
    np.savez_compressed(cache, stamp=json.dumps(stamp), P=out[0], C=out[1],
                        PA=out[2] if augment else np.zeros(0), y=out[3], groups=out[4], modules=out[5],
                        files=np.array(out[6]))
    return out


def _one_window(job):
    """Features of one saved window (runs in a worker process)."""
    path, name, module, pin, m, augment = job
    tpl = _template(name)
    ref = reference_patches(tpl, SLACK)[module][pin - 1]
    big = cv2.cvtColor(cv2.imread(path), cv2.COLOR_BGR2RGB)
    p, c = window_features(window(big, m), ref)
    pa = None
    if augment:
        x0, y0, _, _ = patch_box()
        pa = [presence_features(*compare(cp, ref)[:2]) for cp in augmented(big, m, (m - x0, m - y0))]
    return p, c, pa


@lru_cache(maxsize=None)
def _template(name):
    return Template.load(name)


def _compute_all(data, sessions, templates, augment):
    jobs, y, groups, modules, files = [], [], [], [], []
    for name in templates:
        for session in sessions:
            folder = os.path.join(data, "patches", session)
            for r in csv.DictReader(open(os.path.join(folder, "labels.csv"), encoding="utf-8")):
                if r["located"] != "True" or r["module"].split(".")[0] != name:
                    continue
                m = int(r.get("margin") or 0)
                if augment and m < 3:
                    raise SystemExit("augmentation needs windows cut with a margin: run  python -m ml.eval.build_patches")
                path = os.path.join(folder, r["file"])
                jobs.append((path, name, r["module"], int(r["pin"]), m, augment))
                y.append(int(r["colour"])); groups.append(session + "/" + r["row"])
                modules.append(r["module"]); files.append(path)
    print(f"computing the features of {len(jobs)} windows ...", flush=True)
    with ProcessPoolExecutor() as pool:                     # all CPU cores
        out = list(pool.map(_one_window, jobs, chunksize=32))
    P = np.array([o[0] for o in out]); C = np.array([o[1] for o in out])
    PA = np.array([o[2] for o in out]) if augment else None
    return P, C, PA, np.array(y), np.array(groups), np.array(modules), files


def new_model():
    return ExtraTreesClassifier(n_estimators=400, class_weight="balanced", random_state=0, n_jobs=-1)


def fit(P, C, PA, y, idx):
    """The two models on the windows 'idx': wire or not (with the augmented copies), colour (wired windows only)."""
    wire = (y[idx] != NO_WIRE).astype(int)
    Xp, yp = P[idx], wire
    if PA is not None:
        Xp = np.concatenate([Xp, PA[idx].reshape(-1, P.shape[1])]); yp = np.concatenate([wire, np.repeat(wire, PA.shape[1])])
    wired = idx[y[idx] != NO_WIRE]
    return new_model().fit(Xp, yp), new_model().fit(C[wired], y[wired])


def predict(models, P, C, answers, idx):
    """[no wire, colour...] probabilities for the windows 'idx', in the order of 'answers'."""
    presence, colour = models
    p_wire = presence.predict_proba(P[idx])[:, list(presence.classes_).index(1)]
    pc = np.zeros((len(idx), len(answers) - 1))
    pc[:, [answers.index(int(k)) - 1 for k in colour.classes_]] = colour.predict_proba(C[idx])
    return combine(p_wire, pc)


def fit_temperature(proba, y, answers):
    idx = np.array([answers.index(c) for c in y])
    def nll(t):
        p = calibrated(proba, t)
        return -np.mean(np.log(np.clip(p[np.arange(len(y)), idx], 1e-9, 1)))
    return float(minimize_scalar(nll, bounds=(0.05, 10.0), method="bounded").x)


def scores(proba, y, answers, threshold, temperature=None):
    """The report's numbers for a set of held-out predictions."""
    cal = calibrated(proba, temperature) if temperature else proba
    best = decide(proba)
    pred = np.array(answers)[best]
    conf = cal[np.arange(len(y)), best]
    ok, wired = pred == y, y != NO_WIRE
    out = {"pins": int(len(y)), "pins_right": round(float(ok.mean()), 3),
           "wire_or_not_right": round(float(np.mean((pred != NO_WIRE) == wired)), 3),
           "missed_wires": int(np.sum(wired & (pred == NO_WIRE))), "wires": int(wired.sum()),
           "false_wires": int(np.sum(~wired & (pred != NO_WIRE))), "empty_pins": int((~wired).sum()),
           "colour_right_on_wired_pins": round(float(ok[wired].mean()), 3) if wired.any() else None,
           "ece": round(metrics.ece(conf, ok), 3), "selective": metrics.selective(conf, ok, threshold)}
    labels = [a for a in answers if a in set(y.tolist()) | set(pred.tolist())]
    out["confusion_rows_true_cols_pred"] = {"labels": [NAMES[c] for c in labels],
                                            "matrix": confusion_matrix(y, pred, labels=labels).tolist()}
    return out


def held_out(P, C, PA, y, train_mask, test_mask, answers):
    """Train on the windows in train_mask, predict those in test_mask (both split by wiring row as well)."""
    proba, done = np.zeros((len(y), len(answers))), np.zeros(len(y), bool)
    groups = HELD_GROUPS
    for tr, te in GroupKFold(n_splits=min(7, len(set(groups)))).split(P, y, groups):
        tr, te = tr[train_mask[tr]], te[test_mask[te]]
        if len(te) == 0 or len(set(y[tr])) < 2:
            continue
        proba[te] = predict(fit(P, C, PA, y, tr), P, C, answers, te); done[te] = True
    return proba[done], y[done]


HELD_GROUPS = None


def main():
    global HELD_GROUPS
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="data")
    ap.add_argument("--sessions", nargs="+", default=["session1"])
    ap.add_argument("--templates", nargs="+", default=["A1610", "A4020E"])
    ap.add_argument("--threshold", type=float, default=0.8, help="confidence below this: ask the installer")
    ap.add_argument("--no-augment", action="store_true", help="train without the shifted / turned copies")
    ap.add_argument("--no-checks", action="store_true", help="skip the unseen-background / unseen-device checks")
    ap.add_argument("--no-save", action="store_true", help="only measure; keep the saved model")
    a = ap.parse_args()
    P, C, PA, y, groups, modules, files = load_all(a.data, a.sessions, a.templates, not a.no_augment)
    HELD_GROUPS = groups
    answers = [NO_WIRE] + sorted(set(y.tolist()) - {NO_WIRE})
    everything = np.ones(len(y), bool)

    proba, yy = held_out(P, C, PA, y, everything, everything, answers)
    temperature = fit_temperature(proba, yy, answers)
    report = {"sessions": a.sessions, "templates": a.templates, "rows": len(set(groups)),
              "augmented_copies": 0 if PA is None else int(PA.shape[1]), "temperature": round(temperature, 3),
              "ece_before": scores(proba, yy, answers, a.threshold)["ece"],
              "all": scores(proba, yy, answers, a.threshold, temperature)}
    device = np.array([m.split(".")[0] for m in modules])
    for name in a.templates:                     # the same held-out predictions, per device
        sel = device == name
        report[name] = scores(proba[sel], yy[sel], answers, a.threshold, temperature)
    if not a.no_checks:
        board = device == "A1610"
        blue = np.isin(modules, BLUE)
        if board.any():
            pb, yb = held_out(P, C, PA, y, board & blue, board & ~blue, answers)
            report["unseen_background_blue_to_green"] = scores(pb, yb, answers, a.threshold, temperature)
        if board.any() and (~board).any():
            pr = predict(fit(P, C, PA, y, np.where(board)[0]), P, C, answers, np.where(~board)[0])
            report["unseen_device_board_to_" + "_".join(sorted(set(device[~board])))] = \
                scores(pr, y[~board], answers, a.threshold, temperature)
    print(json.dumps(report, indent=1))
    os.makedirs("ml/runs", exist_ok=True)
    with open("ml/runs/train_pins.json", "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)
    if a.no_save:
        return
    presence, colour = fit(P, C, PA, y, np.arange(len(y)))
    info = {"trained_on": a.sessions, "templates": a.templates, "date": date.today().isoformat(), "pins": int(len(y)),
            "sklearn": sklearn.__version__, "augmented_copies": report["augmented_copies"],
            "cross_validated": {k: report["all"][k] for k in ("pins_right", "wire_or_not_right",
                                                              "colour_right_on_wired_pins", "ece")}}
    joblib.dump({"presence": presence, "colour": colour, "temperature": temperature, "info": info}, MODEL, compress=3)
    print("saved", MODEL, f"({os.path.getsize(MODEL) / 1e6:.1f} MB)")


if __name__ == "__main__":
    main()
