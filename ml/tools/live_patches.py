"""Turn saved live frames with a known wiring into training and test patches for the wire reader.

    python ml/tools/live_patches.py data/live/2026-11-15-a --name live_a ^
        --wiring "A1610.READER1=black,red,green,yellow,-,-,-,-"
    python -m ml.eval.train_pins --sessions session1 session3 live_a live_b

The service saves every frame it reads (IA_SAVE_FRAMES). When all frames of a folder show one wiring that you
know, no hand annotation is needed: locate() places the pins as the service does, the windows are cut as the
service cuts them, and each gets the colour you state for its pin. Modules you do not list are taken as empty
(add --only-listed to leave them out). Frames the service would refuse (board not found, a block that does not
look like its reference, blurred) are skipped, so the patches look exactly like what the reader sees live.

Writes data/patches/<name>/ with labels.csv in the format of ml/eval/build_patches.py; "row" is the folder name,
so train_pins holds the whole batch out together when it measures (wiring-group cross-validation). One batch has
one wiring: to MEASURE the reader on live frames, record at least two batches with different wirings (move two
wires) under different names; each is then tested by a model that never saw it (leave-one-domain-out; Gulrajani
and Lopez-Paz, 2021). Colours: red, black, green, yellow, white, brown, blue, orange, or - for an empty pin, in the
template's pin order (pin 1 first; A1610 Reader 1: -, 12V, A, B, O1, O2, IO7, IO8).
"""
import argparse
import csv
import glob
import json
import os
import sys
from pathlib import Path

import cv2

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from ml.perception_service.locate import TEMPLATES, Template, locate            # noqa: E402
from ml.perception_service.patches import pin_patches, rectify                  # noqa: E402
from ml.perception_service.readers.classical import SHARP_SCALE, TRUST_MIN, sharpness   # noqa: E402

MARGIN = 6
NAMES = ["red", "black", "green", "yellow", "white", "brown", "blue", "orange"]


def parse_wiring(text: str, template: Template):
    wiring = {}
    for part in text.split(";"):
        module, _, colours = part.strip().partition("=")
        if module not in template.modules:
            raise SystemExit(f"{module}: not a module of {template.name} ({', '.join(template.modules)})")
        values = [c.strip().lower() for c in colours.split(",")]
        if len(values) != len(template.modules[module]):
            raise SystemExit(f"{module}: {len(values)} colours given, the module has {len(template.modules[module])} pins")
        for v in values:
            if v not in NAMES and v != "-":
                raise SystemExit(f"{module}: unknown colour '{v}' (use {', '.join(NAMES)} or -)")
        wiring[module] = [NAMES.index(v) if v != "-" else -1 for v in values]
    return wiring


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("folder", help="a folder of saved live frames (IA_SAVE_FRAMES)")
    ap.add_argument("--wiring", required=True, help='e.g. "A1610.READER1=black,red,green,yellow,-,-,-,-" (several: a;b)')
    ap.add_argument("--name", required=True, help="patch set name: data/patches/<name>")
    ap.add_argument("--data", default="data")
    ap.add_argument("--only-listed", action="store_true", help="leave out the modules not listed in --wiring")
    a = ap.parse_args(argv)
    device = a.wiring.split("=")[0].strip().split(".")[0]
    tpl = Template.load(device)
    wiring = parse_wiring(a.wiring, tpl)
    sharp_min = float(json.loads((TEMPLATES / (device + ".json")).read_text(encoding="utf-8")).get("sharp_min", 0)) * SHARP_SCALE
    out = os.path.join(a.data, "patches", a.name)
    os.makedirs(out, exist_ok=True)
    batch = os.path.basename(os.path.normpath(a.folder))
    rows, used, skipped = [], 0, {}
    for f in sorted(glob.glob(os.path.join(a.folder, "*.jpg"))):
        rgb = cv2.cvtColor(cv2.imread(f), cv2.COLOR_BGR2RGB)
        res = locate(tpl, rgb)
        photo = os.path.basename(f)[:-4]
        why = ""
        if not res.found:
            why = "board not found"
        else:
            h, w = rgb.shape[:2]
            listed = next(iter(wiring))
            if listed in res.doubtful or 0 <= res.block_agreement.get(listed, -1.0) < TRUST_MIN:
                why = "block not trusted"
            elif sharpness(rgb, [(x / w, y / h) for x, y in res.pins[listed]]) < sharp_min:
                why = "blurred"
        if why:
            skipped[why] = skipped.get(why, 0) + 1
            continue
        used += 1
        patches = pin_patches(tpl, res, rgb, rectify(tpl, res, rgb), margin=MARGIN)
        for module, pins in tpl.modules.items():
            if module not in wiring and (a.only_listed or module in res.doubtful):
                continue
            colours = wiring.get(module, [-1] * len(pins))
            for i, pin in enumerate(pins):
                name = f"{photo}__{module}__{i + 1}.png"
                cv2.imwrite(os.path.join(out, name), cv2.cvtColor(patches[module][i], cv2.COLOR_RGB2BGR))
                rows.append({"photo": photo, "row": batch, "light": "live", "view": "live", "module": module, "pin": i + 1,
                             "name": pin["name"], "colour": colours[i], "cable": "", "unclear": "", "located": True,
                             "file": name, "margin": MARGIN, "reference_index": res.reference_indices.get(module, 0)})
    if not rows:
        raise SystemExit(f"no usable frame in {a.folder} (skipped: {skipped})")
    with open(os.path.join(out, "labels.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)
    wired = sum(r["colour"] >= 0 for r in rows)
    print(f"{used} frames used, skipped {skipped or 'none'}; {len(rows)} pins ({wired} wired) -> {out}")


if __name__ == "__main__":
    main()
