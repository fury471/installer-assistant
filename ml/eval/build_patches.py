"""Cut the pin patches of every annotated photo and save them with their true label (M3 data set).

    python -m ml.eval.build_patches --session session1

Writes data/patches/<session>/<photo>__<module>__<pin>.png (the pin window plus MARGIN pixels on every side, which
training uses to shift and turn patches) and data/patches/<session>/labels.csv with
photo, row, light, view, module, pin, colour (-1 = no wire), cable, located, file.
Patches come from locate()'s own result (not from the annotation), so they look exactly like what the service sees.
"""
import argparse
import csv
import glob
import json
import os

import cv2

from ml.perception_service.locate import Template, locate
from ml.perception_service.patches import pin_patches, rectify

MARGIN = 6           # extra canonical pixels around each window, for augmentation in train_pins.py


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="data")
    ap.add_argument("--session", default="session1")
    ap.add_argument("--size", type=int, default=1920)
    a = ap.parse_args()
    out = os.path.join(a.data, "patches", a.session)
    os.makedirs(out, exist_ok=True)
    templates = {"controller": Template.load("A1610"), "device": Template.load("A4020E")}
    rows = []
    for f in sorted(glob.glob(os.path.join(a.data, "annotations", a.session, "*.json"))):
        ann = json.loads(open(f, encoding="utf-8").read())
        tpl = templates[ann["end"]]
        bgr = cv2.imread(os.path.join(a.data, ann["image"]))
        s = a.size / max(bgr.shape[:2])
        if s < 1:
            bgr = cv2.resize(bgr, None, fx=s, fy=s, interpolation=cv2.INTER_AREA)
        rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
        res = locate(tpl, rgb, refine_blocks=tpl.name == "A1610")
        photo = os.path.basename(f)[:-5]
        truth = {ann["module"]: ann["terminals"]}
        for m in ann.get("other_modules", []):
            truth[m["module"]] = m["terminals"]
        patches = pin_patches(tpl, res, rgb, rectify(tpl, res, rgb), margin=MARGIN) if res.found else {}
        for module, terms in truth.items():
            for i, t in enumerate(terms):
                name = f"{photo}__{module}__{i + 1}.png"
                if res.found:
                    cv2.imwrite(os.path.join(out, name), cv2.cvtColor(patches[module][i], cv2.COLOR_RGB2BGR))
                rows.append({"photo": photo, "row": ann.get("row", ""), "light": ann.get("light", ""),
                             "view": ann.get("view", ""), "module": module, "pin": i + 1, "name": t["name"],
                             "colour": t["colour"], "cable": t.get("cable", ""), "unclear": t.get("unclear", ""),
                             "located": res.found, "file": name if res.found else "", "margin": MARGIN})
        print(photo, "found" if res.found else "NOT found")
    with open(os.path.join(out, "labels.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0]))
        w.writeheader(); w.writerows(rows)
    print(len(rows), "pins ->", out)


if __name__ == "__main__":
    main()
