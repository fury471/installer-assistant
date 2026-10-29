"""Classical reader (baseline, package A): locate the module, crop beside each terminal, classify colour.

Plan (see docs/work-split-plan.md, Perception):
  1. Locate the module: ORB features + RANSAC homography to a reference photo of it
     (templates/<module>.jpg with hand-placed terminal points in templates/<module>.json, module as on the label,
     e.g. A1610.READER1.jpg, A4020E.jpg). Reader 1 and Reader 2 have the same shape, so a template of one block
     matches both: tell them apart by their place on the board (a template of the whole board with every module's
     points gives all modules from one homography) and by the housing colour (Reader 1 blue, Reader 2 green).
  2. Map the template's terminal points into the image with the homography → positions.
  3. Crop a small patch where each wire enters its terminal; grey-world white balance; nearest CIELAB colour.
  4. "No wire" when the patch looks like the empty terminal (compare with the template patch).
  5. Confidence: temperature-scaled softmax (colour.classify_patch), fitted on held-out photos.
  6. Zone survey: steps 1-4 for every other module the request lists (other_modules); a module that is not found
     is reported with located = False.
  7. Bundle check: follow each wire back to the scanned label (tracing.py: QR labels in the frame, colour region
     growing from the terminal); "this_cable", "other_cable" or "unknown".
Steps 1-2 are TODO; 3 is implemented in colour.py.
"""
from typing import List, Optional

import cv2
import numpy as np

from ..colour import classify_patch, grey_world
from ..locate import Template, locate as find_pins
from ..schemas import CompareRequest, ModuleItems, ReadItem, ReadRequest
from ..tracing import trace_items


class ClassicalReader:
    name = "classical-1"

    def __init__(self):
        self.templates = {"A1610": Template.load("A1610"), "A4020E": Template.load("A4020E")}
        self._last = None              # (frame, device, result): search once per frame, not once per module

    def locate(self, module: str, terminals: List[str], image: np.ndarray) -> List[Optional[tuple]]:
        device = module.split(".")[0]                  # "A1610.READER1" -> "A1610"; "A4020E" -> "A4020E"
        tpl = self.templates.get(device)
        if tpl is None or module not in tpl.modules:
            return [None] * len(terminals)
        if self._last is None or self._last[0] is not image or self._last[1] != device:
            gray = cv2.cvtColor(image, cv2.COLOR_RGB2GRAY)
            res = find_pins(tpl, gray, refine_blocks=(device == "A1610"))
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
        h, w = image.shape[:2]
        balanced = grey_world(image)
        items = []
        for pos in self.locate(module, terminals, image):
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
