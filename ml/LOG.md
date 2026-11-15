# Package A log (Yu)

> **Current bench hardware — 2 Nov 2026 (confirmed by Xiaoxin): AXIS A1610 controller + A4020-E reader.** Keep `A1610.READER1` / `A1610.READER2` label and record IDs. Earlier A1601 bench measurements and the `A1610_A1601.jpg` reference variant retain their original provenance; they are not new A1610 results. Collect a separate batch after the hardware change.


## 2026-10-22 · M0 session 1, templates, annotations

**Session 1 shoot**
- Redmi K80S only (the photos' EXIF says "Xiaomi REDMI K80"), 2304 × 4096 JPEG. 22 rows (C01–C12, U1, U2, R01–R06), 4 photos per row: room front, room tilt, flash front, flash tilt.
- Not taken: the dim photos and the iPhone photos (planned but dropped, time). The QR distance shots were not taken; QR read live at up to about 30–35 cm.
- C02 and C04 were wired wrongly at first and retaken at the end; the first tries are excluded (`slates.csv`, keep = no).
- The C02 slate says "S2" by mistake; it is session 1.
- R05: the flash tilt photo was missing. A crop of the flash front fills that slot, marked `source = edited` in `index.csv`; keep R05 on one side of any split.
- Blurred (phone moved): `s1_rdr_r04_room_tilt`, `s1_rdr_r06_room_tilt`. Kept.
- The 12V wire is red, not orange; `random_wirings.py` now says red (same random wirings).

**Renaming**
- `ml/tools/rename_photos.py` with `slates.csv`: 80 photos in `data/photos/session1/renamed/`, with `index.csv`.

**Templates**
- Reference photos with empty pins: `data/photos/reference/` (copies sent through chat, 1932 × 2576).
- `templates/A1610.jpg/json` (Reader 1, Reader 2, Door 1, Door 2: 24 pins) and `A4020E.jpg/json` (4 wire slots), 1200 × 1600, checked by eye.

**Annotation**
- `ml/tools/annotate.py`: 4 clicks per photo, homography from the template for the board; colours from the frozen shot list (`shot_list.txt`).
- 80 / 80 photos annotated, none skipped, all checked by eye. Files in `data/annotations/session1/`.

**Open**
- Session 2 on another day (same 4 photos per row), then rename and annotate; keep it sealed until the final test.
- Camera resolution: agreed with Xiaoxin to use the largest AR camera configuration up to 1920 × 1080 (ARCore
  default is 640 × 480). Write the size the K80S actually gives here once she reports it.

## 2026-10-23
- Door 2 added to `AxisCatalog.cs` (green 4-pin block, − IO11 − IO12); domain tests updated, all pass. Agreed with the team.
- QR scanning in the app: ZXing.Net 0.16.11 added (`Assets/_Project/ThirdParty/ZXing/`), `ZXING_NET` set for Android
  and Windows. Live mode in the Editor read a printed label through the laptop's own camera. Offline check on session
  photos: C01 and R01 labels read at full size and at 1920 px, nothing at 640 px; not read with two labels in view
  (C02) or with flash glare and tilt (C05). Next: decode rate, 10 tries each at 30, 45 and 60 cm.
- QR reading distance with the phone camera (K80S via iVCam over USB, 1920 × 1080, room light): reads up to about
  25–30 cm. Laptop camera (1280 × 720): only up to about 15 cm. Reason: the QR's squares are 0.58 mm; the decoder
  needs about 2–3 camera pixels per square. Decision: keep the labels; the installer scans the label up close
  (20–30 cm), then steps back for the pin check.
- Decode rate, phone camera (K80S via iVCam over USB, 1920 × 1080, room light), 10 tries each:

  | Distance | Read | Note |
  | --- | --- | --- |
  | 20 cm | 10/10 | |
  | 25 cm | 10/10 | |
  | 30 cm | 6/10 | |
  | 35 cm | 0/10, then 10/10 | read every time after tapping the QR on the phone screen to focus |

  So focus limits the distance more than pixels do: at 35 cm the image was blurred until focused. ARCore uses fixed
  focus by default; to do: ask Xiaoxin to turn on Auto Focus on the AR camera. App text changed (agreed with the team):
  "Hold the phone 20 to 30 cm from the cable label"; if nothing is found, "Move closer to the label" instead of
  "Label missing"; the label search now waits 6 s instead of 2 s.

## 2026-10-29 · M2 locate()
- `ml/perception_service/locate.py`: stage 1 = ORB + ratio test + MAGSAC++ homography of the whole board; stage 2 =
  the column of terminal blocks found again by normalised cross-correlation in the warped frame (the blocks stand
  above the board, so stage 1 alone is about 0.7 pin spacings off), then each block within 5 px.
- First version matched each block on its own: on C02 (two labels hide the board) Reader 1 slipped by one pin.
  Fixed by matching the whole column first and using MAGSAC++ instead of RANSAC.
- Session 1, 56 controller photos (`python -m ml.eval.eval_locate`), tuned on these photos, so a preview only:

  | Frame (long side) | Found | Median error | Pins within half a spacing | Photos all right | Time |
  | --- | --- | --- | --- | --- | --- |
  | 4096 (as taken) | 56/56 | 0.13 | 100 % | 56/56 | 108 ms |
  | 1920 | 56/56 | 0.13 | 100 % | 56/56 | 100 ms |
  | 640 | 50/56 | 0.18 | 79.4 % | 36/56 | 74 ms |

- Tests: `ml/tests/test_locate.py` (known homography of the template, no board, same answer twice).
- Same code on Yu's laptop (Windows, OpenCV 5.0.0, where the service runs) at 1920: 56/56 found, median error 0.14,
  99.2 % of pins within half a spacing, 52/56 photos all right, 46 ms. The 8 pins counted wrong (c02 room front,
  c05 room front, c11 flash tilt, c11 room tilt) still sit on the right opening, at its edge: about 0.5–0.6 spacing,
  close to the annotation's own click error. Small differences between OpenCV builds tip these borderline pins.
  Decision: no more tuning on session 1 (that would fit the method to these photos); the laptop's numbers count.
- Reader end (A4020-E), 24 reader photos at 1920: ORB found the reader on only 5/24 (the flag label hides most of
  it and its QR code draws the features). Tried a colour-block method (light-green terminal block): 12/12 in room
  light, but fails under flash. Chosen instead: DISK features + LightGlue (ICCV 2023) from the LightGlue-ONNX export
  (Apache-2.0), set per template ("matcher": "lightglue" in A4020E.json); the board keeps ORB.
  Result: found 23/24, median error 0.35 slot spacings, 82 % of slots within half a spacing, 15/24 photos all
  right; flash photos work too. No stage 2 for the reader (tried: it made it worse).
  Speed on Yu's laptop CPU (bench_lightglue.py): about 0.7 s per frame at 480 x 640, 1.5 s at 768 x 1024.
  Model: python ml/tools/get_models.py (ml/models/, not in Git, checked by SHA-256).
- Official numbers, Yu's laptop (Windows, OpenCV 5.0.0), session 1 at 1920, frames passed as RGB like the service:
  board 56/56 found, 0.14 median error, 98.4 % of pins within half a spacing, 52/56 photos all right, 48 ms;
  reader 23/24 found, 0.35 median error, 82.3 % of slots within half a spacing, 15/24 photos all right, 874 ms.
- Reader safeguard: of the 9 reader photos not fully right, 7 are borderline (worst slot 0.5-0.73 spacings off) and
  one is really wrong (r03 flash tilt: up to 2.5 slots off, only 19 agreeing pairs; correct photos all have 34 or
  more). A4020E.json now asks for at least 30 agreeing pairs ("min_inliers"), so that frame says "not found"
  instead of a wrong slot. Chosen on session 1 from one bad case: check it again on session 2.

## 2026-10-29 · M2 step 5: wire or no wire (start of M3)
- `ml/perception_service/patches.py`: each frame is warped into the template's view with locate()'s homography;
  every pin gets a patch of the same size and orientation ("patch_box" in the template JSON: board 60 x 20 px to
  the right of the opening, where wires leave; reader 40 x 70 px below the slot). `ml/eval/build_patches.py` cut
  1440 labelled patches from session 1 (1344 board, 96 reader) into data/patches/session1/ with labels.csv.
- The existing colour reader (median colour of a crop at the pin, nearest of 8 reference colours, no "no wire"
  answer): 6 % of board pins right. Not usable.
- First learned classifier (prototype, not in the service yet): extra-trees on the patch shrunk to 20 x 7 in
  CIELAB, classes none/red/black/green/yellow, cross-validated with whole rows held out (7 folds over 14 rows):
  board pins 97.1 % right; wire-or-not 97.4 %, no false wires, 35 of 252 wires missed (14 green, 13 black: dark or
  green wires on the blue/green housing; most in the tangled rows C02, C12); colour right on 84.5 % of wired pins.
  Zone survey (does a module hold any wire): 223 of 224 modules right (the miss: c06 flash front, Reader 1).
- Reader patches (88): colour 78 % (logistic regression); too few photos to trust.

## 2026-10-29 · M3 step 1: pin reader in the service
- `ml/perception_service/pin_reader.py` + `ml/eval/train_pins.py`: extra-trees on the patch features, one model per
  template (`ml/models/pins_A1610.joblib` 2.3 MB, `pins_A4020E.joblib` 0.4 MB; not in Git). Temperature scaling
  fitted on the held-out (row-wise cross-validated) predictions. `classical.py` (now "classical-2") reads every pin
  with it; without a model it falls back to the old colour baseline.
- Cross-validated on session 1 (whole rows held out; the shipped model is then trained on all of session 1):

  | Template | Pins | Pins right | Wire or not | Missed / false wires | Colour on wired pins | ECE before → after | Confident (≥ 0.8): share, right, wrong |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | A1610 board | 1344 | 97.1 % | 97.4 % | 35 of 252 / 0 | 84.5 % | 0.048 → 0.007 | 93.8 %, 99.0 %, 13 |
  | A4020E reader | 88 | 80.7 % | 100 % | 0 / 0 | 80.7 % | 0.206 → 0.082 | 53.4 %, 91.5 %, 4 |

  Board confusion (rows true none/red/black/green/yellow): missed wires are 3 red, 13 black, 14 green, 5 yellow;
  1 black read as red, 3 green as yellow; no empty pin read as a wire.
- Limit: the reader model never saw an empty slot (every reader row has 4 wires), so it cannot say "no wire" at the
  reader end yet.
- End to end: send_frame on s1_ctrl_c05_room_front: 24/24 pins right, 0.66 s (that photo was in the training data,
  so this only shows the plumbing works). Tests: 29 passed.
- Retrained on Yu's laptop (the model shared in Git): board 97.2 % pins right, wire-or-not 97.5 % (34 missed, 0
  false), colour on wired pins 85.3 %, ECE 0.048 -> 0.014, confident (>= 0.8) on 94.6 % of pins with 99.1 % right
  and 12 confident errors. Reader unchanged (80.7 %).
