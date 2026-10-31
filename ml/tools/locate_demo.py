"""M2 step 1: find the board in ONE photo, with no clicks, and draw what happened.

    python ml\\tools\\locate_demo.py data\\photos\\session1\\renamed\\s1_ctrl_c05_flash_tilt.jpg

Works for board photos (_ctrl_) and reader photos (_rdr_). Writes two pictures to data\\locate_demo\\:
  *_matches.jpg   template (left) and photo (right); green lines = feature pairs RANSAC kept, red = thrown out
  *_pins.jpg      the pins: yellow = stage 1 (board only), green = after stage 2 (each block found again);
                  pink rings = your annotation, if the photo has one
"""
import json
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from ml.perception_service.locate import Template, locate   # noqa: E402


def main(photo: Path):
    tpl = Template.load("A4020E" if "_rdr_" in photo.name else "A1610")   # reader photos: the reader template
    img = cv2.imread(str(photo))
    if img is None:
        raise SystemExit(f"Cannot read {photo}")
    res = locate(tpl, cv2.cvtColor(img, cv2.COLOR_BGR2RGB), refine_blocks=tpl.name == "A1610")
    print(f"{tpl.name} found: {res.found}   matcher: {tpl.matcher}   inlier pairs: {res.inliers}")
    if not res.found:
        return
    out = ROOT / "data" / "locate_demo"
    out.mkdir(parents=True, exist_ok=True)

    # 1. the feature pairs
    tplc = cv2.cvtColor(tpl.gray, cv2.COLOR_GRAY2BGR)
    scale = tplc.shape[0] / img.shape[0]
    photo_small = cv2.resize(img, (round(img.shape[1] * scale), tplc.shape[0]))
    both = np.hstack([tplc, photo_small])
    for (a, b, inlier) in res.matches[::3]:
        p = (int(a[0]), int(a[1])); q = (int(tplc.shape[1] + b[0] * scale), int(b[1] * scale))
        cv2.line(both, p, q, (0, 200, 0) if inlier else (0, 0, 255), 1)
    cv2.imwrite(str(out / (photo.stem + "_matches.jpg")), both)

    # 2. the pins
    ann = ROOT / "data" / "annotations" / photo.parent.parent.name / (photo.stem + ".json")
    if ann.exists():
        a = json.loads(ann.read_text(encoding="utf-8"))
        for t in a["terminals"] + [t for m in a.get("other_modules", []) for t in m["terminals"]]:
            cv2.circle(img, (int(t["x"] * img.shape[1]), int(t["y"] * img.shape[0])), 16, (255, 0, 255), 4)
    for module in res.pins:
        for (x, y) in res.board_only[module]:
            cv2.circle(img, (int(x), int(y)), 7, (0, 220, 255), -1)
        for (x, y) in res.pins[module]:
            cv2.circle(img, (int(x), int(y)), 7, (0, 220, 0), -1)
    xs = [x for m in res.pins.values() for x, _ in m]; ys = [y for m in res.pins.values() for _, y in m]
    crop = img[max(0, int(min(ys)) - 200):int(max(ys)) + 200, max(0, int(min(xs)) - 200):int(max(xs)) + 600]
    cv2.imwrite(str(out / (photo.stem + "_pins.jpg")), crop)
    print(f"pictures in {out}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    main(Path(sys.argv[1]))
