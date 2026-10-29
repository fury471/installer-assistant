"""Find the board in a camera frame: where every pin of the template lands in the image (M2, package A).

Two stages:
 1. The whole board. ORB features (Rublee et al., 2011) are found in the template and in the frame and paired;
    a pair is kept only when its best match is clearly better than the second best (Lowe's ratio test, Lowe 2004).
    A robust fit then finds the homography that maps the template onto the frame and ignores the wrong pairs
    (Hartley & Zisserman, 2004, ch. 4): MAGSAC++ (Barath et al., 2020), a refinement of RANSAC (Fischler & Bolles,
    1981) that needs no hand-picked inlier threshold to be exact. Most features lie on the flat circuit board.
 2. Each terminal block. The blocks stand above the board, so the board's homography puts their pins a little off
    (parallax), more when the phone is tilted. The frame is warped into the template's view with that homography,
    and the column of blocks is found again by normalised cross-correlation (cv2.matchTemplate; Lewis, 1995). The
    whole column is matched first: pins repeat, so a single block could slip by one pin, but the column as a whole
    cannot. Each block is then fine-tuned a few pixels around the column's offset, and its pins move with it.
Nothing here knows the wiring: it only says where the pins are.
"""
import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np

TEMPLATES = Path(__file__).resolve().parent / "templates"

WORK_SIDE = 1920        # frames are shrunk to this long side first (the phone sends about 1920 x 1080)
RATIO = 0.75            # Lowe's ratio test
RANSAC_METHOD = cv2.USAC_MAGSAC   # MAGSAC++ (Barath et al., 2020); cv2.RANSAC is the classic alternative
RANSAC_PX = 3.0         # inlier threshold in pixels at WORK_SIDE (MAGSAC++ treats it as an upper bound)
MIN_INLIERS = 15        # fewer inlier pairs: "board not found" instead of a guess
BLOCK_MARGIN = (75, 18, 35)   # block box around its pins on the template: left, top/bottom, right (template px)
BLOCK_SEARCH = 45       # stage 2: how far the column of blocks may move (template px; pins are 24 px apart)
BLOCK_TIGHT = 5         # then how far each block may move on its own, around the column's offset


@dataclass
class Template:
    name: str
    gray: np.ndarray
    modules: Dict[str, List[dict]]          # module -> [{"name", "x", "y"}] in template pixels
    keypoints: list = field(default_factory=list)
    descriptors: Optional[np.ndarray] = None

    @staticmethod
    def load(name: str) -> "Template":
        meta = json.loads((TEMPLATES / (name + ".json")).read_text(encoding="utf-8"))
        gray = cv2.imread(str(TEMPLATES / meta["image"]), cv2.IMREAD_GRAYSCALE)
        if gray is None:
            raise FileNotFoundError(TEMPLATES / meta["image"])
        kp, des = cv2.ORB_create(nfeatures=4000).detectAndCompute(gray, None)
        return Template(name, gray, meta["modules"], kp, des)


@dataclass
class Located:
    found: bool
    inliers: int = 0                                   # stage-1 pairs that agree with the homography
    pins: Dict[str, List[Tuple[float, float]]] = field(default_factory=dict)   # module -> (x, y) in frame pixels
    board_only: Dict[str, List[Tuple[float, float]]] = field(default_factory=dict)  # stage 1 alone, for drawing
    homography: Optional[np.ndarray] = None            # template -> frame (frame pixels)
    matches: list = field(default_factory=list)        # (template point, frame point, inlier?) for drawing


def locate(template: Template, gray: np.ndarray, refine_blocks: bool = True) -> Located:
    """Where the template's pins are in this grey frame (any size). found = False when the board is not in view."""
    h, w = gray.shape[:2]
    s = min(1.0, WORK_SIDE / max(h, w))
    small = cv2.resize(gray, (round(w * s), round(h * s)), interpolation=cv2.INTER_AREA) if s < 1 else gray

    # stage 1: the whole board
    kp, des = cv2.ORB_create(nfeatures=6000).detectAndCompute(small, None)
    if des is None or template.descriptors is None:
        return Located(False)
    pairs = cv2.BFMatcher(cv2.NORM_HAMMING).knnMatch(template.descriptors, des, k=2)
    good = [m for m, n in (p for p in pairs if len(p) == 2) if m.distance < RATIO * n.distance]
    if len(good) < MIN_INLIERS:
        return Located(False, len(good))
    src = np.float32([template.keypoints[m.queryIdx].pt for m in good])
    dst = np.float32([kp[m.trainIdx].pt for m in good])
    cv2.setRNGSeed(0)                                  # same frame, same answer
    H, mask = cv2.findHomography(src, dst, RANSAC_METHOD, RANSAC_PX)
    inliers = int(mask.sum()) if mask is not None else 0
    if H is None or inliers < MIN_INLIERS:
        return Located(False, inliers)

    shifts = _block_shifts(template, small, H) if refine_blocks else {}
    to_frame = np.diag([1 / s, 1 / s, 1.0]) @ H          # template -> full-size frame
    result = Located(True, inliers, homography=to_frame,
                     matches=[(tuple(a), tuple(b / s), bool(k)) for a, b, k in zip(src, dst, mask.ravel())])
    for module, pins in template.modules.items():
        dx, dy = shifts.get(module, (0, 0))
        for target, ox, oy in ((result.board_only, 0, 0), (result.pins, dx, dy)):
            pts = np.float32([[p["x"] + ox, p["y"] + oy] for p in pins]).reshape(-1, 1, 2)
            target[module] = [tuple(map(float, q)) for q in cv2.perspectiveTransform(pts, to_frame).reshape(-1, 2)]
    return result


def _block_shifts(template: Template, small: np.ndarray, H: np.ndarray) -> Dict[str, Tuple[int, int]]:
    """Stage 2: each block's offset in template pixels, found in the frame warped into the template's view."""
    th, tw = template.gray.shape[:2]
    warped = cv2.warpPerspective(small, np.linalg.inv(H), (tw, th))
    left, vert, right = BLOCK_MARGIN

    def match(box, cx, cy, r):
        x0, y0, x1, y1 = box
        patch = template.gray[y0:y1, x0:x1]
        X0, Y0 = max(0, x0 + cx - r), max(0, y0 + cy - r)
        region = warped[Y0:y1 + cy + r, X0:x1 + cx + r]
        if region.shape[0] < patch.shape[0] or region.shape[1] < patch.shape[1]:
            return cx, cy
        _, _, _, loc = cv2.minMaxLoc(cv2.matchTemplate(region, patch, cv2.TM_CCOEFF_NORMED))
        return X0 + loc[0] - x0, Y0 + loc[1] - y0

    boxes = {}
    for module, pins in template.modules.items():
        xs = [p["x"] for p in pins]; ys = [p["y"] for p in pins]
        boxes[module] = (max(0, int(min(xs)) - left), max(0, int(min(ys)) - vert),
                         min(tw, int(max(xs)) + right), min(th, int(max(ys)) + vert))
    # first all blocks together: the whole column of blocks has one shape, so it cannot slip by one pin
    column = (min(b[0] for b in boxes.values()), min(b[1] for b in boxes.values()),
              max(b[2] for b in boxes.values()), max(b[3] for b in boxes.values()))
    cdx, cdy = match(column, 0, 0, BLOCK_SEARCH)
    # then each block on its own, only a little around the common offset
    return {m: match(b, cdx, cdy, BLOCK_TIGHT) for m, b in boxes.items()}
