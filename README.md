# Installer Assistant

AR app (Unity, Android) that helps installers verify the wiring of an AXIS A1610 Network Door Controller and a Reader.
MAMN60 Augmented Reality Interaction, LTH, Lund University – group project with Axis Communications.

## Unity version

**Everyone must use exactly this version:** `6000.3.24f1`

Modules needed: Android Build Support, OpenJDK, Android SDK & NDK Tools.

## Open the project

1. Clone this repository (GitHub Desktop: File → Clone repository).
2. Unity Hub → Add → Add project from disk → select `unity/InstallerAssistant`.
3. The first open takes a few minutes while Unity builds its Library folder.

## Repository layout

| Folder | Contents |
| --- | --- |
| `unity/InstallerAssistant/` | The Unity project |
| `docs/` | Meeting notes, sketches |
| `ml/` | Training code for the detection model |
| `data/` | Not in Git – training photos live on the shared drive: <link> |

## Team and ownership

| Person | Role | Owns |
| --- | --- | --- |
| <name> | <role> | e.g. `Scripts/AR`, `Main.unity` |
| <name> | <role> | e.g. `Scripts/Detection`, `ml/` |
| <name> | <role> | e.g. `Scripts/UI` |

## Rules

1. Same Unity version for everyone.
2. Never commit `Library/`.
3. Always commit `.meta` files with their assets.
4. One person edits a scene at a time; `Main.unity` is edited only by its owner.
5. Work on a branch (`name/topic`), merge into `main` through a pull request.
6. No Git LFS: photos, videos and `.apk` builds go on the shared drive, never in Git.

Full setup guide: <link to the team setup guide>