- `ml/eval/inspect_pins.py`: what the model looks at (feature importance as a map of the patch), calibration curves,
  confusion, the mistakes most-confident-first. Findings: it relies on colour (a*, b*) where the wire leaves the
  opening and hardly on brightness, so dark (black) wires in a dark opening are missed; missed wires often run low
  or slanted in the window; the top "mistake" is U1's two-wire pin (yellow is also there).

## 2026-10-29 · M3 step 2: refinements from the inspection (last tuning on session 1)
- Rule: each change is tried once, with the same row-wise cross-validation, and kept only if it helps; after this,
  no more tuning on session 1 (otherwise the method fits these photos, not the task).
- Board (A1610, 1344 pins, 252 wires; Linux, OpenCV 5.0.0, scikit-learn 1.7.2):

  | Change | Pins right | Missed wires | False wires | Colour on wired pins | Confident (>= 0.8): answered, wrong | Kept? |
  | --- | --- | --- | --- | --- | --- | --- |
  | Base (colour grid + percentiles) | 97.1 % | 35 | 0 | 84.5 % | 93.8 %, 13 | — |
  | Taller window (-10,-10,50,14), for wires running low | 97.0 % | 35 | 1 | 84.5 % | —, 15 | no |
  | + brightness features (L relative to the patch, horizontal edges, dark share per third) | 97.2 % | 31 | 1 | 85.7 % | —, 13 | with augmentation |
  | + augmentation (training copies shifted 2 px and turned 6 deg) | 97.8 % | 23 | 2 | 88.9 % | —, 12 | with brightness |
  | + brightness + augmentation | 98.2 % | 18 | 1 | 90.9 % | 96.9 %, 9 | yes |

  Missed wires halved (35 -> 18), confident errors 13 -> 9, ECE 0.006.
