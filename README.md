# Installer Assistant

AR app (Unity, Android) that helps installers verify the wiring of an AXIS A1610 Network Door Controller and a Reader.
MAMN60 Augmented Reality Interaction, LTH, Lund University – group project with Axis Communications.

## Unity version

**Everyone must use exactly this version:** `6000.3.24f1`

Modules needed: Android Build Support, OpenJDK, Android SDK & NDK Tools.

## Start here

1. `docs/setup-guide.md` – first time only: tools, clone, daily workflow, Android build.
2. `docs/architecture.md` – how the app is built: one app, two modes, the contracts, who owns what.
3. Your package's getting-started page: `docs/packages/A-perception.md`, `B-ar-runtime.md` or `C-app.md`.
4. `docs/blueprint-demo.md` – the Wizard-of-Oz mode: operator keys, photos, calibration, the design system.
5. `docs/work-split-plan.md` – the plan: what is frozen, the three packages, collaboration rules, milestones, risks.
6. `docs/design-rationale.md` – why the app is designed as it is, with references; the source for the course report.

Run the app: open `Assets/_Project/Scenes/App.unity`, Game view 1080 × 2340 portrait, Play.
It starts in Wizard-of-Oz mode (bench photos). Set **Mode = Live** on the `Installer Assistant` object to run on the camera.

## The product and the Wizard of Oz

There is **one app** (`Scripts/App`). It talks to the outside only through contracts, and `Composition.cs` picks what stands behind them:

| Contract | Wizard of Oz (`Scripts/WizardOfOz`) | Live |
| --- | --- | --- |
| `ICameraView` (B) | `PhotoCameraView`: bench photos | `ArCameraView`: webcam now, AR Foundation next |
| `ILabelScanner` (A) | `WozLabelScanner` | `QrLabelScanner` (ZXing.Net) |
| `IWireReader` (A) | `WozWireReader` | `PerceptionWireReader` → `ml/perception_service` |
| `ISecondOpinion` (A) | `WozSecondOpinion` | `PerceptionSecondOpinion` → `ml/perception_service` |
| `IRecordStore` (C) | `InMemoryRecordStore` | `JsonRecordStore` |

The flow, screens, wiring rules, rule check, fixes, verdicts, records and log are the same code in both modes. The Wizard of Oz stays as the user-study fallback and the reference the live parts are tested against.

## Open the project

New to the project? Follow `docs/setup-guide.md`: tools to install, cloning, the daily workflow and the first Android build. In short:

1. Clone this repository (GitHub Desktop: File → Clone repository).
2. Unity Hub → Add → Add project from disk → select `unity/InstallerAssistant`.
3. The first open takes a few minutes while Unity builds its Library folder.

## Repository layout

| Folder | Contents |
| --- | --- |
| `unity/InstallerAssistant/` | The Unity project; our code is in `Assets/_Project/` |
| `docs/` | All project docs: setup guide, architecture, plan, design rationale, package pages, the Wizard-of-Oz guide (index in `docs/README.md`) |
| `ml/` | Perception service, evaluation and (later) training – package A |
| `tools/domain-tests/` | Unit tests of the wiring rules and verdicts: `dotnet run --project tools/domain-tests` |
| `data/` | Not in Git – photos and annotations live on the shared drive: https://drive.google.com/drive/folders/1NTnWubjdTvkZanh5BrwsZjqz3m9TzLMN |

## Team and ownership

Three packages that meet only at the contracts. Each person changes only what they own; anything else goes through a pull request the owner reviews.

| Package | Person | Owns (code) | Owns (scenes) |
| --- | --- | --- | --- |
| A · Perception | Yu | `Scripts/Detection`, `Scripts/AI`, `Labels/QrLabelScanner.cs`, `ml/` | `Scenes/Sandbox/Perception_Test.unity` |
| B · AR runtime and device | Xiaoxin | `Scripts/AR` (`ArCameraView`), Player and XR settings, Android builds | `Scenes/App.unity` (AR session, camera, build list) |
| C · App, records and study | Yaoju | `Scripts/App`, `Scripts/Records`, `Scripts/UI`, `Scripts/WizardOfOz`, `docs/` | `Scenes/Blueprint_Demo.unity` (test fixture) |
| Shared (all three approve) | – | `Scripts/Verification`, `Labels/CableLabel.cs`, the contract files (`ICameraView`, `CameraFrame`, `ILabelScanner`, `IWireReader`, `ISecondOpinion`, `IRecordStore`, `App/Services.cs`) | – |

`Blueprint_Demo.unity` is the frozen Wizard-of-Oz fixture: it must keep working after every merge (a quick manual test). Personal experiments go in `Scenes/Sandbox/<name>*.unity`.

## Rules

1. Same Unity version for everyone.
2. Never commit `Library/`.
3. Always commit `.meta` files with their assets.
4. Each scene has one owner (table above); never save someone else's scene.
5. Work on a branch (`name/topic`), merge into `main` through a pull request reviewed by one other person.
6. A change to a contract (the shared row above) needs all three to approve.
7. `main` must always open in Unity with 0 errors, the domain tests and `python -m pytest ml/tests` must pass, and `Blueprint_Demo` must still run.
8. No Git LFS: photos, videos and `.apk` builds go on the shared drive, never in Git.
