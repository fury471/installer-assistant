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
    What that match cannot do is decide between two offsets a whole pin apart when the block is far, soft or empty:
    on 2 Nov an empty block was placed one pin low that way. So a block whose colour no longer looks like its own
    block in the reference photo is not read at all (Located.block_agreement, readers/classical.py: TRUST_MIN).
    The A4020-E uses calibrated empty views of its rigid housing above the openings. Its old narrow strip around
    the openings followed wires/highlights instead of the housing; a single old view also differed with angle.
    These local references affect only placement, not the empty reference used by the wire/colour model.
Stage 1 pairs features with one of two matchers, chosen per template ("matcher" in its JSON):
  "orb"        ORB + ratio test, fast; enough for the A1610 board, whose printed surface stays mostly visible.
  "lightglue"  learned DISK features (Tyszkiewicz et al., 2020) paired by LightGlue (Lindenberger et al., 2023),
               run from an ONNX export (LightGlue-ONNX, Apache-2.0). For the A4020-E: the flag label hides most of
               the reader and its busy QR code draws ORB's attention, so ORB found it on 5 of 24 photos and
               LightGlue on 23 of 24, flash included. Slower (~0.7 s per frame on a laptop CPU), so only where needed.
               The model is not in Git: python ml/tools/get_models.py downloads it to ml/models/.
