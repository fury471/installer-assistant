# Wizard-of-Oz mode

The app (`Scripts/App`) runs in Wizard-of-Oz mode on bench photos: its flow, screens, wiring rules, rule check, fix messages, verdicts, records and log are the real app. Only the camera and the sensing are scripted, by `WizardOfOzRig` (`Scripts/WizardOfOz`). The same app runs live when its mode is set to Live: see `docs/architecture.md`.

This mode is the user-study fallback, the demo for Axis, and the reference the live parts are tested against.

Design rationale for the report: see the "Installer Assistant – MVP Blueprint" doc.

## Run it

1. Open `Assets/_Project/Scenes/Blueprint_Demo.unity` (the frozen fixture) or `App.unity` with Mode = Wizard of Oz. Both use `Config/DemoLayout2` (real bench photos), set on the `WizardOfOzRig` component.
2. Game view: portrait, 1080 × 2340.
3. Press Play, then click the Game view once so it receives keys.

| Key | Does |
| --- | --- |
| `3` | Planted fault 1: **A/B swapped** (missing label at the controller, AI disagrees on pin 1) |
| `4` | Planted fault 2: **B wire on O2 (pin 6)** |
| `R` | Restart at Home with a fresh site visit |
| `1` / `2` | Start a fresh check at the reader end / at the controller end |
| `S` | Skip to the second end: the first end is filled in as configured |
| `K` | Calibrate the current photo. Reader: click the label, then `+ − A B`. Controller: click the label, pin 1 and pin 8; pins 2–7 are spaced evenly between them. |
| `H` | Hide the operator line (for the user study) |

## The flow

The installer does not need to know anything before starting. The app learns everything from the cable label.

1. **Home** has one action: **Scan a cable**. There is no job list or wiring table to read first.
2. **Either end first.** The label says which cable it is and which end the installer is at (`IA|D1|READER|C01|DEV` at the reader, `…|CTRL` at the controller). The app reads the wires at that end; the installer confirms them (one tap, or a tap on an amber wire the camera is unsure of) and saves.
3. **Walk.** The app says where the other end is (from the label and the wiring rule: "AXIS A1610 · Reader 1 block" or "Door 1 · AXIS A4020-E reader"). **Finish later** keeps the cable on Home under "Waiting for the other end".
4. **Pair and check.** At the other end the installer scans its label. The shared key pairs the two ends, and the rule check runs at once. The verdict is shown on the end in hand.
5. **Fix where you stand.** Wire colours carry no meaning of their own, so a fault that only exchanges the cable's own wires (planted fault 1, A/B swapped) is fixed at whichever end the installer is at, with arrows between the terminals there. Only a fix that needs the controller (planted fault 2, B on O2) sends the installer back to it ("Go back to the controller"), and then every fix is shown there. Starting at the controller with fault 1 therefore ends with the fix at the reader; starting at the reader, both faults are fixed at the controller.
6. **Report** when every wire matches.

Settings → Wizard of Oz (operator): **Participant starts at** chooses which photo the first "Scan a cable" shows (reader end by default). The app itself does not depend on it: it learns the end from the label.

In the session, "Rewired · check again" stands for the installer fixing the bench. The demo then shows the photo of the correctly wired board. For the live demo, re-wire the real bench at that moment.

After a fix at the reader the demo shows `DemoLayout.readerFixed`. DemoLayout2 has no photo there yet, so a drawn reader with the new colours appears: take a photo of the bench reader with A and B swapped (yellow on A, green on B), assign it to Reader Fixed, then press `2`, go through to the fix, "Rewired · check again", and `K` to place the terminals.

`Textures/Demo2/controller_wrongpin_edited.jpg` is `controller_fixed.jpg` with the yellow wire digitally moved to pin 6. Replace it with a real photo of that fault, then press `4`, `2` and `K` to place the pins.

## Code map

```
Scripts/
  App/            InstallerApp.cs (state, start-up, operator API), .Pages.cs (Home, Reports, Settings, report),
                  .Check.cs (scan, record, walk, pair, verdict, fix), Composition.cs (which services),
                  Services.cs (ServiceSet, OverlayStyle, IServiceSource, IStudyOperator)        ← C
  AR/             ICameraView (contract), ArCameraView (live: webcam now, AR Foundation next)    ← B
  Labels/         CableLabel (shared), ILabelScanner (contract), QrLabelScanner (live)           ← A
  Detection/      CameraFrame, IWireReader (contracts), PerceptionClient, PerceptionWireReader,
                  PerceptionTestBench (live)                                                     ← A
  AI/             ISecondOpinion (contract), PerceptionSecondOpinion (live)                      ← A
  Records/        IRecordStore, InMemoryRecordStore, SessionLog, JsonRecordStore (live)          ← C
  Verification/   AxisCatalog, CableRecord, ControllerRecord, RuleChecker, ControllerCheck       ← shared
  UI/             UiKit (design system), ArOverlay (markers, tag layout, fix arrows)             ← C
  WizardOfOz/     WizardOfOzRig (services + operator keys), PhotoCameraView (photos, calibration),
                  WozServices (scripted scanner, reader, second opinion), DemoLayout, MockPhotos ← C
Fonts/            Inter Regular + SemiBold (the only typeface), SIL OFL
```

