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
Stage 1 pairs features with one of two matchers, chosen per template ("matcher" in its JSON):
  "orb"        ORB + ratio test, fast; enough for the A1610 board, whose printed surface stays mostly visible.
  "lightglue"  learned DISK features (Tyszkiewicz et al., 2020) paired by LightGlue (Lindenberger et al., 2023),
               run from an ONNX export (LightGlue-ONNX, Apache-2.0). For the A4020-E: the flag label hides most of
               the reader and its busy QR code draws ORB's attention, so ORB found it on 5 of 24 photos and
               LightGlue on 23 of 24, flash included. Slower (~0.7 s per frame on a laptop CPU), so only where needed.
               The model is not in Git: python ml/tools/get_models.py downloads it to ml/models/.
Nothing here knows the wiring: it only says where the pins are.
"""
import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np

TEMPLATES = Path(__file__).resolve().parent / "templates"
MODELS = Path(__file__).resolve().parents[1] / "models"          # ml/models, not in Git
LIGHTGLUE_MODEL = MODELS / "disk_lightglue_pipeline.onnx"
LIGHTGLUE_SIZE = (480, 640)    # width, height of the picture pair LightGlue sees (bigger: slower, not better here)

WORK_SIDE = 1920        # frames are shrunk to this long side first (the phone sends about 1920 x 1080)
RATIO = 0.75            # Lowe's ratio test
RANSAC_METHOD = cv2.USAC_MAGSAC   # MAGSAC++ (Barath et al., 2020); cv2.RANSAC is the classic alternative
RANSAC_PX = 3.0         # inlier threshold in pixels at WORK_SIDE (MAGSAC++ treats it as an upper bound)
MIN_INLIERS = 15        # fewer inlier pairs: "board not found" instead of a guess
BLOCK_MARGIN = (75, 18, 35, 18)   # default block box around its pins: left, top, right, bottom (template px);
                                  # a template can set its own as "block_margin" in its JSON
BLOCK_SEARCH = 45       # stage 2: how far the column of blocks may move (template px; pins are 24 px apart)
BLOCK_TIGHT = 5         # then how far each block may move on its own, around the column's offset
AGREE_MIN = 0.10        # a placed block must look at least this much like its own block in the reference photo
                        # (colour ZNCC; session 1: correct placements >= 0.19, one block off <= 0.15)


@dataclass
class Template:
    name: str
    gray: np.ndarray
    modules: Dict[str, List[dict]]          # module -> [{"name", "x", "y"}] in template pixels
    keypoints: list = field(default_factory=list)
    descriptors: Optional[np.ndarray] = None
    rgb: Optional[np.ndarray] = None        # colour picture, for LightGlue
    matcher: str = "orb"                    # "orb" or "lightglue", from the template's JSON
    block_margin: Tuple[int, int, int, int] = BLOCK_MARGIN   # stage 2: box around each module's pins
    min_inliers: int = MIN_INLIERS          # fewer agreeing pairs: "not found"; a template can ask for more
    lab: Optional[np.ndarray] = None        # CIELAB of rgb, computed on first use (block check)
    variant: str = ""                       # "" = the template's own photo; else a board variant (see load)
    variants: List["Template"] = field(default_factory=list)   # other revisions of the device, same pin layout

    @staticmethod
    def load(name: str) -> "Template":
        """The template, with its board variants: other revisions of the device with the same pin layout (e.g. the
        AXIS A1601 for the A1610), each a photo of the empty board in this template's pixel frame ("variants" in the
        JSON: {"A1601": "A1610_A1601.jpg"}; made by ml/tools/make_variant.py). locate() uses the one that fits."""
        meta = json.loads((TEMPLATES / (name + ".json")).read_text(encoding="utf-8"))

        def one(image: str, variant: str) -> "Template":
            bgr = cv2.imread(str(TEMPLATES / image))
            if bgr is None:
                raise FileNotFoundError(TEMPLATES / image)
            gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
            kp, des = cv2.ORB_create(nfeatures=4000).detectAndCompute(gray, None)
            return Template(name, gray, meta["modules"], kp, des, cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB),
                            meta.get("matcher", "orb"), tuple(meta.get("block_margin", BLOCK_MARGIN)),
                            int(meta.get("min_inliers", MIN_INLIERS)), variant=variant)

        base = one(meta["image"], "")
        base.variants = [one(image, v) for v, image in meta.get("variants", {}).items()]
        return base

    def variant_named(self, variant: str) -> "Template":
        return next((t for t in [self] + self.variants if t.variant == variant), self)


@dataclass
class Located:
    found: bool
    inliers: int = 0                                   # stage-1 pairs that agree with the homography
    pins: Dict[str, List[Tuple[float, float]]] = field(default_factory=dict)   # module -> (x, y) in frame pixels
    board_only: Dict[str, List[Tuple[float, float]]] = field(default_factory=dict)  # stage 1 alone, for drawing
    homography: Optional[np.ndarray] = None            # template -> frame (frame pixels)
    shifts: Dict[str, Tuple[int, int]] = field(default_factory=dict)   # stage 2: block offsets (template pixels)
    doubtful: Dict[str, str] = field(default_factory=dict)  # modules placed where they do not look right -> why
    variant: str = ""                                  # which board variant fitted ("" = the template's own photo)
    agreement: float = -1.0                            # mean colour agreement of the placed blocks with that photo
    matches: list = field(default_factory=list)        # (template point, frame point, inlier?) for drawing


def locate(template: Template, image: np.ndarray, refine_blocks: bool = True) -> Located:
    """Where the template's pins are in this frame: grey (H x W) or RGB (H x W x 3), any size.
    found = False when the device is not in view. With board variants, each is tried and the one with the most
    blocks agree best in colour with its photo is used (its photo then also serves the block check and the wire reader).
    Agreement, not the number of feature pairs: on 1 Nov the A1610 photo got a few more pairs on an A1601 frame and
    all eight empty pins were read as wires."""
    best = None
    for t in [template] + template.variants:
        r = _locate_one(t, image, refine_blocks)
        r.variant = t.variant
        key = (r.found, r.agreement if r.found else 0.0, r.inliers)
        if best is None or key > best[0]:
            best = (key, r)
    return best[1]


def _locate_one(template: Template, image: np.ndarray, refine_blocks: bool) -> Located:
    h, w = image.shape[:2]
    s = min(1.0, WORK_SIDE / max(h, w))
    small = cv2.resize(image, (round(w * s), round(h * s)), interpolation=cv2.INTER_AREA) if s < 1 else image
    gray = small if small.ndim == 2 else cv2.cvtColor(small, cv2.COLOR_RGB2GRAY)

    # stage 1: the whole device
    if template.matcher == "lightglue":
        rgb = small if small.ndim == 3 else cv2.cvtColor(small, cv2.COLOR_GRAY2RGB)
        src, dst, px = _pairs_lightglue(template, rgb)
    else:
        src, dst = _pairs_orb(template, gray)
        px = RANSAC_PX
    if len(src) < template.min_inliers:
        return Located(False, len(src))
    small = gray
    cv2.setRNGSeed(0)                                  # same frame, same answer
    H, mask = cv2.findHomography(src, dst, RANSAC_METHOD, px)
    inliers = int(mask.sum()) if mask is not None else 0
    if H is None or inliers < template.min_inliers:
        return Located(False, inliers)

    shifts = _block_shifts(template, small, H) if refine_blocks else {}
    to_frame = np.diag([1 / s, 1 / s, 1.0]) @ H          # template -> full-size frame
    result = Located(True, inliers, homography=to_frame, shifts=shifts,
                     matches=[(tuple(a), tuple(b / s), bool(k)) for a, b, k in zip(src, dst, mask.ravel())])
    if len(template.modules) > 1:
        result.doubtful, result.agreement = _check_blocks(template, image if image.ndim == 3 else None, to_frame, shifts)
    for module, pins in template.modules.items():
        dx, dy = shifts.get(module, (0, 0))
        for target, ox, oy in ((result.board_only, 0, 0), (result.pins, dx, dy)):
            pts = np.float32([[p["x"] + ox, p["y"] + oy] for p in pins]).reshape(-1, 1, 2)
            target[module] = [tuple(map(float, q)) for q in cv2.perspectiveTransform(pts, to_frame).reshape(-1, 2)]
    return result


def _zncc(a: np.ndarray, b: np.ndarray) -> float:
    """Zero-mean normalised cross-correlation, averaged over the channels (CIELAB: brightness and colour)."""
    a = a.reshape(-1, a.shape[-1] if a.ndim == 3 else 1).astype(np.float64)
    b = b.reshape(-1, b.shape[-1] if b.ndim == 3 else 1).astype(np.float64)
    a, b = a - a.mean(0), b - b.mean(0)
    return float(np.mean((a * b).sum(0) / (np.sqrt((a * a).sum(0) * (b * b).sum(0)) + 1e-9)))


def _check_blocks(template: Template, rgb: Optional[np.ndarray], to_frame: np.ndarray,
                  shifts: Dict[str, Tuple[int, int]]) -> Tuple[Dict[str, str], float]:
    """Does each placed block look like its own block in the reference photo? A wrong match can put the whole board
    one block off (identical blocks, a far or partly hidden board): then the frame at Reader 1's place looks like
    Reader 2 of the reference (another housing colour), or like nothing. Such modules are returned with the reason;
    the reader reports them as not found instead of reading the wrong pins (bench, 31 Oct). Also returns the mean
    agreement of the blocks with their own reference blocks (used to choose the board variant)."""
    from .colour import srgb_to_lab
    th, tw = template.gray.shape[:2]
    if rgb is None:
        return {}, -1.0
    view = cv2.warpPerspective(rgb, np.linalg.inv(to_frame), (tw, th), flags=cv2.INTER_AREA)
    if template.lab is None:
        template.lab = srgb_to_lab(template.rgb.astype(np.float64)).astype(np.float32)
    left, top, right, bottom = template.block_margin
    boxes = {}
    for m, pins in template.modules.items():
        xs, ys = [p["x"] for p in pins], [p["y"] for p in pins]
        boxes[m] = (max(0, int(min(xs)) - left), max(0, int(min(ys)) - top),
                    min(tw, int(max(xs)) + right), min(th, int(max(ys)) + bottom))
    doubtful, agreements = {}, []
    for m, (x0, y0, x1, y1) in boxes.items():
        dx, dy = shifts.get(m, (0, 0))
        X0, Y0, X1, Y1 = x0 + dx, y0 + dy, x1 + dx, y1 + dy
        if X0 < 0 or Y0 < 0 or X1 > tw or Y1 > th:
            continue
        here = srgb_to_lab(view[Y0:Y1, X0:X1].astype(np.float64))
        own = _zncc(here, template.lab[y0:y1, x0:x1])
        agreements.append(own)
        if own < AGREE_MIN:
            doubtful[m] = f"looks unlike its reference block (agreement {own:.2f})"
            continue
        for o, (ox0, oy0, _, _) in boxes.items():       # blocks with as many pins could be mistaken for each other
            ref = template.lab[oy0:oy0 + (y1 - y0), ox0:ox0 + (x1 - x0)]
            if o != m and len(template.modules[o]) == len(template.modules[m]) and ref.shape == here.shape:
                other = _zncc(here, ref)
                if other > own:
                    doubtful[m] = f"looks more like {o} ({other:.2f}) than itself ({own:.2f})"
                    break
    return doubtful, float(np.mean(agreements)) if agreements else -1.0


def _pairs_orb(template: Template, gray: np.ndarray):
    """ORB features paired with Lowe's ratio test: (template points, frame points)."""
    kp, des = cv2.ORB_create(nfeatures=6000).detectAndCompute(gray, None)
    if des is None or template.descriptors is None:
        return np.zeros((0, 2), np.float32), np.zeros((0, 2), np.float32)
    pairs = cv2.BFMatcher(cv2.NORM_HAMMING).knnMatch(template.descriptors, des, k=2)
    good = [m for m, n in (p for p in pairs if len(p) == 2) if m.distance < RATIO * n.distance]
    return (np.float32([template.keypoints[m.queryIdx].pt for m in good]).reshape(-1, 2),
            np.float32([kp[m.trainIdx].pt for m in good]).reshape(-1, 2))