The second matcher is tried when the first finds less than the template's min_inliers: which one wins depends on the
photo (on 2 Nov the phone's A4020-E frames gave LightGlue 95 pairs but only 10 that agreed, and ORB 121 pairs with 80
that agreed - "not found" against a good homography). Trying it costs nothing when the first matcher works.
Nothing here knows the wiring: it only says where the pins are.
"""
import json
import os
from dataclasses import dataclass, field, replace
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np

from .colour import srgb_to_lab

TEMPLATES = Path(__file__).resolve().parent / "templates"
MODELS = Path(__file__).resolve().parents[1] / "models"          # ml/models, not in Git
LIGHTGLUE_MODEL = MODELS / "disk_lightglue_pipeline.onnx"
# width, height of the picture pair LightGlue sees; multiples of 16 (the extractor halves the picture four times).
# Measured 14 Nov on the 24 session-1 reader photos (1920 px) and 28 live reader frames, same machine:
#   480 x 640: 22/24 found, 87.5 % of pins within half a pitch, 2.45 s per photo; live 7/28 found, 2.58 s
#   384 x 512: 23/24 found, 95.8 %, 1.80 s; live 7/28 found (the same frames), 1.86 s
#   336 x 448: 21/24, 86.5 %, 1.50 s        288 x 384: 20/24, 83.3 %, 1.22 s
LIGHTGLUE_SIZE = (384, 512)

WORK_SIDE = 1920        # frames are shrunk to this long side first (the phone sends about 1920 x 1080)
RATIO = 0.75            # Lowe's ratio test
RANSAC_METHOD = cv2.USAC_MAGSAC   # MAGSAC++ (Barath et al., 2020); cv2.RANSAC is the classic alternative
RANSAC_PX = 3.0         # inlier threshold in pixels at WORK_SIDE (MAGSAC++ treats it as an upper bound)
MIN_INLIERS = 15        # fewer inlier pairs: "board not found" instead of a guess
BLOCK_MARGIN = (75, 18, 35, 18)   # default block box around its pins: left, top, right, bottom (template px);
                                  # a template can set its own as "block_margin" in its JSON
BLOCK_SEARCH = 45       # stage 2: how far the column of blocks may move (template px; pins are 24 px apart)
BLOCK_TIGHT = 5         # then how far each block may move on its own, around the column's offset
PLANE_SEARCH = 84      # stage 2a: how far the blocks' plane may sit from the board's (template px; 3.5 pins)
PLANE_SCALE = (0.88, 1.20)   # and how much larger or smaller its picture may be (pin spacing in the view / in the template)
PLANE_MIN_SCORE = 0.25  # a colour match weaker than this is not used (stage 2 then works as before)
PLANE_MIN_SEEN = 0.5    # at least this share of the column must be inside the frame where it is placed
PERIOD_MIN = 0.08       # autocorrelation at one pin spacing below this: no row of pins in the strip
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
    block_margin: Tuple[int, int, int, int] = BLOCK_MARGIN   # stage 2 box; negative bottom can exclude wires
    min_inliers: int = MIN_INLIERS          # fewer agreeing pairs: "not found"; a template can ask for more
    lab: Optional[np.ndarray] = None        # CIELAB of rgb, computed on first use (block check)
    variant: str = ""                       # "" = the template's own photo; else a board variant (see load)
    variants: List["Template"] = field(default_factory=list)   # other revisions of the device, same pin layout
    block_references: Dict[str, List[np.ndarray]] = field(default_factory=dict)   # calibrated, wire-free local views
    wire_references: Dict[str, List[np.ndarray]] = field(default_factory=dict)    # same views, packed empty pin windows
    wire_reference_margin: int = 0

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
            result = Template(name, gray, meta["modules"], kp, des, cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB),
                              meta.get("matcher", "orb"), tuple(meta.get("block_margin", BLOCK_MARGIN)),
                              int(meta.get("min_inliers", MIN_INLIERS)), variant=variant)
            for module, images in meta.get("block_references", {}).items():
                x0, y0, x1, y1 = _block_boxes(result)[module]
                references = []
                for image in images:
                    patch = cv2.imread(str(TEMPLATES / image), cv2.IMREAD_GRAYSCALE)
                    if patch is None or patch.shape != (y1 - y0, x1 - x0):
                        raise ValueError(f"{image}: block reference must have shape {(y1 - y0, x1 - x0)}")
                    references.append(patch)
                result.block_references[module] = references
            result.wire_reference_margin = int(meta.get("wire_reference_margin", 0))
            for module, images in meta.get("wire_references", {}).items():
                if len(images) != len(result.block_references.get(module, [])):
                    raise ValueError(f"{module}: wire and housing reference views must correspond")
                result.wire_references[module] = []
                for image in images:
                    packed = cv2.imread(str(TEMPLATES / image))
                    if packed is None:
                        raise FileNotFoundError(TEMPLATES / image)
                    result.wire_references[module].append(cv2.cvtColor(packed, cv2.COLOR_BGR2RGB))
            return result

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
    block_agreement: Dict[str, float] = field(default_factory=dict)  # per module: how much it looks like its own block
    matches: list = field(default_factory=list)        # (template point, frame point, inlier?) for drawing
    reference_indices: Dict[str, int] = field(default_factory=dict)   # 0 = original; 1..N = matched housing view
    plane: bool = False                                # stage 2a placed the column of blocks by its colours


def locate(template: Template, image: np.ndarray, refine_blocks: bool = True) -> Located:
    """Where the template's pins are in this frame: grey (H x W) or RGB (H x W x 3), any size.
    found = False when the device is not in view. With board variants, each is tried and the one whose blocks
    agree best in colour with its photo is used (its photo then also serves the block check and the wire reader).
    Agreement, not the number of feature pairs: on 1 Nov the A1610 photo got a few more pairs on an A1601 frame and
    all eight empty pins were read as wires.
    The variants share one pixel frame, so the board has to be found only once: every photo first tries its fast
    matcher, and a photo that finds too little borrows the homography of the one that found most. Only when none
    finds the board is the slow matcher run. (Before, each photo that failed fell back to the slow matcher on its
    own: on the real A1610 the A1601 photo did, and every read took 3 to 5 s instead of under one, 14 Nov.)"""
    h, w = image.shape[:2]
    s = min(1.0, WORK_SIDE / max(h, w))
    small = cv2.resize(image, (round(w * s), round(h * s)), interpolation=cv2.INTER_AREA) if s < 1 else image
    gray = small if small.ndim == 2 else cv2.cvtColor(small, cv2.COLOR_RGB2GRAY)
    candidates = [template] + template.variants
    fits = [_board(t, small, gray, [t.matcher]) for t in candidates]
    if not any(f[0] is not None for f in fits):
        fits = [_board(t, small, gray, [m for m in ("orb", "lightglue") if m != t.matcher]) for t in candidates]
    found = [f for f in fits if f[0] is not None]
    if not found:
        return Located(False, max(f[1] for f in fits))
    shared = max(found, key=lambda f: f[1])
    best = None
    for t, fit in zip(candidates, fits):
        r = _blocks(t, image, small, gray, s, fit if fit[0] is not None else shared, refine_blocks)
        r.variant = t.variant
        key = (r.plane, r.agreement, r.inliers)         # a photo whose housing colours were found in the frame first
        if best is None or key > best[0]:
            best = (key, r)
    return best[1]


def _locate_one(template: Template, image: np.ndarray, refine_blocks: bool) -> Located:
    """locate() with this photo alone, without the template's variants (ml/tools/make_variant.py)."""
    return locate(replace(template, variants=[]), image, refine_blocks)


