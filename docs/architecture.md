# Architecture

One app, two modes. The app (`Scripts/App`) never touches the camera, the perception models or the file system directly. It talks to five contracts, and `App/Composition.cs` decides at start-up which implementation stands behind each one.

```
                    ┌──────────── InstallerApp (C) ────────────┐
                    │ Home · Reports · Settings · Report       │
                    │ Check: scan label → zone check → record  │
                    │ end → walk → pair → rule check, verdicts │
                    │ → fix                                    │
                    └───┬────────┬─────────┬─────────┬────────┬┘
                        │        │         │         │        │
                 ICameraView ILabelScanner IWireReader ISecondOpinion IRecordStore
                   (B)        (A)          (A)        (A)            (C)
 Wizard of Oz:  PhotoCameraView  WozLabelScanner  WozWireReader  WozSecondOpinion  InMemoryRecordStore   ← WizardOfOzRig
 Live:          ArCameraView     QrLabelScanner   PerceptionWireReader  PerceptionSecondOpinion  JsonRecordStore
                                                  └──── HTTP/JSON ──── ml/perception_service (Python)
```

Under the contracts sits the shared domain, plain C# with no Unity types: `Verification` (A1610 and A4020-E facts, records, zone check, bundle check, rule check, verdict table) and `Labels/CableLabel` (label format, the cable's plan and pairing). The domain tests in `tools/domain-tests` compile it directly.

## Choosing the mode

`InstallerApp.mode` on the `Installer Assistant` object in `Scenes/App.unity`:

- **Wizard of Oz**: needs a `WizardOfOzRig` on the same object (it is there). Photos from a `DemoLayout`, scripted readings, operator keys. If the rig is missing the app logs an error and runs live.
- **Live**: `ArCameraView` (added automatically if the scene has none), QR decoding, the perception service at `perceptionUrl`, records in `persistentDataPath/recordsFile`. `testLabel` makes the scanner "find" a label, so the live flow can be tried before QR decoding works.

The `App` folder does not reference the `WizardOfOz` folder: the rig plugs in through `IServiceSource`. Deleting `Scripts/WizardOfOz` would still compile.

## How a check runs

1. **Scan a label.** The app doesn't know the cable or the end yet. It asks the camera view for frames and the label scanner for the label. The label is the cable's plan, `IA|C01|D1|A1610.READER2|A4020E|CTRL`: cable id, door, the controller module and the device module it connects, and the end this label sits on. `AxisCatalog.Resolve` binds the wiring rule to the module the label names (here Reader 2); a label the catalogue can't resolve is refused with a reason. Each wiring rule is extracted from the manuals for one use case; the MVP has one (an OSDP reader on a reader port). If the label has no end, the app asks once which end this is.
2. **Load what is known.** Records for that key come from the record store, so the other end may have been saved on another day.
3. **First end.** The wire reader reads the end, the installer confirms (one tap, or a tap on an amber wire), and the end is saved. The view keeps a snapshot of it. At the controller, the zone check runs first (step 5); at either end, the bundle check runs before the wires can be saved.
4. **Walk.** The app says where the other end is (from the label and the wiring rule). "Finish later" keeps the cable waiting on Home.
5. **Second end.** The label must pair (same key and plan, other end); two labels of one cable with different plans are flagged as a misprint. At the controller, the **zone check** comes first: the wire reader also surveys the controller's other modules (`ControllerReading.OtherModules`), and if the planned module is empty while another holds wires, the app says "Move the cable to Reader 2" (or asks "Is this cable on Reader 1?") and marks both modules in the camera view; "Not this cable" dismisses it. Next, the **bundle check**: the wire reader, given the scanned label, follows each wire back and says whether it reaches this label, another cable's label or cannot tell (`Traces` in the readings); a wire of another cable is marked red ("other cable") and named before any pin is judged, and "They are this cable's" dismisses it. Then the rule check runs, the second opinion runs in parallel, and the verdict table combines them.
6. **Fix where you stand.** Wire colours carry no meaning of their own (assumption below), so a fault that only exchanges the cable's own wires (A/B swapped, + and − reversed, a rotation) is fixed at whichever end the installer is at. Only when a fix needs the controller (a wire on a pin the rule does not use, an extra or a loose wire) does the app send the installer there, and then shows every fix there, in one place. `RuleCheck.FixableAtDevice` decides.

## Assumptions

These hold for the MVP and are explained, with sources, in `docs/design-rationale.md` (the report's list).

1. Every cable carries a QR label at both ends with its plan (controller module → device module) and the end it sits on (`…|CTRL`, `…|DEV`); a missing label is asked for. The label is the plan: the app checks against it and never guesses the module from the door.
2. The wires within one cable have distinct colours (different cables may share colours).
3. One use case: an AXIS A4020-E reader on a reader port of the A1610 (Reader 1 or Reader 2, as the label plans), OSDP over half-duplex RS-485, powered by the controller.
4. The two ends may be far apart and checked at different times: one end is recorded, the other compared with it.
5. The LED symptoms assume a powered controller.
6. The amber threshold (0.80) is a starting value, set from the accuracy test.
7. Wire colours carry no meaning of their own: neither Axis manual assigns colours and OSDP has no colour code, so a colour only tells the wires of one cable apart, and a wiring is correct when both ends agree and the controller end matches the A1610 pin table. (A per-site colour convention could be added later as a setting.)
8. The zone check sees only the modules in the same camera frame as the planned one.
9. The label sits where the cable's wires fan out, in the same picture as the terminals, so each wire can be followed back to it; otherwise the traces are unknown and the bundle check stays silent.

## Positions and anchors

Readers return positions as `ImagePoint`s normalised to the frame they read (x right, y down, 0–1). The app hands each one to the view with `Anchor(key, point, frame)` and asks every frame `TryGetPosition(key)`. Keys come from `CameraKeys`: `label`, `terminal:<i>` (device end), `pin:<n>` (controller), `module:<id>` (a whole module, e.g. `module:A1610.READER2`, for the zone check's hints).

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
| `Labels/CableLabel.cs` | the label format: cable id, door, controller module, device module, end |
| `Detection/IWireReader.cs` | given the rule and the scanned label: colour, confidence, position and trace (where the wire runs back to) per terminal or pin; at the controller also the other modules (zone survey) |
| `AI/ISecondOpinion.cs` | match / mismatch / unknown per pin, from both frames, independent of layer 1 |
| `Records/RecordStore.cs` (`IRecordStore`) | both ends per cable key |
| `App/Services.cs` | `ServiceSet`, `OverlayStyle`, `IServiceSource`, `IStudyOperator` |
| `ml/perception_service/schemas.py` ⇄ `Detection/PerceptionClient.cs` | the HTTP/JSON contract of the service |
