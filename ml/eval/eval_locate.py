"""M2 step 2: how well does locate() find the pins? Scored against the hand annotations.

For every photo of a session it runs locate() with the device's template (A1610 board, or A4020E reader with
--end device) and compares each pin with the annotation:
  found         share of photos where the board was found at all
  error         distance between found pin and annotated pin, in pin spacings (1.0 = one pin off);
                "stage 1 alone" is the same before the blocks are found again
  within half   share of pins less than half a pin spacing off: the pin the colour will be read from is right
  all right     share of photos where every pin is within half a pin spacing
Pin spacing is measured per photo from the annotation, so the numbers do not depend on distance or resolution.
--size shrinks each photo first (long side in pixels), to see what a smaller camera frame does.

    python -m ml.eval.eval_locate --session session1
    python -m ml.eval.eval_locate --session session1 --size 1920 --out ml/runs/locate_s1_1920.csv
    python -m ml.eval.eval_locate --session session1 --size 1920 --end device
"""
import argparse
import csv
import glob
import json
import os
import statistics
import time

import cv2
import numpy as np

from ml.perception_service.locate import Template, locate


def score_photo(tpl, ann, data_dir, size):
    img = cv2.cvtColor(cv2.imread(os.path.join(data_dir, ann["image"])), cv2.COLOR_BGR2RGB)
    h, w = img.shape[:2]
    if size and max(h, w) > size:
        s = size / max(h, w)
        img = cv2.resize(img, (round(w * s), round(h * s)), interpolation=cv2.INTER_AREA)
    fh, fw = img.shape[:2]
    t0 = time.perf_counter()
    res = locate(tpl, img, refine_blocks=tpl.name == "A1610")
    ms = (time.perf_counter() - t0) * 1000
    modules = [(ann["module"], ann["terminals"])] + [(m["module"], m["terminals"]) for m in ann.get("other_modules", [])]
    errors, errors1, spacings = [], [], []
    for module, terms in modules:
        truth = np.array([[t["x"] * fw, t["y"] * fh] for t in terms])
        side_by_side = truth[[1, 0, 2, 3]] if module == "A4020E" else truth   # reader: + - A B sit as - + A B
        spacings.append(float(np.median(np.linalg.norm(np.diff(side_by_side, axis=0), axis=1))))
        if res.found:
            errors += list(np.linalg.norm(np.array(res.pins[module]) - truth, axis=1))
            errors1 += list(np.linalg.norm(np.array(res.board_only[module]) - truth, axis=1))
    spacing = statistics.median(spacings)
    return res, ms, spacing, [e / spacing for e in errors], [e / spacing for e in errors1], (fw, fh)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="data")
    ap.add_argument("--session", default="session1")
    ap.add_argument("--size", type=int, default=0, help="shrink photos to this long side first (0 = as taken)")
    ap.add_argument("--end", default="controller", choices=["controller", "device"])
    ap.add_argument("--out", default="", help="optional CSV with one row per photo")
    a = ap.parse_args()

    tpl = Template.load("A1610" if a.end == "controller" else "A4020E")
    files = sorted(glob.glob(os.path.join(a.data, "annotations", a.session, "*.json")))
    rows = []
    for f in files:
        ann = json.loads(open(f, encoding="utf-8").read())
        if ann["end"] != a.end:
            continue
        res, ms, spacing, err, err1, frame = score_photo(tpl, ann, a.data, a.size)
        rows.append({"photo": os.path.basename(f)[:-5], "found": res.found, "inliers": res.inliers,
                     "frame": "%dx%d" % frame, "pin_spacing_px": round(spacing, 1),
                     "median_error": round(float(np.median(err)), 3) if err else "",
                     "max_error": round(float(np.max(err)), 3) if err else "",
                     "stage1_median_error": round(float(np.median(err1)), 3) if err1 else "",
                     "within_half": round(float(np.mean(np.array(err) < 0.5)), 3) if err else 0.0,
                     "ms": round(ms)})
    if not rows:
        raise SystemExit(f"No {a.end} annotations found in " + os.path.join(a.data, "annotations", a.session))

    found = [r for r in rows if r["found"]]
    print(f"photos: {len(rows)}   size: {a.size or 'as taken'} ({rows[0]['frame']})   OpenCV {cv2.__version__}")
    print(f"found:                  {len(found)}/{len(rows)}")
    if found:
        print(f"median pin error:       {statistics.median(r['median_error'] for r in found):.2f} pin spacings")
        print(f"pins within half:       {statistics.mean(r['within_half'] for r in rows):.1%}  (photos not found count as 0)")
        print(f"photos with all right:  {sum(r['within_half'] == 1.0 for r in rows)}/{len(rows)}")
        print(f"stage 1 alone:          {statistics.median(r['stage1_median_error'] for r in found):.2f} pin spacings (median)")
        print(f"time per photo:         {statistics.median(r['ms'] for r in rows):.0f} ms (median)")
    bad = [r["photo"] for r in rows if r["within_half"] < 1.0]
    if bad:
        print("not all pins right:     " + ", ".join(bad))
    if a.out:
        os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
        with open(a.out, "w", newline="", encoding="utf-8") as fh:
            w = csv.DictWriter(fh, fieldnames=list(rows[0]))
            w.writeheader(); w.writerows(rows)
        print("per photo: " + a.out)


if __name__ == "__main__":
    main()