def _board(template: Template, small: np.ndarray, gray: np.ndarray, matchers: List[str]):
    """Stage 1: the homography template -> (shrunk) frame with the first of the matchers that finds enough pairs.
    Returns (H or None, inlier pairs, matches for drawing)."""
    inliers = 0
    for matcher in matchers:
        if matcher == "lightglue":
            rgb = small if small.ndim == 3 else cv2.cvtColor(small, cv2.COLOR_GRAY2RGB)
            try:
                src, dst, px = _pairs_lightglue(template, rgb)
            except (FileNotFoundError, ImportError) as e:      # no model file downloaded: ORB still works
                print("LightGlue not available (%r)" % e)
                continue
        else:
            src, dst = _pairs_orb(template, gray)
            px = RANSAC_PX
        if len(src) < template.min_inliers:
            continue
        cv2.setRNGSeed(0)                              # same frame, same answer
        h, m = cv2.findHomography(src, dst, RANSAC_METHOD, px)
        k = int(m.sum()) if m is not None else 0
        if h is not None and k >= template.min_inliers:
            return h, k, [(tuple(a), tuple(b), bool(i)) for a, b, i in zip(src, dst, m.ravel())]
        inliers = max(inliers, k)
    return None, inliers, []


def _blocks(template: Template, image: np.ndarray, small: np.ndarray, gray: np.ndarray, s: float, fit,
            refine_blocks: bool) -> Located:
    """Stage 2 for one photo of the device, from the board's homography."""
    H, inliers, pairs = fit
    # stage 2a: the plane the blocks' tops lie in (colour; needs several blocks, some of them in view)
    board = np.diag([1 / s, 1 / s, 1.0]) @ H             # template -> full-size frame, on the circuit board
    plane = _block_plane(template, small, H) if refine_blocks and small.ndim == 3 and len(template.modules) > 1 else None
    if plane is not None:
        H = H @ plane
    # stage 2b on the grey frame: each block's own edges say where it is
    references = {}
    shifts = _block_shifts(template, gray, H, references, column=plane is None) if refine_blocks else {}
    to_frame = np.diag([1 / s, 1 / s, 1.0]) @ H          # template -> full-size frame, on the blocks
    result = Located(True, inliers, homography=to_frame, shifts=shifts,
                     matches=[(a, (b[0] / s, b[1] / s), k) for a, b, k in pairs], reference_indices=references,
                     plane=plane is not None)
    if len(template.modules) > 1:
        result.doubtful, result.agreement, result.block_agreement = _check_blocks(
            template, image if image.ndim == 3 else None, to_frame, shifts)
    for module, pins in template.modules.items():
        dx, dy = shifts.get(module, (0, 0))
        for target, ox, oy, via in ((result.board_only, 0, 0, board), (result.pins, dx, dy, to_frame)):
            pts = np.float32([[p["x"] + ox, p["y"] + oy] for p in pins]).reshape(-1, 1, 2)
            target[module] = [tuple(map(float, q)) for q in cv2.perspectiveTransform(pts, via).reshape(-1, 2)]
    return result