- Reader (A4020E, 88 slots, all wired): pins right 80.7 % (base) / 79.5 % (brightness) / 81.8 % (augmentation) /
  80.7 % (both): one slot either way, i.e. noise. With both: ECE 0.082 -> 0.031, answered at >= 0.8 53 % -> 61 %,
  confident errors 4 -> 3. Same settings for both ends to keep one rule; the reader needs more data, not tuning.
- Code: build_patches cuts each patch with a 6 px margin (labels.csv column "margin"); train_pins adds 6 copies per
  patch on the training side of each fold only (never on the held-out side); pin_reader.features adds the
  brightness features (model bundle "brightness": true; an older model without it still loads and uses the old
  features). Flags --no-augment, --no-brightness, --no-save reproduce the table. Tests: 30 passed.
- Official run on Yu's laptop (Windows; patches rebuilt there, 2 reader photos "not found" so 88 reader slots):
  board 98.7 % pins right, wire-or-not 99.0 %, 13 of 252 wires missed, 0 false, colour on wired pins 92.9 %,
  T = 0.525, ECE 0.035 -> 0.005, answered at >= 0.8: 97.5 % with 10 wrong. Reader 80.7 %, ECE 0.175 -> 0.031,
  answered 61.4 % with 3 wrong. Models in Git: pins_A1610.joblib 7.2 MB (was 2.3; 7x the training rows), A4020E 1.1 MB.
- Same comparison on the laptop's patches (ml/eval/report_figures.py, cross-validated; "both" equals the official
  run): base 97.2 % / 34 missed / 0 false / colour 85.3 % / 12 sure-and-wrong; taller 96.9 / 37 / 2 / 84.1 / 13;
  brightness 97.8 / 27 / 0 / 88.1 / 11; augmentation 98.3 / 18 / 1 / 91.3 / 9; both 98.7 / 13 / 0 / 92.9 / 10.
  Augmentation alone has one fewer sure-and-wrong pin; both is best on everything else, so both is kept.
  The refinement fixed 19 pins and broke none; 18 are still wrong (7 of the 12 most confident at Reader 1 A/B,
  where wires cross; the top one is U1's two-wire pin).

## 2026-10-29 · Report figures
- `ml/eval/report_figures.py` draws 8 figures from the data into docs/figures/report/ (PDF + PNG 300 dpi, IEEE
  column / page width) with numbers.json and README.md (draft captions with the numbers): pipeline, locate_cdf,
  ablation, confusion, confidence, examples, augmentation, importance. Slow parts cached per OS in ml/runs/report/.
- New facts from the figures: the homography alone puts only 13.9 % of board pins within half a spacing (parallax:
  the blocks stand above the board); block refinement brings it to 98.2 %. Shipped board model: feature groups
  L*a*b* grid 79 %, edges 10 %, relative L* 6 %, percentiles 4 %, dark share per third < 1 % (unused).
  At confidence >= 0.8: shipped answers 97.5 % of pins with 0.76 % wrong; base 94.6 % with 0.94 % wrong.

## 2026-10-29 · Saving live frames (for the bench day, 31 Oct)
- `IA_SAVE_FRAMES=<folder>` makes the service save every /read: the JPEG as the phone sent it (not re-encoded),
  the request without the image + the answer + the time (JSON), and a line in index.csv (`frame_log.py`).
  `ml/tools/review_frames.py <folder>` draws the answers on the frames and sums up per end: camera sizes, share of
  frames with every pin found, service time median / 95th percentile. Tests: 33 passed.
- Dry run with two session-1 photos through send_frame.py: both saved, every pin found, 0.66 s (board) and 3.4 s
  (reader) on the slow test machine.

## 2026-11-01 · Bench test 31 Oct (with Xiaoxin) and the fixes
- The bench had an AXIS A1601 (not the A1610 of session 1): other silkscreen ("B / O2" for "LED2/O2", "IO8" for
  "BUZZER/IO8"), an "Access Control Levels" sticker, a red IO13/IO14 block. Live frames were not saved (the service ran
  without IA_SAVE_FRAMES), so the issues below come from 9 screenshots.
- Issues: empty pins read as wires (issues 2, 5, 6) and empty modules surveyed as "1 wire" -> "Is this cable on
  Door 1?" (1, 3, 4); one frame placed Reader 1's pins on Reader 2 (6); black read as green and unstable reads at the
  reader end (8, 9); the floating window's dots off the slots (7); marks stuck to the screen, not the pins.
- Cause of 1-6, measured: the pin reader had learned the A1610's background. Stand-in test on session 1 (train on the
  blue blocks, test on the green ones): the shipped reader called 614 of 616 empty green pins a wire.
- Fix 1, one general wire reader for every device (pin_reader.py, patches.py, train_pins.py):
  - every pin window is cut in one canonical shape: turned so the wire leaves to the right, scaled so one pin spacing
    is 24 px (template JSON: "pin_spacing", "wire_direction"); the board's windows are unchanged by this;
  - wire or not from how the window DIFFERS from the same window of the empty reference photo (colour difference after
    matching brightness/colour cast, edge difference, both tolerant to 3 px of misalignment, shape of the change;
    change detection, Radke et al. 2005); trained with the shifted/turned copies;
  - colour from a second model on wired windows only: the 20 x 7 CIELAB grid with brightness features + the colour of
    the changed pixels along the wire's path after von Kries light matching to the reference;
  - a wire of uncertain colour stays a wire: "wire or not" is decided on the raw presence probability, temperature
    scaling only sets the confidence.
  Session 1, row-wise cross-validated (Linux, scikit-learn 1.7.2; ml/runs/train_pins.json):

  | Test | Pins right | False wires | Missed wires | Colour on wired pins |
  | --- | --- | --- | --- | --- |
  | Board, rows not trained on | 97.3 % (was 98.7 %) | 8 / 1092 (was 0) | 15 / 252 (was 13) | 88.9 % (was 92.9 %) |
  | Reader, rows not trained on | 77.3 % (was 80.7 %) | - | 0 / 88 | 77.3 % |
  | Unseen background: trained on blue blocks, tested on green | 96.7 % (was 7.6 %) | **0 / 616 (was 614)** | 15 / 56 | 60.7 % |
  | Unseen device: trained on the board, tested on the reader | 62.5 % | - | **3 / 88** | 62.5 % |

  The price: about 1.4 points on the board it was trained on. The gain: a new background or device no longer looks
  like wires. Wire-or-not carries over to a new device; colour across devices is weaker (63 %), so there the app's
  "unsure -> ask" step matters and data from more devices helps. Confidence: ECE 0.05 -> 0.01 (T = 0.54).
  Experiments that led here (all on session 1): reference-only presence + reference-only colour (colour 43 % on unseen
  background) -> grid colour on wired windows only (79-82 %) -> + edge difference and augmentation for presence (fewer
  missed) -> wire-pixel colour after light matching (alone 55 % board -> reader, kept as extra features).
