"""Measure what the AR view did on the phone, from its AR records (package B; ArRecords.cs).

    adb pull /sdcard/Android/data/com.unity.template.ar_mobile/files/ar-records data/ar-records
    python ml/tools/ar_records.py data/ar-records [--frames data/live/<folder>] [--csv out.csv]

The phone writes one JSON line per read (pose, matrices, the image points the reader returned, where the marks
were drawn in that frame before the read, what the view did with it), a 1 Hz sample of where the marks were on
the screen, and the names of screenshots taken with a three-finger tap. No camera images: those are in the
service's frame log (IA_SAVE_FRAMES) under the same frame ids.

What it reports, per record file and over all files:
  - mark offset at re-read: for every read of an end that already had marks, how far each mark was (in pin
    pitches) from where the reader found that pin in the new frame. This is what the installer saw as "the mark
    is next to the pin" at that moment, measured without hand labels (the reader's own error is ~0.05-0.28 pitch
    on the bench, so offsets above ~0.3 pitch are mostly the overlay's). Median, 90th percentile, share within
    half a pitch; split into the first re-read of a set of marks and later ones (after fusion has views).
  - outcomes: first / fused / rejected / relocated / replaced (fusion off).
  - views behind each mark (sightings) and the widest baseline between them.
  - ray method: how far the display-only ray lands from the intrinsics ray, in image pixels. ~0 means the CPU
    image and the screen texture share one field of view; a growing value away from the centre means they do not.
  - spacing depth per read (metres) and the camera configuration (CPU image vs GPU texture size).
With --frames, every saved frame of a read that had marks is drawn to <records>/review/: a green ring where the
reader found each pin, a red dot where the mark was, and a line between them.
"""
import argparse
import csv
import json
import re
import sys
from datetime import datetime
from pathlib import Path

import numpy as np


def load(path: Path):
    events = []
    with open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                print(f"{path.name}:{n}: not JSON, skipped (the app may have stopped mid-line)")
    return events


def pct(values, q):
    return float(np.percentile(values, q)) if len(values) else float("nan")


def offsets_of(read):
    """Per pin: offset in pitches between the mark (markU/markV) and the new reading (u/v), in image pixels / spacing."""
    w, h = read.get("w") or 0, read.get("h") or 0
    pts = [p for p in read.get("points", []) if "markU" in p]
    if not pts or not w or not h:
        return []
    # the row's spacing in image pixels, as the phone computed it (median gap along the row)
    xy = np.array([[p["u"] * w, p["v"] * h] for p in read.get("points", [])], float)
    if len(xy) < 2:
        return []
    centred = xy - xy.mean(0)
    axis = np.linalg.svd(centred, full_matrices=False)[2][0]
    order = xy[np.argsort(centred @ axis)]
    gaps = np.linalg.norm(np.diff(order, axis=0), axis=1)
    gaps = gaps[gaps > 1e-6]
    if not len(gaps):
        return []
    spacing = float(np.median(gaps))
    return [float(np.hypot((p["markU"] - p["u"]) * w, (p["markV"] - p["v"]) * h) / spacing) for p in pts]


def summarise(events, name):
    reads = [e for e in events if e.get("kind") == "read"]
    outcome = {}
    for r in reads:
        outcome[r.get("outcome", "?")] = outcome.get(r.get("outcome", "?"), 0) + 1
    first, later, rows = [], [], []
    since_clear = 0
    for e in events:
        if e.get("kind") == "clear":
            since_clear = 0
            continue
        if e.get("kind") != "read":
            continue
        off = offsets_of(e)
        if off:
            (first if since_clear == 1 else later).extend(off)
        since_clear += 1
        sightings = [p.get("sightings", 0) for p in e.get("points", [])]
        base = [p.get("baselineMm", 0) for p in e.get("points", [])]
        method = [p["rayMethodPx"] for p in e.get("points", []) if p.get("rayMethodPx", -1) >= 0]
        rows.append({"file": name, "clock": e.get("clock", ""), "frame": e.get("frame"), "outcome": e.get("outcome"),
                     "pins": len(e.get("points", [])), "depth_m": e.get("depth"),
                     "offset_median_pitch": float(np.median(off)) if off else "",
                     "offset_max_pitch": float(np.max(off)) if off else "",
                     "sightings_max": max(sightings) if sightings else 0,
                     "baseline_mm_max": max(base) if base else 0,
                     "ray_method_px_median": float(np.median(method)) if method else ""})
    every = first + later
    method = [p["rayMethodPx"] for r in reads for p in r.get("points", []) if p.get("rayMethodPx", -1) >= 0]
    cams = [e for e in events if e.get("kind") == "camera"]
    return {
        "file": name,
        "device": next((e.get("device") for e in events if e.get("kind") == "start"), ""),
        "camera": [{"cpu_image": c.get("cpuImage"), "texture": c.get("texture")} for c in cams],
        "reads": len(reads),
        "outcomes": outcome,
        "offset_pitch": {
            "all": block(every), "first_reread": block(first), "later_rereads": block(later)},
        "ray_method_px": {"median": pct(method, 50), "p90": pct(method, 90), "n": len(method)},
        "depth_m_median": pct([r["depth"] for r in reads if r.get("depth")], 50),
        "baseline_mm_max": max([p.get("baselineMm", 0) for r in reads for p in r.get("points", [])] or [0]),
        "screenshots": [e.get("file") for e in events if e.get("kind") == "shot"],
    }, rows