def _zncc(a: np.ndarray, b: np.ndarray) -> float:
    """Zero-mean normalised cross-correlation, averaged over the channels (CIELAB: brightness and colour)."""
    a = a.reshape(-1, a.shape[-1] if a.ndim == 3 else 1).astype(np.float64)
    b = b.reshape(-1, b.shape[-1] if b.ndim == 3 else 1).astype(np.float64)
    a, b = a - a.mean(0), b - b.mean(0)
    return float(np.mean((a * b).sum(0) / (np.sqrt((a * a).sum(0) * (b * b).sum(0)) + 1e-9)))


def _check_blocks(template: Template, rgb: Optional[np.ndarray], to_frame: np.ndarray,
                  shifts: Dict[str, Tuple[int, int]]) -> Tuple[Dict[str, str], float, Dict[str, float]]:
    """Does each placed block look like its own block in the reference photo? A wrong match can put the whole board
    one block off (identical blocks, a far or partly hidden board): then the frame at Reader 1's place looks like
    Reader 2 of the reference (another housing colour), or like nothing. Such modules are returned with the reason;
    the reader reports them as not found instead of reading the wrong pins (bench, 31 Oct). Also returns the mean
    agreement of the blocks with their own reference blocks (used to choose the board variant) and each block's own
    agreement (the wire reader uses it to tell a doubtful block from a doubtful wire)."""
    th, tw = template.gray.shape[:2]
    if rgb is None:
        return {}, -1.0, {}
    view = cv2.warpPerspective(rgb, np.linalg.inv(to_frame), (tw, th), flags=cv2.INTER_AREA)
    ref_lab = _lab(template)
    boxes = _block_boxes(template)
    doubtful, agreements, by_module = {}, [], {}
    for m, (x0, y0, x1, y1) in boxes.items():
        dx, dy = shifts.get(m, (0, 0))
        X0, Y0, X1, Y1 = x0 + dx, y0 + dy, x1 + dx, y1 + dy
        if X0 < 0 or Y0 < 0 or X1 > tw or Y1 > th:
            continue
        here = srgb_to_lab(view[Y0:Y1, X0:X1].astype(np.float64))
        own = _zncc(here, ref_lab[y0:y1, x0:x1])
        agreements.append(own)
        by_module[m] = own
        if own < AGREE_MIN:
            doubtful[m] = f"looks unlike its reference block (agreement {own:.2f})"
            continue
        for o, (ox0, oy0, _, _) in boxes.items():       # blocks with as many pins could be mistaken for each other
            ref = ref_lab[oy0:oy0 + (y1 - y0), ox0:ox0 + (x1 - x0)]
            if o != m and len(template.modules[o]) == len(template.modules[m]) and ref.shape == here.shape:
                other = _zncc(here, ref)
                if other > own:
                    doubtful[m] = f"looks more like {o} ({other:.2f}) than itself ({own:.2f})"
                    break
    return doubtful, float(np.mean(agreements)) if agreements else -1.0, by_module


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
        options = ort.SessionOptions()
        # ONNX Runtime uses every physical core by default. When Unity runs on the same computer (the laptop test)
        # that takes the processor from the app for the length of each read; IA_ORT_THREADS leaves it some
        # (ONNX Runtime documentation, "Thread management": intra_op_num_threads). Unset = the default.
        threads = int(os.environ.get("IA_ORT_THREADS", "0") or 0)
        if threads > 0:
            options.intra_op_num_threads = threads
        _lightglue_session = ort.InferenceSession(str(LIGHTGLUE_MODEL), sess_options=options, providers=ort.get_available_providers())
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


def _block_boxes(template: Template) -> Dict[str, Tuple[int, int, int, int]]:
    """module -> the box around its pins (template px), grown by the template's block margin."""
    th, tw = template.gray.shape[:2]
    left, top, right, bottom = template.block_margin
    boxes = {}
    for m, pins in template.modules.items():
        xs, ys = [p["x"] for p in pins], [p["y"] for p in pins]
        boxes[m] = (max(0, int(min(xs)) - left), max(0, int(min(ys)) - top),
                    min(tw, int(max(xs)) + right), min(th, int(max(ys)) + bottom))
    return boxes


