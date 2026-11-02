"""Add a board variant to a template: another revision of the device with the same pin layout (e.g. the AXIS A1601
for the A1610 template), from photos or saved live frames of that board with EVERY PIN EMPTY.

    python ml\\tools\\make_variant.py --template A1610 --name A1601 data\\live\\1101\\<frame>.jpg [more frames ...]

Each frame is located with the template (as the service does), warped into the template's view, and each terminal
block is moved back by its stage-2 offset so the blocks sit exactly where the template's pins are. The per-pixel
median of all frames (hands, reflections and blur differ from frame to frame) is saved as
ml/perception_service/templates/<template>_<name>.jpg and listed under "variants" in <template>.json.
The service then tries every variant, uses the one that fits a frame best, and compares that frame's pins with that
variant's empty board: a new board revision needs a few frames of the empty board, no training.
Use 3 or more frames, roughly straight on, with the whole board in view.
"""
import argparse
import json
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from ml.perception_service.locate import TEMPLATES, Template, _locate_one   # noqa: E402


def aligned_view(tpl: Template, rgb: np.ndarray):
    """The frame in the template's view with every block moved back to its template place, or None."""
    res = _locate_one(tpl, rgb, refine_blocks=True)
    if not res.found:
        return None, res
    th, tw = tpl.gray.shape[:2]
    view = cv2.warpPerspective(rgb, np.linalg.inv(res.homography), (tw, th), flags=cv2.INTER_AREA)
    out = view.copy()
    left, top, right, bottom = tpl.block_margin
    for module, pins in tpl.modules.items():
        dx, dy = res.shifts.get(module, (0, 0))
        xs, ys = [p["x"] for p in pins], [p["y"] for p in pins]
        x0, y0 = max(0, int(min(xs)) - left), max(0, int(min(ys)) - top)
        x1, y1 = min(tw, int(max(xs)) + right), min(th, int(max(ys)) + bottom)
        X0, Y0 = x0 + dx, y0 + dy
        if 0 <= X0 and 0 <= Y0 and X0 + (x1 - x0) <= tw and Y0 + (y1 - y0) <= th:
            out[y0:y1, x0:x1] = view[Y0:Y0 + (y1 - y0), X0:X0 + (x1 - x0)]
    return out, res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--template", default="A1610")
    ap.add_argument("--name", required=True, help="the variant's name, e.g. A1601")
    ap.add_argument("--min-inliers", type=int, default=80, help="skip frames matched with fewer pairs")
    ap.add_argument("frames", nargs="+")
    a = ap.parse_args()
    tpl = Template.load(a.template)
    views = []
    for f in a.frames:
        bgr = cv2.imread(f)
        if bgr is None:
            print("cannot read", f); continue
        view, res = aligned_view(tpl, cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))
        if view is None or res.inliers < a.min_inliers:
            print(f"skipped {Path(f).name}: {res.inliers} pairs"); continue
        views.append(view)
        print(f"used {Path(f).name}: {res.inliers} pairs, block offsets {res.shifts}")
    if not views:
        raise SystemExit("no usable frame")
    stack = np.stack(views).astype(np.float32)
    valid = stack.sum(axis=3, keepdims=True) > 0                       # outside the frame the warp is black
    stack[~np.repeat(valid, 3, axis=3)] = np.nan
    median = np.nanmedian(stack, axis=0)
    median = np.where(np.isnan(median), tpl.rgb.astype(np.float32), median).astype(np.uint8)
    image = f"{a.template}_{a.name}.jpg"
    cv2.imwrite(str(TEMPLATES / image), cv2.cvtColor(median, cv2.COLOR_RGB2BGR), [cv2.IMWRITE_JPEG_QUALITY, 92])
    path = TEMPLATES / f"{a.template}.json"
    text = path.read_text(encoding="utf-8")
    meta = json.loads(text)
    variants = dict(meta.get("variants", {}))
    variants[a.name] = image
    if "variants" in meta:
        meta["variants"] = variants
        path.write_text(json.dumps(meta, indent=1, ensure_ascii=True) + "\n", encoding="utf-8")
    else:                                                              # keep the file's layout: one line added
        text = text.replace('"image": "%s",' % meta["image"],
                            '"image": "%s", "variants": %s,' % (meta["image"], json.dumps(variants)), 1)
        path.write_text(text, encoding="utf-8")
    print(f"{len(views)} frames -> {TEMPLATES / image}; {path.name} variants: {variants}")


if __name__ == "__main__":
    main()
