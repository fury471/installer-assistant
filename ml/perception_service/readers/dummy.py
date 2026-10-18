"""A reader that does not look at the image. It exists so the app's live flow can be tested end to end
before any model works. Never report its numbers.

mode "correct": the bench is wired correctly (A4020-E on A1610 Reader 1): +→pin 2 red, −→pin 1 black,
                A→pin 3 green, B→pin 4 yellow; confident; positions in a row across the middle.
mode "unknown": nothing found, so the app asks to read again.
"""
from typing import List, Optional

import numpy as np

from ..schemas import NO_WIRE, CompareRequest, ReadItem, ReadRequest

RED, BLACK, GREEN, YELLOW = 0, 1, 2, 3
DEVICE_COLOURS = [RED, BLACK, GREEN, YELLOW]                       # +, −, A, B
CONTROLLER_COLOURS = [BLACK, RED, GREEN, YELLOW] + [NO_WIRE] * 4   # pins 1..8


class DummyReader:
    def __init__(self, mode: str = "correct"):
        self.mode = mode
        self.name = "dummy-" + mode

    def read(self, req: ReadRequest, image: np.ndarray) -> List[ReadItem]:
        n = len(req.terminals)
        if self.mode == "unknown":
            return [ReadItem() for _ in range(n)]
        colours = DEVICE_COLOURS if req.end == "device" else CONTROLLER_COLOURS
        items = []
        for i in range(n):
            x = 0.5 if n == 1 else 0.3 + 0.4 * i / (n - 1)
            c = colours[i] if i < len(colours) else NO_WIRE
            items.append(ReadItem(colour=c, confidence=0.93, x=x, y=0.5, found=True))
        return items

    def compare(self, req: CompareRequest, device: Optional[np.ndarray], controller: Optional[np.ndarray]) -> List[str]:
        if self.mode == "unknown":
            return ["unknown"] * req.pin_count
        return ["match"] * req.pin_count
