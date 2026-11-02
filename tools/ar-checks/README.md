# AR image-to-overlay checks (package B)

Run Unity in batch mode, from the repository root (substitute your installed Editor path):

```powershell
& 'E:\uni install\6000.3.25f1\Editor\Unity.exe' -batchmode -nographics `
  -projectPath "$PWD\unity\InstallerAssistant" `
  -executeMethod InstallerAssistant.AR.Editor.ArGeometryChecks.Run `
  -logFile "$PWD\data\ar-overlay-unity-check.log"
```

The checks exercise the production `ArRig.Anchor` / `TryGetPosition` path using real Unity camera matrices and
known slot coordinates: four display orientations, portrait crop, an asymmetric projection, delayed responses
after camera movement, invalid transforms, and the webcam's actual RectTransform / UV mirror. No AR provider
or perception model is substituted for a test of recognition accuracy. Successful execution exits with 0 and
writes `data/ar-overlay-checks/checks.txt` (31 checks).

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
