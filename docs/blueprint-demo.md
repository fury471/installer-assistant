# MVP blueprint demo

The Wizard-of-Oz demo is the blueprint for the app. Its flow, screens, wiring rules, rule check, fix messages, verdicts, records and log are the app's real code. Only the three sensing services are scripted. Building the app means replacing those three services, nothing else.

Design rationale for the report: see the "Installer Assistant – MVP Blueprint" doc.

## Run it

1. Open `Assets/_Project/Scenes/Blueprint_Demo.unity`. It uses `Config/DemoLayout2` (real bench photos).
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
5. **Fix.** Fixes are always shown on the controller block, with arrows between pins, because the A1610 defines what each pin does and a wrong-pin fault can only be fixed there. If the check ran at the reader and found a fault, the app sends the installer back to the controller ("Go back to the controller") and shows the fix there.
6. **Report** when every wire matches.

Settings → Demo: **Participant starts at** chooses which photo the first "Scan a cable" shows (reader end by default). The app logic itself does not depend on it.

In the session, "Rewired · check again" stands for the installer fixing the bench. The demo then shows the photo of the correctly wired board. For the live demo, re-wire the real bench at that moment.

`Textures/Demo2/controller_wrongpin_edited.jpg` is `controller_fixed.jpg` with the yellow wire digitally moved to pin 6. Replace it with a real photo of that fault, then press `4`, `2` and `K` to place the pins.

## Code map

```
Scripts/
  Verification/   domain, no Unity: AxisCatalog (A1610 + A4020-E facts), CableRecord (reader end),
                  ControllerRecord (controller end), RuleChecker (fixes),
                  ControllerCheck (verdict table)                                ← real, keep
  Labels/         CableLabel (cable key + which end) + ILabelScanner             ← real parser; scanner to build
  Detection/      IWireReader                                                    ← to build (layer 1)
  AI/             ISecondOpinion                                                 ← to build (layer 3)
  Records/        IRecordStore (both ends, keyed by cable), SessionLog (CSV)     ← real
  UI/             UiKit (design system: colours, type, cards, buttons, icons),
                  ArOverlay (markers, tag layout, fix arrows)                    ← real
  WizardOfOz/     WozServices (scripted ILabelScanner / IWireReader / ISecondOpinion),
                  DemoLayout (photos, positions, faults),
                  InstallerAssistantDemo.cs (state, navigation), .Pages.cs (Home, Reports,
                  Settings, report), .Check.cs (either end first, walk, pair, verdict, fix)
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

The fonts are created at runtime from the TTFs assigned on the `InstallerAssistantDemo` component. If they are left empty, the TextMesh Pro default font is used.

## Replacing the Wizard of Oz

| Service | Wizard-of-Oz version | Real version |
| --- | --- | --- |
| `ILabelScanner` | `WozLabelScanner` returns the layout's label, with the end of the photo shown | QR decoding (e.g. ZXing.Net) on the AR camera CPU image; labels printed in pairs (`…\|DEV`, `…\|CTRL`). A label without an end code would make the app ask "Which end is this?" (not built in the demo) |
| `IWireReader` | `WozWireReader` returns the layout's colours | AR image target of the board → terminal crop → VLM, JSON answer |
| `ISecondOpinion` | `WozSecondOpinion` compares and disagrees once | Separate VLM call with both crops and the pairing rule |
| Terminal positions | `DemoLayout` points on the photo | Same points relative to the tracked board (image target) |

In `InstallerAssistantDemo.Restart`, construct the real classes instead of the `Woz…` ones. The flow does not change.

## Tests

`tools/domain-tests` compiles the domain sources directly and checks both planted faults, power reversal, a missing wire, a foreign wire, a three-wire rotation, the verdict table, label parsing (including which end and pairing), the controller-first check and the CSV log.

```
dotnet run --project tools/domain-tests
```
