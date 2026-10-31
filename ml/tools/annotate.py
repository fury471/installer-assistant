"""Annotate the session photos: which wire colour sits on which pin, and where each pin is in the photo.

For every renamed photo it opens a window and asks for 4 clicks:
  controller photos (ctrl): 4 landmarks on the board. The template's 24 pins are then mapped onto the photo through
                            them (a homography, Hartley & Zisserman 2004, ch. 4: the same geometry locate() will use).
  reader photos (rdr):      the 4 wire slots of the green terminal block, from left to right (-, +, A, B).
Each click has two steps: a rough click on the whole photo, then a precise click in the zoomed view.
The panel on the right shows on the template where to click.

Then it draws every pin, ringed in the colour of the wire the shot list says is there (white = no wire). Check:
does every ring sit in an opening, and does the ring colour match the wire in that opening?
  Enter = save, next photo     r = redo this photo     s = skip (write why in ml/LOG.md)     q = quit
  Esc (while zoomed) = back to the whole photo
You can stop at any time: photos that already have an annotation are not asked again.

The wire colours are not typed by hand: they come from the shot list (random_wirings.py), the answer key. The
first run saves it as <session>/shot_list.txt, so the answer key stays fixed for that session.
One JSON per photo goes to data/annotations/<session>/, in the format at the top of ml/eval/evaluate.py.

Run from the project folder:
    python ml\\tools\\annotate.py data\\photos\\session1
    python ml\\tools\\annotate.py data\\photos\\session1 --only s1_ctrl_c05_flash_tilt.jpg   (redo one photo)
"""
import argparse
import csv
import json
import re
import subprocess
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]              # the project folder
TEMPLATES = ROOT / "ml" / "perception_service" / "templates"
RANDOM_WIRINGS = ROOT / "ml" / "tools" / "random_wirings.py"

COLOUR = {"red": 0, "black": 1, "green": 2, "yellow": 3}   # codes as in ml/perception_service/schemas.py
NO_WIRE = -1
RING = {0: "#ff2020", 1: "#000000", 2: "#00d040", 3: "#ffe000", NO_WIRE: "#ffffff"}

CABLE_LABEL = "IA|C01|D1|A1610.READER1|A4020E|"           # + CTRL or DEV: the label on the bench cable
# The other labelled cable (C02, the Door 2 reader's cable) sits on Reader 2, wired correctly, except the one
# wire the shot list moves onto a pin of this cable's module. Pin numbers on Reader 2:
OTHER_CABLE_ON_READER2 = {"red": 2, "black": 1, "green": 3, "yellow": 4}
CORRECT = {"red": 2, "black": 1, "green": 3, "yellow": 4}   # this cable on its module

# Board landmarks: (what to click, where it is, x and y on the template A1610.jpg).
# Two are pins from A1610.json, two are the mounting screws next to Door 2 and Door 1 (measured on the template).
BOARD_LANDMARKS = [
    ("Door 2, TOP pin (IO12)", "the opening of the top pin of the small green block at the very top", (169, 650)),
    ("Door 2 screw", "the centre of the silver screw right of the text DOOR 2", (339, 663)),
    ("Door 1 screw", "the centre of the silver screw right of the text DOOR 1", (339, 1174)),
    ("Door 1, BOTTOM pin (-)", "the opening of the bottom pin of the small blue block at the very bottom", (162, 1259)),
]
READER_CLICKS = [("-", "the LEFT wire slot"), ("+", "the 2nd slot from the left"),
                 ("A", "the 3rd slot from the left"), ("B", "the RIGHT wire slot")]


# ---------------------------------------------------------------- answer key (no window needed)

def load_shot_list(session_dir: Path, session_no: int) -> str:
    """The shot list for this session, saved next to the photos the first time so it never changes."""
    path = session_dir / "shot_list.txt"
    if path.exists():
        return path.read_text(encoding="utf-8")
    text = subprocess.run([sys.executable, "-X", "utf8", str(RANDOM_WIRINGS)], capture_output=True,
                          encoding="utf-8", check=True).stdout
    first = text.strip().splitlines()[0]
    if first.strip() != f"SESSION {session_no}":
        raise SystemExit(f"random_wirings.py prints '{first}', but this is session {session_no}. "
                         f"Set SESSION = {session_no} in it, run again, then set it back.")
    path.write_text(text, encoding="utf-8")
    return text


