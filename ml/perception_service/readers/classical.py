"""Classical reader (package A): find the device, cut a patch where each wire leaves its pin, read the patch.

  1. locate.py: the template's pins mapped into the frame (ORB or LightGlue features + MAGSAC++ homography; on the
     board each terminal block found again by normalised cross-correlation). Once per frame and device: the zone
     survey asks about the other modules of the same frame and gets the same result.
  2. patches.py: the frame warped into the template's view; a window per pin where its wire leaves, in the same
     canonical shape for every device, and the same window from the device's empty reference photo.
  3. pin_reader.py: one general reader for every device says no wire / red / black / green / yellow, with a
     temperature-scaled confidence (ml/eval/train_pins.py): wire or not from how the window differs from the empty
     reference, the colour from the wire's own pixels. If ml/models/pins.joblib is missing, the old baseline
     (grey-world white balance, nearest CIELAB reference colour at the pin; colour.py) is used and a warning printed.
  4. Bundle check: tracing.py follows each wire back to the scanned label: "this_cable", "other_cable" or "unknown".
"""
import json
import os
from typing import List, Optional

import cv2
import numpy as np

from ..colour import classify_patch, grey_world
from ..locate import TEMPLATES, Template, locate as find_pins
from ..patches import pin_patches, reference_patches
from ..pin_reader import SLACK, PinReader, compare
from ..schemas import CompareRequest, ModuleItems, ReadItem, ReadRequest
from ..tracing import trace_items


# Below this sharpness a module's pins are not read: the frame is blurred (the phone moved). Variance of the Laplacian
# (a standard focus measure; Pertuz et al., 2013) of the module's area scaled to 200 px. Per device ("sharp_min" in
# the template JSON), because a smooth housing scores lower: board 450 (live frames of 1 Nov: the smeared frame 411,
# sharp frames mostly 700-2400; session-1 stills >= 1172), reader 150 (sharp session-1 stills 296-504).
# IA_SHARP_SCALE multiplies every threshold (0 switches the check off). The app then says "No wires found".
SHARP_SCALE = float(os.environ.get("IA_SHARP_SCALE", "1"))


def sharpness(image: np.ndarray, positions) -> float:
    """Focus measure of the area around the given pins (normalised positions)."""
    h, w = image.shape[:2]
    pts = np.array([(x * w, y * h) for x, y in positions])
    x0, y0 = np.maximum(pts.min(0) - 40, 0).astype(int)
    x1, y1 = np.minimum(pts.max(0) + 40, [w, h]).astype(int)
    patch = image[y0:y1, x0:x1]
    if patch.size == 0:
        return 0.0
    gray = cv2.cvtColor(patch, cv2.COLOR_RGB2GRAY) if patch.ndim == 3 else patch
    k = 200.0 / max(gray.shape)
    gray = cv2.resize(gray, None, fx=k, fy=k, interpolation=cv2.INTER_AREA)
    return float(cv2.Laplacian(gray, cv2.CV_64F).var())