- Fix 2, zone survey: a surveyed wire counts only when the reader is >= 0.8 sure (app.py SURVEY_SURE, the app's amber
  line). Session 1 cross-validated, module level: empty modules "holding a wire" 1/160 -> 0/160; modules with wires
  missed 0/64 -> 1/64.
- Fix 3, block check (locate.py _check_blocks): each placed block is compared in colour (CIELAB ZNCC) with its own
  reference block and with the reference blocks that have as many pins; if it looks more like another one (blue
  Reader 1 vs green Reader 2) or barely like itself (< 0.10), that module is "not found" instead of read on the wrong
  pins. Session 1: correct placements score >= 0.19, one block off <= 0.15; 0 / 56 correct placements flagged, 56 / 56
  simulated one-block slips caught.
- Fix 4 (app): the floating window's still is now the frame that was read (TakeSnapshot), not the camera 1-3 s later.
- Fix 5 (app, AR): ArRig.cs builds AR Foundation 6.5 at run time on the phone (ArCameraView.arCamera, on by default):
  ARCore camera at up to 1920 px with auto-focus, world anchors from raycasts against planes / feature points, marks
  projected every frame so they stay on the pins. Untested on a phone yet; arFlipImageX / arFlipImageY fix mirrored
  marks if the display-matrix convention differs on the K80S.
- Speed (slow test machine): controller read 1.1-1.4 s, reader 3.6-4.1 s with the new reader and checks.

## 2026-11-01 · First live test with the AR camera (K80S, AXIS A1601), 68 frames saved (data/live/1101)
- Service on port 5055 (Windows reserves 7979-8078 here), adb reverse tcp:8000 tcp:5055; app installed by adb push +
  the phone's file manager (Xiaoxin's phone blocks "Install via USB"). Frames: 1920 x 1080, landscape sensor image.
- Service time on the laptop: median 451 ms, 95th percentile 772 ms per controller read.
- AR marks were mirrored across the portrait screen; the service's positions in the frames were right. Cause: image y
  runs top-down, the camera texture's v bottom-up. ArRig now uses 1 - y (flags still flip either axis).
- The board was an A1601 again. With only the A1610 reference, empty pins read as wires and blocks were rejected.
  New: board variants (Template.variants, ml/tools/make_variant.py): the A1601 picture = per-pixel median of 8 frames of
  the empty board warped into the A1610 template's view, blocks moved back by their stage-2 offsets (stage 2 put the
  pins exactly on the openings: checked with names drawn). locate() picks the variant whose blocks agree best in
  colour; the wire reader compares with the variant whose empty pin windows look most like the frame's
  (classical.closest_variant), because the best-fitting photo for finding the board is not always the best empty board.
- Replay of all 68 frames: board not found in 14 (close-ups with part of the board, the floor); Reader 1 rejected by the
  block check in 6 (steep views from above: pins would land on the block tops - right to reject). Empty board,
  21 frames: 0 false wires on 168 pins (optimistic: 8 of these frames built the A1601 picture). Wired, 7 frames with
  known wiring (- green, 12V black, A yellow): 45 / 56 pins right (80 %); the A1601 picture alone would give 17 / 56
  in that warmer light, so the choice of reference matters. Misses: black missed, the orange-ish yellow read red.
- DISK + LightGlue as fallback for close-ups: still not found (5-8 pairs); not used.
- App text now asks to hold the phone in front of the block, 30-45 cm, so the holes where the wires go in are seen.
- Next: label live frames (one saved folder per wiring) and train with them; the model has only seen session-1 stills.

## 2026-11-01 (evening) · AR marks, blur check; the demo board is the A1610
- AR orientation: with 1 - y only the marks were still in reverse order along the block; the image maps onto the
  camera texture turned 180 degrees (ArRig.Orientation = 3, both axes). Checked on the K80S: marks along Reader 1 in
  the right order. A three-finger tap cycles the four orientations on the phone (to check another phone).
- AR distance: raycasts often missed the board (marks left over the floor after moving). Marks now get their distance
  from the pin spacing: depth = focal length x spacing / spacing in pixels (pinhole model). Spacing measured on
  session-1 photos against the 21.5 mm QR labels: A1610/A1601 blocks 4.96 mm (17 photos, IQR 4.78-5.11; taken as
  5.0 mm, the standard pitch), A4020-E 5.78 mm (13 photos, IQR 5.64-5.88). Raycast only for single points (label).
- Live frames 19:07 (A1601, evening light): misreads come from motion blur, one-pin slips (stage 2 matches the A1610's
  blocks, whose printed text differs from the A1601's) and the reading itself (black at 12V missed, empty pins read
  green). Not tuned further: the final test and demo use the A1610 (decided 1 Nov).
- Blur check (classical.py): variance of the Laplacian of the module's area at 200 px (Pertuz et al., 2013); below the
  template's "sharp_min" the pins are not read and the app asks to hold still. Board 450 (live: smeared frame 411,
  sharp 700-2400; session-1 stills >= 1172), reader 150 (sharp session-1 stills 296-504).
- Next: live test on the A1610 with saved, labelled batches; retrain with live frames if the reading falls short.
- 2 Nov, 00:22 (A1601 again): all four wires read right (- black, 12V red, A green, B yellow), but the marks were in
  reverse order along the block and drifted to the table under the controller after moving. Reverse order cannot come
  from moving the phone (the marks lie on one line), so the orientation was wrong: default now 2 (image y mirrored
  only; 0 had put the marks across the screen). The number is shown after every read so the next test confirms it.
  Pins never take their depth from a plane (the table holding the controller); only the label uses a raycast.

