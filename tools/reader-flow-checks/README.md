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
