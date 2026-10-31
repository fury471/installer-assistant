"""Classical reader (package A): find the device, cut a patch where each wire leaves its pin, read the patch.

  1. locate.py: the template's pins mapped into the frame (ORB or LightGlue features + MAGSAC++ homography; on the
     board each terminal block found again by normalised cross-correlation). Once per frame and device: the zone
     survey asks about the other modules of the same frame and gets the same result.
  2. patches.py: the frame warped into the template's view; a fixed window per pin where its wire leaves.
  3. pin_reader.py: a trained classifier says no wire / red / black / green / yellow, with a temperature-scaled
     confidence (ml/eval/train_pins.py). If no trained model is in ml/models/, the old baseline (grey-world white
     balance, nearest CIELAB reference colour at the pin; colour.py) is used instead and a warning is printed.
  4. Bundle check: tracing.py follows each wire back to the scanned label: "this_cable", "other_cable" or "unknown".
"""
from typing import List, Optional

import numpy as np

from ..colour import classify_patch, grey_world
from ..locate import Template, locate as find_pins
from ..patches import pin_patches
from ..pin_reader import PinReader
from ..schemas import CompareRequest, ModuleItems, ReadItem, ReadRequest
from ..tracing import trace_items


class ClassicalReader:
    name = "classical-2"

    def __init__(self):
        self.templates = {"A1610": Template.load("A1610"), "A4020E": Template.load("A4020E")}
        self._last = None              # (frame, device, result): search once per frame, not once per module
        self._patches = None           # (frame, device, {module: patches}): cut once per frame as well
        self.pin_readers = {t: PinReader(t) for t in self.templates if PinReader.available(t)}
        missing = [t for t in self.templates if t not in self.pin_readers]
        if missing:
            print("No trained pin reader for %s (python -m ml.eval.train_pins): using the colour baseline there."
                  % ", ".join(missing))

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
        if device in self.pin_readers and all(p is not None for p in positions):
            if self._patches is None or self._patches[0] is not image or self._patches[1] != device:
                self._patches = (image, device, pin_patches(self.templates[device], self._last[2], image))
            reads = self.pin_readers[device].read(self._patches[2][module])
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