## 2026-11-02 · Correct A4020-E terminal identities against the physical reference view

- Xiaoxin confirmed the top/reference-photo view's physical left-to-right order as **minus, plus, A, B**. The local
  template had wrongly assigned **B, A, plus, minus** to those positions. The earlier test repeated that mistaken
  assignment; a passing test did not establish the physical identity of the slots.
- Corrected only the names' coordinates in `templates/A4020E.json`: response order remains **plus, minus, A, B**,
  with x = **557, 502, 613, 674** respectively. The geometric slots and trained `pins.joblib` are unchanged.
  No retraining, wiring-rule change, record-schema change, or new phone APK is needed for this correction.
- Three independent physical-slot checks failed before the fix and passed after: reference order, actual matching
  of a rotated image against fixed physical annotations, and the production patch cutter sampling four known
  colours painted at those physical positions. Full Python suite: 41 passed.
- Restarted the port-8000 service with its existing `IA_SHARP_SCALE=0.6`; new captures go to
  `data/live/2026-11-02-f-terminal-map`. HTTP replay of the saved failing frame from batch e (f315, replay ID 9000315)
  gives physical left-to-right **minus/no wire, plus/red, A/green, B/yellow**. Positions and colour predictions are
  exactly the old ones reassigned to the correct terminal identities. The black wire's no-wire prediction remains
  a separate recognition error. Replay evidence is in `data/reader-terminal-mapping-checks/` (not Git).
- Earlier saved reader records were made with the old mapping and are not automatically migrated. Exit the current
  check and scan/read the reader again before using its stored colours; do not treat old records as corrected data.

## 2026-11-02 · A4020-E housing calibration from the 24-photo black-wire batch

- Verified all 24 session3 annotations (96 slots). The old narrow local matching strip included openings, wires,
  nearby components and highlights; its x offset could jump almost one terminal. Broadening that strip alone
  did not reliably handle the different projected housing shape at front/tilted views.
- Added four small, grayscale housing references from only the all-empty R01 row (room/flash x front/tilt).
  Board rectification plus the median manual pin translation places each crop into the existing template's
  coordinate system. Matching uses the rigid housing above the wire entries; pin identities, geometry and the
  classifier's empty reference remain unchanged. The controller keeps its existing column refinement.
- `ml/tools/calibrate_reader_block.py` reproduces the four reference PNGs byte-for-byte from that row. Source
  annotation/reference hashes and generation details accompany the assets. No wired row is used as a reference.
- Unified `build_patches` and `eval_locate` with the service's refinement (both had disabled it on the reader).
  Actual 1920-long-side evaluation: 24/24 located; all 96 pins within half a pitch; maximum error **0.938 -> 0.277**
  pitches; frames with any error above 0.25 pitch **15 -> 1**. Median per-photo median error after: **0.052** pitches.
  The 20 wired photos are same-session development validation, not an independent-day or live-phone final test.
- Generated/checked all 96 `data/patches/session3` crops (72 x 32, including training margin). Diagnostics and
  before/after images: `data/reader-localization-fix/`. Full Python suite: **43 passed**; housing mutation without
  calibrated references fails the new regression as intended. A rotated-frame evaluation regression also passes.
- Restarted localhost:8000, preserving `IA_SHARP_SCALE=0.6`; new captures: `data/live/2026-11-02-g-reader-localization`.
  HTTP replay of R04 flash/tilt located every slot with max error **0.101** pitches after JPEG encoding. No APK change.
- No model training or replacement: pins.joblib SHA256 remains
  `ddc1324a151dd2750c23efdcd66e13d0c77b57002be9eb078ec19c04694568ee`. Old training data is still needed for a merged
  retrain. App live-frame testing and black-wire presence/colour improvements remain separate follow-up work.

## 2026-11-02 · First merged session1 + session3 training run

- Original session1 photos, annotations and patches were restored under data/. Refreshed only the old reader
  patches with the corrected local positioning: 88 usable reader windows; the 1344 controller windows remain
  unchanged. Added session3's 96 windows: 1528 training pins, 26 wiring groups. Original reader patches/labels
  and the active model are backed up in `data/readerblack-training/before/`.
- Added candidate-output and worker-count arguments to train_pins, plus per-session metrics from the existing
  held-out predictions. Ran once: `python -m ml.eval.train_pins --sessions session1 session3 --workers 4
  --no-checks --out ml/models/pins_readerblack_20261102.joblib`. This runs one 7-fold wiring-group CV and a final
  fit; extra domain checks and repeated tests were not run. Session2 was not used.
- Candidate overall grouped accuracy: 95.4%; controller: 97.3%; new session3: 74/96 (77.1%). On the new batch,
  the old active model (which did not train on that batch) has 46 empty-slot false positives and 18/20 correct
  black wires. Candidate held-out predictions reduce empty-slot false positives to 6 but correctly classify
  only 12/20 black wires. All four green examples are predicted black and all four yellow examples are missed;
  the single full-colour wiring group does not establish reliable generalisation in this batch.
- Kept the existing active model: the targeted black-wire performance regresses despite improved total accuracy.
  Candidate remains available at the path above; no service restart or APK change. Full metrics, baseline
  comparison and the decision are in `data/readerblack-training/`. Active pins.joblib SHA256 is unchanged:
  `ddc1324a151dd2750c23efdcd66e13d0c77b57002be9eb078ec19c04694568ee`.

## 2026-11-02 · Diagnose reference mismatch and thin-black-wire suppression

- Diagnosed the previous candidate using saved crops/features rather than rerunning the old validation. Empty
  reader windows differed from the single old empty reference on a median 22.4% of pixels. Paired empty views
  selected by the rigid housing reduced that mismatch. Added four packed empty-window references generated from
  all-empty session3/R01 only, and persisted the locator's reference index in labels.csv for training/runtime parity.
- A housing-reference candidate eliminated empty false positives but still missed eight black windows. These are
  recorded individually in `data/readerblack-training/diagnosis/black-missed-pins.json`. Independent per-pixel
  reference shifts were suppressing the thin black line by matching it to nearby dark background pixels.
