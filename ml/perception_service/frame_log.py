"""Save the frames the app sends, with what it asked and what the service answered (bench days, live checks).

Switched on by the environment variable IA_SAVE_FRAMES = a folder, e.g. (PowerShell)
    $env:IA_SAVE_FRAMES = "data/live/2026-10-31"
    python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 8000
Off when it is not set (the default). data/ is never committed, so the frames stay out of Git.

For every /read it writes, in that folder:
    <time>_<end>_<module>_f<frame id>.jpg    the image exactly as the phone sent it (the JPEG bytes, not re-encoded)
    <same name>.json                          the request without the image, the answer, the time taken
    index.csv                                 one line per frame, for a quick overview in a spreadsheet
These are the live frames for the report: what the app's camera really gives (size, blur, light), how often the
board is found live, and how long a read takes. ml/tools/review_frames.py draws the answers onto them.
Saving never breaks a read: if it fails, the service prints why and goes on.
"""
import base64
import csv
import json
import os
import threading
from datetime import datetime
from pathlib import Path
from typing import Optional

INDEX_FIELDS = ["file", "time", "frame_id", "end", "module", "label", "width", "height", "pins_found", "pins",
                "wired", "other_located", "ms", "model", "error"]
EXTENSIONS = {"jpeg": ".jpg", "jpg": ".jpg", "png": ".png"}


class FrameLog:
    def __init__(self, folder: str):
        self.folder = Path(folder)
        self.folder.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()          # uvicorn may answer two requests at once
        print(f"saving every frame the app sends to {self.folder.resolve()}")

    @staticmethod
    def from_environment() -> Optional["FrameLog"]:
        folder = os.environ.get("IA_SAVE_FRAMES", "").strip()
        return FrameLog(folder) if folder else None

    def save(self, req, resp, ms: float, size=(0, 0)) -> Optional[Path]:
        """Store one /read: req is the ReadRequest, resp the ReadResponse, size the decoded (width, height)."""
        try:
            now = datetime.now()
            module = req.module.replace("|", "_").replace("/", "_").replace("\\", "_")
            stem = f"{now:%Y%m%d-%H%M%S-%f}"[:-3] + f"_{req.end}_{module}_f{req.frame_id}"
            ext = EXTENSIONS.get(req.image.format.lower(), ".bin")
            image_file = self.folder / (stem + ext)
            if req.image.present:
                image_file.write_bytes(base64.b64decode(req.image.data_b64))
            request = req.model_dump()
            request["image"].pop("data_b64", None)
            answer = resp.model_dump()
            record = {"saved": now.isoformat(timespec="milliseconds"), "ms": round(ms, 1),
                      "decoded_size": list(size), "request": request, "response": answer,
                      "image_file": image_file.name if req.image.present else ""}
            (self.folder / (stem + ".json")).write_text(json.dumps(record, ensure_ascii=False, indent=1),
                                                        encoding="utf-8")
            items = answer["items"]
            row = {"file": image_file.name if req.image.present else "", "time": record["saved"],
                   "frame_id": req.frame_id, "end": req.end, "module": req.module, "label": req.label,
                   "width": size[0], "height": size[1], "pins_found": sum(i["found"] for i in items),
                   "pins": len(items), "wired": sum(i["found"] and i["colour"] >= 0 for i in items),
                   "other_located": sum(b["located"] for b in answer.get("other_blocks", [])),
                   "ms": round(ms), "model": answer.get("model", ""), "error": answer.get("error", "")}
            with self._lock:
                index = self.folder / "index.csv"
                new = not index.exists()
                with open(index, "a", newline="", encoding="utf-8") as f:
                    w = csv.DictWriter(f, fieldnames=INDEX_FIELDS)
                    if new:
                        w.writeheader()
                    w.writerow(row)
            return image_file
        except Exception as e:                  # never let saving spoil the read
            print(f"frame not saved: {e!r}")
            return None