_lightglue_session = None


def _lightglue():
    global _lightglue_session
    if _lightglue_session is None:
        import onnxruntime as ort                        # only needed for templates that use LightGlue
        if not LIGHTGLUE_MODEL.exists():
            raise FileNotFoundError(f"{LIGHTGLUE_MODEL} is missing: run  python ml/tools/get_models.py")
        _lightglue_session = ort.InferenceSession(str(LIGHTGLUE_MODEL), providers=ort.get_available_providers())
    return _lightglue_session


def _fit(img: np.ndarray, width: int, height: int):
    """Shrink into width x height keeping the shape, pad right/bottom with black; returns (canvas, scale)."""
    s = min(width / img.shape[1], height / img.shape[0])
    r = cv2.resize(img, (max(1, round(img.shape[1] * s)), max(1, round(img.shape[0] * s))), interpolation=cv2.INTER_AREA)
    canvas = np.zeros((height, width, 3), np.uint8)
    canvas[:r.shape[0], :r.shape[1]] = r
    return canvas, s


def _pairs_lightglue(template: Template, rgb: np.ndarray):
    """DISK features paired by LightGlue: (template points, frame points, inlier threshold in frame pixels).
    The points were found on the small picture LightGlue sees, so the threshold grows by the same factor."""
    width, height = LIGHTGLUE_SIZE
    turned = rgb.shape[1] > rgb.shape[0]                 # a landscape frame (as cameras send it) is matched upright
    frame = np.ascontiguousarray(np.rot90(rgb, -1)) if turned else rgb      # -1 = a quarter turn clockwise
    a, sa = _fit(template.rgb, width, height)
    b, sb = _fit(frame, width, height)
    x = np.stack([a, b]).transpose(0, 3, 1, 2).astype(np.float32) / 255.0
    keypoints, matches, _ = _lightglue().run(None, {"images": np.ascontiguousarray(x)})
    matches = matches[matches[:, 0] == 0]
    src = keypoints[0][matches[:, 1]].astype(np.float32) / sa
    dst = keypoints[1][matches[:, 2]].astype(np.float32) / sb
    if turned:                                           # back to the frame as it came: x = y', y = H - 1 - x'
        dst = np.stack([dst[:, 1], rgb.shape[0] - 1 - dst[:, 0]], axis=1).astype(np.float32)
    return src.reshape(-1, 2), dst.reshape(-1, 2), RANSAC_PX / sb


def _block_shifts(template: Template, small: np.ndarray, H: np.ndarray) -> Dict[str, Tuple[int, int]]:
    """Stage 2: each block's offset in template pixels, found in the frame warped into the template's view."""
    th, tw = template.gray.shape[:2]
    warped = cv2.warpPerspective(small, np.linalg.inv(H), (tw, th))
    left, top, right, bottom = template.block_margin

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
        boxes[module] = (max(0, int(min(xs)) - left), max(0, int(min(ys)) - top),
                         min(tw, int(max(xs)) + right), min(th, int(max(ys)) + bottom))
    # first all blocks together: the whole column of blocks has one shape, so it cannot slip by one pin
    column = (min(b[0] for b in boxes.values()), min(b[1] for b in boxes.values()),
              max(b[2] for b in boxes.values()), max(b[3] for b in boxes.values()))
    cdx, cdy = match(column, 0, 0, BLOCK_SEARCH)
    # then each block on its own, only a little around the common offset
    return {m: match(b, cdx, cdy, BLOCK_TIGHT) for m, b in boxes.items()}
