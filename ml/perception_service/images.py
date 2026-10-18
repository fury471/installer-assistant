"""Decoding the images the app sends."""
import base64
import io

import numpy as np
from PIL import Image

from .schemas import ImagePayload


def decode(payload: ImagePayload) -> np.ndarray:
    """Returns the image as an RGB uint8 array (H, W, 3), row 0 at the top."""
    data = base64.b64decode(payload.data_b64)
    with Image.open(io.BytesIO(data)) as im:
        return np.asarray(im.convert("RGB"))
