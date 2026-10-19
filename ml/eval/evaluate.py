"""Runs a reader over annotated photos and writes the accuracy report.

Annotations (data/annotations/*.json, one file per photo; photos stay on the shared drive, see .gitignore):
{
  "image": "photos/ctrl_ab_swapped_01.jpg",    # relative to the data folder
  "end": "controller",                         # or "device"
  "usage": "READER",
  "label_visible": true,
  "terminals": [                               # in the app's order: device +, −, A, B / controller pins 1..8
    {"name": "−", "colour": 1, "x": 0.412, "y": 0.533},
    {"name": "12V", "colour": 0, "x": 0.441, "y": 0.533},
    ...
  ]
}

    python -m ml.eval.evaluate --data data --reader classical --threshold 0.8 --out ml/runs/eval.json
"""
import argparse
import base64
import glob
import json
import os
import time

from PIL import Image

from ml.perception_service import images
from ml.perception_service.readers import get_reader
from ml.perception_service.schemas import ImagePayload, ReadRequest

from . import metrics


def load(path: str) -> dict:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="data")
    ap.add_argument("--reader", default="classical")
    ap.add_argument("--threshold", type=float, default=0.8)
    ap.add_argument("--out", default="ml/runs/eval.json")
    a = ap.parse_args()

    reader = get_reader(a.reader)
    true, pred, conf, ok, perr, latency = [], [], [], [], [], []
    files = sorted(glob.glob(os.path.join(a.data, "annotations", "*.json")))
    for f in files:
        ann = load(f)
        img_path = os.path.join(a.data, ann["image"])
        with open(img_path, "rb") as fh:
            data = fh.read()
        with Image.open(img_path) as im:
            w, h = im.size
        payload = ImagePayload(width=w, height=h, data_b64=base64.b64encode(data).decode())
        terms = ann["terminals"]
        req = ReadRequest(end=ann["end"], usage=ann["usage"], terminals=[t["name"] for t in terms], image=payload)
        t0 = time.perf_counter()
        items = reader.read(req, images.decode(payload))
        latency.append((time.perf_counter() - t0) * 1000)
        for t, it in zip(terms, items):
            true.append(t["colour"]); pred.append(it.colour); conf.append(it.confidence); ok.append(t["colour"] == it.colour)
            if it.found and "x" in t:
                perr += metrics.position_error([(it.x, it.y)], [(t["x"], t["y"])], [h / w])

    report = {
        "reader": reader.name, "photos": len(files), "terminals": len(true),
        "colour_accuracy": metrics.accuracy(true, pred),
        "ece": metrics.ece(conf, ok),
        "selective": metrics.selective(conf, ok, a.threshold),
        "position_error_median": float(sorted(perr)[len(perr) // 2]) if perr else None,
        "latency_ms_median": float(sorted(latency)[len(latency) // 2]) if latency else None,
        "confusion": metrics.confusion(true, pred).tolist(),
    }
    os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    print(json.dumps({k: v for k, v in report.items() if k != "confusion"}, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