def _lab(template: Template) -> np.ndarray:
    """CIELAB of the template's photo, computed on first use."""
    if template.lab is None:
        template.lab = srgb_to_lab(template.rgb.astype(np.float64)).astype(np.float32)
    return template.lab


def _block_plane(template: Template, small: np.ndarray, H: np.ndarray) -> Optional[np.ndarray]:
    """Stage 2a: the plane of the terminal blocks, as a 3 x 3 map in template pixels (template point -> where it
    is in the frame rectified with the board's homography), or None when too little of the column is in view.

    The blocks' tops lie in a plane parallel to the board, about a centimetre above it. Seen through the board's
    homography, a parallel plane is the board's picture scaled about the point under the camera (a planar homology
    whose axis is the line at infinity; Hartley & Zisserman, 2004, sec. A7.2 and ch. 13): one scale and one shift,
    no rotation. Close up or from the side that shift is several pins (14 Nov: every pin two terminals low at
    15 cm), more than the old search of 45 px allowed, and in grey the pins repeat, so the match slipped by whole
    pins. Here the column of blocks is matched in colour (CIELAB), where the change of housing colour between
    blocks is a landmark that does not repeat, at several scales, and only with the part of the column that is
    inside the frame (a close-up cuts the column off; black outside the frame is not a block)."""
    th, tw = template.gray.shape[:2]
    inv = np.linalg.inv(H)
    view = cv2.warpPerspective(small, inv, (tw, th), flags=cv2.INTER_AREA)
    seen = cv2.warpPerspective(np.full(small.shape[:2], 255, np.uint8), inv, (tw, th), flags=cv2.INTER_NEAREST)
    boxes = _block_boxes(template)
    x0, y0 = min(b[0] for b in boxes.values()), min(b[1] for b in boxes.values())
    x1, y1 = max(b[2] for b in boxes.values()), max(b[3] for b in boxes.values())
    r = PLANE_SEARCH
    X0, X1, Y0, Y1 = max(0, x0 - r), min(tw, x1 + r), max(0, y0 - r), min(th, y1 + r)
    mask = (seen[Y0:Y1, X0:X1] > 0).astype(np.float32)
    spacing = _pin_spacing(template)
    lab = srgb_to_lab(view[Y0:Y1, X0:X1].astype(np.float64)).astype(np.float32)
    # 1. the scale, from how far apart the pins are in this view: the one thing a repeating row tells exactly
    period = _row_period(lab[:, :, 0], mask, spacing)
    own = _row_period(_lab(template)[Y0:Y1, X0:X1, 0], np.ones(mask.shape, np.float32), spacing)   # same measure, same strip
    if period is None or own is None:
        return None
    k = period / own
    resized = lambda img: cv2.resize(img, None, fx=k, fy=k, interpolation=cv2.INTER_AREA if k < 1 else cv2.INTER_LINEAR)
    # 2. the place, from the colours alone (a* and b*, no lightness): the pins and their shadows repeat in
    #    lightness, the housings' colours do not, so this match cannot slip by a pin. Only pixels the frame
    #    shows count (a close-up cuts the column off), and at least PLANE_MIN_SEEN of the column must be among them.
    column = resized(_lab(template)[y0:y1, x0:x1])
    if column.shape[0] > lab.shape[0] or column.shape[1] > lab.shape[1]:
        return None
    score, cover = _masked_zncc(lab[:, :, 1:], mask, column[:, :, 1:])
    score[cover < PLANE_MIN_SEEN] = -1
    _, top, _, (lx, ly) = cv2.minMaxLoc(score)
    if top < PLANE_MIN_SCORE:
        return None
    # 3. then exactly, with lightness too, but less than half a pin around that place
    near = int(period * 0.45)
    ax, ay = max(0, lx - near), max(0, ly - near)
    bx, by = min(lab.shape[1], lx + near + column.shape[1]), min(lab.shape[0], ly + near + column.shape[0])
    fine, cover = _masked_zncc(lab[ay:by, ax:bx], mask[ay:by, ax:bx], column)
    fine[cover < PLANE_MIN_SEEN] = -1
    _, _, _, (fx, fy) = cv2.minMaxLoc(fine)
    lx, ly = ax + fx, ay + fy
    # template point (x, y) is at (X0 + lx + k (x - x0), Y0 + ly + k (y - y0)) in the rectified frame
    return np.array([[k, 0, X0 + lx - k * x0], [0, k, Y0 + ly - k * y0], [0, 0, 1]], float)


