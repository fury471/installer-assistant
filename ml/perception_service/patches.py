"""Pin patches: the small picture around each pin where its wire enters, cut from the frame turned into the
template's view (M2 step 5 / M3).

locate() gives the homography template -> frame. Warping the frame back into the template's view ("rectifying")
makes every pin's surroundings look the same size and the same way up, whatever the phone's angle, distance or
rotation. Each patch is then cut at the pin's template position (plus its block's stage-2 offset), in a window
the template sets ("patch_box": [left, top, right, bottom] in template pixels, relative to the pin). On the board
wires leave their opening to the right; on the A4020-E they leave the slot downwards, so the windows differ.
Same size and orientation for every pin is what makes the colour and "wire or not" decisions comparable.
"""
import json
from typing import Dict, List

import cv2
import numpy as np

from .locate import TEMPLATES, Located, Template


def patch_box(template) -> List[int]:
    """The template's pin window [left, top, right, bottom]; 'template' is a Template or its name."""
    name = template if isinstance(template, str) else template.name
    meta = json.loads((TEMPLATES / (name + ".json")).read_text(encoding="utf-8"))
    return meta.get("patch_box", [-10, -10, 50, 10])


def rectify(template: Template, located: Located, rgb: np.ndarray) -> np.ndarray:
    """The frame warped into the template's view (template size)."""
    th, tw = template.gray.shape[:2]
    return cv2.warpPerspective(rgb, np.linalg.inv(located.homography), (tw, th), flags=cv2.INTER_AREA,
                               borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0))


def pin_patches(template: Template, located: Located, rgb: np.ndarray, view: np.ndarray = None,
                margin: int = 0) -> Dict[str, List[np.ndarray]]:
    """module -> one RGB patch per pin, in the template's pin order. Pass 'view' to reuse a rectified frame.
    margin > 0 cuts that many extra pixels on every side (training uses them to shift and turn patches)."""
    if view is None:
        view = rectify(template, located, rgb)
    x0, y0, x1, y1 = patch_box(template)
    x0, y0, x1, y1 = x0 - margin, y0 - margin, x1 + margin, y1 + margin
    th, tw = view.shape[:2]
    out = {}
    for module, pins in template.modules.items():
        dx, dy = located.shifts.get(module, (0, 0))
        patches = []
        for p in pins:
            x, y = int(round(p["x"] + dx)), int(round(p["y"] + dy))
            patch = np.zeros((y1 - y0, x1 - x0, 3), np.uint8)
            ya, yb, xa, xb = max(0, y + y0), min(th, y + y1), max(0, x + x0), min(tw, x + x1)
            if yb > ya and xb > xa:
                patch[ya - (y + y0):yb - (y + y0), xa - (x + x0):xb - (x + x0)] = view[ya:yb, xa:xb]
            patches.append(patch)
        out[module] = patches
    return out


def window(patch: np.ndarray, margin: int) -> np.ndarray:
    """The pin window from a patch cut with 'margin' extra pixels on every side."""
    return patch[margin:patch.shape[0] - margin, margin:patch.shape[1] - margin] if margin else patch
