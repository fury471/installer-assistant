# Architecture

One app, two modes. The app (`Scripts/App`) never touches the camera, the perception models or the file system directly. It talks to five contracts, and `App/Composition.cs` decides at start-up which implementation stands behind each one.

```
                    ┌──────────── InstallerApp (C) ────────────┐
                    │ Home · Reports · Settings · Report       │
                    │ Check: scan label → record end → walk →  │
                    │ pair → rule check + verdicts → fix       │
                    └───┬────────┬─────────┬─────────┬────────┬┘
                        │        │         │         │        │
                 ICameraView ILabelScanner IWireReader ISecondOpinion IRecordStore
                   (B)        (A)          (A)        (A)            (C)
 Wizard of Oz:  PhotoCameraView  WozLabelScanner  WozWireReader  WozSecondOpinion  InMemoryRecordStore   ← WizardOfOzRig
 Live:          ArCameraView     QrLabelScanner   PerceptionWireReader  PerceptionSecondOpinion  JsonRecordStore
                                                  └──── HTTP/JSON ──── ml/perception_service (Python)
```

Under the contracts sits the shared domain, plain C# with no Unity types: `Verification` (A1610 and A4020-E facts, records, rule check, verdict table) and `Labels/CableLabel` (label format and pairing). The domain tests in `tools/domain-tests` compile it directly.

## Choosing the mode

`InstallerApp.mode` on the `Installer Assistant` object in `Scenes/App.unity`:

- **Wizard of Oz**: needs a `WizardOfOzRig` on the same object (it is there). Photos from a `DemoLayout`, scripted readings, operator keys. If the rig is missing the app logs an error and runs live.
- **Live**: `ArCameraView` (added automatically if the scene has none), QR decoding, the perception service at `perceptionUrl`, records in `persistentDataPath/recordsFile`. `testLabel` makes the scanner "find" a label, so the live flow can be tried before QR decoding works.

The `App` folder does not reference the `WizardOfOz` folder: the rig plugs in through `IServiceSource`. Deleting `Scripts/WizardOfOz` would still compile.

## How a check runs

1. **Scan a label.** The app doesn't know the cable or the end yet. It asks the camera view for frames and the label scanner for the label. The label gives the cable key (door, usage, id) and the end (`…|DEV` or `…|CTRL`). If the label has no end, the app asks once which end this is.
2. **Load what is known.** Records for that key come from the record store, so the other end may have been saved on another day.
3. **First end.** The wire reader reads the end, the installer confirms (one tap, or a tap on an amber wire), and the end is saved. The view keeps a snapshot of it.
4. **Walk.** The app says where the other end is (from the label and the wiring rule). "Finish later" keeps the cable waiting on Home.
5. **Second end.** The label must pair (same key, other end). The rule check runs at once, the second opinion runs in parallel, and the verdict table combines them.
6. **Fix where you stand.** Wire colours carry no meaning of their own (assumption below), so a fault that only exchanges the cable's own wires (A/B swapped, + and − reversed, a rotation) is fixed at whichever end the installer is at. Only when a fix needs the controller (a wire on a pin the rule does not use, an extra or a loose wire) does the app send the installer there, and then shows every fix there, in one place. `RuleCheck.FixableAtDevice` decides.

## Assumptions

These hold for the MVP and are explained, with sources, in `docs/design-rationale.md` (the report's list).

1. Every cable carries a QR label at both ends that says which end it is (`…|DEV`, `…|CTRL`); a missing label is asked for.
2. The wires within one cable have distinct colours (different cables may share colours).
3. One use case: an AXIS A4020-E reader on the A1610's Reader 1 block, OSDP over half-duplex RS-485, powered by the controller.
4. The two ends may be far apart and checked at different times: one end is recorded, the other compared with it.
5. The LED symptoms assume a powered controller.
6. The amber threshold (0.80) is a starting value, set from the accuracy test.
7. Wire colours carry no meaning of their own: neither Axis manual assigns colours and OSDP has no colour code, so a colour only tells the wires of one cable apart, and a wiring is correct when both ends agree and the controller end matches the A1610 pin table. (A per-site colour convention could be added later as a setting.)

## Positions and anchors

Readers return positions as `ImagePoint`s normalised to the frame they read (x right, y down, 0–1). The app hands each one to the view with `Anchor(key, point, frame)` and asks every frame `TryGetPosition(key)`. Keys come from `CameraKeys`: `label`, `terminal:<i>` (device end), `pin:<n>` (controller).

- Photo: the frame *is* the photo, so a position maps straight onto it.
- Live (stage 1): the view turns the point into a world anchor (raycast), and projects it back every frame, so markers stay on the terminals while the phone moves.

Markers without a known position are hidden, never guessed. Tags are laid out in screen space by `UI/ArOverlay.cs` (`TagLayout`) so they never overlap or leave the free area.

## Contracts

The contracts are frozen: change one only with a pull request all three approve. They are

| File | What it says |
| --- | --- |
| `AR/ICameraView.cs` | the camera image, frames, anchors, snapshots (`EndSnapshot`, `CameraKeys`) |
| `Detection/CameraFrame.cs` | one frame: pixels (RGBA32 bottom-up or JPEG), size, intrinsics, pose, id |
| `Labels/ILabelScanner.cs` | label + corners per frame |
| `Detection/IWireReader.cs` | colour, confidence and position per terminal or pin |
| `AI/ISecondOpinion.cs` | match / mismatch / unknown per pin, from both frames, independent of layer 1 |
| `Records/RecordStore.cs` (`IRecordStore`) | both ends per cable key |
| `App/Services.cs` | `ServiceSet`, `OverlayStyle`, `IServiceSource`, `IStudyOperator` |
| `ml/perception_service/schemas.py` ⇄ `Detection/PerceptionClient.cs` | the HTTP/JSON contract of the service |
