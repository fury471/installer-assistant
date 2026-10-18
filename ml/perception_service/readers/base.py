from typing import List, Optional, Protocol

import numpy as np

from ..schemas import CompareRequest, ReadItem, ReadRequest


class Reader(Protocol):
    """What every reader implements. 'image' is RGB uint8 (H, W, 3), row 0 at the top."""

    name: str

    def read(self, req: ReadRequest, image: np.ndarray) -> List[ReadItem]:
        """One item per name in req.terminals, in that order."""
        ...

    def compare(self, req: CompareRequest, device: Optional[np.ndarray], controller: Optional[np.ndarray]) -> List[str]:
        """Independent second opinion: "match" / "mismatch" / "unknown" per pin (index = pin - 1)."""
        ...
