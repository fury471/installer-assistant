"""Reads one pin patch: no wire, or which colour, with a calibrated confidence (M3).

A small classifier trained on labelled pin patches (ml/eval/train_pins.py): extremely randomised trees
(Geurts et al., 2006) on the patch shrunk to a few pixels in CIELAB, plus the patch's darkest, middle and brightest
colours. Its probabilities are then temperature-scaled (Guo et al., 2017), the temperature fitted on predictions for
rows it was not trained on, so that a confidence of 0.8 means about 80 % right.
One model per device (the board and the reader look different): ml/models/pins_<template>.joblib, not in Git.
"""
from pathlib import Path
from typing import List, Optional, Tuple

import cv2
import numpy as np

from .colour import srgb_to_lab

MODELS = Path(__file__).resolve().parents[1] / "models"
NO_WIRE = -1
FEATURE_SIZE = {"A1610": (20, 7), "A4020E": (8, 14)}   # (width, height) the patch is shrunk to


def features(patch_rgb: np.ndarray, size: Tuple[int, int]) -> np.ndarray:
    """The patch shrunk to 'size' in CIELAB, plus the 5th, 50th and 95th percentile of L, a, b over the patch."""
    small = cv2.resize(patch_rgb, size, interpolation=cv2.INTER_AREA).astype(np.float64)
    lab = srgb_to_lab(small)
    return np.concatenate([lab.ravel(), np.percentile(lab.reshape(-1, 3), [5, 50, 95], axis=0).ravel()])


def calibrated(proba: np.ndarray, temperature: float) -> np.ndarray:
    """Temperature scaling of class probabilities: softmax(log p / T)."""
    logp = np.log(np.clip(proba, 1e-6, 1.0)) / temperature
    logp -= logp.max(axis=-1, keepdims=True)
    p = np.exp(logp)
    return p / p.sum(axis=-1, keepdims=True)


class PinReader:
    """Loads the trained model of one template; read() gives (colour, confidence) per patch."""

    def __init__(self, template: str, path: Optional[Path] = None):
        import joblib                                   # comes with scikit-learn
        self.path = path or MODELS / f"pins_{template}.joblib"
        bundle = joblib.load(self.path)
        self.model = bundle["model"]
        self.temperature = float(bundle["temperature"])
        self.size = tuple(bundle["size"])
        self.classes = [int(c) for c in self.model.classes_]
        self.info = bundle.get("info", {})
        import sklearn
        trained = self.info.get("sklearn")
        if trained and trained.split(".")[:2] != sklearn.__version__.split(".")[:2]:
            print(f"Warning: {self.path.name} was trained with scikit-learn {trained}, this is {sklearn.__version__}. "
                  "Install the version in ml/requirements.txt, or retrain: python -m ml.eval.train_pins")

    @staticmethod
    def available(template: str) -> bool:
        return (MODELS / f"pins_{template}.joblib").exists()

    def read(self, patches: List[np.ndarray]) -> List[Tuple[int, float]]:
        if not patches:
            return []
        X = np.array([features(p, self.size) for p in patches])
        p = calibrated(self.model.predict_proba(X), self.temperature)
        best = p.argmax(axis=1)
        return [(self.classes[k], float(p[i, k])) for i, k in enumerate(best)]