def _masked_zncc(image: np.ndarray, mask: np.ndarray, patch: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    """Zero-mean normalised cross-correlation of patch at every place in image, counting only the pixels where
    mask is 1 (Padfield, 2012, with the mask on the image side), averaged over the channels. Returns the score
    map and the share of the patch that lay on masked-in pixels at each place."""
    ones = np.ones(patch.shape[:2], np.float32)
    corr = lambda a, b: cv2.matchTemplate(np.ascontiguousarray(a, np.float32), np.ascontiguousarray(b, np.float32), cv2.TM_CCORR)
    count = corr(mask, ones)
    safe = np.maximum(count, 1.0)
    total = np.zeros_like(count)
    channels = image.shape[2] if image.ndim == 3 else 1
    for c in range(channels):
        i = (image[:, :, c] if image.ndim == 3 else image) * mask
        t = patch[:, :, c] if patch.ndim == 3 else patch
        si, si2, sit = corr(i, ones), corr(i * i, ones), corr(i, t)
        st, st2 = corr(mask, t), corr(mask, t * t)
        num = sit - si * st / safe
        den = np.sqrt(np.maximum(si2 - si * si / safe, 0) * np.maximum(st2 - st * st / safe, 0))
        total += num / (den + 1e-6 * patch.size)
    return total / channels, count / float(ones.size)


def _row_period(lightness: np.ndarray, mask: np.ndarray, expected: float) -> Optional[float]:
    """The pin spacing (px) in a strip that holds a column of terminal blocks, pins repeating down the strip:
    the lag of the strongest autocorrelation peak near the expected spacing, followed out over several periods
    for precision. None when the strip shows no clear period there."""
    rows = lightness.astype(np.float64)
    n = rows.shape[0] - 1
    if n < 5 * expected:
        return None
    # narrow strips, each autocorrelated on its own and then averaged: the pins need not line up across the strip
    # (a view that is a little skewed), and strips without pins only add a flat floor. Rows outside the frame
    # are left out strip by strip.
    ac, weight = np.zeros(n), np.zeros(n)
    width = 12
    for x in range(0, rows.shape[1] - width + 1, width):
        inside = mask[:, x:x + width].min(1) > 0
        edges = np.flatnonzero(np.diff(np.concatenate(([0], inside.astype(np.int8), [0]))))
        if edges.size == 0:
            continue
        runs = edges.reshape(-1, 2)
        first, last = runs[np.argmax(runs[:, 1] - runs[:, 0])]
        if last - first < 5 * expected:
            continue
        profile = np.diff(rows[first:last, x:x + width].mean(1))
        p = profile - cv2.GaussianBlur(profile.reshape(-1, 1), (1, 0), expected).ravel()
        m = len(p)
        ac[:m] += np.correlate(p, p, "full")[m - 1:]
        weight[:m] += np.arange(m, 0, -1) / m                # fewer products at far lags
    if ac[0] <= 0:
        return None
    ac = ac / np.maximum(weight, 1e-9)
    ac = ac / (ac[0] + 1e-12)
    n = int(np.flatnonzero(weight > 0.2 * weight[0]).max()) + 1
    lo, hi = PLANE_SCALE[0] * expected, PLANE_SCALE[1] * expected
    best = None
    lag = int(round(lo)) + int(np.argmax(ac[int(round(lo)):int(round(hi)) + 1]))
    if ac[lag] < PERIOD_MIN:
        return None
    period = float(lag)
    if 0 < lag < n - 1:
        d = ac[lag - 1] - 2 * ac[lag] + ac[lag + 1]
        period = lag + (0.5 * (ac[lag - 1] - ac[lag + 1]) / d if d < 0 else 0.0)
    for m in range(2, 9):                                    # follow the peak outwards: the m-th peak is at m periods
        centre = int(round(period * m))
        if centre + 3 >= n - int(2 * expected):
            break
        j = centre - 3 + int(np.argmax(ac[centre - 3:centre + 4]))
        if ac[j] < PERIOD_MIN:
            break
        if 0 < j < n - 1:                                    # sub-pixel: a parabola through the peak
            d = ac[j - 1] - 2 * ac[j] + ac[j + 1]
            j = j + (0.5 * (ac[j - 1] - ac[j + 1]) / d if d < 0 else 0.0)
        period = j / m
    return period


def _pin_spacing(template: Template) -> float:
    """Median distance between neighbouring pins of the template's modules (template px)."""
    gaps = []
    for pins in template.modules.values():
        pts = np.array([[p["x"], p["y"]] for p in pins], float)
        if len(pts) > 1:
            gaps += list(np.linalg.norm(np.diff(pts, axis=0), axis=1))
    return float(np.median(gaps)) if gaps else 24.0


def _block_shifts(template: Template, small: np.ndarray, H: np.ndarray,
                  reference_indices: Optional[Dict[str, int]] = None, column: bool = True) -> Dict[str, Tuple[int, int]]:
    """Stage 2: each block's offset in template pixels, found in the frame warped into the template's view."""
    th, tw = template.gray.shape[:2]
    warped = cv2.warpPerspective(small, np.linalg.inv(H), (tw, th))
    gray = warped if warped.ndim == 2 else cv2.cvtColor(warped, cv2.COLOR_RGB2GRAY)
    boxes = _block_boxes(template)
    column_box = (min(b[0] for b in boxes.values()), min(b[1] for b in boxes.values()),
                  max(b[2] for b in boxes.values()), max(b[3] for b in boxes.values()))

    def match(box, cx, cy, r, references=()):
        x0, y0, x1, y1 = box
        patch = template.gray[y0:y1, x0:x1]
        X0, Y0 = max(0, x0 + cx - r), max(0, y0 + cy - r)
        region = gray[Y0:y1 + cy + r, X0:x1 + cx + r]
        if region.shape[0] < patch.shape[0] or region.shape[1] < patch.shape[1]:
            return (cx, cy), 0
        # Empty views of the same rigid housing at different angles/lighting. Wires are outside these crops;
        # the highest correlation chooses a view, never a wire colour or an annotated answer at runtime.
        index, best = max(enumerate(cv2.minMaxLoc(cv2.matchTemplate(region, ref, cv2.TM_CCOEFF_NORMED))
                                    for ref in [patch, *references]), key=lambda item: item[1][1])
        loc = best[3]
        return (X0 + loc[0] - x0, Y0 + loc[1] - y0), index

    if len(boxes) == 1 and template.block_references:
        shifts = {}
        for m, b in boxes.items():
            shifts[m], index = match(b, 0, 0, BLOCK_SEARCH, template.block_references.get(m, ()))
            if reference_indices is not None:
                reference_indices[m] = index
        return shifts

    if not column:                                       # stage 2a placed the column already: only the fine-tuning
        return {m: match(b, 0, 0, BLOCK_TIGHT)[0] for m, b in boxes.items()}
    # first all blocks together: the whole column of blocks has one shape, so it cannot slip by one pin unnoticed
    (cdx, cdy), _ = match(column_box, 0, 0, BLOCK_SEARCH)
    # then each block on its own, only a little around the common offset
    return {m: match(b, cdx, cdy, BLOCK_TIGHT)[0] for m, b in boxes.items()}
