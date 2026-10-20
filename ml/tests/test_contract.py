"""The service answers in the shape the Unity client expects (Detection/PerceptionClient.cs)."""
import base64
import io

import numpy as np
from fastapi.testclient import TestClient
from PIL import Image

from ml.perception_service import app as service
from ml.perception_service.readers import get_reader
from ml.perception_service.schemas import ReadRequest

client = TestClient(service.app)

READER_PINS = ["−", "12V", "A", "B", "O1", "O2", "IO7", "IO8"]
READER2_PINS = ["−", "12V", "A", "B", "O3", "O4", "IO9", "IO10"]
DOOR_PINS = ["−", "IO5", "−", "IO6"]


def jpeg_b64(w=64, h=48) -> str:
    buf = io.BytesIO()
    Image.fromarray(np.full((h, w, 3), 128, np.uint8)).save(buf, format="JPEG")
    return base64.b64encode(buf.getvalue()).decode()


def controller_body(frame_id=7):
    return {"frame_id": frame_id, "end": "controller", "module": "A1610.READER1", "terminals": READER_PINS,
            "other_modules": [{"module": "A1610.READER2", "terminals": READER2_PINS},
                              {"module": "A1610.DOOR1", "terminals": DOOR_PINS}],
            "image": {"width": 64, "height": 48, "format": "jpeg", "data_b64": jpeg_b64(), "intrinsics": []}}


def test_health():
    r = client.get("/health")
    assert r.status_code == 200 and r.json()["status"] == "ok"


def test_read_returns_one_item_per_terminal_in_order():
    r = client.post("/read", json=controller_body()).json()
    assert r["frame_id"] == 7 and r["error"] == "" and len(r["items"]) == 8
    for it in r["items"]:
        assert -1 <= it["colour"] <= 7 and 0 <= it["confidence"] <= 1 and 0 <= it["x"] <= 1 and 0 <= it["y"] <= 1


def test_read_surveys_the_other_modules_in_request_order():
    r = client.post("/read", json=controller_body()).json()
    blocks = r["other_blocks"]
    assert [b["module"] for b in blocks] == ["A1610.READER2", "A1610.DOOR1"]
    assert [len(b["items"]) for b in blocks] == [8, 4]


def test_device_end_has_no_survey():
    body = {"frame_id": 3, "end": "device", "module": "A4020E", "terminals": ["+", "−", "A", "B"],
            "image": {"data_b64": jpeg_b64()}}
    r = client.post("/read", json=body).json()
    assert len(r["items"]) == 4 and r["other_blocks"] == []


def test_read_without_image_reports_an_error():
    body = {"frame_id": 1, "end": "device", "module": "A4020E", "terminals": ["+", "−", "A", "B"], "image": {}}
    r = client.post("/read", json=body).json()
    assert r["error"] and len(r["items"]) == 4


def test_compare_one_opinion_per_pin():
    body = {"controller_module": "A1610.READER1", "device_module": "A4020E", "pin_count": 8, "pairs": [{"terminal": "+", "pin": 2}],
            "device_image": {"data_b64": jpeg_b64()}, "controller_image": {}}
    r = client.post("/compare", json=body).json()
    assert len(r["opinions"]) == 8 and set(r["opinions"]) <= {"match", "mismatch", "unknown"}


def test_dummy_elsewhere_puts_the_cable_in_the_first_other_module(monkeypatch):
    monkeypatch.setattr(service, "reader", get_reader("dummy-elsewhere"))
    r = client.post("/read", json=controller_body()).json()
    assert all(it["colour"] == -1 and it["found"] for it in r["items"])            # Reader 1: in view, empty
    reader2 = r["other_blocks"][0]
    assert reader2["located"] and sum(it["colour"] >= 0 for it in reader2["items"]) == 4


def test_a_broken_survey_skips_the_zone_check_but_keeps_the_reading(monkeypatch):
    class Broken:
        name = "broken"

        def read(self, req, image):
            return get_reader("dummy").read(req, image)

        def survey(self, req, image):
            raise RuntimeError("no template")

    monkeypatch.setattr(service, "reader", Broken())
    r = client.post("/read", json=controller_body()).json()
    assert r["error"] == "" and len(r["items"]) == 8 and r["other_blocks"] == []


def test_dummy_traces_each_wire_to_the_label(monkeypatch):
    monkeypatch.setattr(service, "reader", get_reader("dummy"))
    body = controller_body()
    body["label"] = "IA|C01|D1|A1610.READER1|A4020E"
    items = client.post("/read", json=body).json()["items"]
    assert [it["trace"] for it in items[:4]] == ["this_cable"] * 4 and items[4]["trace"] == "unknown"


def test_dummy_tangled_traces_pin_2_to_another_cable(monkeypatch):
    monkeypatch.setattr(service, "reader", get_reader("dummy-tangled"))
    body = controller_body()
    body["label"] = "IA|C01|D1|A1610.READER1|A4020E"
    items = client.post("/read", json=body).json()["items"]
    assert items[1]["trace"] == "other_cable" and items[0]["trace"] == "this_cable"


def test_without_a_label_nothing_is_traced(monkeypatch):
    monkeypatch.setattr(service, "reader", get_reader("dummy"))
    items = client.post("/read", json=controller_body()).json()["items"]
    assert all(it["trace"] == "unknown" for it in items)
