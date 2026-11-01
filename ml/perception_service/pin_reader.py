"""Reads one pin window: no wire, or which colour, with a calibrated confidence (M3). One reader for every device.

Every window is cut in the same canonical shape (patches.py: wire leaving to the right, one pin spacing = 24 px),
and next to it the same window from the device's reference photo, where every pin is empty. Two small models
(extremely randomised trees, Geurts et al., 2006; ml/eval/train_pins.py):

  1. Wire or not, from how the live window DIFFERS from the empty reference (change detection: Radke et al., 2005):
     the colour difference after matching the frame's brightness and colour cast to the reference, the difference
     in edges, both tolerant to a few pixels of misalignment, and the shape of what changed (a wire is long and
     leaves the window). It never looks at the device itself, so a new background (another housing colour, other
     printed text, another device) does not look like a wire. Trained with shifted and turned copies of each
     window (augmentation), so a wire that runs a little lower or at a slant still counts.
  2. Which colour, for windows with a wire: the window shrunk to 20 x 7 in CIELAB with brightness features, and the
     colour of the changed pixels along the wire's path after the light is matched to the reference (von Kries
     gains on the unchanged pixels).

The answer's confidence is temperature-scaled (Guo et al., 2017), fitted on predictions for wiring rows the model
was not trained on, so that 0.8 means about 80 % right. The model is ml/models/pins.joblib (shared through Git).
A new device needs data, not training: its template JSON with a reference photo of the empty device.
"""
from pathlib import Path
from typing import List, Optional, Tuple

import cv2
import numpy as np

from .colour import srgb_to_lab

MODELS = Path(__file__).resolve().parents[1] / "models"
MODEL = MODELS / "pins.joblib"
NO_WIRE = -1
GRID = (20, 7)                                         # (width, height) the canonical window is shrunk to
FEATURE_SIZE = {"A1610": GRID, "A4020E": GRID}         # same shape for every device (kept for older tools)
SLACK = 3          # canonical px of misalignment tolerated when comparing with the reference (reference margin)
CHANGED = 20.0     # colour difference (CIELAB units) above which a pixel counts as changed


def features(patch_rgb: np.ndarray, size: Tuple[int, int] = GRID, brightness: bool = True) -> np.ndarray:
    """The window shrunk to 'size' in CIELAB, plus the 5th, 50th and 95th percentile of L, a, b over the window;
    with 'brightness' also: L relative to the window's median L (shrunk to 'size'), the strength of horizontal edges
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
    """Training copies of one window cut with 'margin' extra pixels: shifted 2 px left/right/up/down and turned
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


def _linear(rgb):
    c = rgb.astype(np.float64) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _srgb(lin):
    lin = np.clip(lin, 0, 1)
    return np.where(lin <= 0.0031308, 12.92 * lin, 1.055 * lin ** (1 / 2.4) - 0.055) * 255.0


def _gradient(L):
    return np.hypot(cv2.Sobel(L, cv2.CV_32F, 1, 0, ksize=3), cv2.Sobel(L, cv2.CV_32F, 0, 1, ksize=3))


def compare(win_rgb: np.ndarray, ref_rgb: np.ndarray, slack: int = SLACK):
    """How the live window differs from the reference window (cut with 'slack' extra px on every side).
    Returns D (colour difference per pixel, CIELAB units, after removing the overall brightness and colour cast),
    E (edge difference) - each the smallest over shifts up to 'slack' px - and the window's CIELAB colours with the
    light matched to the reference (von Kries gains estimated on the pixels that did not change)."""
    h, w = win_rgb.shape[:2]
    W = srgb_to_lab(win_rgb.astype(np.float64)).astype(np.float32)
    Rf = srgb_to_lab(ref_rgb.astype(np.float64)).astype(np.float32)
    R0 = Rf[slack:slack + h, slack:slack + w]
    Wc = W - np.median((W - R0).reshape(-1, 3), axis=0)
    GW, GR = _gradient(Wc[..., 0]), _gradient(Rf[..., 0])
    # every shift of the reference at once: (shifts, h, w, ...) views, then the smallest difference per pixel
    shifted = np.lib.stride_tricks.sliding_window_view(Rf, (h, w), axis=(0, 1))     # (2s+1, 2s+1, 3, h, w)
    diff = shifted - Wc.transpose(2, 0, 1)[None, None]
    D = np.sqrt(np.einsum("abcij,abcij->abij", diff, diff).min(axis=(0, 1)))
    E = np.abs(np.lib.stride_tricks.sliding_window_view(GR, (h, w)) - GW).min(axis=(0, 1))
    same = D <= np.percentile(D, 40)            # never empty, even when nothing changed
    Wl, Rl = _linear(win_rgb), _linear(ref_rgb[slack:slack + h, slack:slack + w])
    gain = (np.median(Rl[same], 0) + 1e-3) / (np.median(Wl[same], 0) + 1e-3)
    matched = srgb_to_lab(_srgb(Wl * gain)).astype(np.float32)
    return D, E, matched


