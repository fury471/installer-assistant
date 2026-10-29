"""IA_SAVE_FRAMES: every /read is saved (image bytes as sent, request without the image, answer, index line), and a
failing save never spoils the read."""
import base64
import csv
import json

from ml.perception_service import app as service
from ml.perception_service.frame_log import FrameLog
from ml.tests.test_contract import client, controller_body


def test_reads_are_saved_with_their_answer(tmp_path, monkeypatch):
    monkeypatch.setattr(service, "frame_log", FrameLog(str(tmp_path)))
    body = controller_body(frame_id=3)
    r = client.post("/read", json=body).json()
    images = list(tmp_path.glob("*.jpg"))
    assert len(images) == 1 and images[0].read_bytes() == base64.b64decode(body["image"]["data_b64"])
    rec = json.loads(images[0].with_suffix(".json").read_text(encoding="utf-8"))
    assert "data_b64" not in rec["request"]["image"] and rec["response"] == r and rec["decoded_size"] == [64, 48]
    rows = list(csv.DictReader(open(tmp_path / "index.csv", encoding="utf-8")))
    assert len(rows) == 1 and rows[0]["frame_id"] == "3" and rows[0]["pins"] == "8"


def test_a_failing_save_does_not_break_the_read(tmp_path, monkeypatch):
    log = FrameLog(str(tmp_path))
    log.folder = tmp_path / "missing" / "folder"          # writing there fails
    monkeypatch.setattr(service, "frame_log", log)
    r = client.post("/read", json=controller_body())
    assert r.status_code == 200 and len(r.json()["items"]) == 8


def test_off_by_default(monkeypatch):
    monkeypatch.delenv("IA_SAVE_FRAMES", raising=False)
    assert FrameLog.from_environment() is None