def parse_shot_list(text: str):
    """Returns (ctrl, rdr). ctrl[n] = (module, {colour: pin}, other) where other = (colour, pin) or None.
    rdr[n] = {colour: terminal}."""
    ctrl, rdr = {}, {}
    for line in text.splitlines():
        m = re.match(r"ctrl wiring (\d+) on (READER\d): (.*?)(?:\s+\+ other cable's (\w+) → (\d+)\b.*)?$", line.strip())
        if m:
            wiring = {c: int(p) for c, p in re.findall(r"(\w+) → (\d+)", m.group(3))}
            other = (m.group(4), int(m.group(5))) if m.group(4) else None
            ctrl[int(m.group(1))] = ("A1610." + m.group(2), wiring, other)
            continue
        m = re.match(r"rdr\s+wiring (\d+): (.*)$", line.strip())
        if m:
            rdr[int(m.group(1))] = {c: t for c, t in re.findall(r"(\w+) → ([-+AB])", m.group(2))}
    return ctrl, rdr


def controller_truth(row: str, ctrl) -> dict:
    """{(module, pin number): {colour, cable, ...}} for one controller row; pins not listed hold no wire."""
    truth = {}
    if row in ("U1", "U2"):                                   # the two unclear cases: correct wiring, plus...
        for c, p in CORRECT.items():
            truth[("A1610.READER1", p)] = {"colour": COLOUR[c], "cable": "this"}
        if row == "U1":                                       # green and yellow both pushed into 3 A, pin 4 empty
            del truth[("A1610.READER1", 4)]
            truth[("A1610.READER1", 3)].update(second_colour=COLOUR["yellow"], unclear="two wires in one pin")
        else:                                                 # yellow only half inserted in 4 B
            truth[("A1610.READER1", 4)]["unclear"] = "wire half inserted"
        return truth
    module, wiring, other = ctrl[int(row[1:])]
    for c, p in wiring.items():
        truth[(module, p)] = {"colour": COLOUR[c], "cable": "this"}
    if other:
        oc, op = other
        truth[(module, op)] = {"colour": COLOUR[oc], "cable": "other"}
        for c, p in OTHER_CABLE_ON_READER2.items():
            if c != oc:
                truth[("A1610.READER2", p)] = {"colour": COLOUR[c], "cable": "other"}
    return truth


# ---------------------------------------------------------------- building the annotation

def map_pins(template: dict, template_pts, photo_pts) -> dict:
    """Template pin positions -> photo positions, through the homography the 4 point pairs define."""
    H = cv2.getPerspectiveTransform(np.float32(template_pts), np.float32(photo_pts))
    out = {}
    for module, pins in template["modules"].items():
        pts = np.float32([[p["x"], p["y"]] for p in pins]).reshape(-1, 1, 2)
        out[module] = [tuple(map(float, q)) for q in cv2.perspectiveTransform(pts, H).reshape(-1, 2)]
    return out


def controller_annotation(image_rel, info, template, pin_xy, truth, size, clicks) -> dict:
    w, h = size

    def terminals(module):
        result = []
        for i, pin in enumerate(template["modules"][module]):
            t = {"name": pin["name"], "colour": NO_WIRE}
            t.update(truth.get((module, i + 1), {}))
            x, y = pin_xy[module][i]
            t["x"], t["y"] = round(x / w, 4), round(y / h, 4)
            result.append(t)
        return result

    label_module = "A1610.READER1"                           # the module the cable's label names
    return {
        "image": image_rel, "end": "controller", "module": label_module,
        "label": CABLE_LABEL + "CTRL", "label_visible": True,
        "terminals": terminals(label_module),
        "other_modules": [{"module": m, "in_view": True, "terminals": terminals(m)}
                          for m in template["modules"] if m != label_module],
        "row": info["row"], "light": info["light"], "view": info["view"], "source": info.get("source", "camera"),
        "clicks": [[round(x, 1), round(y, 1)] for x, y in clicks], "method": "4 landmarks + template homography",
    }


