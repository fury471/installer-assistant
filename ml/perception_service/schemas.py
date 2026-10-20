"""JSON contract between the Unity app (Detection/PerceptionClient.cs) and this service.

Keep both sides identical. Field names are snake_case on the wire. Positions are normalised to the image:
x from 0 (left) to 1 (right), y from 0 (top) to 1 (bottom). Colour codes match WireColours in Unity:
0 Red, 1 Black, 2 Green, 3 Yellow, 4 White, 5 Brown, 6 Blue, 7 Orange, -1 no wire.
Modules are named as on the cable label: "A1610.READER1" (controller module) or "A4020E" (device).
"""
from typing import List, Literal

from pydantic import BaseModel, Field

COLOUR_NAMES = ["Red", "Black", "Green", "Yellow", "White", "Brown", "Blue", "Orange"]
NO_WIRE = -1


class ImagePayload(BaseModel):
    width: int = 0
    height: int = 0
    format: str = "jpeg"
    data_b64: str = ""            # empty = no image (Unity's JsonUtility cannot send null objects)
    intrinsics: List[float] = Field(default_factory=list)   # fx, fy, cx, cy in pixels, or empty

    @property
    def present(self) -> bool:
        return bool(self.data_b64)


class ModuleRequest(BaseModel):
    module: str                   # e.g. "A1610.READER2"
    terminals: List[str]          # its terminal names as printed, pin 1..n


class ReadRequest(BaseModel):
    frame_id: int = 0
    end: Literal["device", "controller"]
    module: str                   # the module the label names, e.g. "A1610.READER1" or "A4020E": selects the template
    terminals: List[str]          # names as printed, in the order the response must use
    other_modules: List[ModuleRequest] = Field(default_factory=list)   # controller only: survey these too (zone check)
    label: str = ""               # the scanned cable label without its end, e.g. "IA|C01|D1|A1610.READER1|A4020E":
                                  # each wire is traced back to it (bundle check); empty = do not trace
    image: ImagePayload


class ReadItem(BaseModel):
    colour: int = NO_WIRE
    confidence: float = 0.0       # calibrated probability that 'colour' is right
    x: float = 0.0
    y: float = 0.0
    found: bool = False           # False when the terminal could not be located
    trace: Literal["this_cable", "other_cable", "unknown"] = "unknown"   # where the wire runs back to (bundle check)


class ModuleItems(BaseModel):
    module: str
    located: bool = False         # False when the module was not found in the frame
    items: List[ReadItem] = Field(default_factory=list)   # one per terminal of that module, in request order


class ReadResponse(BaseModel):
    frame_id: int = 0
    items: List[ReadItem]
    other_blocks: List[ModuleItems] = Field(default_factory=list)   # one per requested other module, same order
    model: str = ""
    error: str = ""


class PairPayload(BaseModel):
    terminal: str
    pin: int


class CompareRequest(BaseModel):
    controller_module: str        # e.g. "A1610.READER2"
    device_module: str            # e.g. "A4020E"
    pin_count: int
    pairs: List[PairPayload]
    device_image: ImagePayload = Field(default_factory=ImagePayload)
    controller_image: ImagePayload = Field(default_factory=ImagePayload)


class CompareResponse(BaseModel):
    opinions: List[Literal["match", "mismatch", "unknown"]]   # index = pin - 1
    model: str = ""
    error: str = ""
