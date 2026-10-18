"""Classical reader (baseline, package A): locate the connector, crop beside each terminal, classify colour.

Plan (see the plan doc, Perception):
  1. Locate the terminal block: ORB features + RANSAC homography to a reference photo of the connector
     (templates/<usage>_<end>.jpg with hand-placed terminal points in templates/<usage>_<end>.json).
  2. Map the template's terminal points into the image with the homography → positions.
  3. Crop a small patch where each wire enters its terminal; grey-world white balance; nearest CIELAB colour.
  4. "No wire" when the patch looks like the empty terminal (compare with the template patch).
  5. Confidence: temperature-scaled softmax (colour.classify_patch), fitted on held-out photos.
Steps 1-2 are TODO; 3 is implemented in colour.py.
"""
from typing import List, Optional

import numpy as np

from ..colour import classify_patch, grey_world
from ..schemas import CompareRequest, ReadItem, ReadRequest


class ClassicalReader:
    name = "classical-0"

    def locate(self, req: ReadRequest, image: np.ndarray) -> List[Optional[tuple]]:
        """Terminal positions (x, y normalised) or None per terminal. TODO: template + homography."""
        return [None] * len(req.terminals)

    def read(self, req: ReadRequest, image: np.ndarray) -> List[ReadItem]:
        h, w = image.shape[:2]
        balanced = grey_world(image)
        items = []
        for pos in self.locate(req, image):
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
