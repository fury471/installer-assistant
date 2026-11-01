"""Pin windows: the small picture where each pin's wire leaves, in one common shape for every device (M2 step 5 / M3).

locate() gives the homography template -> frame. Warping the frame back into the template's view ("rectifying")
makes every pin's surroundings look the same size and the same way up, whatever the phone's angle, distance or
rotation. Each pin's window is then cut in a CANONICAL frame, the same for every device:
  - turned so the wire leaves to the right ("wire_direction" in the template JSON: right, down, left or up),
  - scaled so one pin spacing is 24 px ("pin_spacing" in the template JSON, in template pixels),
  - CANON_BOX = 10 px before the pin to 50 px after it along the wire, 10 px to either side.
So a model trained on one device's pins sees another device's pins in the same shape: the wire reader is general,
and a new device needs only data (a reference photo with its pins empty, the pin positions, spacing and wire
direction), no training. reference_patches() cuts the same windows from the empty reference photo: the wire
reader compares each live window with it (pin_reader.py).
"""
import json
from functools import lru_cache
from typing import Dict, List, Tuple

import cv2
import numpy as np

from .locate import TEMPLATES, Located, Template

CANON_BOX = (-10, -10, 50, 10)    # along the wire from, across from, along to, across to (canonical px; pin at 0, 0)
CANON_SPACING = 24.0              # canonical px per pin spacing
DIRECTIONS = {"right": ((1, 0), (0, 1)), "down": ((0, 1), (-1, 0)), "left": ((-1, 0), (0, -1)), "up": ((0, -1), (1, 0))}


@lru_cache(maxsize=None)
def geometry(name: str) -> Tuple[float, str]:
    """(pin spacing in template px, wire direction) from the template's JSON."""
    meta = json.loads((TEMPLATES / (name + ".json")).read_text(encoding="utf-8"))
    direction = meta.get("wire_direction", "right")
    if direction not in DIRECTIONS:
        raise ValueError(f"{name}.json: wire_direction must be one of {sorted(DIRECTIONS)}")
    return float(meta.get("pin_spacing", CANON_SPACING)), direction


def patch_box(template=None) -> Tuple[int, int, int, int]:
    """The window in canonical px (the same for every template): pin at (-box[0], -box[1]) inside the window."""
    return CANON_BOX


def cut(img: np.ndarray, x: float, y: float, spacing: float, direction: str, margin: int = 0) -> np.ndarray:
    """The canonical window around the pin at (x, y) of img (template pixels), with 'margin' extra canonical px on
    every side. Size (CANON_BOX[3] - CANON_BOX[1] + 2 margin) x (CANON_BOX[2] - CANON_BOX[0] + 2 margin)."""
    s = spacing / CANON_SPACING
    a0, c0, a1, c1 = CANON_BOX[0] - margin, CANON_BOX[1] - margin, CANON_BOX[2] + margin, CANON_BOX[3] + margin
    d, n = (np.array(v, float) for v in DIRECTIONS[direction])
    o = np.array([x, y], float) + s * (a0 * d + c0 * n)
    A = np.float32([[s * d[0], s * n[0], o[0]], [s * d[1], s * n[1], o[1]]])          # canonical -> template
    return cv2.warpAffine(img, cv2.invertAffineTransform(A), (a1 - a0, c1 - c0), flags=cv2.INTER_AREA,
                          borderMode=cv2.BORDER_REFLECT)


def rectify(template: Template, located: Located, rgb: np.ndarray) -> np.ndarray:
    """The frame warped into the template's view (template size)."""
    th, tw = template.gray.shape[:2]
    return cv2.warpPerspective(rgb, np.linalg.inv(located.homography), (tw, th), flags=cv2.INTER_AREA,
                               borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0))


def pin_patches(template: Template, located: Located, rgb: np.ndarray, view: np.ndarray = None,
                margin: int = 0) -> Dict[str, List[np.ndarray]]:
    """module -> one canonical window per pin, in the template's pin order. Pass 'view' to reuse a rectified frame.
    margin > 0 cuts that many extra canonical px on every side (training uses them to shift and turn windows)."""
    if view is None:
        view = rectify(template, located, rgb)
    spacing, direction = geometry(template.name)
    out = {}
    for module, pins in template.modules.items():
        dx, dy = located.shifts.get(module, (0, 0))
        out[module] = [cut(view, p["x"] + dx, p["y"] + dy, spacing, direction, margin) for p in pins]
    return out


_REFERENCES: Dict[tuple, Dict[str, List[np.ndarray]]] = {}


def reference_patches(template: Template, margin: int) -> Dict[str, List[np.ndarray]]:
    """module -> the same windows cut from the template's photo (all pins empty), cached. Pass the board variant
    that locate() found (template.variant_named(located.variant)) so the windows come from the right revision."""
    key = (template.name, template.variant, margin)
    if key not in _REFERENCES:
        spacing, direction = geometry(template.name)
        _REFERENCES[key] = {m: [cut(template.rgb, p["x"], p["y"], spacing, direction, margin) for p in pins]
                            for m, pins in template.modules.items()}
    return _REFERENCES[key]


def window(patch: np.ndarray, margin: int) -> np.ndarray:
    """The pin window from a patch cut with 'margin' extra pixels on every side."""
    return patch[margin:patch.shape[0] - margin, margin:patch.shape[1] - margin] if margin else patch
