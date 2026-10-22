"""Give every session photo a name that says what is in it.

The phone names photos by time (IMG_20261022_165716.jpg). This script turns them into
names like  s1_ctrl_c05_flash_tilt.jpg  using two facts:
  1. slates.csv says which slate photo starts which row (C01, R03, U1, ...).
  2. The check sheet order is: room front, room tilt, flash front, flash tilt.
     So the 1st and 3rd photo of a row are "front", the 2nd and 4th are "tilt".
  3. Each photo remembers whether the flash really fired (EXIF), so the light in the
     name is what actually happened, even if the flash failed. index.csv also keeps
     the planned slot from the sheet.

It only COPIES. The raw folder is never changed, so you can always run it again.

Run from the project folder:
    python ml\\tools\\rename_photos.py data\\photos\\session1 --dry-run   (only print)
    python ml\\tools\\rename_photos.py data\\photos\\session1             (copy for real)
Output: <session>/renamed/*.jpg and <session>/renamed/index.csv (new name -> raw file).
A raw file with "_crop" in its name is an edited copy, not a camera photo; index.csv marks
it "edited" so the evaluation can keep it out of the test set.
"""
import argparse
import csv
import shutil
from pathlib import Path

from PIL import Image

FLASH_TAG = 0x9209       # EXIF "Flash"; bit 0 is 1 when the flash fired
EXIF_SUB_IFD = 0x8769    # the part of EXIF where camera settings live


def flash_fired(path):
    exif = Image.open(path).getexif()
    value = exif.get_ifd(EXIF_SUB_IFD).get(FLASH_TAG, 0)
    return bool(value & 1)


def read_slates(session):
    with open(session / "slates.csv", newline="", encoding="utf-8") as f:
        return {r["slate_file"]: r for r in csv.DictReader(f)}


def new_name(session_no, row, light, view):
    # view can be "tilt_2" when a row has two room-tilt photos (the flash failed)
    kind = "rdr" if row.startswith("R") else "ctrl"          # R rows are the reader end
    return f"s{session_no}_{kind}_{row.lower()}_{light}_{view}.jpg"


def plan(session):
    """Return a list of (raw_file, new_name or None, row, light, view, planned)."""
    session_no = "".join(ch for ch in session.name if ch.isdigit()) or "1"
    slates = read_slates(session)
    photos = sorted((session / "raw" / "redmi").glob("*.jpg"))   # sorted = time order
    slots = ["room_front", "room_tilt", "flash_front", "flash_tilt"]   # check sheet order
    result, row, keep, count, used = [], None, False, 0, set()
    for p in photos:
        if p.name in slates:                                    # a slate starts a new row
            row = slates[p.name]["row"]
            keep = slates[p.name]["keep"].strip().lower() == "yes"
            count = 0
            continue
        if row is None:
            result.append((p.name, None, "-", "-", "before first slate", "-"))
            continue
        count += 1
        planned = slots[count - 1] if count <= len(slots) else "extra"
        light = "flash" if flash_fired(p) else "room"
        view = "front" if count % 2 == 1 else "tilt"
        name = None
        if keep:
            name = new_name(session_no, row, light, view)
            n = 2
            while name in used:                        # same light and view twice in one row
                name = new_name(session_no, row, light, f"{view}_{n}")
                n += 1
            used.add(name)
        result.append((p.name, name, row, light, view, planned))
    return result


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("session", type=Path, help=r"for example data\photos\session1")
    ap.add_argument("--dry-run", action="store_true", help="only print what would happen")
    args = ap.parse_args()

    rows = plan(args.session)
    out = args.session / "renamed"
    names = [n for _, n, *_ in rows if n]
    doubles = {n for n in names if names.count(n) > 1}
    if doubles:
        raise SystemExit(f"Two photos would get the same name: {sorted(doubles)}. Check slates.csv.")

    if not args.dry_run:
        out.mkdir(exist_ok=True)
    for raw, name, row, light, view, planned in rows:
        print(f"{raw}  ->  {name or 'skipped (' + ('keep=no' if row != '-' else view) + ')'}")
        if name and not args.dry_run:
            shutil.copy2(args.session / "raw" / "redmi" / raw, out / name)
    if not args.dry_run:
        with open(out / "index.csv", "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["file", "raw_file", "row", "light", "view", "planned", "source"])
            for raw, name, row, light, view, planned in rows:
                if name:
                    source = "edited" if "_crop" in raw else "camera"
                    w.writerow([name, raw, row, light, view, planned, source])
    print(f"\n{len(names)} photos {'would be' if args.dry_run else 'were'} copied to {out}")


if __name__ == "__main__":
    main()
