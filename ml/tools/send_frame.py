"""Send one photo to the running perception service, the way the app does, and show what comes back.

Start the service first (another terminal):
    python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 8000
Then:
    python ml\\tools\\send_frame.py data\\photos\\session1\\renamed\\s1_ctrl_c05_room_front.jpg

It shrinks the photo to 1920 px (the agreed camera size), sends a /read request with the label's module, the other
modules (zone survey) and the label (bundle check), prints every pin, and saves data\\send_frame\\<photo>.jpg with a
ring per pin in the colour the service read (white = no wire, grey = not found). If the photo is annotated, the
table also shows the true colour.
"""
import base64
import io
import json
import sys
import urllib.request
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
URL = "http://localhost:8000/read"
LABEL = "IA|C01|D1|A1610.READER1|A4020E"          # the bench cable, without its end
NAMES = ["red", "black", "green", "yellow", "white", "brown", "blue", "orange"]
RGB = [(255, 30, 30), (0, 0, 0), (0, 200, 60), (255, 220, 0), (255, 255, 255), (140, 80, 30), (30, 90, 255), (255, 140, 0)]


def colour_name(c):
    return NAMES[c] if 0 <= c < len(NAMES) else "no wire"


def main(photo: Path):
    board = json.loads((ROOT / "ml/perception_service/templates/A1610.json").read_text(encoding="utf-8"))["modules"]
    reader = json.loads((ROOT / "ml/perception_service/templates/A4020E.json").read_text(encoding="utf-8"))["modules"]
    at_controller = "_ctrl_" in photo.name
    if at_controller:
        module, terms = "A1610.READER1", [p["name"] for p in board["A1610.READER1"]]
        others = [{"module": m, "terminals": [p["name"] for p in pins]} for m, pins in board.items() if m != module]
    else:
        module, terms, others = "A4020E", [p["name"] for p in reader["A4020E"]], []

    img = Image.open(photo).convert("RGB")
    img.thumbnail((1920, 1920))
    buf = io.BytesIO(); img.save(buf, "JPEG", quality=85)
    req = {"frame_id": 1, "end": "controller" if at_controller else "device", "module": module, "terminals": terms,
           "other_modules": others, "label": LABEL,
           "image": {"width": img.width, "height": img.height, "format": "jpeg",
                     "data_b64": base64.b64encode(buf.getvalue()).decode()}}
    http = urllib.request.Request(URL, data=json.dumps(req).encode(), headers={"Content-Type": "application/json"})
    try:
        resp = json.loads(urllib.request.urlopen(http, timeout=30).read())
    except OSError as e:
        raise SystemExit(f"No answer from {URL}: is the service running? ({e})")
    if resp.get("error"):
        print("service error:", resp["error"])

    truth = {}
    ann = ROOT / "data" / "annotations" / photo.parent.parent.name / (photo.stem + ".json")
    if ann.exists():
        a = json.loads(ann.read_text(encoding="utf-8"))
        truth[a["module"]] = [t["colour"] for t in a["terminals"]]
        for m in a.get("other_modules", []):
            truth[m["module"]] = [t["colour"] for t in m["terminals"]]

    print(f"model {resp.get('model')}   {img.width}x{img.height}")
    blocks = [(module, resp["items"], True)] + [(b["module"], b["items"], b["located"]) for b in resp.get("other_blocks", [])]
    draw = ImageDraw.Draw(img)
    right = total = 0
    for name, items, located in blocks:
        print(f"\n{name}  ({'found' if located else 'NOT found'})")
        print(f"  {'pin':5} {'read':8} {'conf':>5}  {'true':8} trace")
        for i, (t, it) in enumerate(zip(terms if name == module else [p['name'] for p in board[name]], items)):
            true = truth.get(name, [None] * len(items))[i]
            if true is not None:
                total += 1; right += it["colour"] == true
            mark = "" if true is None else ("ok" if it["colour"] == true else "WRONG")
            print(f"  {t:5} {colour_name(it['colour']) if it['found'] else '-':8} {it['confidence']:5.2f}  "
                  f"{colour_name(true) if true is not None else '':8} {it['trace']:12} {mark}")
            if it["found"]:
                x, y = it["x"] * img.width, it["y"] * img.height
                ring = RGB[it["colour"]] if it["colour"] >= 0 else (255, 255, 255)
                draw.ellipse([x - 9, y - 9, x + 9, y + 9], outline=ring, width=4)
    if total:
        print(f"\ncolours right: {right}/{total}")
    out = ROOT / "data" / "send_frame"
    out.mkdir(parents=True, exist_ok=True)
    img.save(out / photo.name, quality=90)
    print("picture:", out / photo.name)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    main(Path(sys.argv[1]))
