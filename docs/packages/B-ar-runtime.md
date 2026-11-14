# Package B · AR runtime and device

> **Current bench hardware — 2 Nov 2026 (confirmed by Xiaoxin): AXIS A1610 controller + A4020-E reader.** Keep `A1610.READER1` / `A1610.READER2` label and record IDs. Earlier A1601 bench measurements and the `A1610_A1601.jpg` reference variant retain their original provenance; they are not new A1610 results. Collect a separate batch after the hardware change.


**Goal:** the live camera view: real AR frames for the readers, and markers that stay on the terminals while the phone moves. The app runs on an Android phone from `Scenes/App.unity`.

## You own

- `Scripts/AR/ArCameraView.cs` (the contract `ICameraView.cs` is shared)
- `Scenes/App.unity`: the AR session, XR Origin and AR camera, and the build list
- Player settings, XR Plug-in Management, Android builds

## Get it running (30 minutes)

1. `Scenes/App.unity` → `Installer Assistant` → Mode = Live, Test Label = `IA|C01|D1|A1610.READER1|A4020E`. Start the perception service (see `A-perception.md`, step 1). Play: `ArCameraView` uses the webcam (stage 0), so the whole live flow runs in the Editor.
2. Build to a phone once with Mode = Wizard of Oz, to check the Android setup. First fix the Player settings left over from the AR template: Company/Product name, **Package name** (`applicationIdentifier` is still `com.unity.template.ar_mobile`; use e.g. `se.lth.mamn60.installerassistant`), portrait only, minimum API level for ARCore.
3. On the phone the perception service is the laptop: set `perceptionUrl` to the laptop's address (same Wi-Fi) and allow HTTP (Player → Other Settings → Allow downloads over HTTP).

## What to build, in order (stage 1)

The steps are listed at the top of `ArCameraView.cs`, with the AR Foundation 6.5 calls:

1. **Scene:** `ArRig.Create` creates the AR Session, XR Origin, AR camera background and vertical plane detection at runtime on Android. Keep one active rig; no additional scene rig is needed for this implementation.
2. **Frames:** `ARCameraManager.TryAcquireLatestCpuImage` → asynchronous RGBA32 conversion with `MirrorX` → `CameraFrame` (with acquisition-time intrinsics and pose).
3. **See-through:** return `SeeThrough = true`; the app then keeps the check screen transparent over the camera feed.
4. **Anchors:** image point → screen (display matrix) → raycast → world point; project it every frame in `TryGetPosition`. Keys include `module:<id>` (e.g. `module:A1610.READER2`) for the zone check's hints, which sit on a whole module rather than one pin.
5. **Snapshots:** copy the converted CPU image into a `Texture2D`.
6. Keep the webcam path for the Editor (or use XR Simulation).

Test it against the Wizard of Oz: the same flow must work, and markers must not drift more than a terminal pitch (about 5 mm) when you step back and forth.

## State on 14 Nov 2026 (Yu, covering B)

- Marks: each pin is triangulated from every read of its end (`AR/AnchorFusion.cs`), rays from the CPU image's
  intrinsics, marks held by an ARCore anchor; the app re-reads in the background every 3 s while marks are shown.
  Why and the simulation: `tools/ar-checks/README.md`, `ml/LOG.md` (14 Nov).
- Records: `AR/ArRecords.cs` writes `ar-records/*.jsonl` on the phone; `ml/tools/ar_records.py` measures them.
- Switches on `ArCameraView` (Inspector, `App.unity` → Installer Assistant): `arIntrinsicsRays`, `arFuseReads`,
  `arHoldWithArAnchor`, `arRecords`; on `InstallerApp`: `keepMarksAligned`, `alignEverySeconds`. All default on;
  turning `arFuseReads` and `keepMarksAligned` off gives the 2 Nov behaviour, for an A/B test.

### Phone test that leaves records (about 20 minutes)

1. Laptop: start the service with the frame log on (PowerShell, repo root):
   `$env:IA_SAVE_FRAMES = "data/live/2026-11-14-ar"`, then
   `python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 5055`, and
   `adb reverse tcp:5055 tcp:5055` with the phone on USB.
2. Phone: install the new build. Controller end first: read, then **move the phone slowly 5–10 cm sideways and
   up/down** for ~10 s while the marks are shown (the app reads again every 3 s), then hold still. Look at whether
   the marks stay on the holes. Three-finger tap to save a screenshot whenever they look wrong.
3. Tilt test: the same with the phone at ~30–40° to the block (look at it from the side).
4. Device end: the same.
5. Laptop: `adb pull /sdcard/Android/data/com.unity.template.ar_mobile/files/ar-records data/ar-records`, then
   `python ml/tools/ar_records.py data/ar-records --frames data/live/2026-11-14-ar --csv data/ar-records/reads.csv`.
   Look at `data/ar-records/review/*.jpg` (green ring: where the reader finds the pin now; red dot: where the mark was).

### Laptop test with a phone as the camera (added 14 Nov, evening)

In the Editor there is no AR pose; the webcam view keeps the marks on the pins by following the picture
(`AR/PlaneTracker.cs`) and by the quiet re-reads. Steps:

1. Phone as webcam (iVCam), preview window on the laptop closed.
2. Service (PowerShell, repo root, `conda activate installer-ml`):
   `$env:IA_SAVE_FRAMES = "data/live/<date>-editor"`, optionally `$env:IA_SHARP_SCALE = "0.6"` (iVCam's video is
   softer than the phone's own camera image the blur threshold was set on), then
   `python -m uvicorn ml.perception_service.app:app --host 127.0.0.1 --port 5055`.
3. Unity: `App.unity`, Installer Assistant: Mode = Live, Perception Url = `http://127.0.0.1:5055`; Play, Game view
   in a portrait size.
4. Hold the phone 20-40 cm from the block. After the read, move it slowly: the marks should stay on the holes;
   when tracking is lost the marks are hidden; the next successful position-only read can restore them.
   Recovery depends on the service locating the terminals and is not guaranteed within a fixed time.
   Switches: `ArCameraView.webcamTracking`, `InstallerApp.keepMarksAligned`, `burstFrames`.

For iVCam, enable full-resolution access, select Full HD and 30 fps, and check the PC client's output size.
Unity requests 1920 × 1080 by default; true portrait output is 1080 × 1920 and should use matching request values.
The console logs requested versus received dimensions. Verify that the source fills the output frame without
black sidebars: a portrait image padded into landscape was rejected by the current reader in saved-frame replay.
Unity's portrait Game view alone does not establish the camera frame's actual size or orientation.

## Rules for you

- `App.unity` is yours: others never save it. Others test in their own Sandbox scenes.
- Keep `Blueprint_Demo.unity` out of the AR setup: it is the Wizard-of-Oz fixture.
- A change to `ICameraView` or `CameraFrame` needs all three to approve.
