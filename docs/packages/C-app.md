# Package C · App, records and study

**Goal:** the app around the check is complete and robust, records survive closing the app, and the user study can be run and logged.

## You own

- `Scripts/App` (`InstallerApp`, `Composition`), `Scripts/Records`, `Scripts/UI`, `Scripts/WizardOfOz`
- `Scenes/Blueprint_Demo.unity` – the Wizard-of-Oz fixture
- `docs/`

## Get it running (15 minutes)

1. `Scenes/Blueprint_Demo.unity` (or `App.unity` in Wizard-of-Oz mode), Game view 1080 × 2340, Play. Operator keys: `docs/blueprint-demo.md`.
2. `dotnet run --project tools/domain-tests` – the domain tests.

## What to build, in order

1. **Records that last:** `JsonRecordStore` works (one JSON file). Next: one file per site, the end snapshots saved as JPEG next to it, and a cable list from the store on Home (today Home lists only this session's cables).
2. **Report export:** the report and the session log (`SessionLog.ToCsv`) as files the installer can share (CSV now, PDF later).
3. **Robust flow:** what happens when the app is closed in the middle of a check, when the same cable is scanned twice, when the phone loses the label. The flow lives in `InstallerApp.Check.cs` (`CheckFlow`, `Arrive`, `ScanLabel`). The zone check (`ZoneSheet`, `ShowZone`) runs before the pins at the controller, then the bundle check (`BundleSheet`, `MarkForeign`) at either end; their words come from `Verification/ZoneCheck.cs` and `BundleCheck.cs`.
4. **Label printing:** labels are the cable's plan, `IA|<cable id>|<door>|<controller module>|<device module>|<CTRL or DEV>` (e.g. `IA|C02|D2|A1610.READER2|A4020E|CTRL`), printed in pairs that differ only in the last field. Print the fields as text under the QR code too ("C02 · D2 · A1610.READER2 · A4020E · CTRL"); the parser reads both. The planner picks the module, never the app: Door 1's reader may go on Reader 2. Fix the label where the cable's wires fan out, so the camera sees label and terminals together (the bundle check follows each wire back to it).
5. **User study:** the Wizard-of-Oz operator (`WizardOfOzRig`), the task script, the log fields for task time, errors shown and installer overrides; SUS and NASA-TLX forms.
6. **Accessibility:** contrast (WCAG 2.2 AA, already met), touch targets ≥ 48 dp, text size.

## Rules for you

- New screens use `UI/UiKit.cs` (colours, type, components); don't add new colours or fonts.
- `Blueprint_Demo.unity` must keep working after every merge.
- The app talks only to the contracts in `App/Services.cs`; never call a Wizard-of-Oz or a live class from the app directly (that belongs in `Composition.cs`).
