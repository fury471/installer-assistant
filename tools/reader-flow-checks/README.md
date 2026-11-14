# Reader retry recovery checks

Run Unity with graphics enabled so the actual Unity UI can be rendered:

```powershell
& 'E:\uni install\6000.3.25f1\Editor\Unity.exe' -batchmode `
  -projectPath "$PWD\unity\InstallerAssistant" `
  -executeMethod InstallerAssistant.App.Editor.ReaderFlowCheckBatch.Run `
  -logFile "$PWD\data\reader-flow-unity.log"
```

The test opens a temporary, unsaved scene and runs the real `InstallerApp` coroutines in Play Mode. Controlled
camera responses exercise the app's recovery behaviour; they are not tests of recognition accuracy. The harness
is entirely under `#if UNITY_EDITOR` and is absent from the Android player. Product and WoZ scenes are not saved.

Coverage:

- Four uncertain reader verdicts retain a visible, clickable retry action, with an accurate title.
- A failed retry offers another retry, cannot save an empty failed response, and drops old slot anchors.
- The next successful retry saves the fresh colours. Plain retry never calls the WoZ operator's rewire action.
- Correct verdicts keep Finish; incorrect wiring keeps the explicit physical-fix action.
- An uncertain controller pin outside the four reader terminals sends the installer to that pin, rather than
  leaving an amber result with no corresponding reader row to tap.
- A partial reader response keeps uncertain 'no wire' rows editable and blocks saving until they are confirmed.
- Several empty slots are not misreported as duplicate wire colours.

Evidence is written to the ignored `data/reader-flow-checks/`: `checks.txt`, `reader-amber.png`, and
`reader-failed-retry.png`. The PNGs show the actual Unity UI in the controlled fixture, not a physical camera test.
Temporarily restoring the missing verdict retry button was caught by the assertion
`all-amber verdict keeps a retry button` (`data/reader-flow-mutation.log`). The fixed source was restored afterward.

The recognition model and perception code are not changed by this fix. The saved-frame comparison in
`reader-current-code-replay.json` shows that the current service code reproduces the last live reader result;
it does not establish that the result was correct.

Validated on 2 Nov 2026: 30 Play Mode assertions passed, 39 existing Python tests passed, and the C# domain
suite reported `ALL PASSED`. The missing-button mutation failed as expected. Android development build succeeded
with the locally installed Unity 6000.3.25f1. The new APK is
`unity/InstallerAssistant/Builds/InstallerAssistant_ReaderRetry_20261102.apk`; physical phone retesting remains pending.

To rebuild it, use the same Unity command with `-buildTarget Android -nographics` and
`-executeMethod InstallerAssistant.App.Editor.ReaderFlowCheckBatch.BuildAndroid`.

## Current version (14 Nov 2026)

- Scanning a completed cable starts at end 1 with no old floating reference. `IRecordStore.Forget(key)`
  removes only that cable's old pairing readings; existing in-session reports retain their evidence.
  Unfinished checks resume the saved first end, including after reopening the JSON store.
- Camera CPU-image conversion and perception JPEG compression are asynchronous. Read and comparison share
  the JPEG for the same immutable frame. Camera metadata belongs to acquisition time, not conversion completion.
  Conversions time out after two seconds; cached camera images older than one second are not returned as fresh.
- The app still performs position-only reads while awaiting installer input. Recovery can run even if the
  initial scan placed no markers. It retries sooner when markers are missing, rejects stale responses and
  stops the quiet-read coroutine when the end changes, a modal opens or the request times out.
- Webcam marks use optical flow. A tracking loss hides them until a successful position-only read restores
  them. This is not true world tracking or guaranteed recovery within a fixed number of seconds.
- Unity requests Full HD by default and logs requested versus actual webcam dimensions. iVCam's own output
  must be configured accordingly; the Unity Game view does not establish camera resolution. Black-padded
  portrait captures can still defeat terminal localization. This revision does not crop that padding.

The HTTP/JSON payload is unchanged. The only shared interface addition is the documented per-cable record reset.
The source fixes do not update an already-installed Android app; a new build is required.

### Validation

Final pre-commit validation: 45 assertions passed when the same UI fixture was run in a temporary standalone
Windows player built with Unity 6000.3.24f1, using controlled sensor services and `-nographics`. This includes
fresh checks, failed retries, automatic position recovery after an empty placement, unchanged colours during
recovery, and worker-JPEG decoding/orientation/reuse. The standalone run caught malformed UTF-8 punctuation
in button labels; the labels were corrected before the passing run.

The editor's Play Mode startup remained blocked, so this is a player-runtime result, not a new editor Play
Mode result. The earlier 42 Play Mode assertions are historical. Also passed: 75 AR geometry/tracker checks,
the domain suite and 51 Python tests (one dependency deprecation warning). See `ml/LOG.md` for evidence paths.
These checks do not establish visual layout, camera frame rate, real ARCore tracking or recognition accuracy.
Manual webcam, Android and Blueprint_Demo checks remain separate.

Direct image checks can run without Play Mode using
`-executeMethod InstallerAssistant.App.Editor.ReaderFlowCheckBatch.RunImageChecks -nographics`.
They verify JPEG decoding, dimensions, orientation and reuse between read/comparison. The full flow suite
uses `InstallerAssistant.App.Editor.ReaderFlowCheckBatch.Run`.