class ClassicalReader:
    name = "classical-3"

    def __init__(self):
        self.templates = {"A1610": Template.load("A1610"), "A4020E": Template.load("A4020E")}
        self._last = None              # (frame, device, result): search once per frame, not once per module
        self._patches = None           # (frame, device, {module: patches}): cut once per frame as well
        self.sharp_min = {n: float(json.loads((TEMPLATES / (n + ".json")).read_text(encoding="utf-8")).get("sharp_min", 0))
                          for n in self.templates}
        self.pin_reader = PinReader() if PinReader.available() else None
        if self.pin_reader is None:
            print("No trained wire reader (ml/models/pins.joblib; python -m ml.eval.train_pins): "
                  "using the colour baseline.")

    def locate(self, module: str, terminals: List[str], image: np.ndarray) -> List[Optional[tuple]]:
        device = module.split(".")[0]                  # "A1610.READER1" -> "A1610"; "A4020E" -> "A4020E"
        tpl = self.templates.get(device)
        if tpl is None or module not in tpl.modules:
            return [None] * len(terminals)
        if self._last is None or self._last[0] is not image or self._last[1] != device:
            res = find_pins(tpl, image, refine_blocks=(device == "A1610"))   # RGB: LightGlue uses colour
            self._last = (image, device, res)
        res = self._last[2]
        if not res.found:
            return [None] * len(terminals)
        if module in res.doubtful:                     # placed, but it does not look like this module: say so
            print("%s not trusted: %s" % (module, res.doubtful[module]))
            return [None] * len(terminals)
        if len(res.pins[module]) != len(terminals):
            return [None] * len(terminals)
        h, w = image.shape[:2]
        return [(x / w, y / h) for (x, y) in res.pins[module]]

    def read(self, req: ReadRequest, image: np.ndarray) -> List[ReadItem]:
        items = self._read_module(req.module, req.terminals, image)
        if req.label:
            points = [(it.x, it.y) if it.found and it.colour >= 0 else None for it in items]
            for it, trace in zip(items, trace_items(image, points, req.label)):
                it.trace = trace
        return items

    def survey(self, req: ReadRequest, image: np.ndarray) -> List[ModuleItems]:
        out = []
        for m in req.other_modules:
            items = self._read_module(m.module, m.terminals, image)
            out.append(ModuleItems(module=m.module, located=any(it.found for it in items), items=items))
        return out

    def _read_module(self, module: str, terminals: List[str], image: np.ndarray) -> List[ReadItem]:
        positions = self.locate(module, terminals, image)
        device = module.split(".")[0]
        if all(p is not None for p in positions):
            sharp, need = sharpness(image, positions), self.sharp_min.get(device, 0.0) * SHARP_SCALE
            if sharp < need:
                print("%s blurred (sharpness %.0f < %.0f): not read" % (module, sharp, need))
                return [ReadItem() for _ in terminals]
        if self.pin_reader is not None and all(p is not None for p in positions):
            tpl = self.templates[device]
            if self._patches is None or self._patches[0] is not image or self._patches[1] != device:
                windows = pin_patches(tpl, self._last[2], image)
                self._patches = (image, device, windows, closest_variant(tpl, windows))
            refs = reference_patches(self._patches[3], SLACK)[module]
            reads = self.pin_reader.read(self._patches[2][module], refs)
            return [ReadItem(colour=c, confidence=conf, x=x, y=y, found=True)
                    for (c, conf), (x, y) in zip(reads, positions)]
        return self._read_module_baseline(positions, image)

    def _read_module_baseline(self, positions, image: np.ndarray) -> List[ReadItem]:
        h, w = image.shape[:2]
        balanced = grey_world(image)
        items = []
        for pos in positions:
            if pos is None:
                items.append(ReadItem())
                continue
            x, y = pos
            r = max(4, int(0.015 * max(w, h)))
            cx, cy = int(x * w), int(y * h)
            patch = balanced[max(0, cy - r):cy + r, max(0, cx - r):cx + r]
            colour, conf, _ = classify_patch(patch)
            items.append(ReadItem(colour=colour, confidence=conf, x=x, y=y, found=True))
        return items

    def compare(self, req: CompareRequest, device: Optional[np.ndarray], controller: Optional[np.ndarray]) -> List[str]:
        # The second opinion is a separate model (a VLM call), not this reader: see the plan doc.
        return ["unknown"] * req.pin_count


def closest_variant(tpl: Template, windows) -> Template:
    """The board variant whose empty pins look most like this frame's pins (most pins are empty): the median, over
    every pin of every module, of the median colour difference to that variant's empty window. Chosen apart from
    locate(), which may fit the board with another variant's photo (bench, 1 Nov: an A1601 frame matched with the
    A1610 photo read all eight empty pins as wires)."""
    if not tpl.variants:
        return tpl
    best, best_score = tpl, float("inf")
    for t in [tpl] + tpl.variants:
        refs = reference_patches(t, SLACK)
        score = float(np.median([np.median(compare(w, r)[0]) for m, ws in windows.items() for w, r in zip(ws, refs[m])]))
        if score < best_score:
            best, best_score = t, score
    return best
