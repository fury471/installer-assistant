"""Classical colour baseline: grey-world white balance, then the nearest reference colour in CIELAB.

This is the transparent baseline the learned/VLM readers are compared against (report: evaluation).
References: Buchsbaum (1980) grey-world assumption; CIE 1976 L*a*b*; Sharma et al. (2005) for CIEDE2000.
"""
from typing import Dict, Tuple

import numpy as np

# Typical insulation colours in sRGB. Re-fit these on the bench photos (eval/fit_references.py, TODO) before
# reporting numbers: lighting and cable brands shift them.
REFERENCE_RGB: Dict[int, Tuple[int, int, int]] = {
    0: (200, 30, 35),     # Red
    1: (25, 25, 28),      # Black
    2: (30, 140, 60),     # Green
    3: (235, 200, 30),    # Yellow
    4: (235, 235, 230),   # White
    5: (120, 70, 35),     # Brown
    6: (30, 80, 190),     # Blue
    7: (240, 120, 20),    # Orange
}


def grey_world(rgb: np.ndarray) -> np.ndarray:
    """Scales each channel so the image mean is grey. rgb: float or uint8 (..., 3) → float 0..255."""
    img = rgb.astype(np.float64)
    mean = img.reshape(-1, 3).mean(axis=0)
    gain = mean.mean() / np.maximum(mean, 1e-6)
    return np.clip(img * gain, 0, 255)


def srgb_to_lab(rgb: np.ndarray) -> np.ndarray:
    """sRGB 0..255 (..., 3) → CIELAB (D65), (..., 3)."""
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    xyz = c @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > (6 / 29) ** 3, np.cbrt(xyz), xyz / (3 * (6 / 29) ** 2) + 4 / 29)
    L = 116 * f[..., 1] - 16
    a = 500 * (f[..., 0] - f[..., 1])
    b = 200 * (f[..., 1] - f[..., 2])
    return np.stack([L, a, b], axis=-1)


_REF_CODES = np.array(sorted(REFERENCE_RGB))
_REF_LAB = srgb_to_lab(np.array([REFERENCE_RGB[k] for k in _REF_CODES]))


def classify_patch(patch_rgb: np.ndarray, temperature: float = 8.0) -> Tuple[int, float, np.ndarray]:
    """Colour of a crop of one wire (already white-balanced if you want that).

    Returns (colour code, confidence, probabilities over the 8 colours). The probabilities are a softmax over
    negative CIE76 distances; 'temperature' is fitted on held-out data (temperature scaling, Guo et al. 2017)
    so that the confidence is calibrated.
    """
    pixels = patch_rgb.reshape(-1, 3).astype(np.float64)
    lab = srgb_to_lab(np.median(pixels, axis=0))
    d = np.linalg.norm(_REF_LAB - lab, axis=1)
    logits = -d / max(temperature, 1e-6)
    p = np.exp(logits - logits.max())
    p /= p.sum()
    k = int(np.argmax(p))
    return int(_REF_CODES[k]), float(p[k]), p
