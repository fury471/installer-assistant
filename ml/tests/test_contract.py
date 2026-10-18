"""The service answers in the shape the Unity client expects (Detection/PerceptionClient.cs)."""
import base64
import io

import numpy as np
from fastapi.testclient import TestClient
from PIL import Image

from ml.perception_service.app import app

client = TestClient(app)


def jpeg_b64(w=64, h=48) -> str:
    buf = io.BytesIO()
    Image.fromarray(np.full((h, w, 3), 128, np.uint8)).save(buf, format="JPEG")
    return base64.b64encode(buf.getvalue()).decode()


def test_health():
    r = client.get("/health")
    assert r.status_code == 200 and r.json()["status"] == "ok"


def test_read_returns_one_item_per_terminal_in_order():
    body = {"frame_id": 7, "end": "controller", "usage": "READER",
            "terminals": ["−", "12V", "A", "B", "O1", "O2", "IO7", "IO8"],
            "image": {"width": 64, "height": 48, "format": "jpeg", "data_b64": jpeg_b64(), "intrinsics": []}}
    r = client.post("/read", json=body).json()
    assert r["frame_id"] == 7 and r["error"] == "" and len(r["items"]) == 8
    for it in r["items"]:
        assert -1 <= it["colour"] <= 7 and 0 <= it["confidence"] <= 1 and 0 <= it["x"] <= 1 and 0 <= it["y"] <= 1


def test_read_without_image_reports_an_error():
    body = {"frame_id": 1, "end": "device", "usage": "READER", "terminals": ["+", "−", "A", "B"], "image": {}}
    r = client.post("/read", json=body).json()
    assert r["error"] and len(r["items"]) == 4


def test_compare_one_opinion_per_pin():
    body = {"usage": "READER", "pin_count": 8, "pairs": [{"terminal": "+", "pin": 2}],
            "device_image": {"data_b64": jpeg_b64()}, "controller_image": {}}
    r = client.post("/compare", json=body).json()
    assert len(r["opinions"]) == 8 and set(r["opinions"]) <= {"match", "mismatch", "unknown"}
