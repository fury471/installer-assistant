# Package A · notes for the report

Yu · kept up to date as the work goes on. Everything the Perception section of the course report needs: what was
built, why, how it was measured, the numbers, and what went wrong. The day-by-day record is `ml/LOG.md`; this file
is the summary to write the report from. References are given by their key in `docs/references.bib`
(full entries in `docs/references-ieee.md`).

**Status:** M0 done (except session 2) · M1 done (except scanning through the AR camera) · M2 steps 1–4 done
(locate on board and reader, in the service, evaluated); step 5 done (wire or not, zone survey) · M3: pin reader in the service. Last update 29 Oct 2026.

All numbers below are on **session 1**, the photos the methods were developed and tuned on. They are previews.
The report's final numbers come from **session 2**, kept sealed until the final week.

---

## 1. Data (M0)

| What | Value |
| --- | --- |
| Phone | Redmi K80S (EXIF: "Xiaomi REDMI K80"), the phone the app runs on; photos 2304 × 4096 JPEG |
| Session 1 | 22 rows × 4 photos = 88 shot, 80 kept after removing two wrong first takes: 56 controller (board), 24 reader |
| Rows | 12 controller wirings + 2 unclear cases (U1: two wires in one pin; U2: wire half inserted) + 6 reader wirings |
| Wirings | Random: 0, 1 or 2 mistakes (swap / move) per row, weights 20/50/30 (`ml/tools/random_wirings.py`, fixed seed per session). Per session 2 rows with the whole cable on the wrong module (zone check) and 2 rows with a second labelled cable's wire on the module (bundle check) |
| Conditions | room light / flash × front / tilted (~30°). Dim light and a second phone were planned but not shot |
| Answer key | the shot list (`shot_list.txt`), frozen per session; wire colours red, black, green, yellow |
| Annotation | `ml/tools/annotate.py`: 4 clicks per board photo, all 24 pins placed by a homography from the template (`hartley2004`); 4 clicked slots per reader photo. 80/80 photos annotated, each checked by eye |
| Templates | `A1610.json`: 24 pins (Reader 1, Reader 2, Door 1, Door 2); `A4020E.json`: 4 wire slots; both on 1200 × 1600 reference photos with all pins empty |

Known flaws (say so in the report): R05's flash-tilt photo is a crop of its flash-front photo (marked `edited`,
keep both on the same side of any split); two reader photos are motion-blurred (kept); only one phone and two
lighting conditions; the annotations themselves carry click error of roughly 0.1–0.2 pin spacings.

## 2. Reading the cable label (M1)

**Built:** QR decoding in the app with ZXing.Net 0.16.11 (Apache-2.0), on a worker thread so the camera picture
stays smooth. Label format v2: `IA|<cable>|<door>|<controller module>|<device module>|CTRL/DEV`, QR 21.5 mm with
0.58 mm modules, error correction M.

**Decode rate**, phone camera (K80S via iVCam over USB, 1920 × 1080, room light), 10 tries each:

| Distance | 20 cm | 25 cm | 30 cm | 35 cm |
| --- | --- | --- | --- | --- |
| Read | 10/10 | 10/10 | 6/10 | 0/10; 10/10 after tapping to focus |

