# Package B · AR runtime and device

**Goal:** the live camera view: real AR frames for the readers, and markers that stay on the terminals while the phone moves. The app runs on an Android phone from `Scenes/App.unity`.

## You own

- `Scripts/AR/ArCameraView.cs` (the contract `ICameraView.cs` is shared)
- `Scenes/App.unity`: the AR session, XR Origin and AR camera, and the build list
- Player settings, XR Plug-in Management, Android builds

## Get it running (30 minutes)

1. `Scenes/App.unity` → `Installer Assistant` → Mode = Live, Test Label = `IA|D1|READER|C01`. Start the perception service (see `A-perception.md`, step 1). Play: `ArCameraView` uses the webcam (stage 0), so the whole live flow runs in the Editor.
2. Build to a phone once with Mode = Wizard of Oz, to check the Android setup. First fix the Player settings left over from the AR template: Company/Product name, **Package name** (`applicationIdentifier` is still `com.unity.template.ar_mobile`; use e.g. `se.lth.mamn60.installerassistant`), portrait only, minimum API level for ARCore.
3. On the phone the perception service is the laptop: set `perceptionUrl` to the laptop's address (same Wi-Fi) and allow HTTP (Player → Other Settings → Allow downloads over HTTP).

## What to build, in order (stage 1)

The steps are listed at the top of `ArCameraView.cs`, with the AR Foundation 6.5 calls:

1. **Scene:** add AR Session and XR Origin (Mobile AR) with the AR camera to `App.unity`; the camera gets `ARCameraBackground`. Enable vertical plane detection (the controller board is on a wall).
2. **Frames:** `ARCameraManager.TryAcquireLatestCpuImage` → RGBA32 with `MirrorY` → `CameraFrame` (with intrinsics and pose).
3. **See-through:** return `SeeThrough = true`; the app then keeps the check screen transparent over the camera feed.
4. **Anchors:** image point → screen (display matrix) → raycast → world point; project it every frame in `TryGetPosition`.
5. **Snapshots:** copy the converted CPU image into a `Texture2D`.
6. Keep the webcam path for the Editor (or use XR Simulation).

Test it against the Wizard of Oz: the same flow must work, and markers must not drift more than a terminal pitch (about 5 mm) when you step back and forth.

## Rules for you

- `App.unity` is yours: others never save it. Others test in their own Sandbox scenes.
- Keep `Blueprint_Demo.unity` out of the AR setup: it is the Wizard-of-Oz fixture.
- A change to `ICameraView` or `CameraFrame` needs all three to approve.