## Design system

White and content-first, in the style of news and data apps: no frames around content. Grouping comes from white space, light grey blocks and thin dividers; lists read like a feed (a timeline on Home and Reports); one blue marks actions and selection. Every text colour meets WCAG 2.2 AA (at least 4.5:1). Contrast ratios are given on white.

| Token | Value | Used for |
| --- | --- | --- |
| Page / Surface | `#FFFFFF` | Page, sheets, camera pills and tags |
| Panel / Sunken | `#F6F7F9` / `#EEF0F3` | Grouped blocks / secondary buttons, tracks, segmented control |
| Hairline | `#E5E7EB` | 2 px dividers between list rows (no borders around blocks) |
| Ink / Ink 2 / Ink 3 | `#111827` (17.7:1) / `#4B5563` (7.6:1) / `#6B7280` (4.8:1) | Headlines and text / secondary / times and captions |
| Accent (blue) | `#2B63E6` (5.2:1), tint `#EBF1FE` | Primary button, active tab, selected option, switches, progress |
| Ok / Bad | `#15803D` (5.0:1) / `#C8281E` (5.6:1), tints `#E8F6EC` / `#FDEDEC` | Status, always with an icon and a word |
| Warn | fill `#F59E0B` with ink on top; text `#B45309` (5.0:1); tint `#FFF4DE` | "Check" (amber), "1 of 2 ends" |

Type: Inter only. SemiBold for headlines (54–72 px, tracking −1.5), row titles and buttons; Regular for reading text (body 38 px ≈ 14.5 sp). Buttons are fully rounded; one primary (blue) per screen, secondary buttons are light grey. Touch targets are at least 128 px (48 dp). A cable waiting for its other end stays one tap away in a floating bar above the tab bar.

**Tags on the camera view adapt to the screen** (`TagLayout` in `UI/ArOverlay.cs`). Each tag is as wide as its words. Tags are packed in the order of their terminals inside the free area (the screen minus the top controls and the bottom sheet), so they never overlap, never leave the screen and their leader lines never cross. A row that does not fit splits into two staggered tiers, then shrinks to at most 70 %; tags below a terminal row flip above it when the sheet covers the space. This is view management in the sense of Bell, Feiner & Höllerer (2001) and works the same on any phone aspect ratio.

The camera photo always fills the screen, like a real camera feed. If a view's `zoom` is below what its `pan` needs, the photo is scaled up to cover the screen, and the pan never goes past the photo's edges.

The fonts are created at runtime from the TTFs assigned on the `InstallerApp` component. If they are left empty, the TextMesh Pro default font is used.

## From the Wizard of Oz to live

Each scripted service has a live counterpart behind the same contract; `App/Composition.cs` picks them from the app's mode. Every service gets a `CameraFrame` and returns where it found things in that frame as normalised `ImagePoint`s (x and y from 0 to 1, y down); the camera view anchors them.

| Contract | Owner | Wizard of Oz | Live |
| --- | --- | --- | --- |
| `ICameraView` | B · AR runtime | `PhotoCameraView`: the photo of the end, frames without pixels | `ArCameraView`: webcam now; ARCore CPU image, intrinsics, pose and world anchors next |
| `ILabelScanner.Scan(frame)` | A · Perception and AI | `WozLabelScanner`: the layout's label and its corners | `QrLabelScanner`: ZXing.Net on the frame |
| `IWireReader.Read…End(rule, frame)` | A · Perception and AI | `WozWireReader`: the layout's colours and points | `PerceptionWireReader` → `ml/perception_service` `/read` |
| `ISecondOpinion.Compare(record, deviceFrame, controllerFrame)` | A · Perception and AI | `WozSecondOpinion`: disagrees once | `PerceptionSecondOpinion` → `/compare` |
| `IRecordStore` | C · App and records | `InMemoryRecordStore` | `JsonRecordStore` |

The contracts are frozen: change one only with a pull request all three approve.

## Tests

`tools/domain-tests` compiles the domain sources directly and checks both planted faults, power reversal, a missing wire, a foreign wire, a three-wire rotation, the verdict table, label parsing (including which end and pairing), the controller-first check and the CSV log.

```
dotnet run --project tools/domain-tests
```
