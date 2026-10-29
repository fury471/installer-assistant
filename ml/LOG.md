# Package A log (Yu)

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
