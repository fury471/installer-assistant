# Perception service – JSON contract

Mirrors `unity/InstallerAssistant/Assets/_Project/Scripts/Detection/PerceptionClient.cs` and `schemas.py`. Change all three together.

Positions are normalised to the image: `x` 0 (left) to 1 (right), `y` 0 (top) to 1 (bottom).
Colour codes: 0 Red, 1 Black, 2 Green, 3 Yellow, 4 White, 5 Brown, 6 Blue, 7 Orange, -1 no wire.
Modules are named as on the cable label: `A1610.READER1`, `A1610.READER2`, `A1610.DOOR1` (controller modules) and `A4020E` (the reader).
An image is `{"width", "height", "format": "jpeg", "data_b64", "intrinsics": [fx, fy, cx, cy]}`; an empty `data_b64` means "no image" (Unity's JsonUtility cannot send null).

## GET /health

`{"status": "ok", "model": "classical-1"}` (or `dummy-correct` when started with `IA_READER=dummy`)

## POST /read

```json
{
  "frame_id": 12,
  "end": "controller",
  "module": "A1610.READER1",
  "terminals": ["−", "12V", "A", "B", "O1", "O2", "IO7", "IO8"],
  "other_modules": [
    {"module": "A1610.READER2", "terminals": ["−", "12V", "A", "B", "O3", "O4", "IO9", "IO10"]},
    {"module": "A1610.DOOR1", "terminals": ["−", "IO5", "−", "IO6"]}
  ],
  "label": "IA|C01|D1|A1610.READER1|A4020E",
  "image": {"width": 1280, "height": 720, "format": "jpeg", "data_b64": "...", "intrinsics": []}
}
```

`module` is the module the cable's label names; it selects the reference template. `terminals` are its names as printed, in the order the answer must use: device end `+, −, A, B`; controller pins 1 to 8. At the controller, `other_modules` lists the controller's other modules for the **zone survey**; at the device end it is empty. `label` is the scanned cable label without its end; each wire is traced back to it (**bundle check**). Empty = do not trace.

```json
{
  "frame_id": 12,
  "items": [{"colour": 1, "confidence": 0.93, "x": 0.41, "y": 0.53, "found": true, "trace": "this_cable"}, "..."],
  "other_blocks": [
    {"module": "A1610.READER2", "located": true, "items": [{"colour": -1, "confidence": 0.9, "x": 0.41, "y": 0.62, "found": true}, "..."]},
    {"module": "A1610.DOOR1", "located": false, "items": ["..."]}
  ],
  "model": "classical-1",
  "error": ""
}
```

`items`: one per terminal of `module`, same order. `confidence` must be calibrated: the app turns a terminal amber below 0.80. `found: false` means the terminal was not located (the app hides its marker). `trace` says where the wire runs back to: `this_cable` (it reaches the label in `label`), `other_cable` (it reaches another cable's label) or `unknown`. `unknown` never counts against the wiring; a wire on `other_cable` is named before any pin is judged. A non-empty `error` makes the app treat the whole reading as failed.

`other_blocks`: one per entry of `other_modules`, same order, each with one item per terminal. `located: false` means the module is not in the frame. The app's zone check (`Verification/ZoneCheck.cs`) only needs "which pins hold a wire" here. An empty list skips the zone check: the service returns `[]` when the survey fails, so a bad survey never spoils the reading.

## POST /compare

```json
{
  "controller_module": "A1610.READER1",
  "device_module": "A4020E",
  "pin_count": 8,
  "pairs": [{"terminal": "+", "pin": 2}, {"terminal": "−", "pin": 1}, {"terminal": "A", "pin": 3}, {"terminal": "B", "pin": 4}],
  "device_image": {"data_b64": "..."},
  "controller_image": {"data_b64": "..."}
}
```

```json
{"opinions": ["match", "match", "mismatch", "mismatch", "match", "match", "match", "match"], "model": "...", "error": ""}
```

One opinion per pin of `controller_module` (index = pin − 1): does the wire on this pin match what the pairing rule and the device end say? `unknown` makes the app ask the installer. This call must not use `/read`'s answer: it is the independent second opinion (layer 3).

## Models (not in Git)

The service needs two kinds of model files in `ml/models/`:

| File | What | How to get it |
| --- | --- | --- |
| `disk_lightglue_pipeline.onnx` | DISK + LightGlue, finds the A4020-E reader (`locate.py`) | `python ml/tools/get_models.py` |
| `pins_A1610.joblib`, `pins_A4020E.joblib` | Pin reader: no wire / colour per pin (`pin_reader.py`) | **In Git** (small): comes with `git pull`. Retrained by package A with `python -m ml.eval.build_patches --session session1`, then `python -m ml.eval.train_pins --sessions session1` (needs the annotated photos) |

The LightGlue model (50 MB) stays out of Git and is downloaded with its checksum checked. The pin readers are
shared through Git so everyone debugs with the same model; they load only with the scikit-learn version pinned in
`ml/requirements.txt` (a warning says so otherwise). Without a pin reader model the service still runs and uses the
old colour baseline there (it prints a warning).

## Saving live frames (bench days)

Set `IA_SAVE_FRAMES` to a folder before starting the service, and every `/read` is saved there: the image exactly
as the phone sent it, a JSON file with the request (without the image) and the answer, and a line in `index.csv`.
Off by default; `data/` is never committed. A failed save never spoils the read.

```
$env:IA_SAVE_FRAMES = "data/live/2026-10-31"
python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 8000
python ml\tools\review_frames.py data\live\2026-10-31
```

`review_frames.py` draws each answer onto its frame (`<folder>\review\`) and prints, per end, the camera sizes the
phone really sent, how often every pin was found and the service time (median, 95th percentile).