def block(values):
    v = np.asarray(values, float)
    if not len(v):
        return {"n": 0}
    return {"n": int(len(v)), "median": round(float(np.median(v)), 3), "p90": round(pct(v, 90), 3),
            "within_half_pitch": round(float(np.mean(v <= 0.5)), 3), "within_one_pitch": round(float(np.mean(v <= 1.0)), 3)}


def frame_files(folder: Path):
    """Saved service frames by frame id: (time, jpg path, json path)."""
    out = {}
    for j in folder.glob("*.json"):
        m = re.search(r"_f(\d+)$", j.stem)
        if not m:
            continue
        try:
            saved = datetime.fromisoformat(json.loads(j.read_text(encoding="utf-8"))["saved"])
        except Exception:
            continue
        jpg = j.with_suffix(".jpg")
        if jpg.exists():
            out.setdefault(int(m.group(1)), []).append((saved, jpg))
    return out


def draw(records_file: Path, events, frames, out_dir: Path):
    from PIL import Image, ImageDraw
    m = re.match(r"(\d{8})-", records_file.stem)
    day = datetime.strptime(m.group(1), "%Y%m%d").date() if m else None
    drawn = 0
    for e in events:
        if e.get("kind") != "read" or not any("markU" in p for p in e.get("points", [])):
            continue
        cands = frames.get(int(e.get("frame", -1)), [])
        if not cands:
            continue
        when = None
        if day and e.get("clock"):
            when = datetime.combine(day, datetime.strptime(e["clock"], "%H:%M:%S.%f").time())
        saved, jpg = min(cands, key=lambda c: abs((c[0] - when).total_seconds()) if when else 0)
        if when and abs((saved - when).total_seconds()) > 60:
            continue                       # same frame id from another app launch
        im = Image.open(jpg).convert("RGB")
        d = ImageDraw.Draw(im)
        w, h = im.size
        r = max(6, w // 90)
        for p in e["points"]:
            x, y = p["u"] * w, p["v"] * h
            d.ellipse([x - r, y - r, x + r, y + r], outline=(0, 230, 80), width=3)
            if "markU" in p:
                mx, my = p["markU"] * w, p["markV"] * h
                d.line([mx, my, x, y], fill=(255, 60, 60), width=2)
                d.ellipse([mx - 4, my - 4, mx + 4, my + 4], fill=(255, 60, 60))
        off = offsets_of(e)
        text = f"f{e['frame']} {e.get('outcome')}  mark offset median {np.median(off):.2f} pitch" if off else f"f{e['frame']}"
        d.rectangle([0, 0, 8 * len(text) + 12, 22], fill=(0, 0, 0))
        d.text((6, 5), text, fill=(255, 255, 255))
        out_dir.mkdir(parents=True, exist_ok=True)
        im.save(out_dir / f"{records_file.stem}_f{e['frame']}.jpg", quality=90)
        drawn += 1
    return drawn


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("records", help="a folder of .jsonl files (adb pull of ar-records) or one .jsonl file")
    ap.add_argument("--frames", help="the service's frame log folder (IA_SAVE_FRAMES) of the same test")
    ap.add_argument("--csv", help="write one row per read to this CSV")
    a = ap.parse_args(argv)
    src = Path(a.records)
    files = sorted(src.glob("*.jsonl")) if src.is_dir() else [src]
    if not files:
        sys.exit(f"no .jsonl files in {src}")
    frames = frame_files(Path(a.frames)) if a.frames else {}
    summaries, rows, all_first, all_later = [], [], [], []
    for f in files:
        events = load(f)
        s, r = summarise(events, f.name)
        summaries.append(s)
        rows.extend(r)
        since = 0
        for e in events:
            if e.get("kind") == "clear":
                since = 0
            elif e.get("kind") == "read":
                since += 1
                (all_first if since == 2 else all_later if since > 2 else []).extend(offsets_of(e))
        if frames:
            n = draw(f, events, frames, (src if src.is_dir() else src.parent) / "review")
            s["frames_drawn"] = n
    total = {"files": len(files), "reads": sum(s["reads"] for s in summaries),
             "offset_pitch": {"first_reread": block(all_first), "later_rereads": block(all_later),
                              "all": block(all_first + all_later)}}
    out = {"total": total, "files": summaries}
    print(json.dumps(out, indent=1))
    if a.csv and rows:
        with open(a.csv, "w", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        print(f"{len(rows)} reads -> {a.csv}")
    target = (src if src.is_dir() else src.parent) / "summary.json"
    target.write_text(json.dumps(out, indent=1), encoding="utf-8")
    print(f"summary -> {target}")


if __name__ == "__main__":
    main()