Laptop webcam (1280 × 720): only up to about 15 cm. On session photos shrunk to 640 px (ARCore's default size), no
label was read.

**Finding:** focus limits the reading distance more than pixels do (35 cm read every time once focused). ARCore
uses fixed focus by default, so the AR camera should request auto-focus (to do with Xiaoxin).
**Decision:** keep the labels; the app asks the installer to hold the phone 20 to 30 cm from the label, says
"Move closer to the label" instead of "Label missing" when nothing is found, and searches 6 s instead of 2 s.
The roadmap target (9 in 10 at 45 cm) is not met with these labels; a 1.0 mm module (about 37 mm QR) would roughly
double the distance and was rejected to keep the labels small.

## 3. Finding the pins (M2, `ml/perception_service/locate.py`)

### Method

Two stages, coarse to fine:

1. **Whole device.** Features of the template and the frame are paired and a homography fitted with MAGSAC++
   (`barath2020`), a refinement of RANSAC (`fischler1981`); the template's pins are carried into the frame by the
   homography (`hartley2004`). Two matchers, chosen per template in its JSON (no code change for a new device):
   - **ORB** (`rublee2011`) + Lowe's ratio test (`lowe2004`) for the A1610 board: fast, and its printed surface
     stays mostly visible.
   - **DISK** features (`tyszkiewicz2020`) + **LightGlue** matching (`lindenberger2023`) for the A4020-E reader,
     from the LightGlue-ONNX export (Apache-2.0), run with onnxruntime (no PyTorch). Model downloaded by
     `ml/tools/get_models.py`, checked by SHA-256.
2. **Each terminal block (board only).** The blocks stand above the circuit board, so a homography fitted to the
   board puts their pins off by parallax (about 0.7 pin spacings). The frame is warped into the template's view and
   the column of blocks found again by normalised cross-correlation (`lewis1995`); then each block is fine-tuned
   within 5 px. Matching the whole column first prevents a block from slipping by one (identical-looking) pin.

Frames are shrunk to 1920 px first (the agreed camera size). Too few agreeing pairs → "not found", never a guess
(15 for the board, 30 for the reader). The same frame always gives the same answer (fixed random seed).

### Measures

Pin error = distance from the annotated pin, in pin spacings measured in each photo (1.0 = one pin off), so it does
not depend on distance or resolution. A pin counts as right when it is within half a spacing: then the right pin
is read. Roadmap target: 80 % of pins right. Tool: `python -m ml.eval.eval_locate`.

### Results (session 1, Yu's laptop: Windows, OpenCV 5.0.0, frames as RGB like the service)

| Device | Frame | Found | Median error | Pins right | Photos all right | Time per frame |
| --- | --- | --- | --- | --- | --- | --- |
| Board (ORB + blocks) | 1920 | 56/56 | 0.14 | 98.4 % | 52/56 | 48 ms |
| Board | 640 | 50/56 | 0.16 | 83.0 % | 38/56 | 27 ms |
| Reader (DISK + LightGlue) | 1920 | 22/24 | 0.35 | 81.2 % | 15/24 | ~0.9 s |

The 640 row supports asking for a 1920 × 1080 camera image instead of ARCore's default 640 × 480.

### What was tried and changed (each is a result for the report)

| Problem | Tried | Outcome |
| --- | --- | --- |
| Board, stage 1 only: pins about 0.7 spacings off | Stage 2: blocks found again by cross-correlation | Typical error 0.72 → 0.13 spacings |
| Stage 2, blocks matched one by one: on C02 (two labels hide the board) Reader 1 slipped by a whole pin | Match the column of blocks first, then each block within 5 px; MAGSAC++ instead of RANSAC | Pins right 96 % → ~99 %, photos all right 50 → 52–56 of 56 |
| Same code, different OpenCV build (Linux 56/56 vs. laptop 52/56 photos all right) | Inspected the differing photos | Not a bug: 8 borderline pins (0.5–0.6 spacing, on the right opening's edge). No more tuning on session 1, to avoid fitting the method to these photos |
| Reader with ORB: found on 5/24 photos | Look at the matches | The flag label hides most of the reader, and its QR code takes most of ORB's features |
| Reader, alternative 1: find the light-green terminal block by colour (colour cue, cf. `swain1991`), then align the template | Evaluated | Room light 12/12 right, flash 3/12 (colour washes out). Rejected |
| Reader, alternative 2: learned features + LightGlue | Evaluated; per-template matcher | Found 23/24, flash works too; ~0.9 s per frame on the laptop CPU (accepted within the 1–3 s budget) |
| LightGlue's points come from a 480 × 640 picture, so a 3 px fit tolerance at 1920 was too strict | Scale the tolerance with the picture | Slots right 76 % → 82 % |
| Reader: stage 2 like the board | Evaluated | Worse; not used |
| One reader photo off by up to 2.5 slots (r03 flash tilt, only 19 agreeing pairs; all correct photos had 34 or more) | Reader asks for at least 30 agreeing pairs | That frame now says "not found". Threshold chosen on one case: re-check on session 2 |

### Speed of the whole read (service, one frame)

On a slow 2-core test machine: locate ~0.1 s (board), reading 4 modules ~0.1 s, wire tracing ~0.7 s; total ~0.9 s
for the controller end, ~3.5 s for the reader end. The laptop is about 2–3× faster (reader locate 0.9 s), inside
the 1–3 s budget. Possible speed-ups if needed: compute the template's LightGlue features once; search only near
the label.

### Limits to state honestly

- Reader end: 15/24 photos fully right; 7 of the 9 others are borderline (worst slot 0.5–0.73 spacings off).
  Live use helps (several frames, the phone moves), but the colour step must read where the wire is clearly visible.
- Tuned on session 1; only one phone, one bench, two lighting conditions.
- The reader method needs the LightGlue model (50 MB) on the laptop and is about 20× slower than the board's.

## 3b. Wire or no wire, first colour results (M2 step 5, start of M3)

**Pin patches:** the frame is warped into the template's view with locate()'s homography, so every pin's
surroundings appear at the same size and the same way up whatever the phone's pose; a fixed window per pin is cut
where its wire leaves (board: right of the opening; reader: below the slot; set in the template JSON).
Data set: 1440 labelled patches from session 1 (`ml/eval/build_patches.py`).

**Results** (session 1, cross-validated with whole wiring rows held out, so no row is both trained and tested):

| Reader of the patch | Board pins right | Wire or not | Missed wires | False wires | Colour on wired pins |
| --- | --- | --- | --- | --- | --- |
| Existing baseline (median colour, nearest of 8 references, no "empty" class) | 6 % | — | — | — | — |
| Extra-trees on the 20 x 7 CIELAB patch (prototype) | 97.1 % | 97.4 % | 35 / 252 | 0 | 84.5 % |

Zone survey (does a module hold any wire?): 223 / 224 modules right.

**In the service** (`pin_reader.py`, trained by `ml/eval/train_pins.py`, one model per template, confidences
temperature-scaled on the held-out predictions, `guo2017`; extra-trees: `geurts2006`):

| Template | Pins | Pins right | Wire or not | Missed / false wires | Colour on wired pins | ECE before → after | Confident (≥ 0.8): share answered, right, wrong |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A1610 board | 1344 | 97.1 % | 97.4 % | 35 of 252 / 0 | 84.5 % | 0.048 → 0.007 | 93.8 %, 99.0 %, 13 |
| A4020E reader | 88 | 80.7 % | 100 % | 0 / 0 | 80.7 % | 0.206 → 0.082 | 53.4 %, 91.5 %, 4 |

The 13 confident errors on the board are the dangerous case (wrong and sure); below 0.8 the app asks the
installer. The reader model never saw an empty slot, so it cannot report a missing wire at the reader end yet. Missed wires are mostly green (on the
green/blue housing) and black, many in the tangled rows. Reader end: 78 % colour on 88 slots, too little data.

## 4. Open (to fill in later)

- [ ] Session 2: final numbers for all tables above (`eval_locate --session session2`, both ends).
- [ ] Camera size the K80S actually sends (service log line `… WxH image …`).
- [ ] Live check in the app at the bench: do the dots stay on the pins at 30–60 cm?
- [ ] M3 colours: accuracy, confusion matrix, calibration (ECE), "no wire" detection.
- [ ] Decode rate through the AR camera with auto-focus.

## 5. Tools that produced the numbers

| Tool | What it does |
| --- | --- |
| `ml/tools/random_wirings.py` | Shot list (answer key) per session |
| `ml/tools/rename_photos.py` | Names photos by row and condition from the slates and EXIF flash |
| `ml/tools/annotate.py` | 4-click annotation with template homography |
| `ml/eval/eval_locate.py` | Pin error, pins right, photos all right, time; `--size`, `--end device` |
| `ml/tools/locate_demo.py` | Pictures of the matches and the found pins for one photo |
| `ml/tools/send_frame.py` | Sends one photo to the running service like the app |
| `data/_downloads/bench_lightglue.py` | LightGlue speed on this laptop |
