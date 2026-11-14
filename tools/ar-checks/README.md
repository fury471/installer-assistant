# AR image-to-overlay checks (package B)

Run Unity in batch mode, from the repository root (substitute your installed Editor path):

```powershell
& 'D:\Program\Unity\Hub\Editor\6000.3.24f1\Editor\Unity.exe' -batchmode -nographics `
  -projectPath "$PWD\unity\InstallerAssistant" `
  -executeMethod InstallerAssistant.AR.Editor.ArGeometryChecks.Run `
  -logFile "$PWD\data\ar-overlay-unity-check.log"
```

The checks exercise the production `ArRig.Anchor` / `TryGetPosition` path using real Unity camera matrices and
known slot coordinates: four display orientations, portrait crop, an asymmetric projection, delayed responses
after camera movement, invalid transforms, and the webcam's actual RectTransform / UV mirror. No AR provider
or perception model is substituted for a test of recognition accuracy. Successful execution exits with 0 and
writes `data/ar-overlay-checks/checks.txt` (every check by name) and `fusion-simulation.txt` (below).

Added 14 Nov (close Unity first; the batch run needs the project unlocked):

- **Intrinsics rays.** A portrait screen showing a landscape CPU image through the quarter-turn display transform,
  with the projection built from the image's intrinsics. Same field of view: the intrinsics ray must land exactly
  where the display ray does (the image point comes from the shader's forward transform, so this also checks the
  quarter-turn signs). CPU image covering 80 % of the texture's view: only the intrinsics ray lands on the point,
  and the display-only ray is shown to miss.
- **Fusion.** A row of 8 pins tilted 35° at 35 cm, read from two places 5 cm apart: from a third place 10 cm away
  the marks are within ¼ pitch; with `arFuseReads` off (each read replaces the marks) they are more than a pitch
  off. A read 3 pitches across the row, from a place whose epipolar lines run along the row, is refused.
- **Simulation table.** Monte Carlo of the production `AnchorFusion` (reader noise 0.08 pitch): mark offset after
  a 10 cm move for one read against two reads 2 and 5 cm apart, at 0°, 20° and 35° tilt.

If the two local captures from `data/live/2026-11-02-b` are present, it also writes `reader-replay.png` and
`controller-replay.png`. These are **replays**, not screenshots of a new phone test. They retain the recorded
positions and colours, undo the old captures' storage rotation, sample a portrait background using the shader's
forward transform, and place the dots using the production inverse. They demonstrate placement, not recognition
accuracy or tracking stability. Photos and outputs remain under the ignored `data/` folder.

Build the actual product scene with `-executeMethod InstallerAssistant.AR.Editor.ArGeometryChecks.BuildAndroid`
and `-buildTarget Android`. This writes `unity/InstallerAssistant/Builds/InstallerAssistant_ARAlignment_20261102.apk`.
No scene or shared contract is changed by these checks. Keep the team's required Unity version for shared commits;
the path above is the Editor currently available on Xiaoxin's machine.

## Why the mapping changed (2 Nov 2026)

`XRCpuImage.Transformation.MirrorX` reverses rows, which converts the native CPU image to Unity's bottom-up RGBA
storage. `MirrorY` reverses columns and is not a row conversion. After decoding that old buffer as bottom-up,
the JPEG was rotated 180 degrees. Existing captures retain their original pixels; new captures use MirrorX.
See [Unity's transformation definitions](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.5/api/UnityEngine.XR.ARSubsystems.XRCpuImage.Transformation.html).

The background shader samples `mul(float4(viewport.x, viewport.y, 1, 0), _UnityDisplayTransform).xy`.
That display transform already includes the texture y flip, display rotation and crop; invert its 2D affine
part, then unproject with the frame's saved projection and pose. Applying sensor intrinsics directly to the
portrait camera pose skips the rotation/crop. No orientation guessing or three-finger calibration is needed.
The SDK reference implementation is the installed `com.unity.xr.arcore/Assets/Shaders/ARCoreBackground.shader`.

## Validation on 2 Nov 2026

- Unity geometry checks: 31 passed. Deliberately skipping the display rotation failed on the portrait reader
  fixture (`data/ar-overlay-mutation.log`); the fixed source was restored and all checks passed again.
- Existing Python suite: 39 passed (one dependency deprecation warning).
- Existing C# domain suite: ALL PASSED (`DOTNET_ROLL_FORWARD=Major` for this machine).
- Android development APK: built successfully with the locally installed Unity 6000.3.25f1; see
  `data/ar-overlay-android-build.log`. APK stays in the ignored `Builds/` directory.
- Replayed both saved images after the storage-orientation correction: all 4 reader slots and all 8 controller
  slots were located, and colour answers stayed identical to the old orientation. Mean corresponding-position
  differences were 0.00289 and 0.000412 of the normalised image, respectively (two frames, not an accuracy study).
- Physical phone follow-up: check both ends at the slots, then translate the phone and read again. A replay cannot
  establish this. Depth still assumes the terminal row is approximately parallel to the camera; steep views need
  separate testing. Existing colour-reader mistakes are not repaired by changing the overlay transform.

## Why each pin is triangulated (14 Nov 2026)

One read gives the block one depth, from the pin spacing in the image. That is exact only when the block faces
the camera: the spacing shrinks with the cosine of the tilt, so at 35° the depth is 22 % long and the marks are
~3.7 pitches off after a 10 cm move (simulation above). So every read is a sighting per pin, and each pin is the
least-squares point of all its rays, with the spacing depth only as a weak prior (`AR/AnchorFusion.cs`; Hartley
and Sturm, 1997). Two reads 5 cm apart put 99 % of the marks within half a pitch from 10 cm away. The app makes
those reads itself: while the marks are shown it reads the same end every 3 s and uses only the positions
(`InstallerApp.KeepMarksAligned`). A read that does not meet the earlier rays is not fused.

The ray itself now comes from the CPU image's intrinsics, with only the quarter-turn taken from the display
transform: the display transform describes the GPU texture, and ARCore may give the CPU image a different field
of view. The display-only ray is still computed and the difference is in the AR records (`rayMethodPx`).

## AR records from the phone

`ArCameraView.arRecords` (on by default) writes `ar-records/<start time>.jsonl` under the app's persistent data
folder: one line per read, a 1 Hz sample of where the marks were drawn, and screenshots (three-finger tap).

```powershell
adb pull /sdcard/Android/data/com.unity.template.ar_mobile/files/ar-records data/ar-records
python ml/tools/ar_records.py data/ar-records --frames data/live/<the IA_SAVE_FRAMES folder> --csv data/ar-records/reads.csv
```

## The webcam view's tracker (14 Nov, evening)

`ArGeometryChecks.Run` now also runs `PlaneTrackerChecks` (`AR/Editor/PlaneTrackerChecks.cs`, no Unity types): a
picture of random blobs moved by a known shift, turn and zoom for 40 steps must be followed to within 0.6 px
(0.11 px outside Unity), a different picture and a flat picture must be reported as lost. The tracker keeps the
marks on the pins in the Editor / webcam view, where there is no AR pose; on the phone ARCore's pose does that.

## Final pre-commit validation (14 Nov 2026)

75 assertions passed with Unity 6000.3.24f1 in an isolated project (`data/commit-geometry.log`). Coverage includes
captured-pose retention during asynchronous image delivery, rejection of stale cached images, looking away
and returning, and preserving an existing anchor when a new camera transform is invalid. The camera interfaces
and HTTP schema are unchanged. This does not validate a physical ARCore provider or real-device frame rate.
The current implementation retains automatic position-only rereads; webcam optical-flow loss can still hide
marks until the next successful reading. A new Android build and physical-camera follow-up remain pending.