- Added `housing-aligned`: a single reference translation selected on the least-changed 70% of the window. This
  applies only to reader features, in both original and augmented training windows and live inference. Existing
  model metadata defaults to the old comparison; the controller's reference/comparison functions are unchanged.
- Each actual preprocessing correction received one grouped CV run, not a hyperparameter search. The aligned
  candidate is `ml/models/pins_readerblack_aligned_20261102.joblib`. Excluding the four empty reference photos,
  the remaining 20 photos/80 pins yield **20/20 black correctly classified**, **3/48 empty false positives**,
  **1/4 green correct**, **1/4 yellow correct**. These are same-session development results, not live-phone results.
- An actual ClassicalReader call with this candidate in memory correctly reads the previously missed + black
  window. Grouped predictions are now saved once with the validation, allowing per-pin diagnosis without rerunning
  CV. Diagnostic images/metrics are in `data/readerblack-training/diagnosis/`.
- Keep the existing active model: black detection has improved, but green/yellow colour regression remains and
  must be addressed before replacing the service model. No service restart or APK change for this candidate.
  No extra photos or manual annotation were requested in this step; the current batch sufficed for diagnosis.

## 2026-11-02 · Reader entry foreground colours and activated model

- Inspected the actual pixels selected on the four full-colour session3 views. The old fixed centre-band top-D
  sampler selected the dark opening shadow on green windows and green PCB pixels on yellow windows; the wires
  bent toward the crop edge, while a neighbouring yellow wire entered the green window farther away.
- Added `housing-anchored` mode: colour summaries use changed, chromatic connected foreground entering this slot;
  components beginning farther along the crop cannot displace its entry wire. Neutral/dark wires use changed
  near-entry pixels. Presence features use the entry plus half a canonical pin pitch, accommodating sideways
  exits. Train, augmentation and live inference use identical flags; legacy models/controller features retain
  their original extraction. No terminal name or expected colour is used to choose these pixels.
- One grouped validation for this preprocessing change: 1528 windows across 26 wiring groups. Excluding the four
  empty reference images, the remaining 20 photos/80 pins have **32/32 wired colours correct** (black20, red4,
  green4, yellow4), **4/48 empty false positives**, all below 0.8 confidence; no high-confidence errors. These
  are same-session development results, not a fresh-phone/final-test score. Session2 remains unused.
- New foreground regressions: 2 passed (green and yellow against a centre shadow and later crossing wire).
  Did not repeat the full project test suite. Saved sampled-pixel/before-after diagnostics and validation in
  `data/readerblack-training/colour-diagnosis/`.
- Promoted `pins_readerblack_anchored_20261102.joblib` to the active `ml/models/pins.joblib`; SHA256
  `f6d4ce6daaed633994edd403fa0403f13b32a8a670f19e876211005305d535cd`. Original model remains in
  `data/readerblack-training/before/pins.joblib`. Model metadata declares `reference_mode=housing-anchored`.
- Restarted the port-8000 perception service, preserving `IA_SHARP_SCALE=0.6`; new saved frames go to
  `data/live/2026-11-02-h-reader-colour`. Actual HTTP replays of room/flash R06 front images both return
  plus/red, minus/black, A/green, B/yellow. Flash yellow is still low confidence (0.482) and needs confirmation.
  Existing APK works; fresh device scans with supplement light off/on are the next practical verification.


## 2026-11-14 · AR marks: triangulated from several reads, intrinsics rays, AR records (Yu, covering B and C)

**Why.** After the 2 Nov alignment fix the marks were still "not good enough", and the phone kept no record of
what it showed. Reading the code: every read gave the whole block **one depth from its pin spacing**, which is
exact only when the block faces the camera. The spacing shrinks with the cosine of the tilt, so the depth is long
by 1/cos − 1 (35°: 22 %, ~8 cm at 35 cm), and the marks slide off the pins as soon as the phone moves (Chen's 2 Nov
note: "steep views need separate testing"). The image → ray step also took the CPU image for the screen texture;
ARCore's CPU image may cover a different field of view than that texture.

**What changed (no contract file touched; `ICameraView`, `CameraFrame`, `IWireReader`, the HTTP schema are as they were).**
- `AR/AnchorFusion.cs` (new): each read is one sighting per pin, a Gaussian around its ray (0.25 pitch across,
  25 % of the range along it from the spacing depth). A pin is the least-squares point of its sightings (linear
  triangulation, Hartley & Sturm 1997; Hartley & Zisserman 2004), robust: with ≥ 3 views the ray farthest beyond
  0.4 pitch is dropped. A new read must meet the earlier rays (epipolar gate 0.4 pitch, majority of the pins) or
  it is not fused; two refused reads in a row that agree with each other move the marks (the first read was
  wrong). Row spacing is now the median gap along the row, not the nearest-neighbour distance.
- `AR/ArRig.cs`: rays from the CPU image's own intrinsics, with only the quarter-turn read from the display
  transform (`CameraImageGeometry.TryIntrinsicsRay`); the display-only ray stays as the fallback and the
  difference between them is recorded per pin. Sightings of a frame are taken together (the spacing needs all
  pins). Module hints and single pins take the anchored pins' depth instead of a raycast (which finds the table).
  All marks of an end sit under one transform held by an ARCore anchor (`ARAnchorManager.TryAddAnchorAsync`).