def presence_features(D: np.ndarray, E: np.ndarray) -> np.ndarray:
    """Wire or not, from the differences only: where and how much the window changed, and the shape of the change."""
    h, w = D.shape
    parts = [cv2.resize(D, GRID, interpolation=cv2.INTER_AREA).ravel(),
             cv2.resize(E, GRID, interpolation=cv2.INTER_AREA).ravel(),
             np.percentile(D, [50, 75, 90, 95, 99]), np.percentile(E, [50, 75, 90, 95, 99]),
             [(D > t).mean() for t in (10, 20, 30)], [(E > t).mean() for t in (20, 40, 80)]]
    M = (D > CHANGED).astype(np.uint8)
    parts.append([M[:, i * w // 6:(i + 1) * w // 6].mean() for i in range(6)])
    n, _, st, _ = cv2.connectedComponentsWithStats(M, 8)
    if n > 1:                                      # the biggest changed region: long, and does it leave the window?
        k = 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA]))
        x, y, ww, hh, a = st[k]
        parts.append([ww / w, hh / h, a / (w * h), float(x + ww >= w - 3), float(x <= 14)])
    else:
        parts.append([0, 0, 0, 0, 0])
    return np.concatenate([np.ravel(np.asarray(p, np.float32)) for p in parts])


def wire_colour_features(D: np.ndarray, matched: np.ndarray) -> np.ndarray:
    """The colour of the most changed pixels along the wire's path (three bands from the pin to the right)."""
    out = []
    for r0, r1, c0, c1 in [(7, 13, 12, 34), (6, 14, 12, 50), (4, 16, 8, 30)]:
        d, c = D[r0:r1, c0:c1].ravel(), matched[r0:r1, c0:c1].reshape(-1, 3)
        px = c[np.argsort(d)[-max(8, len(d) // 3):]]
        chroma = np.hypot(px[:, 1], px[:, 2])
        out.append(np.concatenate([np.median(px, 0), np.percentile(px, [20, 80], axis=0).ravel(),
                                   [np.median(chroma), np.median(np.arctan2(px[:, 2], px[:, 1])), np.mean(chroma < 12)]]))
    return np.concatenate(out).astype(np.float32)


def window_features(win_rgb: np.ndarray, ref_rgb: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    """(features for wire-or-not, features for the colour) of one canonical window and its reference."""
    D, E, matched = compare(win_rgb, ref_rgb)
    return presence_features(D, E), np.concatenate([features(win_rgb), wire_colour_features(D, matched)])


def calibrated(proba: np.ndarray, temperature: float) -> np.ndarray:
    """Temperature scaling of class probabilities: softmax(log p / T)."""
    logp = np.log(np.clip(proba, 1e-6, 1.0)) / temperature
    logp -= logp.max(axis=-1, keepdims=True)
    p = np.exp(logp)
    return p / p.sum(axis=-1, keepdims=True)


def combine(p_wire: np.ndarray, p_colour: np.ndarray) -> np.ndarray:
    """One probability per answer [no wire, colour 1, colour 2, ...] from the two models."""
    return np.hstack([(1 - p_wire)[:, None], p_wire[:, None] * p_colour])


def decide(proba: np.ndarray) -> np.ndarray:
    """Index of the answer from the models' raw probabilities (before temperature scaling, which would let a wire of
    uncertain colour lose to 'no wire'): no wire when that is more likely than a wire, else the most likely colour."""
    return np.where(proba[:, 0] >= proba[:, 1:].sum(axis=1), 0, 1 + proba[:, 1:].argmax(axis=1))


class PinReader:
    """The general wire reader; read() gives (colour, confidence) per window."""

    def __init__(self, path: Optional[Path] = None):
        import joblib                                   # comes with scikit-learn
        self.path = path or MODEL
        bundle = joblib.load(self.path)
        self.presence, self.colour = bundle["presence"], bundle["colour"]
        self.temperature = float(bundle["temperature"])
        self.answers = [NO_WIRE] + [int(c) for c in self.colour.classes_]
        self.info = bundle.get("info", {})
        import sklearn
        trained = self.info.get("sklearn")
        if trained and trained.split(".")[:2] != sklearn.__version__.split(".")[:2]:
            print(f"Warning: {self.path.name} was trained with scikit-learn {trained}, this is {sklearn.__version__}. "
                  "Install the version in ml/requirements.txt, or retrain: python -m ml.eval.train_pins")

    @staticmethod
    def available(path: Optional[Path] = None) -> bool:
        return (path or MODEL).exists()

    def read(self, windows: List[np.ndarray], references: List[np.ndarray]) -> List[Tuple[int, float]]:
        """windows: canonical pin windows; references: the same windows of the empty device, cut with SLACK px more."""
        if not windows:
            return []
        F = [window_features(w, r) for w, r in zip(windows, references)]
        p_wire = self.presence.predict_proba(np.array([f[0] for f in F]))[:, 1]
        raw = combine(p_wire, self.colour.predict_proba(np.array([f[1] for f in F])))
        best = decide(raw)
        p = calibrated(raw, self.temperature)
        return [(self.answers[k], float(p[i, k])) for i, k in enumerate(best)]
