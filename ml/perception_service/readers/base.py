from typing import List, Optional, Protocol

import numpy as np

from ..schemas import CompareRequest, ModuleItems, ReadItem, ReadRequest


class Reader(Protocol):
    """What every reader implements. 'image' is RGB uint8 (H, W, 3), row 0 at the top."""

    name: str

    def read(self, req: ReadRequest, image: np.ndarray) -> List[ReadItem]:
        """One item per name in req.terminals, in that order (the module the label names, req.module).
        With req.label set, also say for each wire where it runs back to (item.trace, see tracing.py)."""
        ...

    def survey(self, req: ReadRequest, image: np.ndarray) -> List[ModuleItems]:
        """Zone survey: one entry per req.other_modules, in that order: is the module in the frame, and which of
        its pins hold wires. Return [] to skip the zone check (the app then trusts that the cable is in req.module)."""
        ...

    def compare(self, req: CompareRequest, device: Optional[np.ndarray], controller: Optional[np.ndarray]) -> List[str]:
        """Independent second opinion: "match" / "mismatch" / "unknown" per pin (index = pin - 1)."""
        ...
