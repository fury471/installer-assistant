# Package A · Perception and AI

**Goal:** the live readers return what the Wizard of Oz returns today, from real camera frames, for **any** wiring the installer makes, with calibrated confidence, and we can show how accurate they are.

## You own

- `Scripts/Labels/QrLabelScanner.cs` – QR decoding (ZXing.Net)
- `Scripts/Detection/PerceptionClient.cs`, `PerceptionWireReader.cs`, `PerceptionTestBench.cs`
- `Scripts/AI/PerceptionSecondOpinion.cs`
- `ml/` – the perception service, evaluation, later training
- `Scenes/Sandbox/Perception_Test.unity`

## Get it running (30 minutes)

1. Start the service from the repository root:
   ```
   python -m pip install -r ml/requirements.txt
   python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 8000
   python -m pytest ml/tests
   ```
   It starts with the real reader (`classical`). To test the plumbing with fixed answers, start it with `IA_READER=dummy` (PowerShell: `$env:IA_READER = "dummy"`): a correctly wired bench, whatever the photo. Never use it for a demo.
2. Unity: open `Scenes/Sandbox/Perception_Test.unity`, assign a bench photo to `PerceptionTestBench.image` (import settings: Read/Write on), Play. You see the dots the service returned.
3. The whole app on live services: `Scenes/App.unity` → `Installer Assistant` → Mode = Live, Test Label = `IA|C01|D1|A1610.READER1|A4020E`. Play: the laptop webcam is the camera, the service's reader reads the wires (start it with the dummy reader to see the full flow before the real one works), and the flow runs from the label to the report. With `IA_READER=dummy-elsewhere` the cable "sits" on Reader 2 instead, so you see the zone check ask to move it to Reader 1; with `IA_READER=dummy-tangled` the wire on 12V traces to another cable, so you see the bundle check. Use a Test Label with the end, e.g. `IA|C01|D1|A1610.READER1|A4020E|CTRL`, to go straight to the controller.

## What to build, in order

1. **QR labels.** Add ZXing.Net (see the comment at the top of `QrLabelScanner.cs`), add the define `ZXING_NET`, print test labels, measure the decode rate at 30–60 cm. A label is the cable's plan: `IA|C01|D1|A1610.READER1|A4020E|DEV` at the reader and the same with `|CTRL` at the controller (format in `Labels/CableLabel.cs`). Print a pair for Reader 2 too (`IA|C02|D2|A1610.READER2|A4020E|…`). The ZXing code path is written but untested until the DLL is in the project.
2. **Locate the modules** (`readers/classical.py`, `locate`): ORB + RANSAC homography to a template photo with hand-placed terminal points. Output: positions. At the controller, use one template of the **whole board** with the points of every module, so one homography places Reader 1, Reader 2 and Door 1 at once. Reader 1 and Reader 2 have the same shape; their place on the board and their housing colour (blue, green) tell them apart.
3. **Zone survey** (`survey`): for every module in `other_modules`, report whether it is in the frame and which pins hold a wire. Here the main question is "a wire or not"; colours only make the app's message firmer. The app uses it to say "move the cable to Reader 1" when the module the label names is empty. Measure it as module occupancy accuracy (`evaluate.py`).
4. **Trace the wires to the label** (`tracing.py`, the bundle check). `/read` gets the scanned label; for each wire say whether it runs back to that label (`this_cable`), to another cable's label (`other_cable`) or `unknown`. The baseline works on synthetic pictures (see `ml/tests/test_tracing.py`): it decodes every QR label in the frame and grows a region from each terminal over the wire's colour (seeded region growing, Adams & Bischof 1994). Measure trace coverage and accuracy on tangled bench photos; if coverage is low, follow centre lines instead (Zhang & Suen 1984 thinning, least bending at crossings as in mBEST, Choi et al. 2023), then try learned wire segmentation (FASTDLO / Ariadne+, Caporali et al. 2022). "Unknown" is always safe: it never counts against the wiring.
5. **Colour** (`colour.py`): nearest CIELAB reference. Replace the whole-image grey-world balance (the green board biases it) with a balance from a known white area; move the reference colours out of the code into a data file fitted on session-1 photos; fit the softmax temperature so the confidence is calibrated; then try a small trained classifier against this baseline.
6. **Second opinion**: a separate VLM call (`/compare`) on both images and the pairing rule, never given layer 1's reading. JSON answer per pin.
7. **Evaluation** (`ml/eval/evaluate.py`): decode rate, colour accuracy and confusion matrix, position error, ECE, coverage above the 0.80 amber threshold, module occupancy accuracy (zone survey), trace coverage and accuracy (bundle check), latency. Annotations format: see the top of `evaluate.py`. The shot list (`ml/tools/random_wirings.py`) puts about one controller shot in six on Reader 2 while the label says Reader 1. For tangled shots, make a second labelled bundle from the spare wires and put one of its wires on a pin of this module; annotate that terminal with `"cable": "other"`.
8. Option for later: export a model to ONNX and run it on the phone (Unity Sentis) behind the same `IWireReader`.

## Rules for you

- Change the JSON contract in `schemas.py` and `PerceptionClient.cs` together, in one pull request.
- Nothing hard-coded in live mode: no fault, colour order or pin position in code. Layouts are templates, colours and temperature are fitted data files.
- Never commit photos, datasets, model weights or API keys (`.gitignore` covers `data/`, `ml/runs/`, `*.pt`, `secrets*.json`).
- `python -m pytest ml/tests` must pass before you merge.