def reader_annotation(image_rel, info, wiring, clicks, size) -> dict:
    w, h = size
    by_terminal = {t: COLOUR[c] for c, t in wiring.items()}
    xy = dict(zip([n for n, _ in READER_CLICKS], clicks))   # clicked - + A B, saved in the app's order + - A B
    return {
        "image": image_rel, "end": "device", "module": "A4020E",
        "label": CABLE_LABEL + "DEV", "label_visible": True,
        "terminals": [{"name": "−" if n == "-" else n, "colour": by_terminal.get(n, NO_WIRE),
                       "x": round(xy[n][0] / w, 4), "y": round(xy[n][1] / h, 4)} for n in ["+", "-", "A", "B"]],
        "row": info["row"], "light": info["light"], "view": info["view"], "source": info.get("source", "camera"),
        "clicks": [[round(x, 1), round(y, 1)] for x, y in clicks], "method": "4 clicked slots",
    }


# ---------------------------------------------------------------- the window

class Annotator:
    def __init__(self, session_dir: Path, photos, ctrl, rdr, out_dir: Path):
        import tkinter as tk
        from PIL import ImageTk
        self.tk, self.ImageTk = tk, ImageTk
        self.session_dir, self.photos, self.ctrl, self.rdr, self.out_dir = session_dir, photos, ctrl, rdr, out_dir
        self.board = json.loads((TEMPLATES / "A1610.json").read_text(encoding="utf-8"))
        self.board_img = Image.open(TEMPLATES / self.board["image"]).convert("RGB")
        self.reader_img = Image.open(TEMPLATES / "A4020E.jpg").convert("RGB")
        self.reader_tpl = json.loads((TEMPLATES / "A4020E.json").read_text(encoding="utf-8"))

        self.root = tk.Tk()
        self.root.title("Annotate photos")
        sh = self.root.winfo_screenheight()
        self.view_h = max(500, sh - 220)                  # height of the photo area
        self.guide_w = 330
        top = tk.Frame(self.root)
        top.pack(fill="x")
        self.title = tk.Label(top, font=("Segoe UI", 13, "bold"), anchor="w")
        self.title.pack(fill="x", padx=8, pady=(6, 0))
        self.prompt = tk.Label(top, font=("Segoe UI", 12), anchor="w", justify="left", fg="#0a4")
        self.prompt.pack(fill="x", padx=8, pady=(0, 6))
        body = tk.Frame(self.root)
        body.pack()
        self.canvas = tk.Canvas(body, width=int(self.view_h * 0.75), height=self.view_h, bg="#222", cursor="crosshair")
        self.canvas.pack(side="left")
        self.guide = tk.Canvas(body, width=self.guide_w, height=self.view_h, bg="#333")
        self.guide.pack(side="left")
        self.status = tk.Label(self.root, anchor="w", font=("Segoe UI", 10),
                               text="Enter = save   r = redo   s = skip   q = quit   Esc = back from zoom")
        self.status.pack(fill="x", padx=8, pady=4)
        self.canvas.bind("<Button-1>", self.on_click)
        for key, fn in [("<Return>", self.on_save), ("r", self.on_redo), ("s", self.on_skip),
                        ("q", self.on_quit), ("<Escape>", self.on_escape)]:
            self.root.bind(key, fn)
        self.index = -1
        self.saved = 0
        self.next_photo()

    # -- photo flow
    def next_photo(self):
        self.index += 1
        if self.index >= len(self.photos):
            self.title.config(text=f"Done: {self.saved} saved this time. Close the window.")
            self.prompt.config(text="")
            self.canvas.delete("all")
            return
        self.info = self.photos[self.index]
        self.image = Image.open(self.session_dir / "renamed" / self.info["file"]).convert("RGB")
        self.is_ctrl = self.info["file"].split("_")[1] == "ctrl"
        self.start_photo()

    def start_photo(self):
        self.clicks, self.mode, self.pin_xy = [], "rough", None
        self.show_whole()
        self.ask()

    def ask(self):
        k = len(self.clicks)
        n = len(self.photos)
        self.title.config(text=f"{self.index + 1}/{n}   {self.info['file']}   (row {self.info['row']})")
        if self.is_ctrl:
            name, where, _ = BOARD_LANDMARKS[k]
        else:
            name, where = READER_CLICKS[k]
            name = f"slot {name}"
        step = "rough click" if self.mode == "rough" else "precise click"
        self.prompt.config(text=f"Click {k + 1}/4, {step}: {name}  ({where})", fg="#0a4")
        self.draw_guide(k)

    def show_whole(self):
        self.mode = "rough"
        w, h = self.image.size
        self.scale = self.view_h / h
        self.offset = (0, 0)
        self.show(self.image.resize((int(w * self.scale), self.view_h)))
        for x, y in self.clicks:
            self.mark(x, y, "#ff00ff")

    def show_zoom(self, x, y):
        self.mode = "fine"
        half = 160                                        # photo pixels each side of the rough click
        w, h = self.image.size
        x0, y0 = int(min(max(x - half, 0), w - 2 * half)), int(min(max(y - half, 0), h - 2 * half))
        crop = self.image.crop((x0, y0, x0 + 2 * half, y0 + 2 * half))
        side = min(self.view_h, int(self.view_h * 0.75))
        self.scale = side / (2 * half)
        self.offset = (x0, y0)
        self.show(crop.resize((side, side)))

    def show(self, pil):
        self.photo_tk = self.ImageTk.PhotoImage(pil)
        self.canvas.delete("all")
        self.canvas.config(width=pil.width, height=pil.height)
        self.canvas.create_image(0, 0, image=self.photo_tk, anchor="nw")

    def to_photo(self, cx, cy):
        return self.offset[0] + cx / self.scale, self.offset[1] + cy / self.scale

    def to_canvas(self, x, y):
        return (x - self.offset[0]) * self.scale, (y - self.offset[1]) * self.scale

    def mark(self, x, y, colour, r=6, width=2):
        cx, cy = self.to_canvas(x, y)
        self.canvas.create_oval(cx - r, cy - r, cx + r, cy + r, outline=colour, width=width)

    # -- events
    def on_click(self, e):
        if self.mode == "rough":
            self.show_zoom(*self.to_photo(e.x, e.y))
            self.ask()
        elif self.mode == "fine":
            self.clicks.append(self.to_photo(e.x, e.y))
            if len(self.clicks) == 4:
                self.show_check()
            else:
                self.show_whole()
                self.ask()

    def on_escape(self, _=None):
        if self.mode == "fine":
            self.show_whole()
            self.ask()

    def on_redo(self, _=None):
        if self.index < len(self.photos):
            self.start_photo()

    def on_skip(self, _=None):
        if self.index < len(self.photos):
            with open(self.out_dir / "skipped.txt", "a", encoding="utf-8") as f:
                f.write(self.info["file"] + "\n")
            self.next_photo()

    def on_quit(self, _=None):
        self.root.destroy()

    def on_save(self, _=None):
        if self.mode != "check":
            return
        size = self.image.size
        image_rel = f"photos/{self.session_dir.name}/renamed/{self.info['file']}"
        if self.is_ctrl:
            ann = controller_annotation(image_rel, self.info, self.board, self.pin_xy, self.truth, size, self.clicks)
        else:
            ann = reader_annotation(image_rel, self.info, self.rdr[int(self.info["row"][1:])], self.clicks, size)
        path = self.out_dir / (Path(self.info["file"]).stem + ".json")
        path.write_text(json.dumps(ann, indent=1, ensure_ascii=True), encoding="utf-8")
        self.saved += 1
        self.next_photo()

    # -- the check view
    def show_check(self):
        self.mode = "check"
        if self.is_ctrl:
            tpl_pts = [p for _, _, p in BOARD_LANDMARKS]
            self.pin_xy = map_pins(self.board, tpl_pts, self.clicks)
            self.truth = controller_truth(self.info["row"], self.ctrl)
            rings = [(module, i, xy, self.truth.get((module, i + 1), {}).get("colour", NO_WIRE))
                     for module, pts in self.pin_xy.items() for i, xy in enumerate(pts)]
        else:
            wiring = self.rdr[int(self.info["row"][1:])]
            by_terminal = {t: COLOUR[c] for c, t in wiring.items()}
            rings = [("A4020E", i, xy, by_terminal.get(n, NO_WIRE))
                     for i, ((n, _), xy) in enumerate(zip(READER_CLICKS, self.clicks))]
        xs = [xy[0] for *_, xy, _c in rings] + [c[0] for c in self.clicks]
        ys = [xy[1] for *_, xy, _c in rings] + [c[1] for c in self.clicks]
        w, h = self.image.size
        pad = 120 if self.is_ctrl else 300                # the reader's 4 slots sit in a row: show more around them
        x0, y0 = max(0, min(xs) - pad), max(0, min(ys) - pad)
        x1, y1 = min(w, max(xs) + (pad * 3 if self.is_ctrl else pad)), min(h, max(ys) + pad)
        crop = self.image.crop((int(x0), int(y0), int(x1), int(y1)))
        self.scale = min(self.view_h / crop.height, (self.view_h * 0.9) / crop.width)
        self.offset = (int(x0), int(y0))
        self.show(crop.resize((int(crop.width * self.scale), int(crop.height * self.scale))))
        for module, i, (x, y), colour in rings:
            self.mark(x, y, RING[colour], r=7, width=3)
        for x, y in self.clicks:
            cx, cy = self.to_canvas(x, y)
            self.canvas.create_line(cx - 6, cy, cx + 6, cy, fill="#ff00ff")
            self.canvas.create_line(cx, cy - 6, cx, cy + 6, fill="#ff00ff")
        self.prompt.config(fg="#c40", text="Check: every ring in an opening? Ring colour = the wire in it "
                                           "(white = empty)?   Enter = save   r = redo   s = skip")
        self.guide.delete("all")
        legend = [("#ff2020", "red wire"), ("#000000", "black wire"), ("#00d040", "green wire"),
                  ("#ffe000", "yellow wire"), ("#ffffff", "no wire")]
        self.guide.create_text(10, 16, anchor="w", fill="white", font=("Segoe UI", 11), text="Ring = what the shot list says:")
        for i, (colour, text) in enumerate(legend):
            y = 46 + i * 28
            self.guide.create_oval(14, y - 8, 30, y + 8, outline=colour, width=3)
            self.guide.create_text(42, y, anchor="w", fill="white", font=("Segoe UI", 11), text=text)

    # -- the guide panel: where to click, on the template
    def draw_guide(self, k):
        if self.is_ctrl:
            box = (60, 560, 420, 1320)
            img, target = self.board_img, BOARD_LANDMARKS[k][2]
        else:
            box = (420, 330, 760, 700)
            img = self.reader_img
            pins = {p["name"].replace("−", "-"): (p["x"], p["y"]) for p in self.reader_tpl["modules"]["A4020E"]}
            target = pins[READER_CLICKS[k][0]]
        crop = img.crop(box)
        s = min(self.guide_w / crop.width, self.view_h / crop.height)
        self.guide_tk = self.ImageTk.PhotoImage(crop.resize((int(crop.width * s), int(crop.height * s))))
        self.guide.delete("all")
        self.guide.create_image(0, 0, image=self.guide_tk, anchor="nw")
        gx, gy = (target[0] - box[0]) * s, (target[1] - box[1]) * s
        self.guide.create_oval(gx - 12, gy - 12, gx + 12, gy + 12, outline="#ff00ff", width=3)
        self.guide.create_text(8, int(crop.height * s) + 14, anchor="w", fill="white",
                               text="Click here (pink ring)", font=("Segoe UI", 11))

    def run(self):
        self.root.mainloop()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("session", type=Path, help=r"for example data\photos\session1")
    ap.add_argument("--only", help="annotate just this photo (overwrites its annotation)")
    a = ap.parse_args()

    session_dir = a.session
    session_no = int("".join(ch for ch in session_dir.name if ch.isdigit()) or 1)
    data_dir = session_dir.parents[1]                         # data\photos\session1 -> data
    out_dir = data_dir / "annotations" / session_dir.name
    out_dir.mkdir(parents=True, exist_ok=True)
    ctrl, rdr = parse_shot_list(load_shot_list(session_dir, session_no))

    with open(session_dir / "renamed" / "index.csv", newline="", encoding="utf-8") as f:
        photos = list(csv.DictReader(f))
    skipped = set()
    if (out_dir / "skipped.txt").exists():
        skipped = set((out_dir / "skipped.txt").read_text(encoding="utf-8").split())
    if a.only:
        photos = [p for p in photos if p["file"] == a.only]
    else:
        photos = [p for p in photos if not (out_dir / (Path(p["file"]).stem + ".json")).exists()
                  and p["file"] not in skipped]
    print(f"{len(photos)} photos to annotate; annotations go to {out_dir}")
    if photos:
        Annotator(session_dir, photos, ctrl, rdr, out_dir).run()


if __name__ == "__main__":
    main()
