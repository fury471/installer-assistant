"""A reader that does not look at the image. It exists so the app's live flow can be tested end to end
before any model works. Never report its numbers.

mode "correct":   the bench is wired correctly in the module the label names (A4020-E on an A1610 reader port):
                  +→pin 2 red, −→pin 1 black, A→pin 3 green, B→pin 4 yellow; confident; positions in a row across
                  the middle. The other modules are in view and empty.
mode "elsewhere": the cable sits in the first other module instead (e.g. Reader 2 when the label says Reader 1);
                  the named module is in view and empty. Shows the zone check ("move the cable to Reader 1").
mode "tangled":   like "correct", but the wire on controller pin 2 (12V) traces to another cable's label. Shows the
                  bundle check ("the wire on 12V is from another cable").
mode "unknown":   nothing found, so the app asks to read again.
With a label in the request, every wire is traced to it ("this_cable"), except in mode "tangled" as above.
"""
from typing import List, Optional

import numpy as np

from ..schemas import NO_WIRE, CompareRequest, ModuleItems, ReadItem, ReadRequest

RED, BLACK, GREEN, YELLOW = 0, 1, 2, 3
DEVICE_COLOURS = [RED, BLACK, GREEN, YELLOW]                       # +, −, A, B
CONTROLLER_COLOURS = [BLACK, RED, GREEN, YELLOW] + [NO_WIRE] * 4   # pins 1..8


def _row(n: int, y: float, colours: List[int], confidence: float = 0.93) -> List[ReadItem]:
    items = []
    for i in range(n):
        x = 0.5 if n == 1 else 0.3 + 0.4 * i / (n - 1)
        c = colours[i] if i < len(colours) else NO_WIRE
        items.append(ReadItem(colour=c, confidence=confidence, x=x, y=y, found=True))
    return items


class DummyReader:
    def __init__(self, mode: str = "correct"):
        self.mode = mode
        self.name = "dummy-" + mode

    def read(self, req: ReadRequest, image: np.ndarray) -> List[ReadItem]:
        n = len(req.terminals)
        if self.mode == "unknown":
            return [ReadItem() for _ in range(n)]
        if req.end == "device":
            items = _row(n, 0.5, DEVICE_COLOURS)
        elif self.mode == "elsewhere" and req.other_modules:
            items = _row(n, 0.5, [])                                   # in view, empty
        else:
            items = _row(n, 0.5, CONTROLLER_COLOURS)
        if req.label:
            for i, it in enumerate(items):
                if it.colour >= 0:
                    tangled = self.mode == "tangled" and req.end == "controller" and i == 1
                    it.trace = "other_cable" if tangled else "this_cable"
        return items

    def survey(self, req: ReadRequest, image: np.ndarray) -> List[ModuleItems]:
        out = []
        for k, m in enumerate(req.other_modules):
            if self.mode == "unknown":
                out.append(ModuleItems(module=m.module, located=False, items=[ReadItem() for _ in m.terminals]))
                continue
            colours = CONTROLLER_COLOURS if self.mode == "elsewhere" and k == 0 else []
            out.append(ModuleItems(module=m.module, located=True, items=_row(len(m.terminals), 0.62 + 0.1 * k, colours)))
        return out

    def compare(self, req: CompareRequest, device: Optional[np.ndarray], controller: Optional[np.ndarray]) -> List[str]:
        if self.mode == "unknown":
            return ["unknown"] * req.pin_count
        return ["match"] * req.pin_count
