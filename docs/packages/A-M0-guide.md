# M0 guide · Photos and template (21–24 Oct)

Yu · package A. The step-by-step version of milestone M0 in your roadmap. Read it once from top to bottom, then work through it day by day. Each step ends with **Result:** what you should have when it is done.

## Why M0 comes first

Everything you build later needs real pictures of the bench:
- `locate()` (M2) needs a **template**: one photo of the board where you know where every pin is.
- The colour reader (M3) needs photos with known wire colours to learn from and to be tested on.
- The report needs numbers measured on photos you never tuned on.

**The final test is live, not photos.** On test day, users hold this phone (the Redmi K80S) and run the real-time AR app on the real bench: the camera stream runs live, markers stay on the pins while the phone moves (Xiaoxin's anchors), and each reading goes phone → your service → phone in 1–3 s. The photos in M0 are only for building (the template) and for measuring accuracy; nothing in the final app reads a stored photo. From M2 on, every milestone is also checked live in the app on this phone.

So M0 produces two things: a **template** and **two labelled photo sets**. Session 1 is for building and tuning. Session 2 is shot the same day, after a break and with the setup changed, and stays unopened until the final week: it is the final test.

## What you will have at the end of M0

| # | Result | Where it goes |
| --- | --- | --- |
| 1 | v2 labels printed and on the bench cable (C01) and the spare wires (C02) | on the bench |
| 2 | The camera resolution the app will use, agreed with Xiaoxin | one line in your log |
| 3 | Reference photos: the whole A1610 board and the A4020-E terminal row, pins empty | `ml/perception_service/templates/A1610.jpg`, `A4020E.jpg` (under 1 MB each, committed) |
| 4 | Template files: every pin's position on those two photos | `ml/perception_service/templates/A1610.json`, `A4020E.json` (committed) |
| 5 | Session-1 photos, about 150 with the Redmi (plus about 20 with the iPhone), renamed | shared drive: `data/photos/session1/` (never in Git) |
| 6 | One annotation file per session-1 photo | shared drive: `data/annotations/session1/` |
| 7 | Session-2 photos, backed up and **not opened** | shared drive: `data/photos/session2/raw/` (renamed and annotated only in the final week) |
| 8 | A short log of what you did and anything that went wrong | `ml/LOG.md` (committed) |

## Day 1 · Prepare (Wed 21 Oct, about 1.5 hours)

**1. Print.**
- `flag_labels_v2_C01_C02.pdf` at 100 % (not "fit to page"). Throw the old labels away; the app rejects them.
- `bench_check_sheet_v2.pdf` (5 pages).

*Result:* labels and check sheet on paper.

**2. Agree the camera resolution with Xiaoxin.**
Send her a message like: *"For my test photos and template I need to know the size of the camera image the app will hand to my service. ARCore gives 640 × 480 by default. Can we use a higher-resolution configuration, at least 1920 × 1080?"*
Why it matters: at 640 × 480 a pin on the board is only a few pixels wide, and both finding the pins and reading the colours get much harder. Your photos can always be shrunk later to the app's size; they can never be enlarged. So take all photos at full resolution, whatever she answers.

*Result:* the resolution written in the check sheet and in your log.

**3. Check the shot list matches the sheet.**
In the project folder:
```
conda activate installer-ml
python ml\tools\random_wirings.py
```
The lines must be the same as the session 1 pages of the check sheet. Save the list as the answer key:
```
python -X utf8 ml\tools\random_wirings.py > shot_list_s1.txt
```
Then set `SESSION = 2` in the script, run it again into `shot_list_s2.txt`, and set it back to 1. Put both text files on the shared drive next to the photos later.

*Result:* `shot_list_s1.txt` and `shot_list_s2.txt`.

**4. Book the bench and the people for one long day.** Session 1 in the morning, session 2 in the afternoon: about 2 hours each plus a break of at least an hour, ideally with a second person (one rewires and checks, one photographs).

**5. Start your log.** Create `ml/LOG.md` with today's date and one line per thing you did. This becomes your report section later.

## Day 2 · At the bench, both sessions (Thu 22 Oct, about 6 hours with the break)

Bring: the Redmi K80S (the app phone) and your iPhone, both charged, the check sheet, labels, clear tape, the spare wires, blank paper and a thick pen, a desk lamp.

**6. Set up.**
- Fold a C01 pair onto the bench cable. READER END near the reader. CONTROLLER END where the wires **fan out** at the A1610, so the label and the terminals are in the same picture. Cover with clear tape.
- Fold the C02 CONTROLLER END label onto one or two spare wires. Keep them out of view until a "+ C02 wire" row.
- Phone settings: normal photo mode (not the 48 MP mode), 1× zoom, no portrait mode, no filters, flash off.
- **Which phone for what:** everything is taken with the **Redmi**, the phone the app runs on. The iPhone is a small extra: **one photo per row, room light, front**, about 20 per session, so the report can say how the reader does on a second phone. If the day runs late, skip the iPhone and note it in your log.

**7. Reference photos first** (with the wires removed, so all pins are empty).
- Rest the Redmi on something steady (a box, a stack of books), lens parallel to the board, **square on**.
- Room light, no flash, no strong reflections on the plastic.
- Fill most of the picture with the **whole A1610 board**: Reader 1, Reader 2 and Door 1 must all be in it, with some board around them.
- Take 3 photos; keep the sharpest. Zoom in on the phone and check that the pin print ("IO7", "+12V") is readable.
- The same for the **A4020-E terminal row**.

*Result:* 2 good reference photos taken with the Redmi (they become the template).

**8. Shoot session 1, row by row.** Follow pages 2–3 of the check sheet. For every row:
1. Wire exactly as the row says. The second person checks it against the sheet.
2. Photograph the **slate**: a paper with the row ID in big letters, e.g. "S1 C03". It marks where each row starts when you sort the photos later.
3. Take the 6 shots with the Redmi: room light front, room light tilted (about 30°), flash front, flash tilted, dim front, dim tilted. Then the one iPhone shot (room light, front). Tick each box.
4. Special rows:
   - **"ON READER 2"**: move the whole cable to the green block, same pin numbers; Reader 1 stays in the picture.
   - **"+ C02 wire"**: put one C02 wire on the named pin, its label nearby; both labels in view.
   - **U1, U2**: the two unclear cases (two wires in one pin; one wire half pushed in).
5. Distance as in the row (30 or 50 cm). The board and the C01 label must be in the picture.

Then the reader end (R01–R06), and the QR distance shots with the Redmi (the label at 30, 45 and 60 cm).

**First check, before the rest:** after the reference photo of the board and row C01, send me 3 photos as original files (copied off the phone, not through a messaging app): the board reference photo, C01 room front, and C01 dim tilt. I check the framing, sharpness, label and resolution; if they are fine, carry on with C02.

**How many photos per session (Redmi):** 20 rows × 6 = 120, plus 20 slates and 3 QR shots, plus about 6 reference tries in session 1: about 150. iPhone: about 20.

**9. Back up session 1 before the break.** Copy every photo from both phones to the shared drive, `data/photos/session1/raw/redmi/` and `.../iphone/`, as original files (by cable, or as "original" in your cloud app; messengers compress photos). Count them: about 150 from the Redmi, about 20 from the iPhone.

*Result:* all session-1 photos safely on the shared drive, pages 2–3 of the check sheet fully ticked.

**10. Break, then change the setup.** At least an hour, so the daylight in the room changes. Do not look at the session-1 photos. Then make session 2 look different from session 1, so the final test is not a near-copy of what you tuned on:
- move or turn the bench, and change what is behind it;
- re-route the cable and take the labels off and fix them again;
- use the session-2 pages of the check sheet (different wirings).

Why this matters: photos from one sitting share the light, the white balance and even the cable's bends. If the test photos are near-copies of the tuning photos, the numbers look better than the reader really is.

**11. Shoot session 2** exactly like session 1, with pages 4–5 of the check sheet, and back it up to `data/photos/session2/raw/`. **Do not open, rename or annotate these photos until the final week (11 Nov).**

*Result:* session-2 photos backed up and sealed. Note in your log that both sessions were shot the same day, and what you changed between them; this goes into the report as a limitation.

## Day 3 · Template (Fri 23 Oct, about 2 hours)

**12. Make the template images.** The repository only takes photos under 1 MB, and the pin positions must be measured on exactly the file you commit. So shrink first, then mark the pins. In the project folder:
```
python -c "from PIL import Image; im=Image.open(r'PATH\TO\board_photo.jpg'); im.thumbnail((1600,1600)); im.save(r'ml\perception_service\templates\A1610.jpg', quality=88); print(im.size)"
```
Do the same for the reader photo as `A4020E.jpg`. Note the printed sizes (width, height). Create the folder `ml\perception_service\templates` first if it does not exist.

**13. Mark the pins.** Open `A1610.jpg` in Paint. When you move the mouse over the picture, Paint shows the pixel position at the bottom left. For each pin, point at the **centre of the terminal's opening, where the wire goes in**, and write down x and y. Do this the same way for every pin; consistency matters more than perfection.

Write them into `ml\perception_service\templates\A1610.json` in this shape (pin order 1 to 8 as printed on the board, bottom to top on our photos; the names are the ones the app uses):
```json
{
  "image": "A1610.jpg",
  "width": 1600,
  "height": 1200,
  "modules": {
    "A1610.READER1": [
      {"name": "−", "x": 0, "y": 0},
      {"name": "12V",    "x": 0, "y": 0},
      {"name": "A",      "x": 0, "y": 0},
      {"name": "B",      "x": 0, "y": 0},
      {"name": "O1",     "x": 0, "y": 0},
      {"name": "O2",     "x": 0, "y": 0},
      {"name": "IO7",    "x": 0, "y": 0},
      {"name": "IO8",    "x": 0, "y": 0}
    ],
    "A1610.READER2": [ "the same 8, named − 12V A B O3 O4 IO9 IO10" ],
    "A1610.DOOR1":   [ "4 pins, named − IO5 − IO6" ]
  }
}
```
- `"−"` is the minus sign the app uses for ground; type it exactly like that.
- Replace the placeholder lines for Reader 2 and Door 1 with real entries like Reader 1's.
- `A4020E.json` is the same with one module, `"A4020E"`, and four terminals in the order `+`, `\u2212`, `A`, `B` (the app's order; on the reader itself they sit − + A B from left to right, so take care).

**14. Check the template by eye.** Draw the points on the picture and look:
```
python -c "import json,cv2; t=json.load(open(r'ml\perception_service\templates\A1610.json')); im=cv2.imread(r'ml\perception_service\templates\A1610.jpg'); [cv2.circle(im,(p['x'],p['y']),6,(255,0,255),-1) for m in t['modules'].values() for p in m]; cv2.imwrite('template_check.png',im)"
```
Open `template_check.png`: every dot must sit in a terminal opening, none missing, none doubled. Do not commit `template_check.png`.

**15. Commit the template.**
```
git add ml/perception_service/templates ml/LOG.md
git commit -m "Board and reader templates for locate()"
```

*Result:* two template images and two JSON files in the repository, checked by eye.

## Day 4 · Sort and annotate (Sat 24 Oct, about 3 hours)

**16. Sort and rename the session-1 photos.** Each row starts with its slate photo, so the order of the photos tells you which row and condition each one is. Rename them to the pattern on the check sheet, e.g. `s1_ctrl_c03_flash_tilt_rm.jpg` (`rm` Redmi, `ip` iPhone). Doing about 170 by hand is slow and error-prone; we will write a small script for it together, using the slates. Send me 2–3 photo file names from each phone first, so the script fits how your phones name files.

**17. Annotate session 1.** For every photo, one JSON file in the format at the top of `ml/eval/evaluate.py`: which colour sits on which pin, the module, the label, and for tangled rows `"cable": "other"` on the C02 wire's pin.
- You do **not** need to type the colours: they are already in your shot list (the answer key).
- You do **not** need to click 20 pins per photo: you click 4 well-spread pins that you can see clearly (for example pin 1 and pin 8 of Reader 1 and of Reader 2), and the script maps the template's pins onto the photo through those 4 points (a homography, the same idea as `locate()`). It then draws all pins so you can check them, and writes the file.

We will write this annotation helper together after the shoot (about 60 lines); it is good practice for M2, because it is the same geometry. Colour codes, if you ever need them by hand: 0 red, 1 black, 2 green, 3 yellow, 4 white, 5 brown, 6 blue, 7 orange, −1 no wire.

*Result:* `data/annotations/session1/` with one JSON per photo, each checked by its overlay picture.

**18. Close M0.** Write in your log: number of photos per phone, anything that went wrong (a slipped wire, a retaken row), the agreed camera resolution and the QR distance results.

## M0 is done when

- [ ] `templates/A1610.json` and `A4020E.json` exist, are committed, and every dot sits on its pin in `template_check.png`
- [ ] session-1 photos are on the shared drive, renamed: about 150 Redmi, about 20 iPhone
- [ ] every session-1 photo has an annotation file
- [ ] session-2 photos are backed up on the shared drive and still unopened
- [ ] the log says what you did and what went wrong

**If you get stuck for half a day** (for example the bench is not available): photograph what you can with one phone, build the template first (M2 needs only the template to start), and annotate the rest while M2 is under way.

## References

- Hartley, R., & Zisserman, A. (2004). *Multiple View Geometry in Computer Vision* (2nd ed.). Cambridge University Press. Ch. 4: estimating a homography from point correspondences (used for the 4-click annotation and for `locate()`).
- Google. ARCore: camera image configurations, default CPU image 640 × 480. [developers.google.com](https://developers.google.com/ar/develop/java/machine-learning)
