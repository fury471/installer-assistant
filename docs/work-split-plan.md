# Installer Assistant – Blueprint Freeze & Work Split

Yu · 18 Oct 2026, updated 20 Oct 2026 (labels carry the cable's plan; zone check; bundle check). The team's plan: what is frozen, who builds what, how we work together, and when. Change it through a pull request like any other file.

Freeze the blueprint now: the flow, screens, wiring rules and verdict logic are settled, and the sensing services are already behind interfaces. Split the work along those interfaces into three packages: A · Perception & AI (Yu), B · AR runtime & device (Xiaoxin), C · App, records & study (Yaoju).

## Freeze verdict

Yes, freeze it, with one contract change first. The blueprint already separates what is settled (flow, screens, A1610 wiring rules, rule check, verdict table, records, log) from what is still faked (the sensing services behind interfaces). That boundary is exactly where three people can work in parallel.

| Frozen now | Still open (owned, not frozen) |
| --- | --- |
| Either-end-first flow, Home, walk, verdict, fix and report screens | Pixel polish after the user test |
| Wiring rules (`AxisCatalog`), `RuleChecker` (incl. fix where you stand), `VerdictCombiner` (amber below 0.80) | The confidence threshold value, tuned from real data |
| Label format `IA\|id\|door\|controller module\|device module\|CTRL/DEV` (changed 20 Oct, below) | Physical label size and material |
| Interfaces `ICameraView`, `ILabelScanner`, `IWireReader`, `ISecondOpinion`, `IRecordStore` | How each is implemented (AR runtime, QR library, CV method, which VLM, file format) |
| Design system (`UiKit`), adaptive tag layout (`TagLayout`) | Tag behaviour under live camera motion |

**The one change before freezing (made on 18 Oct).** The terminal positions on the photo came from hand-calibrated points in `DemoLayout`. On a live camera nothing provides them, so the wire reader must return them. An image position per terminal (and the frame it refers to) is now on `DeviceEndReading` and `ControllerReading`, and a `CameraFrame` type carries the image from the AR runtime to the readers. After that change, no package needs to edit another package's code. It is in the code: `Detection/CameraFrame.cs`, `ImagePoint` positions on both readings, and frames passed to `ILabelScanner`, `IWireReader` and `ISecondOpinion`; the Wizard-of-Oz versions fill them from the bench photos.

## Three work packages

Each package owns one layer and talks to the others only through the frozen contracts, so each person can build and test alone with the Wizard-of-Oz stand-ins until integration. Getting-started pages: `docs/packages/A-perception.md`, `B-ar-runtime.md`, `C-app.md`.

| Package | Owner | Builds | Owns (code) | Done when |
| --- | --- | --- | --- | --- |
| A. Perception & AI | Yu | QR label decoding; terminal localisation on the A1610 board (Reader 1, Reader 2, Door 1) and the A4020-E terminal row; the zone survey of the controller's other modules; tracing each wire back to the scanned label; wire-colour reading with confidence; the independent AI second opinion; an offline evaluation set and harness | `Scripts/Detection`, `Scripts/AI`, `Labels/QrLabelScanner.cs`, `ml/` | On held-out bench photos of random wirings: labels decoded, terminals located, modules surveyed and colours read with a measured accuracy and latency; the readers pass the same C# tests as the WoZ versions |
| B. AR runtime & device | Xiaoxin | Android build with AR Foundation/ARCore; the live camera view replacing the photo; hands each frame to A as a `CameraFrame`; anchors A's 2D terminal positions in the world so markers and tags stay put while the phone moves | `Scripts/AR` (`ArCameraView`; the contract `ICameraView` is shared), `Scenes/App.unity` (AR session and camera), Player and XR settings, Android builds | On the phone: live camera, markers and tags locked to the terminals while the installer moves, 30 fps, one-tap APK build |
| C. App, records & study | Yaoju | The app around the check (Home, Reports, Settings, report export); records saved to a JSON file per site; QR label pair printing from the cable's plan; session log; user-study protocol, NASA-TLX/SUS forms, and lead on the report | `Scripts/App` (the app and `Composition`), `Scripts/Records`, `Scripts/UI`, `Scripts/WizardOfOz`, `docs/`, `Scenes/Blueprint_Demo.unity` (the Wizard-of-Oz fixture) | Records survive an app restart; labels print in CTRL/DEV pairs that carry the plan; the study runs end to end on the WoZ build and on the live build |

Shared and owned by all three through review: `Scripts/Verification` (wiring rules, rule check, verdict table), `Labels/CableLabel.cs` and the contract files.

## Ownership map

```mermaid
flowchart TB
    subgraph A["A · Perception & AI · Yu"]
        A1["QR label decoding"]
        A2["Terminal localisation, zone survey, wire tracing"]
        A3["Wire colour + confidence"]
        A4["AI second opinion"]
    end
    subgraph B["B · AR runtime & device · Xiaoxin"]
        B1["ARCore camera, Android build"]
        B2["Camera frames to A"]
        B3["World anchor per terminal"]
        B4["Markers and tags placed live"]
    end
    subgraph C["C · App, records & study · Yaoju"]
        C1["Home, Reports, Settings"]
        C2["JSON record store"]
        C3["QR label pairs carrying the plan"]
        C4["Study protocol and report"]
    end
    K["Frozen contracts<br/>ICameraView · CameraFrame · ILabelScanner · IWireReader · ISecondOpinion · IRecordStore · terminal anchors"]
    D["Shared domain<br/>AxisCatalog · RuleChecker · VerdictCombiner · ZoneCheck · BundleCheck · CableLabel"]
    A --> K
    B --> K
    C --> K
    K --> D
```

No package calls into another's code: they meet in the contract layer, and the Wizard-of-Oz stand-ins implement every contract until the live versions land.

## Frozen contracts

These types are the only things two packages share; changing one needs a pull request approved by all three. Everything behind them is free for its owner to change. The full list with files is in `docs/architecture.md`.

| Contract | From → to | What crosses | Note |
| --- | --- | --- | --- |
| `CameraFrame` | B → A | CPU image (RGBA32 or JPEG), width, height, intrinsics, timestamp, camera pose, id | B fills it from ARCore's CPU image; A never touches AR Foundation |
| `ICameraView` | B → app | The camera image, frames for the readers, anchors (`Anchor`, `TryGetPosition`) and end snapshots | `PhotoCameraView` in the Wizard of Oz, `ArCameraView` live; carries the terminal anchors |
| `ILabelScanner.Scan` | A → app | `LabelScan`: status, `CableLabel` (cable id, door, plan: controller module → device, and CTRL/DEV), label corners in the image | Corners let B anchor the label box |
| `IWireReader.ReadDeviceEnd / ReadControllerEnd` | app → A → app | In: the rule, the scanned `CableLabel`, the frame. Out: `DeviceEndReading` / `ControllerReading`: colour + confidence per terminal or pin, image position per terminal, `Traces` (where each wire runs back to: this cable, another cable, unknown); at the controller also `OtherModules`, the controller's other modules seen in the same frame | Position in normalised image coordinates of the frame it was read from; no survey = no zone check; no traces = no bundle check |
| `ISecondOpinion.Compare` | A → app | `Opinion` per pin (match, mismatch, no answer) | Must not see the rule check's answer |
| Terminal anchors | B → UI | World anchor per terminal, projected to screen each frame | Replaces `DemoLayout` points; `ArOverlay` stays as it is |
| `IRecordStore` | C → app | Save and get both ends by cable key | JSON file per site |
| Perception HTTP/JSON | A (Python) ⇄ A (C#) | `/read`, `/compare`, `/health` | `ml/perception_service/schemas.py` ⇄ `Detection/PerceptionClient.cs` |
| `RuleChecker`, `VerdictCombiner`, `AxisCatalog`, `ZoneCheck`, `BundleCheck` | shared | Pure C#, unit-tested in `tools/domain-tests` | Change only with a new test |

The Wizard-of-Oz versions stay as the fallback implementation of every contract, so the demo and the user study keep running whatever state the live parts are in.

**One app, two modes (added 18 Oct).** The product is one app in `Scripts/App`. `Composition.cs` picks the Wizard-of-Oz or the live implementation of each contract from the app's Mode, so the demo is no longer a separate program. `App.unity` starts in Wizard-of-Oz mode; Mode = Live runs a webcam (until B's AR view lands), QR decoding, the perception service in `ml/perception_service` and JSON records. Details: `docs/architecture.md`.

**Labels carry the plan; zone check (changed 20 Oct, needs all three to approve).** The label no longer names a usage (`IA|D1|READER|C01`); it names the cable's plan: `IA|C01|D1|A1610.READER2|A4020E|CTRL` (cable id, door, controller module, device module, this end). The door is for people only, because a planner may put Door 1's reader on Reader 2. `AxisCatalog.Resolve(label)` binds the wiring template to the named module, so Reader 2 is now supported with no new rule. At the controller, the wire reader also surveys the controller's other modules, and `ZoneCheck` runs before the pins: when the planned module is empty but another holds wires, the app says "Move the cable to Reader 1" and marks both modules, with "Not this cable" to dismiss it. Then, at either end, `BundleCheck`: the wire reader gets the scanned label and traces each wire back to it, and a wire that runs to another cable is named before any pin is judged ("The wire on 12V is from another cable"), because different cables often share colours. Each wiring rule stays extracted from the manuals for one use case; there is still one (an OSDP reader on a reader port). What changes per package. **A**: `/read` takes `module`, `other_modules` and `label`, and returns `other_blocks` and a `trace` per item; `IWireReader` takes the label; `/compare` takes `controller_module` and `device_module`; the reader needs a whole-board template. **B**: anchors also use `module:<id>` keys; `ArCameraView` needs no change. **C**: print labels in the new format; the zone sheet and AR hints are in `InstallerApp.Check.cs`. Old labels and records in the old format no longer load. Details: `docs/design-rationale.md`, "The label is the plan, and the zone check".

## Perception and AI plan (Yu)

Run perception as a small Python service on a laptop on the same Wi-Fi, with one thin C# client per contract in Unity. That keeps package A in Python and OpenCV, keeps the AI API keys off the phone, and makes the whole pipeline testable on photos without Unity. The app already waits on a progress bar, so 1–3 s per reading fits the flow.

**Pipeline per end** (one `POST /read` with the frame, the end and the module the label names; at the controller also the other modules to survey):

1. **Label.** Decode the QR code (ISO/IEC 18004) and return its corners. In Unity this can stay on the phone with ZXing.Net, since it needs no model.
2. **Locate the terminals.** Match the frame to a reference photo (the whole A1610 board with every module's pins marked, and the A4020-E terminal row) with ORB features (Rublee et al., 2011) and a RANSAC homography (Fischler & Bolles, 1981; Hartley & Zisserman, 2004). The known pin layout from `AxisCatalog` is then projected into the frame, giving every terminal's position. Fallback: two taps on the end pins, as the Wizard of Oz's calibration already does.
3. **Trace each wire back to the label.** Find every QR label in the frame, grow a region from each terminal over the wire's colour (seeded region growing; Adams & Bischof, 1994) and see which label it reaches: this cable, another cable, or unknown (`tracing.py`, the baseline). Next steps: centre lines with crossing resolution (Zhang & Suen, 1984; Choi et al., 2023), then learned wire instance segmentation (Caporali et al., 2022).
4. **Survey the other modules.** The same board homography places Reader 2 and Door 1 too; report whether each is in view and which pins hold a wire, for the zone check. Reader 1 and Reader 2 have the same shape, so their place on the board and their housing colour (blue, green) tell them apart.
5. **Read the colours.** Crop a short strip of wire beside each terminal. Baseline: grey-world white balance (Buchsbaum, 1980), then the nearest of the 8 wire colours in CIELAB, with the margin to the second-nearest as the raw confidence. Second path: a vision-language model on the crops, not the whole frame, because these models miss fine visual detail in full scenes (Tong et al., 2024).
6. **Calibrate confidence.** Fit temperature scaling on held-out data (Guo et al., 2017) so that 0.80 means about 80 % correct. Only then does the amber threshold mean what the UI says it means.
7. **Second opinion.** A separate model call that sees both ends' crops and the pairing rule, never the rule check's answer, as layer 3 of the verdict design.

**Nothing hard-coded.** The live readers must work for any wiring an installer makes, so nothing in package A assumes a fault or a colour order. Connector layouts are templates (a reference photo with its pins marked), colour references and the temperature are fitted data files, and adding a connector or a colour means adding data, not code. The `dummy` readers are for testing the plumbing only and are never the default.

**Evaluation, built first.** A labelled set from the bench built from **random wirings**: a script (`ml/tools/random_wirings.py`) draws the wirings, mostly shuffles among the pins the rule uses and some with wires on pins that must stay empty, about one controller shot in six with the whole cable on Reader 2 while its label says Reader 1, and some tangled shots with a second labelled bundle whose wire sits on a pin of this module, so the set is not biased towards the faults we expect. Each wiring is photographed under 3 lighting conditions and 2 angles; a few unclear cases (two wires in one pin, a half-inserted wire) are added on purpose to test that the reader reports low confidence. Two sessions on different days: session 1 for fitting, session 2 only for testing, so test photos are never near-duplicates of fitting ones. The harness in `ml/eval` reports:

- label decode rate
- terminal position error, and the share within half a pin pitch
- colour accuracy with a confusion matrix
- module occupancy accuracy (zone survey: does each module hold wires)
- trace coverage and trace accuracy (bundle check: share of wires traced to a cable, and how often rightly)
- expected calibration error
- latency p50 and p95 measured from the phone

These numbers go straight into the report's results section.

## Collaboration rules

Most Unity merge pain comes from two people editing the same scene, prefab or asset; these rules make that impossible by ownership rather than by care.

- **One scene per package.** A: `Sandbox/Perception_Test.unity`, B: `App.unity` (the product scene; it gets the AR session), C: `Blueprint_Demo.unity` (the Wizard-of-Oz fixture, which must keep running after every merge). Nobody saves another package's scene; experiments go in `Sandbox/<name>*.unity`.
- **Branches.** `name/topic`, as the repo already does (e.g. `fury/wire-reader`), off `main`; a pull request into `main` for every merge, reviewed by one other member. `main` must always open in Unity without errors.
- **Code owners.** `docs/CODEOWNERS.example` maps each folder to its owner. Fill in the GitHub names and copy it to `.github/CODEOWNERS`, so GitHub asks the right person to review; the shared folders need all three.
- **Contract changes.** Proposed as a pull request that changes only the contract and its WoZ stand-in, with a one-line reason; merged when all three approve.
- **Tests on every PR.** `dotnet run --project tools/domain-tests` and `python -m pytest ml/tests` stay green; A adds a perception test that runs the C# client against saved responses.
- **No Git LFS** (the group decided against it). Keep committed photos under 1 MB each; the perception dataset lives outside the repo on the shared drive, with only a small sample set committed.
- **Docs live in the repo.** This plan, the design rationale and the guides are files in `docs/`; change them with a pull request, not in a separate shared document.
- **Weekly integration.** One fixed slot a week where `main` is built to the phone and the full flow is run once.

## Milestones

Live check end to end by 15 Nov, leaving four weeks for fixes and the study. Proposed dates to the 17 Dec seminar:

| When | What |
| --- | --- |
| 19–25 Oct | Contract change, stubs, dataset |
| 26 Oct – 15 Nov | A, B and C build in parallel |
| 1 Nov | **M1** live camera + label scan |
| 8 Nov | **M2** terminals found live |
| 15 Nov | **M3** full check live |
| 16–29 Nov | Fixes and refinement |
| 30 Nov – 13 Dec | User study and report |
| 17 Dec | Seminar demo |

```mermaid
gantt
    dateFormat YYYY-MM-DD
    axisFormat %d %b
    section Build
    Contract change, stubs, dataset :2026-10-19, 2026-10-25
    A, B and C in parallel          :2026-10-26, 2026-11-15
    M1 live camera + label scan     :milestone, 2026-11-01, 0d
    M2 terminals found live         :milestone, 2026-11-08, 0d
    M3 full check live              :milestone, 2026-11-15, 0d
    section Finish
    Fixes and refinement            :2026-11-16, 2026-11-29
    User study and report           :2026-11-30, 2026-12-13
    Seminar demo                    :milestone, 2026-12-17, 0d
```

Each milestone is a weekly integration run on the phone: M1 needs B's live camera and A's label scan, M2 adds A's terminal positions anchored by B, and M3 runs the whole check with C's stored records. The two weeks after M3 are fixes only, as the group planned for weeks 6–7.

## Risks and fallbacks

The largest risk is terminal localisation on a live camera; every risk below has a fallback that keeps the seminar demo and the user study running.

| Risk | Owner | Early sign | Fallback |
| --- | --- | --- | --- |
| Terminals not found reliably by feature matching (repetitive, glossy blocks) | A | Under 80 % of test photos within half a pin pitch by milestone 2 | Two-tap calibration of the end pins, then projection from the catalogue layout |
| Wires not traceable in clutter (same colours touching, label out of view) | A | Trace coverage under 70 % on tangled test photos | Traces stay "unknown", so the bundle check stays silent and nothing else changes; improve with centre lines and crossing resolution |
| Reader 1 and Reader 2 confused (identical blocks) | A | Module occupancy accuracy under 95 % on test photos | Whole-board template plus housing colour; if still unsure, return no survey: the zone check stays silent and the pin check reports missing wires |
| Colour reading wrong under server-room light | A | Confusion between black/brown or yellow/orange above 10 % | Amber more often (higher threshold), installer confirms; the flow already supports it |
| AI service slow or offline | A | p95 latency above 5 s | Classical colour reading alone; second opinion switched off in Settings |
| AR anchors drift while the phone moves | B | Markers move more than one pin pitch | Freeze the frame for the result view (a still image with the overlay) |
| No power at the bench, so no LED symptom to show | C | — | Already handled: the app shows the documented symptom as text |
| Integration lands late | all | Milestone 3 slips | WoZ build stays the study build; live build shown at the seminar for the parts that work |

## References

Design rationale for the frozen blueprint: `docs/design-rationale.md`. Run and code notes: `docs/architecture.md` and `docs/blueprint-demo.md`.

- Adams, R., & Bischof, L. (1994). Seeded region growing. *IEEE Transactions on Pattern Analysis and Machine Intelligence*, 16(6), 641–647. [doi:10.1109/34.295913](https://doi.org/10.1109/34.295913)
- Brooke, J. (1996). SUS: A "quick and dirty" usability scale. In *Usability Evaluation in Industry* (pp. 189–194). Taylor & Francis.
- Buchsbaum, G. (1980). A spatial processor model for object colour perception. *Journal of the Franklin Institute*, 310(1), 1–26.
- Caporali, A., Galassi, K., Zanella, R., & Palli, G. (2022). FASTDLO: Fast deformable linear objects instance segmentation. *IEEE Robotics and Automation Letters*, 7(4), 9075–9082. [doi:10.1109/LRA.2022.3189791](https://doi.org/10.1109/LRA.2022.3189791)
- Choi, A., Tong, D., Park, B., Terzopoulos, D., Joo, J., & Jawed, M. K. (2023). mBEST: Realtime deformable linear object detection through minimal bending energy skeleton pixel traversals. *IEEE Robotics and Automation Letters*. [doi:10.1109/LRA.2023.3290419](https://doi.org/10.1109/LRA.2023.3290419)
- Fischler, M. A., & Bolles, R. C. (1981). Random sample consensus: A paradigm for model fitting with applications to image analysis and automated cartography. *Communications of the ACM*, 24(6), 381–395. [doi:10.1145/358669.358692](https://doi.org/10.1145/358669.358692)
- Guo, C., Pleiss, G., Sun, Y., & Weinberger, K. Q. (2017). On calibration of modern neural networks. *Proc. ICML 2017*.
- Hart, S. G., & Staveland, L. E. (1988). Development of NASA-TLX (Task Load Index). *Advances in Psychology*, 52, 139–183.
- Hartley, R., & Zisserman, A. (2004). *Multiple View Geometry in Computer Vision* (2nd ed.). Cambridge University Press.
- ISO/IEC 18004:2015. Information technology – Automatic identification and data capture techniques – QR Code bar code symbology specification.
- Martin, R. C. (2017). *Clean Architecture*. Prentice Hall.
- Rublee, E., Rabaud, V., Konolige, K., & Bradski, G. (2011). ORB: An efficient alternative to SIFT or SURF. *Proc. ICCV 2011*, 2564–2571.
- Tong, S., Liu, Z., Zhai, Y., Ma, Y., LeCun, Y., & Xie, S. (2024). Eyes wide shut? Exploring the visual shortcomings of multimodal LLMs. *Proc. CVPR 2024*.
- Zhang, T. Y., & Suen, C. Y. (1984). A fast parallel algorithm for thinning digital patterns. *Communications of the ACM*, 27(3), 236–239. [doi:10.1145/357994.358023](https://doi.org/10.1145/357994.358023)
