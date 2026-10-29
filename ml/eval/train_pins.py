"""Train the pin reader (no wire / red / black / green / yellow) on the labelled pin patches, measure it honestly,
and save it for the service (M3).

    python -m ml.eval.build_patches --session session1        (once: cuts the patches)
    python -m ml.eval.train_pins --sessions session1

Measured by cross-validation with whole wiring rows held out (a row's 4 photos never sit on both sides), so the
numbers say how it does on wirings it has not seen. The temperature for calibrated confidences is fitted on those
held-out predictions (Guo et al., 2017). Then one model per template is trained on all patches and saved to
ml/models/pins_<template>.joblib. Session 2 is never used here: it is the final test.
"""
import argparse
import csv
import json
import os
from datetime import date

import cv2
import joblib
import numpy as np
import sklearn
from scipy.optimize import minimize_scalar
from sklearn.ensemble import ExtraTreesClassifier
from sklearn.metrics import confusion_matrix
from sklearn.model_selection import GroupKFold

from ml.eval import metrics
from ml.perception_service.pin_reader import FEATURE_SIZE, MODELS, NO_WIRE, calibrated, features

NAMES = {NO_WIRE: "none", 0: "red", 1: "black", 2: "green", 3: "yellow", 4: "white", 5: "brown", 6: "blue", 7: "orange"}


def load(data, sessions, template):
    X, y, groups = [], [], []
    for session in sessions:
        folder = os.path.join(data, "patches", session)
        for r in csv.DictReader(open(os.path.join(folder, "labels.csv"), encoding="utf-8")):
            if r["located"] != "True" or (r["module"] == "A4020E") != (template == "A4020E"):
                continue
            patch = cv2.cvtColor(cv2.imread(os.path.join(folder, r["file"])), cv2.COLOR_BGR2RGB)
            X.append(features(patch, FEATURE_SIZE[template]))
            y.append(int(r["colour"]))
            groups.append(session + "/" + r["row"])
    return np.array(X), np.array(y), np.array(groups)


def new_model():
    return ExtraTreesClassifier(n_estimators=400, class_weight="balanced", random_state=0, n_jobs=-1)


def fit_temperature(proba, y, classes):
    idx = np.array([classes.index(c) for c in y])
    def nll(t):
        p = calibrated(proba, t)
        return -np.mean(np.log(np.clip(p[np.arange(len(y)), idx], 1e-9, 1)))
    return float(minimize_scalar(nll, bounds=(0.05, 10.0), method="bounded").x)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="data")
    ap.add_argument("--sessions", nargs="+", default=["session1"])
    ap.add_argument("--templates", nargs="+", default=["A1610", "A4020E"])
    ap.add_argument("--threshold", type=float, default=0.8, help="confidence below this: ask the installer")
    a = ap.parse_args()
    os.makedirs(MODELS, exist_ok=True)
    for template in a.templates:
        X, y, groups = load(a.data, a.sessions, template)
        rows = sorted(set(groups))
        folds = min(7, len(rows))
        classes = sorted(set(y.tolist()))
        proba = np.zeros((len(y), len(classes)))
        for train, test in GroupKFold(n_splits=folds).split(X, y, groups):
            m = new_model().fit(X[train], y[train])
            p = np.zeros((len(test), len(classes)))
            p[:, [classes.index(c) for c in m.classes_]] = m.predict_proba(X[test])
            proba[test] = p
        temperature = fit_temperature(proba, y, classes)
        cal = calibrated(proba, temperature)
        pred = np.array(classes)[cal.argmax(axis=1)]
        conf = cal.max(axis=1)
        ok = pred == y
        wired = y != NO_WIRE
        report = {
            "template": template, "pins": int(len(y)), "rows": len(rows), "folds": folds,
            "pins_right": round(float(ok.mean()), 3),
            "wire_or_not_right": round(float(np.mean((pred != NO_WIRE) == wired)), 3),
            "missed_wires": int(np.sum(wired & (pred == NO_WIRE))), "wires": int(wired.sum()),
            "false_wires": int(np.sum(~wired & (pred != NO_WIRE))),
            "colour_right_on_wired_pins": round(float(ok[wired].mean()), 3) if wired.any() else None,
            "temperature": round(temperature, 3),
            "ece_before": round(metrics.ece(proba.max(axis=1), proba.argmax(axis=1) == np.array([classes.index(c) for c in y])), 3),
            "ece_after": round(metrics.ece(conf, ok), 3),
            "selective": metrics.selective(conf, ok, a.threshold),
            "confusion_rows_true_cols_pred": {"labels": [NAMES[c] for c in classes],
                                              "matrix": confusion_matrix(y, pred, labels=classes).tolist()},
        }
        print(json.dumps(report, indent=1))
        model = new_model().fit(X, y)
        info = {"trained_on": a.sessions, "date": date.today().isoformat(), "pins": int(len(y)),
                "sklearn": sklearn.__version__, "cross_validated": {k: report[k] for k in
                ("pins_right", "wire_or_not_right", "colour_right_on_wired_pins", "ece_after")}}
        path = MODELS / f"pins_{template}.joblib"
        joblib.dump({"model": model, "temperature": temperature, "size": FEATURE_SIZE[template], "info": info},
                    path, compress=3)
        os.makedirs("ml/runs", exist_ok=True)
        with open(f"ml/runs/train_pins_{template}.json", "w", encoding="utf-8") as f:
            json.dump(report, f, indent=1)
        print("saved", path, f"({os.path.getsize(path) / 1e6:.1f} MB)\n")


if __name__ == "__main__":
    main()