- `AR/ArRecords.cs` (new): `persistentDataPath/ar-records/<start>.jsonl`, one line per read (pose, matrices,
  intrinsics, the reader's points, where each mark was in that frame before the read, outcome), a 1 Hz sample of
  where the marks were drawn, and screenshots on a three-finger tap. Geometry only, no images.
- `App/InstallerApp.Check.cs`: an explicit read (read, read again, check, find the pins) starts the marks afresh
  (`ClearAnchors` first, as the device end already did); while the app waits for the installer, `KeepMarksAligned`
  reads the same end again every 3 s and hands only the positions to the view (colours, verdicts and the session
  log untouched; dropped if an explicit read started meanwhile). Switches: `InstallerApp.keepMarksAligned`,
  `alignEverySeconds`; `ArCameraView.arIntrinsicsRays`, `arFuseReads` (off = the 2 Nov behaviour),
  `arHoldWithArAnchor`, `arRecords`.
- `ml/tools/ar_records.py` (new) + `ml/tests/test_ar_records.py`: mark offset at each re-read in pitches (where
  the mark was vs where the reader finds the pin now; no hand labels), outcomes, views per mark, baseline, ray
  method difference; with `--frames` draws both on the service's saved frames.

**Simulation (production AnchorFusion, Monte Carlo, 8 pins at 35 cm, reader noise 0.08 pitch; Editor check writes
it to `data/ar-overlay-checks/fusion-simulation.txt`).** Mark offset seen from a point 10 cm away from the reads:

| Row tilt | One read (2 Nov) | Two reads 2 cm apart | Two reads 5 cm apart |
| --- | --- | --- | --- |
| 0° | median 0.50 pitch, 51 % within ½ pitch | 0.32, 71 % | 0.14, 99 % |
| 20° | 1.17, 17 % | 0.33, 69 % | 0.15, 99 % |
| 35° | 3.67, 0 % | 0.38, 62 % | 0.15, 99 % |

Depth bias of the spacing estimate with the block facing the camera: nearest neighbour +3.9 %, row gaps −0.4 %.
Four reads with one of them a pin off along the epipolar line: plain fusion 12.4 mm, robust 3.6 mm 3D error.
A read one pin off *along* the epipolar line cannot be refused by one test (it looks like a depth change); robust
fusion outvotes it once more views agree. These are simulations, not phone results.

**Checks.** C# compiled (player + editor) against Unity 6000.3.24f1's assemblies and the project's AR Foundation
6.5. New Editor checks in `ArGeometryChecks` (intrinsics ray = display ray when the views match; only the
intrinsics ray is right when the CPU image covers 80 % of the texture; fusion of a 35° row within ¼ pitch from
10 cm away; a read 3 pitches across the row refused; the simulation table) — **to be run in Unity** (batch
command in `tools/ar-checks/README.md`); the fixtures were cross-checked numerically outside Unity. Python suite 47
passed.

**Next (phone).** Build, run both ends with `IA_SAVE_FRAMES` on, move the phone 5–10 cm between reads, pull the
records, run `ar_records.py --frames`. The report numbers are the "first re-read" (2 Nov behaviour) against the
"later re-reads" (after fusion) offsets.

## 2026-11-14 (evening) · Laptop test with the real A1610: pins two terminals off, slow reads, blurred frames

**Test.** Yu, Unity Editor + phone as webcam (iVCam, 1080 x 1920), real A1610, wires on Reader 1 pins 1-4 (black,
red, green, yellow). 17 reads saved in `data/live/2026-11-14-editor`. The marks were not on the slots.

**What the frames show** (replayed through the service code):
1. **Every close-up put the pins 1-3 terminals off.** 15 of 17 reads returned nothing, the 2 that returned
   colours had them 2 terminals low (yellow read on pin 7/6 instead of 4). Cause: the blocks stand ~1 cm above the
   board; at 15-25 cm, seen from the side, they sit up to 3 pins from where the board's homography puts them. The
   old stage 2 searched +-45 px (< 2 pins) in grey, where pins repeat, and the close-up cut the column off, so the
   match slipped by whole pins. Measured without hand labels by where the housing colour changes from green to
   blue along the located column: **6 of 16 frames within half a pin before**.
2. **Reads took 2.7-5.2 s.** On the A1610 the A1601 variant photo found too few ORB pairs and fell back to
   LightGlue (~2.9 s) on every read.
3. **13 of 17 frames were motion-blurred** and refused by the blur check: the app sent the single frame of the
   moment of the tap.
4. In the Editor the marks did not follow the camera at all (webcam view kept them at fixed image points).

**Fixes.**
- `locate.py` stage 2a (new, `_block_plane`): the blocks' plane is the board's picture scaled about the point
  under the camera (planar homology; Hartley & Zisserman 2004), so one scale and one shift. Scale from the pin
  period in the rectified strip (autocorrelation of narrow strips, measured the same way on the template);
  place from **colour only** (a*, b*: the housing colours do not repeat) with masked ZNCC over the pixels the
  frame shows (Padfield 2012), search +-84 px; then lightness too within less than half a pin; then the old
  per-block fine-tuning. Falls back to the old column match when the colours are not found.
  **Live frames: 16 of 16 within half a pin (6 before). Session 1 (56 photos, 1920): 98.4 % of pins within half
  a pitch (98.1 % before), median 0.16 (0.15), all pins right in 50 photos (52).** New tests: a column two pins off
  in a cut-off close-up (fails on the old code), the pin period, 51 tests pass.
- `locate()`: variants share one pixel frame, so the board is found once; a variant photo that finds too little
  borrows the homography instead of running LightGlue. Variant chosen by (colours found, agreement). **~0.5 s
  per controller read instead of ~3.3 s** (same machine, same frames); the base A1610 photo is now chosen on
  15 of 16 frames (it is an A1610).
- App (`InstallerApp.Check.cs`, `App/FrameQuality.cs`): a read takes the sharpest of a 5-frame burst over 0.5 s
  (variance of the Laplacian; ranks today's frames as the service does: the two it accepted score 3130 and 3844,
  the smeared ones 127-590), and retries twice by itself when nothing is found, before asking the installer.
- Webcam view (`AR/PlaneTracker.cs`, `ArCameraView`): Shi-Tomasi corners + pyramidal Lucas-Kanade + RANSAC affine
  between consecutive camera images (180 x 320 grey); every anchor moves with the picture, a read's positions
  are carried from its frame to now, the quiet re-reads (every 3 s, now also for the webcam) correct the drift,
  and marks are hidden when the picture cannot be followed. Simulated hand motion on today's sharp frame
  (warp + motion blur + noise, 5 s): worst pin 0.05 pitch at moderate speed, 0.2 at fast, lost and recovered at
  very fast; 7-10 ms per frame (Mono). Not yet measured on real video.

**Still wrong: the wire reader on this board.** With the pins now in the right place (blur check off for this replay), red (pin 2) is read
right in 15 of 16 frames, yellow (pin 4) in 11 (all sharp close-ups) and the four empty pins in all, but **green (pin 3) is read as "no wire" in 15 of 16
and black (pin 1) in 14 of 16, with confidence 0.6-1.0** (so no amber mark). The wires are plainly visible in the
windows. The model has seen no live frame of this camera; this is the domain gap, not placement. Next step:
`ml/tools/live_patches.py` (new) turns a folder of saved frames with a known wiring into labelled patches
without hand annotation; record two or more batches with different wirings, then
`train_pins --sessions session1 session3 live_a live_b` measures each batch with a model that never saw it.
(Numbers above were computed with scikit-learn 1.9 on the model trained with 1.7; rerun on the laptop.)

## 2026-11-14 · Final verification of the retained camera/recovery version

The retained version uses asynchronous CPU-image conversion on Android and worker JPEG encoding shared
between wire reading and comparison. Acquisition-time camera metadata stays with each image. Webcam defaults
request 1920 x 1080; startup logs requested and actual dimensions because iVCam can negotiate a lower size.
The app retains automatic position-only rereads, including recovery after an empty initial placement. Quiet
responses are discarded when the flow changes; no retained-reference scene matcher is included in this version.
A completed cable starts a fresh first-end scan using the separately documented IRecordStore.Forget(key)
operation, without clearing other cables. UI punctuation encoding was corrected after a standalone check
caught a mismatched repair-button label. AR record writers close when the rig is disposed.

Pre-commit checks:

- Domain suite: ALL PASSED, including scoped record reset and preservation of unrelated saved work.
- Python: 51 passed, one dependency deprecation warning, using the declared requirements in an isolated
  environment (`data/contract-checks-venv`). No model weights, captures or environment files are committed.
- Unity 6000.3.24f1: 75 AR geometry/tracker assertions passed (`data/commit-geometry.log`).
- The same reader-flow fixture ran in a temporary standalone Windows player: 45 assertions passed,
  including fresh scans, retry flows, position recovery and three JPEG checks
  (`data/commit-flow-player-final.log`, `data/commit-ui-player/evidence/checks.txt`). The test source was copied
  from ReaderFlowChecks.cs, with editor-only setup replaced by a player startup entry and exit status.
- Both editor and player scripts compiled; the temporary Windows test-player build succeeded. The editor's
  Play Mode startup still stalled, so the player result does not claim a new editor Play Mode pass.
- Changed-file whitespace, text encoding, local Markdown links, new Unity asset metadata and project GUIDs
  were checked. No generated builds, datasets, credentials or large binaries are included.

No new Android APK was produced. Visual layout, Blueprint_Demo, real ARCore tracking, camera frame rate and
recognition accuracy still need the normal manual/device integration checks. Saved iVCam replay identified
black-padded portrait input as a localization failure; configuring full-resolution, correctly oriented camera
output is required. No automatic letterbox-cropping change is included here.

## 2026-11-14 (night) · Lag and latency: measured first, then fixed where the time went

**Measured** (1348 reads logged 18:08-21:19 in `data/live/2026-11-14-editor/index.csv`; profiles of the service
code on those frames; the Unity loops timed outside the Editor, so the Editor's own numbers still need the
Unity Profiler):
- The service was inside a read **66 % of the wall time**. Device-end reads: median 1.8 s (2.5-3.5 s when the
  reader is in view), 86 % of 1002 found nothing. Controller reads: median 1.1 s, 67 % of 346 found nothing.
  The quiet re-reads came every 3 s (every 1 s while marks were missing), and on the laptop the service and
  Unity share one processor.
- Device read: 2.4 of 2.9 s is the LightGlue network. Controller read (1.44 s on the test machine): locate 0.49,
  wire reader 0.40, zone survey 0.35, tracing 0.35 of which 0.31 was converting the whole frame to CIELAB for a
  label that was not in the picture; both forests' predict 140 ms of thread dispatch for 8 windows.
- Unity main thread per camera frame (webcam view): the tracker 5.9-7.9 ms and ~1.5 MB of new arrays per step;
  every frame handed to a reader copied 2 million pixels one by one (7.9 ms) after a second 8 MB fetch from the
  camera.

**Changed** (no contract file; answers identical unless noted):
- Re-reads spaced like detection and tracking in AR systems (Wagner et al. 2010; Klein & Murray 2007): 3, 6, 12 s
  while the marks are confirmed (`alignMaxSeconds`), 1, 2, 4, 8 s while they are missing; a new explicit read
  starts again. The view's tracking (webcam) or world anchors (ARCore) hold the marks in between.
- LightGlue input 480 x 640 -> **384 x 512**: session-1 reader photos 22/24 -> 23/24 found, pins within half a
  pitch 87.5 % -> 95.8 %, 2.45 -> 1.80 s per photo; 28 live reader frames: the same 7 found, 2.58 -> 1.86 s.
  (336 x 448 and 288 x 384 are faster and worse; table in `locate.py`.) Positions change slightly; tuned on
  session 1, session 2 untouched.
- Forests predict in one thread (140 -> 38 ms, identical probabilities; scikit-learn "Computational performance").
- Tracing converts the frame to CIELAB only when the scanned label is in the picture (-0.3 s per read otherwise);
  `srgb_to_lab` reads the gamma curve from a 256-entry table for whole-number pixels (bit-identical, 0.33 -> 0.23 s
  per frame); no white balance of the frame when nothing was located.
  **Controller read 1.44 -> ~1.0 s, device read 2.9 -> ~2.5 s on the test machine (2 cores).**
- `IA_ORT_THREADS` (optional): ONNX Runtime threads for LightGlue; by default it takes every core, which on the
  laptop takes them from Unity during each device read (ONNX Runtime "Thread management").
- `PlaneTracker`: buffers allocated once and swapped (no garbage per step; Unity "Garbage collection best
  practices"), gradient images built once per level, windows sampled with shared bilinear weights, 110 points
  instead of 220, new corners sought every 8 steps. **5.9-7.9 -> 2.4-3.6 ms per frame**, same error at slow and
  moderate motion (0.01 / 0.03 pitch median), 0.13 -> 0.16 at fast motion; the Editor checks pass (0.21 px).
- `ArCameraView`: one camera fetch per rendered frame shared by the tracker and a handed-out frame; the frame's
  bytes by one block copy (7.9 -> 3.7 ms).

**Not measured here:** frame time in the Editor and on the phone. Window > Analysis > Profiler, CPU module, sort by
"Time ms" and by "GC Alloc": `ArCameraView.Tick`, `InstallerApp.Update`, `PerceptionClient` should be small now.
