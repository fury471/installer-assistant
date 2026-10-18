"""JSON contract between the Unity app (Detection/PerceptionClient.cs) and this service.

Keep both sides identical. Field names are snake_case on the wire. Positions are normalised to the image:
x from 0 (left) to 1 (right), y from 0 (top) to 1 (bottom). Colour codes match WireColours in Unity:
0 Red, 1 Black, 2 Green, 3 Yellow, 4 White, 5 Brown, 6 Blue, 7 Orange, -1 no wire.
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


class ReadRequest(BaseModel):
    frame_id: int = 0
    end: Literal["device", "controller"]
    usage: str                    # label usage, e.g. "READER": selects the connector template
    terminals: List[str]          # names as printed, in the order the response must use
    image: ImagePayload


class ReadItem(BaseModel):
    colour: int = NO_WIRE
    confidence: float = 0.0       # calibrated probability that 'colour' is right
    x: float = 0.0
    y: float = 0.0
    found: bool = False           # False when the terminal could not be located


class ReadResponse(BaseModel):
    frame_id: int = 0
    items: List[ReadItem]
    model: str = ""
    error: str = ""


class PairPayload(BaseModel):
    terminal: str
    pin: int


class CompareRequest(BaseModel):
    usage: str
    pin_count: int
    pairs: List[PairPayload]
    device_image: ImagePayload = Field(default_factory=ImagePayload)
    controller_image: ImagePayload = Field(default_factory=ImagePayload)


class CompareResponse(BaseModel):
    opinions: List[Literal["match", "mismatch", "unknown"]]   # index = pin - 1
    model: str = ""
    error: str = ""
