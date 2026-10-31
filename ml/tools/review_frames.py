"""Look at the live frames the service saved (IA_SAVE_FRAMES) and sum them up.

    python ml\\tools\\review_frames.py data\\live\\2026-10-31

For every saved frame it draws what the service answered: a ring per pin in the colour it read (white = no wire,
grey cross = not found), an amber ring when the confidence is below 0.80 (the app asks the installer there), and
small dots on the other modules of the zone survey. The pictures go to <folder>\\review\\. Then it prints a summary
per end: how many frames, the camera sizes the phone really sent, how often every pin was found, and the time per
read in the service (median and 95th percentile; the phone's round trip adds the network). The summary is also
saved as <folder>\\review\\summary.json for the log and the report.
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

AMBER = 0.80
RGB = [(255, 30, 30), (0, 0, 0), (0, 200, 60), (255, 220, 0), (255, 255, 255), (140, 80, 30), (30, 90, 255), (255, 140, 0)]
NO_WIRE_RGB, MISSING_RGB, AMBER_RGB = (255, 255, 255), (150, 150, 150), (255, 170, 0)


def draw(record, image_path: Path, out_path: Path):
    im = Image.open(image_path).convert("RGB")
    d = ImageDraw.Draw(im)
    w, h = im.size
    r = max(8, w // 70)

    def ring(item, radius, width):
        x, y = item["x"] * w, item["y"] * h
        if not item["found"]:
            d.line([x - radius, y - radius, x + radius, y + radius], fill=MISSING_RGB, width=width)
            d.line([x - radius, y + radius, x + radius, y - radius], fill=MISSING_RGB, width=width)
            return
        col = RGB[item["colour"]] if 0 <= item["colour"] < len(RGB) else NO_WIRE_RGB
        halo = (255, 255, 255) if sum(col) < 200 else (0, 0, 0)        # so a red ring shows on a red wire
        d.ellipse([x - radius - 1, y - radius - 1, x + radius + 1, y + radius + 1], outline=halo, width=width + 2)
        d.ellipse([x - radius, y - radius, x + radius, y + radius], outline=col, width=width)
        if item["confidence"] < AMBER:
            d.ellipse([x - radius - width - 3, y - radius - width - 3, x + radius + width + 3, y + radius + width + 3],
                      outline=AMBER_RGB, width=max(2, width // 2))

    resp = record["response"]
    for block in resp.get("other_blocks", []):
        for item in block["items"]:
            if block["located"]:
                ring(item, r // 2, 2)
    for item in resp["items"]:
        ring(item, r, 4)
    req = record["request"]
    text = f"{req['end']} {req['module']}  {record['ms']:.0f} ms  {w}x{h}" + (f"  ERROR {resp['error']}" if resp["error"] else "")
    d.rectangle([0, 0, 9 * len(text) + 12, 24], fill=(0, 0, 0))
    d.text((6, 5), text, fill=(255, 255, 255))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    im.save(out_path, quality=90)


def summarise(records):
    out = {}
    for end in sorted({r["request"]["end"] for r in records}):
        rs = [r for r in records if r["request"]["end"] == end]
        ms = np.array([r["ms"] for r in rs])
        all_found = [all(i["found"] for i in r["response"]["items"]) and not r["response"]["error"] for r in rs]
        sizes = sorted({"%dx%d" % tuple(r["decoded_size"]) for r in rs})
        out[end] = {"frames": len(rs), "sizes": sizes, "all_pins_found": int(sum(all_found)),
                    "share_all_found": round(float(np.mean(all_found)), 3),
                    "errors": int(sum(bool(r["response"]["error"]) for r in rs)),
                    "ms_median": round(float(np.median(ms))), "ms_p95": round(float(np.percentile(ms, 95)))}
    return out


def main(folder: Path):
    files = sorted(p for p in folder.glob("*.json"))
    if not files:
        raise SystemExit(f"No saved frames in {folder} (was the service started with IA_SAVE_FRAMES set?)")
    records = []
    for f in files:
        rec = json.loads(f.read_text(encoding="utf-8"))
        records.append(rec)
        if rec.get("image_file"):
            draw(rec, folder / rec["image_file"], folder / "review" / (f.stem + ".jpg"))
    summary = summarise(records)
    for end, s in summary.items():
        print(f"{end}: {s['frames']} frames, sizes {', '.join(s['sizes'])}; every pin found in {s['all_pins_found']} "
              f"({s['share_all_found']:.0%}); errors {s['errors']}; service time median {s['ms_median']} ms, "
              f"95th percentile {s['ms_p95']} ms")
    (folder / "review").mkdir(exist_ok=True)
    (folder / "review" / "summary.json").write_text(json.dumps(summary, indent=1), encoding="utf-8")
    print("pictures and summary.json in", folder / "review")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    main(Path(sys.argv[1]))
