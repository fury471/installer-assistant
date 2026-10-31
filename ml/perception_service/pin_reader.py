"""Reads one pin patch: no wire, or which colour, with a calibrated confidence (M3).

A small classifier trained on labelled pin patches (ml/eval/train_pins.py): extremely randomised trees
(Geurts et al., 2006) on the patch shrunk to a few pixels in CIELAB, plus the patch's darkest, middle and brightest
colours, plus brightness features (brightness relative to the patch, horizontal edges, dark share per third) so a
black wire is not mistaken for a dark, empty opening. Trained with shifted and slightly turned copies of every
patch (augmentation), so a wire that runs a little lower or at a slant still looks familiar. Its probabilities are then temperature-scaled (Guo et al., 2017), the temperature fitted on predictions for
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


def features(patch_rgb: np.ndarray, size: Tuple[int, int], brightness: bool = True) -> np.ndarray:
    """The patch shrunk to 'size' in CIELAB, plus the 5th, 50th and 95th percentile of L, a, b over the patch;
    with 'brightness' also: L relative to the patch's median L (shrunk to 'size'), the strength of horizontal edges
    (a wire's borders; shrunk to half the width) and the share of clearly darker pixels in each third."""
    small = cv2.resize(patch_rgb, size, interpolation=cv2.INTER_AREA).astype(np.float64)
    lab = srgb_to_lab(small)
    parts = [lab.ravel(), np.percentile(lab.reshape(-1, 3), [5, 50, 95], axis=0).ravel()]
    if brightness:
        L = srgb_to_lab(patch_rgb.astype(np.float64))[..., 0].astype(np.float32)
        rel = L - np.median(L)
        edges = np.abs(cv2.Sobel(L, cv2.CV_32F, 0, 1, ksize=3))
        w = L.shape[1]
        parts += [cv2.resize(rel, size, interpolation=cv2.INTER_AREA).ravel(),
                  cv2.resize(edges, (size[0] // 2, size[1]), interpolation=cv2.INTER_AREA).ravel(),
                  np.array([(rel[:, i * w // 3:(i + 1) * w // 3] < -20).mean() for i in range(3)])]
    return np.concatenate(parts)


def augmented(big: np.ndarray, margin: int, pin_xy: Tuple[float, float]) -> List[np.ndarray]:
    """Training copies of one patch cut with 'margin' extra pixels: shifted 2 px left/right/up/down and turned
    6 degrees either way about the pin, each cut back to the window size."""
    h, w = big.shape[:2]
    out = []
    for dx, dy, deg in [(2, 0, 0), (-2, 0, 0), (0, 2, 0), (0, -2, 0), (0, 0, 6), (0, 0, -6)]:
        b = big
        if deg:
            M = cv2.getRotationMatrix2D(pin_xy, deg, 1.0)
            b = cv2.warpAffine(big, M, (w, h), borderMode=cv2.BORDER_REFLECT)
        out.append(b[margin + dy:h - margin + dy, margin + dx:w - margin + dx])
    return out


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
        self.brightness = bool(bundle.get("brightness", False))
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
        X = np.array([features(p, self.size, self.brightness) for p in patches])
        p = calibrated(self.model.predict_proba(X), self.temperature)
        best = p.argmax(axis=1)
        return [(self.classes[k], float(p[i, k])) for i, k in enumerate(best)]
