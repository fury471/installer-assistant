# Team setup guide

After this guide you have the project's exact Unity version, a clone of the repository, the app running in the Editor, and the app on an Android phone. Everyone in the group develops on Windows, so paths and menu names are for Windows.

The repository already exists and is set up (see [How the repository is set up](#how-the-repository-is-set-up) at the end). Start at Step 1.

**Five golden rules.** Most team problems come from breaking one of these.

1. Everyone uses the exact same Unity version: **6000.3.24f1**.
2. Never commit Unity's `Library` folder. The `.gitignore` handles it.
3. Always commit a `.meta` file together with its asset. Without it, links between files break for everyone else.
4. Each scene has one owner (README → Team and ownership). Never save someone else's scene.
5. `main` must always open and run. Work on your own branch and merge through a reviewed pull request.

| Step | What | Who |
| --- | --- | --- |
| 1 | Install the tools | Everyone |
| 2 | Clone and open the project | Everyone |
| 3 | Check that it works | Everyone |
| 4 | Daily workflow | Everyone |
| 5 | First Android build | Everyone with an Android phone |

## Step 1 – Install the tools

Most of the time goes into waiting for downloads.

1. **Unity Hub** from unity.com/download. Sign in with a free Unity account.
2. **Unity Editor 6000.3.24f1**, exactly this version. In Unity Hub: Installs → Install Editor → Archive / "Install other versions" if it is not listed. Tick these modules:
   - Android Build Support
   - OpenJDK (under Android Build Support)
   - Android SDK & NDK Tools (under Android Build Support)
3. **Git** from git-scm.com. The default installer options are fine.
   - Install Git on Windows itself and keep the project in a normal Windows folder, not inside WSL: Unity works badly with files stored there.
   - Use a short folder path with no spaces and only English letters, e.g. `C:\dev\installer-assistant`. Long or unusual paths can break Android builds.
4. **Tell Git who you are.** Once per computer, with the name and email of your GitHub account:
   ```
   git config --global user.name "Your Name"
   git config --global user.email "you@example.com"
   ```
5. **GitHub Desktop** from desktop.github.com. Optional, but the easiest way to see changes, commit and push.
6. **A code editor.** Visual Studio Community (workload "Game development with Unity") or VS Code with the Unity extension. In Unity, set it under Edit → Preferences → External Tools → External Script Editor.
7. **.NET 8 SDK** from dot.net, for the domain tests (`dotnet run --project tools/domain-tests`).
8. **Python 3.10 or newer** from python.org (tick "Add python.exe to PATH"), for the perception service and its tests. Package A needs it daily; everyone needs it to run the live mode.

Everyone needs a GitHub account. Students can get the free GitHub Student Developer Pack with their university email.

**Check:** in a terminal, `git --version`, `git config user.name`, `dotnet --version` and `python --version` each print something sensible.

## Step 2 – Clone and open the project

1. Accept the GitHub invitation to the repository from your email.
2. **Clone:** GitHub Desktop → File → Clone repository → `installer-assistant` → local path such as `C:\dev\installer-assistant`.
3. **Open:** Unity Hub → Add → Add project from disk → choose `…\installer-assistant\unity\InstallerAssistant` (the folder that contains `Assets`).
   - If Hub says the editor version is missing, install 6000.3.24f1. Never pick another version or upgrade the project.
4. The first open takes several minutes while Unity builds its `Library` folder. This is normal.
5. Check that File → Build Profiles shows **Android** as the active platform. If not, select Android → Switch Platform.
6. **Make your own test scene:** Project window → right-click `Assets/_Project/Scenes/Sandbox` → Create → Scene → name it after yourself (e.g. `Sandbox/Yu.unity`). Commit it on a branch (Step 4).

## Step 3 – Check that it works

1. **The app in the Editor.** Open `Assets/_Project/Scenes/App.unity`. In the Game view, choose a portrait resolution of 1080 × 2340 (add it with "+" if needed). Press Play and click the Game view once. Home shows "Scan a cable"; press it and follow the check at the reader end. The operator keys (`R`, `1`, `2`, `S`, `3`, `4`, `K`, `H`) are in `docs/blueprint-demo.md`.
2. **The fixture.** Open `Assets/_Project/Scenes/Blueprint_Demo.unity` and press Play: the same app in Wizard-of-Oz mode.
3. **The domain tests**, from the repository root:
   ```
   dotnet run --project tools/domain-tests
   ```
   The last line must be `ALL PASSED`.
4. **The perception tests**, from the repository root:
   ```
   python -m pip install -r ml/requirements.txt
   python -m pytest ml/tests
   ```
5. **Live mode (optional, needs a webcam).** Start the service with `python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 8000`. In `App.unity`, select `Installer Assistant`, set Mode = Live and Test Label = `IA|D1|READER|C01`, and press Play. Don't save the scene afterwards unless it is yours (B owns `App.unity`).

Then read your package page in `docs/packages/`.

## Step 4 – Daily workflow

Work on your own branch, keep commits small, and merge into `main` only through a reviewed pull request.

**Every work session**

1. **Get the latest version.** GitHub Desktop → switch to `main` → Fetch origin → Pull origin. Do this with Unity closed, or Unity may need to re-import.
2. **Make a branch** for your task: Current branch → New branch, named `yourname/topic`, e.g. `fury/qr-label-scan`.
3. **Work in Unity.** Save often: Ctrl+S (scene) and File → Save Project (other assets). Unity writes changes to disk only when you save, so Git cannot see unsaved work.
4. **Commit small pieces** with a clear message, e.g. `Add QR decoding to QrLabelScanner`. Before committing, check the change list:
   - every new asset has its `.meta` file next to it;
   - no `Library`, `Temp` or `.csproj` files appear;
   - no scene you do not own has changed by accident (opening a scene and pressing Play can mark it changed: discard that change).
5. **Push your branch**, then on github.com click Compare & pull request. Write two lines: what you changed and how to test it.
6. **Before you ask for review:** the project opens with 0 errors, the domain tests and `python -m pytest ml/tests` pass, and `Blueprint_Demo.unity` still runs.
7. **One teammate reviews** (opens the branch in Unity if needed) and approves. A change to a contract (README → Team and ownership, shared row) needs all three. Then Merge and delete the branch.
8. Everyone pulls `main` again.

**Avoiding scene conflicts**

Unity scenes (`.unity`) and prefabs (`.prefab`) are long text files that Git cannot merge safely. Two people changing the same scene almost always ends with one person's work lost.

- Each scene has one owner: `App.unity` – B, `Blueprint_Demo.unity` – C, `Sandbox/Perception_Test.unity` – A, `Sandbox/<name>.unity` – that person.
- Build your feature in your Sandbox scene. If it must go into `App.unity`, make it a prefab and ask B to place it.
- Before editing a shared prefab or a `Config/` asset, say so in the team chat.
- Keep logic in scripts and settings in ScriptableObject assets (`Config/`); they are much easier to merge than scenes. Most of the app builds its UI in code for this reason.
- If a scene conflict happens anyway, do not merge it by hand. Keep one version (GitHub Desktop offers "use mine" or "use theirs") and redo the smaller change.

**Never commit secrets.** API keys for an online AI service go in a file like `secrets.json` or `.env`; the `.gitignore` excludes both. A pushed key stays in the history even after you delete the file: if it happens, cancel that key at once and make a new one.

**Photos, videos and builds** go on the shared drive (https://drive.google.com/drive/folders/1NTnWubjdTvkZanh5BrwsZjqz3m9TzLMN), never in Git. We do not use Git LFS, so every committed image stays in the history forever.

## Step 5 – First Android build

**Prepare the phone (once)**

1. Check that the phone supports ARCore on Google's list of supported devices (needed for the live AR mode later).
2. Install or update Google Play Services for AR from the Play Store.
3. Turn on developer mode: Settings → About phone → tap Build number 7 times.
4. In Developer options, turn on USB debugging. On Xiaomi phones also turn on Install via USB.
5. Connect the phone with a USB cable that carries data (some cables only charge). Tap Allow when the phone asks to trust the computer.

**Build and run**

1. File → Build Profiles → Android. Under Run Device, click Refresh and pick your phone.
2. Check the scene list: `Assets/_Project/Scenes/App.unity` is first and ticked.
3. Click Build And Run. Save the `.apk` in a `Builds` folder inside the Unity project; Git ignores it.
4. The app starts in Wizard-of-Oz mode: Home with "Scan a cable", then the check on the bench photos. Tap through it once.
5. For live mode on the phone: the perception service runs on a laptop on the same Wi-Fi. Set `perceptionUrl` to the laptop's address (e.g. `http://192.168.1.20:8000`), not `localhost`, and allow plain HTTP (Player → Other Settings → Allow downloads over HTTP).

Share test builds by uploading the `.apk` to the shared drive, not to Git.

## Common problems and fixes

| Problem | Likely cause | Fix |
| --- | --- | --- |
| `Library/` files appear in the change list | `.gitignore` is not in the repository root | Move it to the root. If `Library` was already committed: `git rm -r --cached unity/InstallerAssistant/Library`, then commit |
| "Missing script" or pink objects after pulling | Someone forgot to commit `.meta` files | Ask them to commit the missing `.meta` files; never create new ones yourself |
| "The referenced script on this Behaviour is missing" in a scene | A script was renamed or moved without its `.meta` | Same fix: the original `.meta` file (its GUID) must move with the script |
| Console says "there is no WizardOfOzRig" and the app opens the webcam | Mode is Wizard of Oz but the rig component is missing | Add `WizardOfOzRig` to the same GameObject as `InstallerApp`, or set Mode = Live on purpose |
| Live mode: "No wires found" every time | The perception service is not running or not reachable | Start it; on a phone use the laptop's IP address and allow HTTP |
| Cloning and pulling get slow | Photos, videos or builds were committed | Keep them on the shared drive; commit a trained model only when it changes |
| Unity Hub says the editor version is missing | Different Unity version | Install 6000.3.24f1; never upgrade the project alone |
| Phone does not appear under Run Device | USB debugging off, charge-only cable, or phone not trusted | Check Developer options, try another cable, accept the prompt on the phone |
| App starts but the camera is black (live mode) | Camera permission refused or Google Play Services for AR missing | Allow camera in the phone's app settings; install Play Services for AR |
| Build fails with Gradle or SDK errors | Android modules missing or unusual project path | Re-add the Android modules in Unity Hub; use a short path with no spaces |
| Merge conflict in a `.unity` or `.prefab` file | Two people edited the same scene or prefab | Keep one version, redo the smaller change, follow the scene-owner rule |
| Push rejected because a file is too large | GitHub blocks files over 100 MB | Don't commit it; move it to the shared drive. If it is already in a commit, ask the repo owner to remove it before pushing |

## How the repository is set up

For reference; this is done and does not need repeating.

- **GitHub:** public repository `installer-assistant`, teammates added as collaborators, `main` protected (pull request with 1 approval required). Because the repository is public, nothing Axis gave us as internal goes into it.
- **Unity project:** created from the AR Mobile template in `unity/InstallerAssistant`, with AR Foundation 6.5 and Google ARCore XR Plugin, Android as the active platform and ARCore ticked under XR Plug-in Management (Project Validation → Fix All).
- **Version-control settings** (Edit → Project Settings → Editor): Version Control Mode = Visible Meta Files, Asset Serialization Mode = Force Text, so scenes are readable text that Git can diff.
- **Player settings:** Product Name = Installer Assistant. Still to do (B): Company Name, the package name (still the template's `com.unity.template.ar_mobile`), portrait only.

**Repository layout**

```
installer-assistant/                 the Git repository
├── .gitignore, .gitattributes
├── README.md                        Unity version, how to open, who owns what, rules
├── docs/                            architecture, package pages, this guide, Wizard-of-Oz guide
├── ml/                              perception service, evaluation, tests (package A)
├── tools/domain-tests/              unit tests of the wiring rules and verdicts
├── data/                            NOT in Git: only a README with the shared-drive link
└── unity/InstallerAssistant/        open THIS folder in Unity Hub
    ├── Assets/
    │   ├── _Project/                everything we make
    │   │   ├── Scenes/              App.unity (product), Blueprint_Demo.unity (fixture)
    │   │   │   └── Sandbox/         one test scene per person, Perception_Test.unity
    │   │   ├── Scripts/
    │   │   │   ├── App/             the app: screens, check flow, which services run (C)
    │   │   │   ├── AR/              camera view, frames, anchors (B)
    │   │   │   ├── Labels/          cable label format, QR scanner (shared / A)
    │   │   │   ├── Detection/       camera frame, wire reader, perception client (A)
    │   │   │   ├── AI/              second opinion (A)
    │   │   │   ├── Records/         record stores, session log (C)
    │   │   │   ├── Verification/    A1610 wiring rules, rule check, verdicts (shared)
    │   │   │   ├── UI/              design system, AR markers and tags (C)
    │   │   │   └── WizardOfOz/      photos, scripted services, operator keys (C)
    │   │   ├── Config/              DemoLayout assets (photos, positions, planted faults)
    │   │   ├── Fonts/               Inter
    │   │   └── Textures/            bench photos for the Wizard of Oz
    │   └── (template folders: XR, XRI, Samples, MobileARTemplateAssets, …)
    ├── Packages/                    in Git: the list of Unity packages
    └── ProjectSettings/             in Git
```

Why it is set up this way:

- `_Project` starts with an underscore so it sorts to the top and keeps our files apart from imported plugins.
- The scripts are split along the app's contracts (camera → label → wires → check → records → screens), so each person owns folders and conflicts stay rare. `docs/architecture.md` explains how they connect.
- `Library/`, `Temp/`, `Logs/` and `UserSettings/` are rebuilt by Unity on every computer and are never committed.
- Git does not save empty folders, so a new folder appears for others only once it contains a file.
