"""Wire tracing for the bundle check (package A): does each wire run back to the scanned cable label?

Different cables often share colours, so a red wire on the right pin may still be another cable's red wire. The app
asks the reader to follow every wire from its terminal back to where it comes from, and reports one of
"this_cable" (it reaches the scanned label), "other_cable" (it reaches another cable's label) or "unknown".
Unknown never counts against the wiring, so tracing can only add evidence, never invent a fault.

Baseline (classical and explainable), used by readers/classical.py:
  1. Find every cable label in the frame: QR codes (ISO/IEC 18004) decoded with OpenCV, with their corners. The scanned
     cable's label is the one with the same key (cable id, door, plan); its end field (CTRL/DEV) may differ.
  2. Follow each wire from its terminal by seeded region growing (Adams & Bischof, 1994): the connected region of pixels
     whose CIELAB colour is close to the colour measured at the terminal.
  3. The region reaches the scanned label's box (widened a little, since the wires leave the cable just beside it) and no
     other label → "this_cable"; it reaches another label only → "other_cable"; none or both → "unknown".

Known limits, and the way forward (measure each against this baseline in ml/eval):
  - Two touching wires of the same colour merge into one region, and a wire whose colour is close to the board leaks
    into it; both give "unknown" (honest, but often). Next: a wire mask thinned to one-pixel centre lines
    (Zhang & Suen, 1984) and followed with the least bending at crossings (Choi et al., 2023, mBEST), then learned
    instance segmentation of wires (Caporali et al., 2022: Ariadne+ and FASTDLO).
  - The label must be in the same picture as the terminals, fixed where the cable's wires fan out (on the bench: a flag
    label around the loose wires). A label out of view gives "unknown".
"""
from dataclasses import dataclass
from typing import List, Optional, Sequence, Tuple

import cv2
import numpy as np

from .colour import srgb_to_lab

THIS, OTHER, UNKNOWN = "this_cable", "other_cable", "unknown"


@dataclass
class Label:
    payload: str
    key: str
    box: Tuple[float, float, float, float]      # x0, y0, x1, y1 in pixels


def label_key(payload: str) -> str:
    """'IA|C01|D1|A1610.READER1|A4020E|CTRL' -> 'C01|D1|A1610.READER1|A4020E' (the part both ends of a cable share)."""
    parts = [p.strip().upper() for p in (payload or "").replace("·", "|").split("|")]
    if parts and parts[0] == "IA":
        parts = parts[1:]
    return "|".join(parts[:4]) if len(parts) >= 4 else ""


def find_labels(image: np.ndarray) -> List[Label]:
    """Every decodable cable label in an RGB image, with its box."""
    gray = cv2.cvtColor(image, cv2.COLOR_RGB2GRAY)
    try:
        ok, texts, points, _ = cv2.QRCodeDetector().detectAndDecodeMulti(gray)
    except cv2.error:
        return []
    labels = []
    if not ok or points is None:
        return labels
    for text, pts in zip(texts, points):
        key = label_key(text)
        if not key:
            continue
        xs, ys = pts[:, 0], pts[:, 1]
        labels.append(Label(text, key, (float(xs.min()), float(ys.min()), float(xs.max()), float(ys.max()))))
    return labels


def _touches(region: np.ndarray, box: Tuple[float, float, float, float], margin: float) -> bool:
    h, w = region.shape
    x0, y0, x1, y1 = box
    mx, my = (x1 - x0) * margin, (y1 - y0) * margin
    a, b = max(0, int(y0 - my)), min(h, int(y1 + my) + 1)
    c, d = max(0, int(x0 - mx)), min(w, int(x1 + mx) + 1)
    return a < b and c < d and bool(region[a:b, c:d].any())


def trace_wire(lab: np.ndarray, start: Tuple[float, float], scanned: Optional[Label], others: Sequence[Label],
               tolerance: float = 16.0, margin: float = 0.35, seed_radius: int = 4) -> str:
    """Where the wire at pixel 'start' (x, y) runs to. 'lab' is the image in CIELAB (H, W, 3)."""
    if scanned is None:
        return UNKNOWN                                   # the scanned label is not in this picture
    h, w = lab.shape[:2]
    x, y = int(round(start[0])), int(round(start[1]))
    if not (0 <= x < w and 0 <= y < h):
        return UNKNOWN
    r = seed_radius
    seed = np.median(lab[max(0, y - r):y + r + 1, max(0, x - r):x + r + 1].reshape(-1, 3), axis=0)
    mask = (np.linalg.norm(lab - seed, axis=2) < tolerance).astype(np.uint8)
    _, comp = cv2.connectedComponents(mask, connectivity=8)
    patch = comp[max(0, y - r):y + r + 1, max(0, x - r):x + r + 1]
    ids = patch[patch > 0]
    if ids.size == 0:
        return UNKNOWN
    region = comp == np.bincount(ids).argmax()
    reaches_scanned = _touches(region, scanned.box, margin)
    reaches_other = any(_touches(region, o.box, margin) for o in others)
    if reaches_scanned and not reaches_other:
        return THIS
    if reaches_other and not reaches_scanned:
        return OTHER
    return UNKNOWN


def trace_items(image: np.ndarray, points: Sequence[Optional[Tuple[float, float]]], scanned_payload: str) -> List[str]:
    """One trace per terminal point (normalised x, y; None = no wire or not located) for the scanned label."""
    if not scanned_payload:
        return [UNKNOWN] * len(points)
    key = label_key(scanned_payload)
    labels = find_labels(image)
    scanned = next((lb for lb in labels if lb.key == key), None)
    others = [lb for lb in labels if lb.key != key]
    h, w = image.shape[:2]
    lab = srgb_to_lab(image.astype(np.float64)) if any(p is not None for p in points) else None
    return [UNKNOWN if p is None else trace_wire(lab, (p[0] * w, p[1] * h), scanned, others) for p in points]
