"""Runs a reader over annotated photos and writes the accuracy report.

Annotations (data/annotations/<session>/*.json, one file per photo, written by ml/tools/annotate.py; photos stay on
the shared drive, see .gitignore):
{
  "image": "photos/ctrl_ab_swapped_01.jpg",    # relative to the data folder
  "end": "controller",                         # or "device"
  "module": "A1610.READER1",                   # the module the label names (device end: "A4020E")
  "label": "IA|C01|D1|A1610.READER1|A4020E",   # the scanned label, optional: wires are traced back to it
  "label_visible": true,
  "terminals": [                               # in the app's order: device +, −, A, B / controller pins 1..8
    {"name": "−", "colour": 1, "x": 0.412, "y": 0.533},
    {"name": "12V", "colour": 0, "x": 0.441, "y": 0.533, "cable": "other"},   # "cable": "this" | "other", optional
    ...
  ],
  "other_modules": [                           # controller only, optional: the other modules in the photo (zone survey)
    {"module": "A1610.READER2", "in_view": true, "terminals": [{"name": "−", "colour": -1}, ...]},
    ...
  ]
}
A wrong-module photo (the cable on Reader 2 while its label says Reader 1) has all "terminals" at -1 and the
cable's colours under A1610.READER2. A tangled photo has a second labelled bundle whose wire sits on a pin of this
module; its terminal gets "cable": "other".

    python -m ml.eval.evaluate --data data --annotations annotations/session1 --reader classical --out ml/runs/eval.json
--annotations is a folder under --data; every .json below it is used (so keep session 2 apart until the final test).
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
from ml.perception_service.schemas import ImagePayload, ModuleRequest, ReadRequest

from . import metrics


def load(path: str) -> dict:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="data")
    ap.add_argument("--annotations", default="annotations", help="folder under --data, e.g. annotations/session1")
    ap.add_argument("--reader", default="classical")
    ap.add_argument("--threshold", type=float, default=0.8)
    ap.add_argument("--out", default="ml/runs/eval.json")
    a = ap.parse_args()

    reader = get_reader(a.reader)
    true, pred, conf, ok, perr, latency = [], [], [], [], [], []
    occ_true, occ_pred = [], []      # zone survey: does each module hold wires (truth vs reader)
    trace_total, trace_known, trace_right = 0, 0, 0   # bundle check: annotated wires, traced (not unknown), traced right
    files = sorted(glob.glob(os.path.join(a.data, a.annotations, "**", "*.json"), recursive=True))
    for f in files:
        ann = load(f)
        img_path = os.path.join(a.data, ann["image"])
        with open(img_path, "rb") as fh:
            data = fh.read()
        with Image.open(img_path) as im:
            w, h = im.size
        payload = ImagePayload(width=w, height=h, data_b64=base64.b64encode(data).decode())
        terms = ann["terminals"]
        others = ann.get("other_modules", [])
        req = ReadRequest(end=ann["end"], module=ann["module"], terminals=[t["name"] for t in terms], image=payload,
                          label=ann.get("label", ""),
                          other_modules=[ModuleRequest(module=m["module"], terminals=[t["name"] for t in m["terminals"]]) for m in others])
        image = images.decode(payload)
        t0 = time.perf_counter()
        items = reader.read(req, image)
        survey = reader.survey(req, image) if others else []
        latency.append((time.perf_counter() - t0) * 1000)
        for t, it in zip(terms, items):
            true.append(t["colour"]); pred.append(it.colour); conf.append(it.confidence); ok.append(t["colour"] == it.colour)
            if it.found and "x" in t:
                perr += metrics.position_error([(it.x, it.y)], [(t["x"], t["y"])], [h / w])
            if t.get("cable") in ("this", "other") and t["colour"] != metrics.NO_WIRE:
                trace_total += 1
                if it.trace != "unknown":
                    trace_known += 1
                    trace_right += it.trace == ("this_cable" if t["cable"] == "this" else "other_cable")
        if others and len(survey) == len(others):
            occ_true.append(metrics.holds_wires([t["colour"] for t in terms]))
            occ_pred.append(metrics.holds_wires([it.colour for it in items]))
            for m, got in zip(others, survey):
                occ_true.append(m.get("in_view", True) and metrics.holds_wires([t["colour"] for t in m["terminals"]]))
                occ_pred.append(got.located and metrics.holds_wires([it.colour for it in got.items]))

    report = {
        "reader": reader.name, "photos": len(files), "terminals": len(true),
        "colour_accuracy": metrics.accuracy(true, pred),
        "ece": metrics.ece(conf, ok),
        "selective": metrics.selective(conf, ok, a.threshold),
        "position_error_median": float(sorted(perr)[len(perr) // 2]) if perr else None,
        "module_occupancy_accuracy": metrics.accuracy(occ_true, occ_pred) if occ_true else None,
        "modules_surveyed": len(occ_true),
        "trace_coverage": trace_known / trace_total if trace_total else None,     # share of wires traced (not unknown)
        "trace_accuracy": trace_right / trace_known if trace_known else None,     # right among the traced ones
        "wires_with_cable_annotated": trace_total,
        "latency_ms_median": float(sorted(latency)[len(latency) // 2]) if latency else None,
        "confusion": metrics.confusion(true, pred).tolist(),
    }
    os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    print(json.dumps({k: v for k, v in report.items() if k != "confusion"}, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
