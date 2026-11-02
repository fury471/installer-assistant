"""Rebuild A4020-E's local housing references from four manually annotated EMPTY views.

    python -m ml.tools.calibrate_reader_block --session session3 --row R01

Only the all-empty calibration row is used; wired rows remain evaluation data. The housing is translated into
the existing template's pin coordinates, without fitting a colour model or changing terminal identities.
"""
import argparse
import hashlib
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

from ml.perception_service.locate import TEMPLATES, Template, _block_boxes, locate
from ml.perception_service.patches import cut, geometry
from ml.perception_service.pin_reader import SLACK


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--data", type=Path, default=Path("data"))
    ap.add_argument("--session", default="session3")
    ap.add_argument("--row", default="R01")
    ap.add_argument("--out", type=Path, default=TEMPLATES)
    args = ap.parse_args()
    tpl = Template.load("A4020E")
    pins = np.float32([[p["x"], p["y"]] for p in tpl.modules["A4020E"]])
    names = [p["name"] for p in tpl.modules["A4020E"]]
    x0, y0, x1, y1 = _block_boxes(tpl)["A4020E"]
    pending, wire_pending, sources = {}, {}, []
    for path in sorted((args.data / "annotations" / args.session).glob("*.json")):
        ann = json.loads(path.read_text(encoding="utf-8"))
        if ann.get("row") != args.row or ann.get("module") != "A4020E":
            continue
        if [t["name"] for t in ann["terminals"]] != names or any(t["colour"] != -1 for t in ann["terminals"]):
            raise ValueError(f"{path}: calibration requires four correctly ordered, empty slots")
        rgb = np.array(Image.open(args.data / ann["image"]).convert("RGB"))
        scale = min(1.0, 1920 / max(rgb.shape[:2]))
        if scale < 1:
            rgb = cv2.resize(rgb, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
        result = locate(tpl, rgb, refine_blocks=False)
        if not result.found:
            raise ValueError(f"{path}: board not located")
        truth = np.float32([[t["x"] * rgb.shape[1], t["y"] * rgb.shape[0]] for t in ann["terminals"]])
        canonical = cv2.perspectiveTransform(truth.reshape(-1, 1, 2), np.linalg.inv(result.homography)).reshape(-1, 2)
        shift = np.median(canonical - pins, axis=0)
        # Decode the rectified RGB through PNG's grayscale conversion, matching the original calibration export.
        warped = cv2.warpPerspective(rgb, np.linalg.inv(result.homography), (tpl.gray.shape[1], tpl.gray.shape[0]))
        encoded = cv2.imencode(".png", cv2.cvtColor(warped, cv2.COLOR_RGB2BGR))[1]
        gray = cv2.imdecode(encoded, cv2.IMREAD_GRAYSCALE)
        aligned = cv2.warpAffine(gray, np.float32([[1, 0, -shift[0]], [0, 1, -shift[1]]]),
                                (tpl.gray.shape[1], tpl.gray.shape[0]))
        suffix = (ann["light"], ann["view"])
        if suffix in pending:
            raise ValueError(f"Duplicate calibration view: {suffix}")
        filename = f"A4020E_block_{suffix[0]}_{suffix[1]}.png"
        pending[suffix] = (filename, aligned[y0:y1, x0:x1])
        aligned_rgb = cv2.warpAffine(warped, np.float32([[1, 0, -shift[0]], [0, 1, -shift[1]]]),
                                    (tpl.gray.shape[1], tpl.gray.shape[0]))
        spacing, direction = geometry(tpl.name)
        wire_filename = f"A4020E_empty_{suffix[0]}_{suffix[1]}.png"
        wire_pending[suffix] = (wire_filename, np.vstack([cut(aligned_rgb, p["x"], p["y"], spacing, direction, SLACK)
                                                        for p in tpl.modules["A4020E"]]))
        sources.append({"annotation": path.as_posix(), "annotation_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                        "reference": filename, "wire_reference": wire_filename, "translation": shift.tolist()})
    if set(pending) != {(light, view) for light in ("room", "flash") for view in ("front", "tilt")}:
        raise ValueError("Need exactly four empty views: room/flash x front/tilt")
    args.out.mkdir(parents=True, exist_ok=True)
    for filename, patch in pending.values():
        if not cv2.imwrite(str(args.out / filename), patch):
            raise OSError(f"Could not save {filename}")
    for filename, packed in wire_pending.values():
        if not cv2.imwrite(str(args.out / filename), cv2.cvtColor(packed, cv2.COLOR_RGB2BGR)):
            raise OSError(f"Could not save {filename}")
    for source in sources:
        source["reference_sha256"] = hashlib.sha256((args.out / source["reference"]).read_bytes()).hexdigest()
        source["wire_reference_sha256"] = hashlib.sha256((args.out / source["wire_reference"]).read_bytes()).hexdigest()
    provenance = {"method": "Median translation of manual pins after board rectification; empty calibration row only.",
                  "box": [x0, y0, x1, y1], "sources": sources}
    (args.out / "A4020E_block_references.json").write_text(json.dumps(provenance, indent=2), encoding="utf-8")
    print(f"Saved four housing views and paired empty pin windows to {args.out}; colour model unchanged")


if __name__ == "__main__":
    main()
