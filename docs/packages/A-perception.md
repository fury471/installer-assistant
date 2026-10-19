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
3. The whole app on live services: `Scenes/App.unity` → `Installer Assistant` → Mode = Live, Test Label = `IA|D1|READER|C01`. Play: the laptop webcam is the camera, the service's reader reads the wires (start it with the dummy reader to see the full flow before the real one works), and the flow runs from the label to the report.

## What to build, in order

1. **QR labels.** Add ZXing.Net (see the comment at the top of `QrLabelScanner.cs`), add the define `ZXING_NET`, print test labels (QR payloads such as `IA|D1|READER|C01|DEV` and `…|CTRL`), measure the decode rate at 30–60 cm. The ZXing code path is written but untested until the DLL is in the project.
2. **Locate the connector** (`readers/classical.py`, `locate`): ORB + RANSAC homography to a template photo of the A1610 Reader 1 block and of the A4020-E terminals, with hand-placed terminal points. Output: positions.
3. **Colour** (`colour.py`): nearest CIELAB reference. Replace the whole-image grey-world balance (the green board biases it) with a balance from a known white area; move the reference colours out of the code into a data file fitted on session-1 photos; fit the softmax temperature so the confidence is calibrated; then try a small trained classifier against this baseline.
4. **Second opinion**: a separate VLM call (`/compare`) on both images and the pairing rule, never given layer 1's reading. JSON answer per pin.
5. **Evaluation** (`ml/eval/evaluate.py`): decode rate, colour accuracy and confusion matrix, position error, ECE, coverage above the 0.80 amber threshold, latency. Annotations format: see the top of `evaluate.py`.
6. Option for later: export a model to ONNX and run it on the phone (Unity Sentis) behind the same `IWireReader`.

## Rules for you

- Change the JSON contract in `schemas.py` and `PerceptionClient.cs` together, in one pull request.
- Nothing hard-coded in live mode: no fault, colour order or pin position in code. Layouts are templates, colours and temperature are fitted data files.
- Never commit photos, datasets, model weights or API keys (`.gitignore` covers `data/`, `ml/runs/`, `*.pt`, `secrets*.json`).
- `python -m pytest ml/tests` must pass before you merge.
