using System;
using System.Collections;
using System.Collections.Generic;
using InstallerAssistant.AI;
using InstallerAssistant.AR;
using InstallerAssistant.Detection;
using InstallerAssistant.Labels;
using InstallerAssistant.Records;
using InstallerAssistant.UI;
using InstallerAssistant.Verification;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static InstallerAssistant.UI.UiKit;

namespace InstallerAssistant.App
{
    /// <summary>
    /// The check itself, on the camera view. The installer starts at whichever end of the cable is in front of
    /// them. The label tells the app which cable it is and which end; the app records that end, sends the installer
    /// to the other end, pairs the two scans by the label, and shows the verdict where the installer stands.
    /// Fixes are shown where the installer stands when that end can do them (a swap among the cable's own wires);
    /// a fix that needs the controller (a wire on an unused pin, an extra or a loose wire) sends them there.
    ///
    /// At the controller, the zone check runs before the pins: the label names the module the cable must land on
    /// (e.g. Reader 2), and when that module is empty but another holds wires, the app says "move the cable to
    /// Reader 2" instead of listing missing wires (ZoneCheck). Then, at either end, the bundle check: a wire the
    /// reader traced to another cable (not back to the scanned label) is shown before any pin is judged (BundleCheck).
    ///
    /// The camera image comes from ICameraView (a photo in the Wizard of Oz, the AR camera live). Markers are
    /// placed where the readers found the terminals: the app anchors each position in the view and asks the view
    /// every frame where it is now, so markers stay on the terminals while the phone moves.
    /// </summary>
    public partial class InstallerApp
    {
        /// <summary>How long the app keeps looking for a label before it asks the installer for help.</summary>
        const float LabelSearchSeconds = 6f;   // time to bring the phone close and let the camera focus (labels read at 20-30 cm)

        // ------------------------------------------------------------------ view state of the check

        bool atController, checkAborted, needsFixAtController, modalOpen, snapshotExpanded, labelBoxOn;
        CableEnd marksEnd;
        readonly List<PinMarker> marks = new List<PinMarker>();
        readonly List<FixArrow> arrows = new List<FixArrow>();
        readonly Dictionary<int, RectTransform> snapshotDots = new Dictionary<int, RectTransform>();
        readonly List<RectTransform> pulseRings = new List<RectTransform>();
        int tagSide;
        PinMarker tapped;
        Vector2 labelExtent;

        // zone check: is the cable in the module its label names? bundle check: do the wires belong to the label?
        ZoneResult zone;
        BundleResult bundle;
        bool zoneDismissed, zoneShown, pendingFirstCheck, bundleDismissed;
        RectTransform zoneFrom, zoneTo;
        Coroutine aiProgress;

        RectTransform camRoot, markersRT, scanLine, qrBox, missingGuide;
        TextMeshProUGUI titleText, stepLine, instructionText, sheetTitle, sheetSub, qrCaption;
        Image instructionDot;
        CanvasGroup instructionGroup, sheetGroup, snapshotGroup, flashGroup;
        RectTransform sheet, sheetContent, sheetButtons, snapshot, snapshotHighlight;

        void ClearCheckViews()
        {
            foreach (var a in arrows) a.Destroy();
            arrows.Clear(); marks.Clear(); snapshotDots.Clear(); pulseRings.Clear();
            if (camRoot != null && view != null) view.Detach();
            camRoot = markersRT = scanLine = qrBox = missingGuide = null;
            zoneFrom = zoneTo = null; zoneShown = false;
            sheet = sheetContent = sheetButtons = snapshot = snapshotHighlight = null;
            qrCaption = null;
            modalOpen = false; snapshotExpanded = false; labelBoxOn = false;
            if (canvasBackground != null) canvasBackground.enabled = true;
        }

        void ClearCheckState()
        {
            cable = null; current = null; record = null; ctrlRecord = null;
            check = null; results = null;
            readColours = null; readConfidence = null; readEdited = null;
            zone = null; zoneDismissed = false; zoneShown = false; pendingFirstCheck = false;
            bundle = null; bundleDismissed = false;
        }

        // ------------------------------------------------------------------ words the app derives from the label

        static CableEnd Other(CableEnd e) { return e == CableEnd.Controller ? CableEnd.Device : CableEnd.Controller; }
        string DeviceWord { get { return cable != null ? rule.Usage.ToLowerInvariant() : "device"; } }
        string EndWord(CableEnd e) { return e == CableEnd.Controller ? "controller" : DeviceWord; }
        string EndTitle(CableEnd e) { return e == CableEnd.Unknown ? "Scan a cable" : EndTitleOf(cable != null ? rule : null, e); }
        static string EndTitleOf(UsageRule r, CableEnd e)
        {
            if (e == CableEnd.Controller) return "Controller end";
            string w = r != null ? r.Usage.ToLowerInvariant() : "device";
            return char.ToUpperInvariant(w[0]) + w.Substring(1) + " end";
        }
        string CableTitle { get { return cable != null ? TitleOf(cable, rule) : "this cable"; } }
        /// <summary>Where the given end is, from the label's plan and the wiring rule: the installer never has to look it up.</summary>
        string WhereIs(CableEnd e)
        {
            return e == CableEnd.Controller ? Short(rule.ControllerModel) + " · " + rule.Connector.Name + " block"
                                            : (cable.HasDoor ? cable.DoorName + " · " : "") + Short(rule.DeviceModel) + " " + DeviceWord;
        }
        /// <summary>The end the installer most likely is at when they press "Scan a cable" (the label decides).</summary>
        CableEnd NextEnd()
        {
            var pending = Pending();
            if (pending.Count > 0) return Other(pending[0].FirstEnd);
            return op != null ? op.StartEnd : CableEnd.Unknown;
        }

        // ================================================================== start, flow, exit

        /// <param name="expected">The end the installer is probably at; Unknown is fine, the label decides.</param>
        void StartCheck(CableEnd expected)
        {
            StopAllCoroutines();
            toastCo = null;
            if (toastGroup != null) toastGroup.alpha = 0;
            ClearCheckState();
            log = new SessionLog(() => Time.unscaledTime - startTime, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            fixesOnFirstCheck = 0; confirmedByYou = 0; firstFixText = null; needsFixAtController = false;
            checkAborted = false; clicked = null; tapped = null;
            if (op != null) op.BeginCheck();

            BuildCheckScreen();
            startTime = Time.unscaledTime;
            log.Add("start", "mode=" + (op != null ? "wizard_of_oz" : "live") + "; expected=" + CableLabel.EndCode(expected) +
                "; second_opinion=" + useSecondOpinion + (op != null && op.FaultNames != null && op.Fault < op.FaultNames.Length ? "; fault=" + op.FaultNames[op.Fault] : ""));
            StartCoroutine(CheckFlow(expected));
        }

        IEnumerator CheckFlow(CableEnd expected)
        {
            CableEnd here = CableEnd.Unknown;
            yield return Arrive(expected, null, "Scan the label", "The label tells the app which cable this is and which end you are at.", e => here = e);
            if (checkAborted) yield break;

            bool otherEndSaved = here == CableEnd.Controller ? record != null : ctrlRecord != null;
            if (!otherEndSaved)
            {
                // first end: record what is there, then send the installer to the other end
                SetTitle(EndTitle(here), CableTitle + " · end 1 of 2");
                yield return RecordEnd(here);
                if (checkAborted) yield break;
                yield return WalkScreen(Other(here), false);
                if (checkAborted) yield break;
                var first = cable;
                yield return Arrive(Other(here), first, "End 2 of 2", "The label pairs this end with the " + EndWord(here) + " end you saved.", e => here = e);
                if (checkAborted) yield break;
            }

            // second end: the label has paired it; check, fix
            SetTitle(EndTitle(here), CableTitle + " · end 2 of 2");
            if (here == CableEnd.Controller) yield return ControllerSecond();
            else
            {
                yield return ReaderSecond();
                if (checkAborted) yield break;
                if (needsFixAtController)
                {
                    yield return WalkScreen(CableEnd.Controller, true);
                    if (checkAborted) yield break;
                    yield return Arrive(CableEnd.Controller, cable, "Fix", "Scan the label to show the fix on this block.", e => here = e);
                    if (checkAborted) yield break;
                    SetTitle(EndTitle(CableEnd.Controller), CableTitle + " · fix");
                    yield return ControllerFix();
                }
            }
            if (checkAborted) yield break;

            float secs = Time.unscaledTime - startTime;
            current.State = CableState.Verified;
            var rep = new Report
            {
                Cable = current, Record = record, At = DateTime.Now, Fixes = fixesOnFirstCheck, CheckedByYou = confirmedByYou,
                Wires = rule.DeviceTerminals.Length, Seconds = secs, Live = true,
                FixText = firstFixText ?? "No fixes needed: both ends match.",
            };
            reports.RemoveAll(x => x.Cable == current);
            reports.Add(rep);
            log.Add("verified", "seconds=" + secs.ToString("0.0") + "; fixes=" + fixesOnFirstCheck + "; checked_by_you=" + confirmedByYou);
            yield return Shutter();
            ShowReport(rep, true);
        }

        void ExitCheck()
        {
            StopAllCoroutines();
            toastCo = null; toastGroup.alpha = 0;
            if (log != null) log.Add("exit");
            ShowHome();
        }

        // ================================================================== arriving at an end

        /// <summary>
        /// The installer is at an end: show it, scan its label and learn from the label which cable and which end
        /// this is. With 'pairWith', the label must be the other end of that cable.
        /// </summary>
        IEnumerator Arrive(CableEnd expected, CableLabel pairWith, string step, string why, Action<CableEnd> done)
        {
            readColours = null; readConfidence = null; readEdited = null;   // a new end: nothing read here yet
            zone = null; zoneDismissed = false; zoneShown = false;
            bundle = null; bundleDismissed = false;
            foreach (var a in arrows) a.Destroy();
            arrows.Clear();
            ClearMarks();
            if (snapshot != null) snapshot.gameObject.SetActive(false);
            atController = expected == CableEnd.Controller;
            view.ShowEnd(expected);
            // before the first label the app does not know which end this is: the label will tell
            SetTitle(pairWith == null ? "Scan a cable" : EndTitle(expected), step);

            CableEnd here = CableEnd.Unknown;
            yield return ScanLabel(expected, pairWith, why, e => here = e);
            atController = here == CableEnd.Controller;
            if (here != expected) view.ShowEnd(here);
            BuildMarks(here);
            done(here);
        }

        IEnumerator ScanLabel(CableEnd expected, CableLabel pairWith, string why, Action<CableEnd> done)
        {
            while (true)
            {
                Instruction("Hold the phone 20 to 30 cm from the cable label", Accent);
                Sheet("Scan the cable label", why);
                ProgressLine("Looking for the " + LabelWord(), true);
                scanLine.gameObject.SetActive(true);
                LabelScan scan = null;
                CameraFrame frame = null;
                float t0 = Time.unscaledTime;
                while (true)
                {
                    scan = null;
                    frame = view.Latest;
                    yield return labels.Scan(frame, s => scan = s);
                    if (scan != null && scan.Status == LabelScanStatus.Found) break;
                    if (Time.unscaledTime - t0 >= LabelSearchSeconds) break;
                    yield return null;
                }
                scanLine.gameObject.SetActive(false);

                if (scan == null || scan.Status == LabelScanStatus.Unreadable)
                {
                    log.Add("label_unreadable");
                    Instruction("The label can't be read", Warn);
                    Sheet("Can't read the label", "Move a little closer, hold the phone still and keep the whole label in view.");
                    Btn(sheetButtons, "Scan again", ButtonKind.Primary, () => clicked = "again");
                    yield return WaitClick();
                    continue;
                }
                if (scan.Status == LabelScanStatus.NotFound)
                {
                    // without the label there is no pairing, so the app asks for it instead of guessing
                    log.Add("label_missing");
                    Instruction("Move closer to the label", Warn);
                    ShowMissingGuide(true);
                    Sheet("Can't find the label", "Hold the phone 20 to 30 cm from the label and keep it still for a moment. " +
                        "If this end has no label yet, attach " +
                        (pairWith != null ? "the label " + pairWith.Display : "the cable's label") + ", then scan again.");
                    Btn(sheetButtons, "Scan again", ButtonKind.Primary, () => clicked = "labelled");
                    yield return WaitClick();
                    ShowMissingGuide(false);
                    if (op != null) op.LabelAttached();
                    view.ShowEnd(expected);
                    continue;
                }

                var label = scan.Label;
                CableEnd end = label.End != CableEnd.Unknown ? label.End
                             : pairWith != null && pairWith.End != CableEnd.Unknown ? Other(pairWith.End) : expected;
                if (pairWith != null && pairWith.SameCableOtherPlan(label))
                {
                    // both labels name this cable but plan different modules: the app can't know which plan is right
                    log.Add("label_plan_mismatch", label.Route + " vs " + pairWith.Route);
                    Instruction("The two labels don't match", Warn);
                    Sheet("The two labels don't match", "Both labels say " + label.Display + ", but this one says " + label.Route + " and the other end's says " +
                        pairWith.Route + ". Reprint the labels so both ends carry the same plan, then scan again.");
                    Btn(sheetButtons, "Scan again", ButtonKind.Primary, () => clicked = "again");
                    yield return WaitClick();
                    continue;
                }
                if (pairWith != null && label.Key != pairWith.Key)
                {
                    log.Add("label_other_cable", label.Display);
                    Instruction("This is another cable", Warn);
                    Sheet("This is another cable", "This label is " + label.Display + ". Find the other end of " + pairWith.Display + " and scan its label.");
                    Btn(sheetButtons, "Scan again", ButtonKind.Primary, () => clicked = "again");
                    yield return WaitClick();
                    continue;
                }
                if (pairWith != null && pairWith.End != CableEnd.Unknown && end == pairWith.End)
                {
                    log.Add("label_same_end", label.Display);
                    Instruction("This end is already saved", Warn);
                    Sheet("This end is already saved", "This is the " + EndWord(end) + " end of " + pairWith.Display + ". Go to the " +
                        EndWord(Other(end)) + " end and scan the label there.");
                    Btn(sheetButtons, "Scan again", ButtonKind.Primary, () => clicked = "again");
                    yield return WaitClick();
                    continue;
                }
                string problem;
                var r = AxisCatalog.Resolve(label, out problem);
                if (r == null)
                {
                    // the label's plan names modules the app has no rule for: it never guesses one
                    log.Add("label_unknown_plan", label.Display + " · " + label.Route);
                    Instruction("Unknown cable plan", Warn);
                    Sheet("The app can't check this cable", problem + " (label " + label.Display + ", " + label.Route + ")");
                    Btn(sheetButtons, "Scan again", ButtonKind.Primary, () => clicked = "again");
                    yield return WaitClick();
                    continue;
                }
                rule = r;
                cable = label;
                if (end == CableEnd.Unknown)
                {
                    // the label does not say which end it is, and nothing else does: ask once
                    Instruction("Which end is this?", Accent);
                    Sheet("Which end is this?", "The label of " + label.Display + " does not say. Choose the end you are at.");
                    Btn(sheetButtons, "Controller end", ButtonKind.Secondary, () => clicked = "ctrl");
                    Btn(sheetButtons, EndTitleOf(r, CableEnd.Device), ButtonKind.Primary, () => clicked = "dev");
                    yield return WaitClick();
                    end = clicked == "ctrl" ? CableEnd.Controller : CableEnd.Device;
                }

                cable = label.AtEnd(end);
                current = CableFor(cable);
                records.TryGet(cable.Key, out record);
                records.TryGet(cable.Key, out ctrlRecord);
                if (qrCaption != null) qrCaption.text = cable.Display;
                ShowLabelBox(scan, frame);
                yield return Wait(0.25f);
                Instruction("Label found · hold steady", Ok);
                log.Add("label_found", cable.Display + " · " + cable.Route + " · " + CableLabel.EndCode(end) +
                    "; saved=" + (record != null ? "DEV" : "") + (ctrlRecord != null ? "CTRL" : ""));
                yield return Wait(0.9f);
                labelBoxOn = false;
                done(end);
                yield break;
            }
        }

        /// <summary>Frames the label where the scanner found it (its corners), anchored in the camera view.</summary>
        void ShowLabelBox(LabelScan scan, CameraFrame frame)
        {
            Vector2 c = view.LabelHint;
            labelExtent = Vector2.zero;
            bool ok = scan.Corners != null && scan.Corners.Length >= 4;
            if (ok) foreach (var p in scan.Corners) ok &= p.Valid;
            if (ok)
            {
                float x0 = 1, x1 = 0, y0 = 1, y1 = 0;
                foreach (var p in scan.Corners) { x0 = Mathf.Min(x0, p.X); x1 = Mathf.Max(x1, p.X); y0 = Mathf.Min(y0, p.Y); y1 = Mathf.Max(y1, p.Y); }
                c = new Vector2((x0 + x1) / 2, (y0 + y1) / 2);
                labelExtent = new Vector2(x1 - x0, y1 - y0);
            }
            view.Anchor(CameraKeys.Label, new ImagePoint(c.x, c.y), frame);
            var caption = (RectTransform)qrBox.Find("LabelText");
            float edge = c.x > 0.65f ? 1f : c.x < 0.35f ? 0f : 0.5f;
            caption.anchorMin = caption.anchorMax = new Vector2(edge, 1);
            caption.pivot = new Vector2(edge, 0.5f);
            labelBoxOn = true;
        }

        // ================================================================== first end: record what is there

        IEnumerator RecordEnd(CableEnd end)
        {
            yield return ConfirmWires("Save " + EndWord(end) + " end");
            var now = DateTime.Now;
            if (end == CableEnd.Device)
            {
                record = new CableRecord(cable.AtEnd(CableEnd.Device), rule, (int[])readColours.Clone(), (bool[])readEdited.Clone(), now);
                records.Save(record);
            }
            else
            {
                ctrlRecord = new ControllerRecord(cable.AtEnd(CableEnd.Controller), rule, (int[])readColours.Clone(), (float[])readConfidence.Clone(),
                    (bool[])readEdited.Clone(), now);
                records.Save(ctrlRecord);
            }
            int edits = CountEdits();
            confirmedByYou += edits;
            log.Add(end == CableEnd.Device ? "reader_saved" : "controller_saved",
                "colours=" + string.Join(" ", Array.ConvertAll(readColours, WireColours.Name)) + "; edited=" + edits);
            MarkOneEnd(current, end, now);
            yield return Shutter();
            Toast(EndTitle(end) + " saved");
            yield return Wait(0.6f);
        }

        int CountEdits()
        {
            int n = 0;
            foreach (bool e in readEdited) if (e) n++;
            return n;
        }

        /// <summary>
        /// Reads the wires at this end and lets the installer confirm or correct them. Only uncertain readings
        /// ask for attention (amber), so a clean reading needs one tap.
        /// </summary>
        IEnumerator ConfirmWires(string saveLabel)
        {
            Sheet("Reading the wires", "This is " + CableTitle + ", " + EndWord(atController ? CableEnd.Controller : CableEnd.Device) +
                " end. Hold the phone still for a moment.");
            var bar = ProgressLine(ReadingWhat());
            yield return ReadWires(bar);

            while (true)
            {
                WiresSheet(saveLabel);
                yield return WaitClickOrTap();
                if (tapped != null)
                {
                    var m = tapped; tapped = null;
                    if (readColours[marks.IndexOf(m)] != WireColours.None) yield return ColourPicker(m);
                    continue;
                }
                if (clicked == "zone_ignore")
                {
                    zoneDismissed = true;
                    zoneShown = false;
                    log.Add("zone_dismissed", zone != null && zone.Found != null ? zone.Found.Id : "");
                    continue;
                }
                if (clicked == "bundle_ignore")
                {
                    bundleDismissed = true;
                    log.Add("bundle_dismissed", BundleNames());
                    for (int i = 0; i < marks.Count; i++) SetReadMark(i);   // back to the colour marks
                    continue;
                }
                if (clicked == "rescan")
                {
                    log.Add("rescan");
                    Sheet("Reading again", "Hold the phone still for a moment.");
                    var b2 = ProgressLine(ReadingWhat());
                    yield return ReadWires(b2);
                    continue;
                }
                if (clicked == "save" && Shown().Count > 0)
                {
                    var open = Shown().Find(x => x.State == Mark.Check);
                    if (open != null) { Toast("Check " + open.Name + " first: tap it"); continue; }
                    string dup = DuplicateColour();
                    if (dup != null) { Toast(dup); continue; }
                    yield break;
                }
            }
        }

        string ReadingWhat()
        {
            return atController ? "Reading " + rule.Connector.Name + ", " + rule.Connector.Count + " pins"
                                : "Reading " + rule.DeviceTerminals.Length + " " + DeviceWord + " terminals";
        }

        IEnumerator ReadWires(Image bar)
        {
            var end = atController ? CableEnd.Controller : CableEnd.Device;
            foreach (var m in marks) if (m.State != Mark.Edited) m.Set(atController ? Mark.Hidden : Mark.Reading, m.Colour, null);
            int[] colours = null; float[] conf = null; ImagePoint[] where = null;
            ControllerReading crd = null;
            DeviceEndReading drd = null;
            var frame = view.Latest;
            scanLine.gameObject.SetActive(true);
            if (atController) StartCoroutine(wires.ReadControllerEnd(rule, cable, frame, r => { crd = r; where = r.Positions; conf = r.Confidence; colours = r.Colours; }));
            else StartCoroutine(wires.ReadDeviceEnd(rule, cable, frame, r => { drd = r; where = r.Positions; conf = r.Confidence; colours = r.Colours; }));
            yield return ProgressUntil(bar, () => colours != null, 2.4f);
            scanLine.gameObject.SetActive(false);

            int n = marks.Count;
            if (readColours == null || readColours.Length != n)
            {
                readColours = new int[n]; readConfidence = new float[n]; readEdited = new bool[n];
            }
            AnchorMarks(end, where, frame);
            TakeSnapshot(end, where, frame);
            for (int i = 0; i < n; i++)
            {
                if (!readEdited[i]) { readColours[i] = Get(colours, i, WireColours.None); readConfidence[i] = Get(conf, i, 0f); }
                SetReadMark(i);
            }
            log.Add(atController ? "controller_read" : "reader_read", "frame=" + frame.Id + "; unsure=" + marks.FindAll(x => x.State == Mark.Check).Count);
            if (atController)
            {
                // first end at the controller: no device end to compare colours with, so the zone check can only ask
                zone = ZoneCheck.Check(rule, crd, record, ClaimedByOthers());
                LogZone();
                ShowZone(crd, frame);
            }
            // then: do the wires read here run back to this cable's label?
            bundle = atController ? BundleCheck.Check(rule, cable, crd) : BundleCheck.Check(rule, cable, drd);
            LogBundle();
            if (BundleOpen()) MarkForeign(readColours);
        }

        /// <summary>
        /// Tells the camera view where the reader found each terminal (index = terminal for the device end, pin - 1
        /// for the controller), in the frame it read. The view keeps them on the terminals from then on.
        /// </summary>
        void AnchorMarks(CableEnd end, ImagePoint[] where, CameraFrame frame)
        {
            if (where == null) return;
            for (int i = 0; i < marks.Count && i < where.Length; i++)
                if (where[i].Valid) view.Anchor(CameraKeys.Of(end, i), where[i], frame);
        }

        /// <summary>
        /// Keeps a still of this end with its positions and the frame it was read from (for the other end and the report).
        /// The still is the frame that was read, so the dots sit on the pins the reader found even when the phone moved
        /// during the 1-3 s the reading took (bench, 31 Oct); the view's own picture is only the fallback.
        /// </summary>
        void TakeSnapshot(CableEnd end, ImagePoint[] where, CameraFrame frame)
        {
            var shot = view.Snapshot(end, where ?? new ImagePoint[0]);
            var still = FrameTexture(frame);
            if (still != null)
            {
                if (shot.Image != null) Destroy(shot.Image);
                still.name = "snapshot-" + (end == CableEnd.Device ? "device" : "controller");
                shot.Image = still;
            }
            if (current == null) return;
            if (end == CableEnd.Device) { current.DeviceShot = shot; current.DeviceFrame = frame; }
            else { current.ControllerShot = shot; current.ControllerFrame = frame; }
        }

        /// <summary>The frame's own pixels as a texture (RGBA32, Unity's bottom-up rows), or null if it has none.</summary>
        static Texture2D FrameTexture(CameraFrame f)
        {
            if (f == null || f.Format != FramePixels.Rgba32 || f.Pixels == null || f.Width <= 0 || f.Height <= 0 ||
                f.Pixels.Length != f.Width * f.Height * 4) return null;
            var tex = new Texture2D(f.Width, f.Height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(f.Pixels);
            tex.Apply(false);
            return tex;
        }

        void SetReadMark(int i)
        {
            var m = marks[i];
            if (readColours[i] == WireColours.None) { m.Set(Mark.Hidden, -1, null); return; }
            Mark s = readEdited[i] ? Mark.Edited : readConfidence[i] < confidenceThreshold ? Mark.Check : Mark.Correct;
            if (m.Set(s, readColours[i], m.Name + "  " + WireColours.Name(readColours[i]))) StartCoroutine(Pop(m.Vis));
        }

        /// <summary>Markers that carry a wire at this end (at the controller, empty pins are not shown).</summary>
        List<PinMarker> Shown()
        {
            var list = new List<PinMarker>();
            for (int i = 0; i < marks.Count; i++) if (readColours != null && readColours[i] != WireColours.None) list.Add(marks[i]);
            return list;
        }

        string DuplicateColour()
        {
            var shown = Shown();
            for (int a = 0; a < shown.Count; a++)
                for (int b = a + 1; b < shown.Count; b++)
                {
                    int ca = readColours[marks.IndexOf(shown[a])], cb = readColours[marks.IndexOf(shown[b])];
                    if (ca == cb) return shown[a].Name + " and " + shown[b].Name + " both show " + WireColours.Lower(ca) + ". Tap one to correct it.";
                }
            return null;
        }

        void WiresSheet(string saveLabel)
        {
            var shown = Shown();
            var unsure = shown.FindAll(x => x.State == Mark.Check);
            string here = EndWord(atController ? CableEnd.Controller : CableEnd.Device);
            if (atController && ZoneOpen()) { ZoneSheet(false); return; }
            if (BundleOpen()) { BundleSheet(false); return; }
            if (shown.Count == 0)
            {
                // nothing read: the camera was not on the terminals, or the reader failed
                Sheet("No wires found", "Point the camera at the " + (atController ? rule.Connector.Name + " block" : DeviceWord + "'s terminals") +
                    " so they fill the middle of the screen, then read again.");
                Btn(sheetButtons, "Read again", ButtonKind.Primary, () => clicked = "rescan");
                Instruction("No wires found", Warn);
                return;
            }
            Sheet(unsure.Count > 0 ? "Check one wire" : shown.Count + " wires read",
                unsure.Count > 0 ? "The camera is not sure about " + unsure[0].Name + ". Tap it to confirm or correct the colour."
                                 : "Save them as the " + here + " end of " + CableTitle + ", or tap a wire to correct it.");
            foreach (var m in ScreenOrder(shown))
            {
                int i = marks.IndexOf(m);
                string detail; Color dc;
                if (m.State == Mark.Check) { detail = "unsure"; dc = WarnInk; }
                else if (m.State == Mark.Edited) { detail = "set by you"; dc = Accent; }
                else if (atController && rule.TerminalFor(m.Id) == null) { detail = "not used by this " + DeviceWord; dc = WarnInk; }
                else { detail = ""; dc = Ink3; }
                var mm = m;
                Row(m.State, m.Name, readColours[i], WireColours.Name(readColours[i]), detail, dc, () => { if (!modalOpen) tapped = mm; });
            }
            Btn(sheetButtons, "Read again", ButtonKind.Secondary, () => clicked = "rescan");
            Btn(sheetButtons, saveLabel, ButtonKind.Primary, () => clicked = "save");
            Instruction(unsure.Count > 0 ? "Tap the amber wire" : "Looks good: save to continue", unsure.Count > 0 ? Warn : Ok);
        }

        // ================================================================== walk to the other end

        /// <param name="to">The end to walk to.</param>
        /// <param name="toFix">True when the installer goes back to the controller to fix what the check found.</param>
        IEnumerator WalkScreen(CableEnd to, bool toFix)
        {
            var o = UiKit.Rect("Walk", screen);
            Stretch(o);
            var bg = Img("Bg", o, null, Page, 0);
            Stretch(bg.rectTransform);
            bg.raycastTarget = true;
            var g = o.gameObject.AddComponent<CanvasGroup>();
            var safe = UiKit.Rect("Safe", o);
            ApplySafeArea(safe);
            var c = Scroll(safe, 40, BottomBarH);

            Space(c, 100);
            var art = UiKit.Rect("Path", c);
            art.gameObject.AddComponent<LayoutElement>().preferredHeight = 220;
            var a = Img("From", art, sCircle, toFix ? BadSoft : OkSoft, 0); Place(a.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-300, 0), new Vector2(160, 160));
            var ah = UiKit.Rect("Icon", a.rectTransform); Stretch(ah); DrawIcon(ah, toFix ? Icon.Cross : Icon.Check, 72, toFix ? Bad : Ok);
            var b = Img("To", art, sCircle, AccentSoft, 0); Place(b.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300, 0), new Vector2(160, 160));
            var bh = UiKit.Rect("Icon", b.rectTransform); Stretch(bh); DrawIcon(bh, Icon.Camera, 72, Accent);
            var dots = new List<Image>();
            for (int i = 0; i < 9; i++)
            {
                var d = Img("Dot", art, sCircle, Hairline, 0);
                Place(d.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-176 + i * 44, 0), new Vector2(14, 14));
                dots.Add(d);
            }
            Space(c, 40);
            Txt(c, toFix ? "Go back to the controller" : "Now go to the " + EndWord(to), DisplaySize - 4, Ink, Face.Semi, TextAlignmentOptions.Center);
            Txt(c, toFix ? "The wiring needs a fix there. Scan the label again and the app shows what to change, pin by pin."
                         : "This end of " + CableTitle + " is saved. At the other end, scan its label: the app pairs the two ends and checks every wire.",
                Body, Ink2, Face.Regular, TextAlignmentOptions.Center);
            Space(c, 24);

            // where the other end is: derived from the label and the wiring rule, so nothing to remember
            var where = Card(c, 36, 10);
            OverlineText(where, "The other end", Ink3);
            Txt(where, WhereIs(to), Head, Ink, Face.Semi);
            Txt(where, "Label " + cable.Display + " · " + CableLabel.EndCode(to), Small, Ink2);

            var card = Card(c, 36, 16);
            if (toFix)
            {
                OverlineText(card, "To fix there", Ink3);
                foreach (var f in ActiveFixes()) Txt(card, f.Headline, Body, Ink, Face.Semi);
            }
            else
            {
                CableEnd from = Other(to);
                OverlineText(card, "Saved at the " + EndWord(from), Ink3);
                var row = HRow(card, 72, 14);
                for (int i = 0; i < marks.Count; i++)
                {
                    int colour = from == CableEnd.Device ? record.Colours[i] : ctrlRecord.PinColours[i];
                    if (colour == WireColours.None) continue;
                    var chip = Img("Wire", row, sRound, Surface, 24);
                    var hl = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
                    hl.padding = new RectOffset(16, 22, 10, 10); hl.spacing = 12; hl.childAlignment = TextAnchor.MiddleLeft;
                    hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
                    Flex(chip);
                    Swatch(chip.rectTransform, colour, 32);
                    Txt(chip.rectTransform, marks[i].Name, Body, Ink, Face.Semi);
                }
            }

            var bar = BottomBar(safe);
            if (!toFix) Btn(bar, "Finish later", ButtonKind.Secondary, () => clicked = "later");
            Btn(bar, "I'm at the " + EndWord(to), ButtonKind.Primary, () => clicked = "go");

            yield return Fade(g, 0, 1, 0.3f);
            clicked = null;
            while (clicked != "go" && clicked != "later")
            {
                float t = Time.unscaledTime * 1.2f;
                for (int i = 0; i < dots.Count; i++)
                    dots[i].color = Color.Lerp(Accent, Hairline, Mathf.Clamp01(Mathf.Repeat(t - i / 9f, 1f) * 3f));
                yield return null;
            }
            if (clicked == "later")
            {
                log.Add("finish_later");
                checkAborted = true;
                ShowHome();
                Toast("Saved. Scan the label at the other end when you get there.");
                yield break;
            }
            log.Add(toFix ? "walk_back_done" : "walk_done");
            yield return Fade(g, 1, 0, 0.25f);
            Destroy(o.gameObject);
        }

        // ================================================================== second end at the controller

        IEnumerator ControllerSecond()
        {
            BuildSnapshot(CableEnd.Device);
            ShowSnapshot(true);
            yield return RunCheck(true);
            yield return ControllerResultsLoop();
        }

        /// <summary>Back at the controller after the check at the reader found a fault: the same verdicts, now on the pins.</summary>
        IEnumerator ControllerFix()
        {
            BuildSnapshot(CableEnd.Device);
            ShowSnapshot(true);
            yield return LocatePins();
            foreach (var m in marks) m.Set(Mark.Hidden, -1, null);
            foreach (var r in results) ApplyResult(r);
            DrawFixArrows();
            log.Add("fix_shown_at_controller");
            yield return ControllerResultsLoop();
        }

        /// <summary>Reads the controller only to find its pins, so the verdicts of the check can be shown on them.</summary>
        IEnumerator LocatePins()
        {
            Sheet("Finding the pins", "Hold the phone still for a moment.");
            var bar = ProgressLine(ReadingWhat());
            Instruction("Hold steady", Accent);
            ControllerReading rd = null;
            var frame = view.Latest;
            scanLine.gameObject.SetActive(true);
            StartCoroutine(wires.ReadControllerEnd(rule, cable, frame, r => rd = r));
            yield return ProgressUntil(bar, () => rd != null, 2.2f);
            scanLine.gameObject.SetActive(false);
            AnchorMarks(CableEnd.Controller, rd.Positions, frame);
        }

        IEnumerator ControllerResultsLoop()
        {
            while (true)
            {
                ControllerResultsSheet();
                yield return WaitClickOrTap();
                if (tapped != null)
                {
                    var m = tapped; tapped = null;
                    var r = ResultFor(m.Id);
                    if (r != null) yield return Detail(r);
                    continue;
                }
                if (clicked == "zone_ignore")
                {
                    zoneDismissed = true;
                    zoneShown = false;
                    log.Add("zone_dismissed", zone != null && zone.Found != null ? zone.Found.Id : "");
                    yield return RunCheck(false);
                    continue;
                }
                if (clicked == "bundle_ignore")
                {
                    bundleDismissed = true;
                    log.Add("bundle_dismissed", BundleNames());
                    yield return RunCheck(false);
                    continue;
                }
                if (clicked == "recheck")
                {
                    if (op != null) op.Rewired(record);   // Wizard of Oz: the bench now shows the fixed wiring
                    view.ShowEnd(CableEnd.Controller);
                    log.Add("recheck");
                    yield return RunCheck(false);
                    continue;
                }
                if (clicked == "finish") yield break;
            }
        }

        IEnumerator RunCheck(bool first)
        {
            foreach (var a in arrows) a.Destroy();
            arrows.Clear();
            HighlightSnapshot(-1);

            Sheet(first ? "Checking the wiring" : "Checking again", "Comparing " + rule.Connector.Name + " with what you saved at the " + DeviceWord + ".");
            var readBar = ProgressLine(ReadingWhat());
            var rulesBar = ProgressLine("Comparing with the " + DeviceWord + " end");
            var aiBar = useSecondOpinion ? ProgressLine("AI second opinion") : null;
            Instruction("Hold steady", Accent);
            foreach (var m in marks) m.Set(record.ExpectedOnPin(m.Id) != WireColours.None ? Mark.Reading : Mark.Hidden, -1, null);

            ControllerReading rd = null;
            Opinion[] ops = null;
            var frame = view.Latest;
            scanLine.gameObject.SetActive(true);
            StartCoroutine(wires.ReadControllerEnd(rule, cable, frame, r => rd = r));
            if (useSecondOpinion)
            {
                // layer 3 runs in parallel and independently: it never sees layer 1's reading
                StartCoroutine(secondOpinion.Compare(record, current.DeviceFrame, frame, o => ops = o));
                aiProgress = StartCoroutine(ProgressUntil(aiBar, () => ops != null, 2.8f));
            }
            yield return ProgressUntil(readBar, () => rd != null, 2.2f);
            scanLine.gameObject.SetActive(false);

            // the zone check first: is the cable in the module its label names? If not, pins mean nothing yet
            zone = ZoneCheck.Check(rule, rd, record, ClaimedByOthers());
            LogZone();
            if (ZoneOpen())
            {
                if (aiProgress != null) StopCoroutine(aiProgress);
                foreach (var m in marks) m.Set(Mark.Hidden, -1, null);
                results = null; check = null;
                pendingFirstCheck = pendingFirstCheck || first;
                ShowZone(rd, frame);
                yield break;
            }
            zoneShown = false;
            AnchorMarks(CableEnd.Controller, rd.Positions, frame);

            // then the bundle check: a wire traced to another cable is shown before any pin is judged
            bundle = BundleCheck.Check(rule, cable, rd);
            LogBundle();
            if (BundleOpen())
            {
                if (aiProgress != null) StopCoroutine(aiProgress);
                foreach (var m in marks) m.Set(Mark.Hidden, -1, null);
                MarkForeign(rd.Colours);
                results = null; check = null;
                pendingFirstCheck = pendingFirstCheck || first;
                yield break;
            }
            first = first || pendingFirstCheck;
            pendingFirstCheck = false;
            TakeSnapshot(CableEnd.Controller, rd.Positions, frame);
            // the store keeps the controller end as it is now (after a fix, the fixed wiring)
            ctrlRecord = new ControllerRecord(cable.AtEnd(CableEnd.Controller), rule, (int[])rd.Colours.Clone(), (float[])rd.Confidence.Clone(),
                new bool[rd.Colours.Length], DateTime.Now);
            records.Save(ctrlRecord);
            check = RuleChecker.Check(record, rd);                               // layer 2: instant and exact
            yield return ProgressFor(rulesBar, 0.5f);
            if (useSecondOpinion) { while (ops == null) yield return null; }
            else ops = RulesOnly();

            results = VerdictCombiner.Evaluate(check, rd, ops, confidenceThreshold);
            foreach (var m in marks) m.Set(Mark.Hidden, -1, null);
            foreach (var r in results) ApplyResult(r);
            DrawFixArrows();
            NoteFirstFixes(first);
        }

        /// <summary>Without the AI second opinion, layer 3 simply agrees with the rules.</summary>
        Opinion[] RulesOnly()
        {
            var ops = new Opinion[rule.Connector.Count];
            foreach (var f in check.Pins) ops[f.Pin - 1] = f.RulesOk ? Opinion.Match : Opinion.Mismatch;
            return ops;
        }

        void NoteFirstFixes(bool first)
        {
            var fixes = ActiveFixes();
            if (first)
            {
                fixesOnFirstCheck = fixes.Count;
                if (fixes.Count > 0) firstFixText = string.Join(" ", fixes.ConvertAll(f => f.Text).ToArray());
            }
            log.Add(first ? "check" : "recheck_result", "at=" + (atController ? "CTRL" : "DEV") + "; wrong=" + Count(Verdict.Wrong) +
                "; amber=" + Count(Verdict.Check) + "; fixes=" + string.Join(" | ", fixes.ConvertAll(f => f.Text).ToArray()));
        }

        void ApplyResult(PinResult r)
        {
            var m = MarkFor(r.Pin);
            if (m == null) return;
            var f = r.Finding;
            string tag = m.Name + "  " + (f.Found == WireColours.None ? "no wire" : WireColours.Name(f.Found));
            if (m.Set(MarkOf(r.Verdict), f.Found, tag)) StartCoroutine(Pop(m.Vis));
        }

        static Mark MarkOf(Verdict v)
        {
            switch (v)
            {
                case Verdict.Wrong: return Mark.Wrong;
                case Verdict.Check: return Mark.Check;
                case Verdict.ConfirmedByInstaller: return Mark.Confirmed;
                default: return Mark.Correct;
            }
        }

        List<FixAction> ActiveFixes()
        {
            var list = new List<FixAction>();
            if (results == null) return list;
            foreach (var r in results)
                if (r.Verdict == Verdict.Wrong && r.Finding.Fix != null && !list.Contains(r.Finding.Fix)) list.Add(r.Finding.Fix);
            return list;
        }

        /// <summary>
        /// At the device end, true when every active fix can be done right here (wire colours carry no meaning, so a
        /// swap among the cable's own wires is fixed where the installer stands; see FixAction).
        /// </summary>
        bool FixHere
        {
            get
            {
                if (atController) return false;
                var fixes = ActiveFixes();
                if (fixes.Count == 0) return false;
                foreach (var f in fixes) if (!f.AtDeviceToo) return false;
                return true;
            }
        }

        void DrawFixArrows()
        {
            foreach (var a in arrows) a.Destroy();
            arrows.Clear();
            if (!atController)
            {
                if (!FixHere) return;
                foreach (var f in ActiveFixes())
                {
                    int i = rule.TerminalIndex(f.DeviceFrom), j = rule.TerminalIndex(f.DeviceTo);
                    if (i < 0 || j < 0 || i == j || i >= marks.Count || j >= marks.Count) continue;
                    arrows.Add(FixArrow.Build(markersRT, marks[i], marks[j], f.Kind == FixKind.Swap));
                }
                return;
            }
            foreach (var f in ActiveFixes())
            {
                if (f.FromPin == f.ToPin) continue;
                var from = MarkFor(f.FromPin); var to = MarkFor(f.ToPin);
                if (from == null || to == null) continue;
                arrows.Add(FixArrow.Build(markersRT, from, to, f.Kind == FixKind.Swap));
            }
        }

        void ControllerResultsSheet()
        {
            if (ZoneOpen()) { ZoneSheet(true); return; }
            if (BundleOpen()) { BundleSheet(true); return; }
            int wrong = Count(Verdict.Wrong), amber = Count(Verdict.Check);
            var fixes = ActiveFixes();
            string title, sub;
            if (wrong > 0)
            {
                title = fixes.Count == 1 ? fixes[0].Headline : fixes.Count + " fixes needed";
                sub = fixes.Count == 1 ? fixes[0].Text : "Tap a red pin for its fix.";
                if (amber > 0) sub += " Then check the amber pin.";
            }
            else if (amber > 0)
            {
                title = amber == 1 ? "Check one pin yourself" : "Check " + amber + " pins yourself";
                sub = "The automation is not sure here. Tap the amber pin and compare it with the " + DeviceWord + " end.";
            }
            else
            {
                int n = 0; foreach (var r in results) if (r.Finding.Expected != WireColours.None) n++;
                title = "All " + n + " wires correct";
                sub = "Every wire matches the " + DeviceWord + " end, and the other pins of " + rule.Connector.Name + " are empty.";
            }
            Sheet(title, sub);

            var ok = new List<PinResult>();
            foreach (var r in ScreenOrder(results))
            {
                if (r.Verdict == Verdict.Correct && (wrong > 0 || amber > 0)) { ok.Add(r); continue; }
                ResultRow(r);
            }
            if (ok.Count > 0) OkSummaryRow(ok);

            if (wrong > 0) Btn(sheetButtons, "Rewired · check again", ButtonKind.Primary, () => clicked = "recheck");
            else if (amber > 0) Btn(sheetButtons, "Check again", ButtonKind.Secondary, () => clicked = "recheck");
            else Btn(sheetButtons, "Finish", ButtonKind.Primary, () => clicked = "finish");

            if (wrong > 0) Instruction("Wiring does not match the " + DeviceWord + " end", Bad);
            else if (amber > 0) Instruction("Tap the amber pin", Warn);
            else Instruction("All pins match", Ok);
        }

        void ResultRow(PinResult r)
        {
            var f = r.Finding;
            var m = MarkFor(r.Pin);
            string detail; Color dc;
            switch (r.Verdict)
            {
                case Verdict.Wrong:
                    detail = f.Expected == WireColours.None ? "should be empty" : "should be " + WireColours.Lower(f.Expected);
                    dc = Bad; break;
                case Verdict.Check:
                    detail = r.Reason == CheckReason.LowConfidence ? "unsure" : "AI disagrees";
                    dc = WarnInk; break;
                case Verdict.ConfirmedByInstaller:
                    detail = "checked by you"; dc = Ink3; break;
                default:
                    detail = "matches"; dc = Ink3; break;
            }
            Row(MarkOf(r.Verdict), m != null ? m.Name : rule.Connector.Pin(r.Pin).Print, f.Found,
                f.Found == WireColours.None ? "No wire" : WireColours.Name(f.Found), detail, dc,
                () => { if (!modalOpen) tapped = m; });
        }

        void OkSummaryRow(List<PinResult> ok)
        {
            var row = HCard(sheetContent, 100);
            Badge(row, Ok, Icon.Check, 44);
            foreach (var r in ok)
            {
                var t = Txt(row, rule.Connector.Pin(r.Pin).Print, Body, Ink, Face.Semi);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                Swatch(row, r.Finding.Found, 30);
            }
            Flex(Txt(row, "correct", Small, Ink3, Face.Regular, TextAlignmentOptions.Right));
        }

        // ================================================================== second end at the reader

        IEnumerator ReaderSecond()
        {
            BuildSnapshot(CableEnd.Controller);
            ShowSnapshot(true);
            yield return ConfirmWires("Check the wiring");

            record = new CableRecord(cable.AtEnd(CableEnd.Device), rule, (int[])readColours.Clone(), (bool[])readEdited.Clone(), DateTime.Now);
            records.Save(record);
            confirmedByYou += CountEdits();
            log.Add("reader_saved", "colours=" + string.Join(" ", Array.ConvertAll(readColours, WireColours.Name)));
            yield return RunCheckAtReader(true);

            while (true)
            {
                ReaderVerdictSheet();
                yield return WaitClickOrTap();
                if (tapped != null)
                {
                    var m = tapped; tapped = null;
                    var r = ResultFor(PinOfTerminal(m.Id));
                    if (r != null) yield return Detail(r);
                    continue;
                }
                if (clicked == "fix") { needsFixAtController = true; yield break; }
                if (clicked == "recheck_here")
                {
                    if (op != null) op.RewiredAtDevice();   // Wizard of Oz: the bench now shows the re-wired reader
                    view.ShowEnd(CableEnd.Device);
                    log.Add("recheck_at_reader");
                    yield return RecheckReader();
                    continue;
                }
                if (clicked == "finish") yield break;
            }
        }

        /// <summary>After a fix at the device end: read this end again and run the same check.</summary>
        IEnumerator RecheckReader()
        {
            foreach (var a in arrows) a.Destroy();
            arrows.Clear();
            HighlightSnapshot(-1);
            readColours = null; readConfidence = null; readEdited = null;   // a new reading of re-wired terminals
            foreach (var m in marks) m.Set(Mark.Hidden, -1, null);
            yield return ConfirmWires("Check again");
            record = new CableRecord(cable.AtEnd(CableEnd.Device), rule, (int[])readColours.Clone(), (bool[])readEdited.Clone(), DateTime.Now);
            records.Save(record);
            confirmedByYou += CountEdits();
            log.Add("reader_saved", "colours=" + string.Join(" ", Array.ConvertAll(readColours, WireColours.Name)) + "; after_fix=true");
            yield return RunCheckAtReader(false);
        }

        int PinOfTerminal(int terminalIndex) { return rule.PinFor(rule.DeviceTerminals[terminalIndex].Id); }

        /// <summary>The same rule check and verdict table, run against the controller end saved earlier.</summary>
        IEnumerator RunCheckAtReader(bool first)
        {
            Sheet(first ? "Checking the wiring" : "Checking again", "Comparing the " + DeviceWord + " end with the controller end you saved.");
            var rulesBar = ProgressLine("Comparing with the controller end");
            var aiBar = useSecondOpinion ? ProgressLine("AI second opinion") : null;
            Instruction("Hold steady", Accent);
            for (int i = 0; i < marks.Count; i++) marks[i].Set(Mark.Reading, record.Colours[i], null);

            var rd = ctrlRecord.AsReading();
            Opinion[] ops = null;
            if (useSecondOpinion)
            {
                StartCoroutine(secondOpinion.Compare(record, current.DeviceFrame, current.ControllerFrame, o => ops = o));
                StartCoroutine(ProgressUntil(aiBar, () => ops != null, 2.8f));
            }
            check = RuleChecker.Check(record, rd);
            yield return ProgressFor(rulesBar, 0.6f);
            if (useSecondOpinion) { while (ops == null) yield return null; }
            else ops = RulesOnly();

            results = VerdictCombiner.Evaluate(check, rd, ops, confidenceThreshold);
            ApplyReaderVerdicts();
            DrawFixArrows();
            NoteFirstFixes(first);
        }

        /// <summary>Each reader terminal takes the verdict of the controller pin its wire must reach.</summary>
        void ApplyReaderVerdicts()
        {
            for (int i = 0; i < marks.Count; i++)
            {
                var r = ResultFor(PinOfTerminal(i));
                if (r == null) continue;
                var m = marks[i];
                if (m.Set(MarkOf(r.Verdict), record.Colours[i], m.Name + "  " + WireColours.Name(record.Colours[i]))) StartCoroutine(Pop(m.Vis));
            }
        }

        void ReaderVerdictSheet()
        {
            int wrong = Count(Verdict.Wrong), amber = Count(Verdict.Check);
            var fixes = ActiveFixes();
            bool here = FixHere;
            if (wrong > 0 && here)
                Sheet(fixes.Count == 1 ? fixes[0].DeviceHeadline + " here" : fixes.Count + " fixes here",
                      (fixes.Count == 1 ? fixes[0].DeviceText : "Tap a red wire for its fix.") +
                      (fixes[0].TouchesPower ? " Do this with the power off." : "") + " No need to go back to the controller.");
            else if (wrong > 0)
                Sheet("Fix at the controller", (fixes.Count == 1 ? fixes[0].Text : fixes.Count + " fixes. Tap a red wire for its fix.") +
                      " The app shows where on the controller.");
            else if (amber > 0)
                Sheet("Check one wire yourself", "The automation is not sure about one wire. Tap the amber wire and decide.");
            else
                Sheet("All " + marks.Count + " wires correct", "Every wire of " + CableTitle + " lands on the right controller pin.");

            foreach (var m in ScreenOrder(marks))
            {
                int i = m.Id;
                int pin = PinOfTerminal(i);
                var r = ResultFor(pin);
                if (r == null) continue;
                int colour = record.Colours[i];
                string detail; Color dc;
                switch (r.Verdict)
                {
                    case Verdict.Wrong:
                        int landed = ctrlRecord.PinWithColour(colour);
                        detail = landed == 0 ? "not connected" : "on " + rule.Connector.Pin(landed).Print + " · pin " + landed;
                        dc = Bad; break;
                    case Verdict.Check:
                        detail = r.Reason == CheckReason.LowConfidence ? "unsure" : "AI disagrees"; dc = WarnInk; break;
                    case Verdict.ConfirmedByInstaller:
                        detail = "checked by you"; dc = Ink3; break;
                    default:
                        detail = "to " + rule.Connector.Pin(pin).Print + " · pin " + pin; dc = Ink3; break;
                }
                var mm = m;
                Row(MarkOf(r.Verdict), m.Name, colour, WireColours.Name(colour), detail, dc, () => { if (!modalOpen) tapped = mm; });
            }

            if (wrong > 0 && here) Btn(sheetButtons, "Rewired · check again", ButtonKind.Primary, () => clicked = "recheck_here");
            else if (wrong > 0) Btn(sheetButtons, "Go to the controller", ButtonKind.Primary, () => clicked = "fix");
            else if (amber == 0) Btn(sheetButtons, "Finish", ButtonKind.Primary, () => clicked = "finish");

            if (wrong > 0 && here) Instruction("Fix it here, at the " + DeviceWord, Bad);
            else if (wrong > 0) Instruction("Wiring does not match the controller end", Bad);
            else if (amber > 0) Instruction("Tap the amber wire", Warn);
            else Instruction("Both ends match", Ok);
        }

        // ================================================================== shared steps

        IEnumerator ColourPicker(PinMarker m)
        {
            int i = marks.IndexOf(m);
            var md = ModalSheet();
            Txt(md, "Which colour is on " + m.Name + "?", Title, Ink, Face.Semi);
            Txt(md, "The camera read " + WireColours.Lower(readColours[i]) + " (" + Mathf.RoundToInt(readConfidence[i] * 100) + "% sure). Tap the right colour.",
                Body, Ink2);
            var grid = UiKit.Rect("Grid", md);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(232, 120);
            gl.spacing = new Vector2(16, 16);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4;
            grid.gameObject.AddComponent<LayoutElement>().preferredHeight = 120 * 2 + 16;
            for (int c = 0; c < WireColours.Names.Length; c++)
            {
                int cc = c;
                bool sel = c == readColours[i];
                var chip = Img("Colour", grid, sRound, sel ? Accent : Panel, 28);
                var inner = Img("Fill", chip.rectTransform, sRound, sel ? AccentSoft : Panel, 26);
                Stretch(inner.rectTransform, sel ? 4 : 0, sel ? 4 : 0, sel ? 4 : 0, sel ? 4 : 0);
                inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                MakeButton(chip, () => { clicked = "colour:" + cc; });
                var hr = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
                hr.padding = new RectOffset(22, 12, 0, 0); hr.spacing = 14; hr.childAlignment = TextAnchor.MiddleLeft;
                hr.childControlWidth = true; hr.childControlHeight = true; hr.childForceExpandWidth = false; hr.childForceExpandHeight = false;
                Swatch(chip.rectTransform, c, 36);
                Txt(chip.rectTransform, WireColours.Names[c], Small, Ink, sel ? Face.Semi : Face.Regular);
            }
            var btns = HRow(md, 136, 20);
            btns.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Btn(btns, "Cancel", ButtonKind.Secondary, () => clicked = "cancel");
            Btn(btns, "Keep " + WireColours.Lower(readColours[i]), ButtonKind.Primary, () => clicked = "keep");
            yield return OpenModal(md);
            clicked = null;
            while (clicked == null || !(clicked == "cancel" || clicked == "dismiss" || clicked == "keep" || clicked.StartsWith("colour:"))) yield return null;
            if (clicked.StartsWith("colour:") || clicked == "keep")
            {
                int c = clicked == "keep" ? readColours[i] : int.Parse(clicked.Substring(7));
                bool changed = c != readColours[i];
                readColours[i] = c;
                readConfidence[i] = 1f;
                readEdited[i] = readEdited[i] || changed;
                if (!changed) confirmedByYou++;
                SetReadMark(i);
                log.Add(changed ? "wire_edit" : "wire_confirm", m.Name + "=" + WireColours.Name(c));
            }
            yield return CloseModal(md);
        }

        IEnumerator Detail(PinResult r)
        {
            var f = r.Finding;
            var pin = rule.Connector.Pin(r.Pin);
            log.Add("pin_detail", pin.Label + " " + r.Verdict + (atController ? "" : " (at reader)"));
            int wire = f.Expected != WireColours.None ? f.Expected : f.Found;
            int term = rule.TerminalIndex(record.TerminalWithColour(wire) ?? "");
            int otherPin = atController ? 0 : (ctrlRecord.PinWithColour(wire) > 0 ? ctrlRecord.PinWithColour(wire) : r.Pin);
            HighlightSnapshot(atController ? term : otherPin);

            var md = ModalSheet();
            var mark = MarkOf(r.Verdict);
            var head = HRow(md, 0, 28);
            Badge(head, PinMarker.ColourOf(mark), PinMarker.IconOf(mark), 84, mark == Mark.Check ? Ink : (Color?)null);
            var ht = VBox(head, 2); Flex(ht);
            string title = atController || term < 0 ? pin.Print + "  ·  pin " + pin.Number
                         : EndTitle(CableEnd.Device).Replace(" end", "") + " " + rule.DeviceTerminals[term].Print + "  →  pin " + pin.Number;
            Txt(ht, title, Title, Ink, Face.Semi);
            string state = r.Verdict == Verdict.Wrong ? (f.Found == WireColours.None ? "No wire on pin " + pin.Number : f.Expected == WireColours.None ? "Pin " + pin.Number + " should be empty" : "Wrong wire on pin " + pin.Number)
                         : r.Verdict == Verdict.Check ? "Please check" : r.Verdict == Verdict.ConfirmedByInstaller ? "Checked by you" : "Correct";
            Txt(ht, state, Body, mark == Mark.Wrong ? Bad : mark == Mark.Check ? WarnInk : Ok, Face.Semi);

            var lines = Card(md, 32, 4);
            ColourLine(lines, atController ? "Expected" : "Pin " + pin.Number + " needs", f.Expected,
                f.Terminal != null ? "from " + DeviceWord + " " + rule.Terminal(f.Terminal).Print : "nothing on this pin");
            Divider(lines);
            string foundNote = f.RulesOk ? "matches" : f.Found == WireColours.None ? "" :
                record.TerminalWithColour(f.Found) != null ? "belongs on " + rule.Connector.Pin(rule.PinFor(record.TerminalWithColour(f.Found))).Label : "not in this cable";
            ColourLine(lines, atController ? "Found" : "Pin " + pin.Number + " has", f.Found, foundNote);

            if (r.Verdict == Verdict.Check)
            {
                string why = r.Reason == CheckReason.LowConfidence
                    ? "The camera reading is not certain (" + Mathf.RoundToInt(r.Confidence * 100) + "%). Move closer or add light, or check it by hand."
                    : "The rule check says " + (f.RulesOk ? "correct" : "wrong") + "; the AI second opinion says " + (f.RulesOk ? "wrong" : "correct") +
                      ". Compare with the other end below and decide.";
                Callout(md, WarnSoft, WarnInk, "What to do", why);
            }
            if (f.Fix != null && (r.Verdict == Verdict.Wrong || (r.Verdict == Verdict.Check && !f.RulesOk)))
            {
                bool fixHere = !atController && FixHere && f.Fix.AtDeviceToo;
                Callout(md, BadSoft, Bad, atController ? "Fix" : fixHere ? "Fix here" : "Fix at the controller",
                    (fixHere ? f.Fix.DeviceText : f.Fix.Text) + (f.Fix.TouchesPower ? " Do this with the power off." : ""));
                Txt(md, "<b>Why.</b> " + f.Fix.Why, Small, Ink2);
                if (!string.IsNullOrEmpty(f.Fix.Symptom)) Txt(md, "<b>On the device.</b> " + f.Fix.Symptom, Small, Ink2);
            }

            var snapCard = Card(md, 16, 12);
            OverlineText(snapCard, atController ? EndTitle(CableEnd.Device) + " · same wire" : "Controller end · pin " + otherPin, Ink3);
            var holder = UiKit.Rect("Holder", snapCard);
            holder.gameObject.AddComponent<LayoutElement>().preferredHeight = 360;
            BuildCrop(holder, atController ? CableEnd.Device : CableEnd.Controller, atController ? term : otherPin, null);
            Txt(md, "Rules: " + (f.RulesOk ? "match" : "mismatch") + "  ·  AI second opinion: " + OpinionWord(r.Opinion) +
                    "  ·  reading " + Mathf.RoundToInt(r.Confidence * 100) + "%", Caption, Ink3);

            var btns = HRow(md, 136, 20);
            btns.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            if (r.Verdict == Verdict.Check)
            {
                Btn(btns, "It's wrong", ButtonKind.Secondary, () => clicked = "itswrong");
                Btn(btns, "Looks right", ButtonKind.Primary, () => clicked = "looksright");
            }
            else Btn(btns, "Close", ButtonKind.Primary, () => clicked = "close");

            yield return OpenModal(md);
            clicked = null;
            while (clicked == null || !(clicked == "close" || clicked == "dismiss" || clicked == "looksright" || clicked == "itswrong")) yield return null;
            if (clicked == "looksright")
            {
                r.Verdict = Verdict.ConfirmedByInstaller;
                confirmedByYou++;
                log.Add("amber_confirmed", pin.Label);
            }
            if (clicked == "itswrong")
            {
                r.Verdict = Verdict.Wrong;
                if (f.Fix == null)
                    f.Fix = new FixAction
                    {
                        Kind = FixKind.Connect, FromPin = r.Pin, ToPin = r.Pin, Colour = f.Expected,
                        Headline = "Re-seat " + WireColours.Lower(f.Expected) + " in " + pin.Print,
                        Text = "Re-seat the " + WireColours.Lower(f.Expected) + " wire in " + pin.Label + " and check again.",
                        Why = "You saw a problem the automation did not.",
                    };
                log.Add("amber_rejected", pin.Label);
            }
            if (atController) ApplyResult(r);
            else ApplyReaderVerdicts();
            DrawFixArrows();
            HighlightSnapshot(-1);
            yield return CloseModal(md);
        }

        // ================================================================== the camera screen

        void BuildCheckScreen()
        {
            ClearCheckViews();
            Clear(screen);
            camRoot = UiKit.Rect("Check", screen);
            Stretch(camRoot);

            // the camera view (a photo in the Wizard of Oz, the AR camera live) and the layer anchored on it
            markersRT = view.Attach(camRoot);
            if (view.SeeThrough) canvasBackground.enabled = false;   // the AR camera is drawn behind the UI

            var top = Img("TopShade", camRoot, sFadeDown, Hex("#000000", 0.35f), 0);
            Place(top.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -220), new Vector2(4000, 440));

            scanLine = UiKit.Rect("ScanLine", camRoot);
            scanLine.anchorMin = new Vector2(0, 1); scanLine.anchorMax = new Vector2(1, 1);
            scanLine.sizeDelta = new Vector2(0, 80);
            scanLine.gameObject.AddComponent<CanvasGroup>();
            Stretch(Img("Glow", scanLine, sSoft, Hex("#60A5FA", 0.30f), 0).rectTransform);
            Place(Img("Core", scanLine, null, Hex("#DBEAFE", 0.95f), 0).rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 3));
            scanLine.gameObject.SetActive(false);

            var fl = Img("Flash", camRoot, null, Color.white, 0);
            Stretch(fl.rectTransform);
            flashGroup = fl.gameObject.AddComponent<CanvasGroup>();
            flashGroup.alpha = 0; flashGroup.blocksRaycasts = false;

            var safe = UiKit.Rect("Safe", camRoot);
            ApplySafeArea(safe);

            // top: close, where am I, help
            var close = RoundButton(safe, Icon.Cross, ExitCheck);
            Place((RectTransform)close.transform.parent, new Vector2(0, 1), new Vector2(Gutter + 56, -96), new Vector2(112, 112));
            var help = RoundButton(safe, Icon.Info, () => Toast(HelpText()));
            Place((RectTransform)help.transform.parent, new Vector2(1, 1), new Vector2(-Gutter - 56, -96), new Vector2(112, 112));
            var tpHolder = UiKit.Rect("TitlePill", safe);
            Place(tpHolder, new Vector2(0.5f, 1), new Vector2(0, -96), new Vector2(560, 112));
            Shadow(tpHolder, 18, 0.18f, 6);   // shadow first, so the pill draws over it
            var tp = Img("Face", tpHolder, sRound, Surface, 44);
            Stretch(tp.rectTransform);
            titleText = Txt(tp.rectTransform, "", Small + 1, Ink, Face.Semi, TextAlignmentOptions.Center);
            Place(titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 16), new Vector2(520, 44));
            stepLine = Txt(tp.rectTransform, "", Caption - 2, Ink2, Face.Regular, TextAlignmentOptions.Center);
            Place(stepLine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -22), new Vector2(520, 36));
            stepLine.enableAutoSizing = true; stepLine.fontSizeMin = 18; stepLine.fontSizeMax = Caption - 2;

            var ins = Pill(safe, "Instruction", new Vector2(0.5f, 1), new Vector2(0, -232), new Vector2(0.5f, 0.5f), Surface);
            instructionDot = Img("Dot", ins, sCircle, Accent, 0); Fixed(instructionDot.rectTransform, 20, 20);
            instructionText = Txt(ins, "", Small, Ink, Face.Regular);
            instructionGroup = ins.gameObject.AddComponent<CanvasGroup>();
            instructionGroup.alpha = 0;

            // label detection frame and missing-label guide (anchored in the camera view)
            qrBox = UiKit.Rect("LabelBox", markersRT);
            qrBox.sizeDelta = new Vector2(300, 300);
            Corners(qrBox, Surface, 64 * style.markerScale, 8 * style.markerScale);
            var qrLbl = Pill(qrBox, "LabelText", new Vector2(0.5f, 1), new Vector2(0, 54), new Vector2(0.5f, 0.5f), Surface);
            qrLbl.localScale = Vector3.one * style.labelTextScale;
            Fixed(Img("Dot", qrLbl, sCircle, Ok, 0).rectTransform, 18, 18);
            qrCaption = Txt(qrLbl, cable != null ? cable.Display : "", Caption, Ink, Face.Semi);
            qrBox.gameObject.SetActive(false);

            missingGuide = UiKit.Rect("MissingLabel", markersRT);
            missingGuide.sizeDelta = new Vector2(330, 300);
            missingGuide.gameObject.AddComponent<CanvasGroup>();
            Dashed(missingGuide, 330, 300, Warn);
            var mg = Pill(missingGuide, "MissingText", new Vector2(0.5f, 0), new Vector2(0, -64), new Vector2(0.5f, 0.5f), WarnSoft);
            mg.localScale = Vector3.one * (style.labelTextScale / style.markerScale);
            Txt(mg, "Attach the label here", Small, WarnInk, Face.Semi);
            missingGuide.gameObject.SetActive(false);

            // zone hints, anchored on the modules: where the cable seems to be, and where its label says it goes
            zoneFrom = ZonePill("ZoneFrom", WarnSoft, WarnInk);
            zoneTo = ZonePill("ZoneTo", AccentSoft, Accent);

            BuildSheet(camRoot);
        }

        void BuildSheet(RectTransform parent)
        {
            sheet = UiKit.Rect("Sheet", parent);
            sheet.anchorMin = sheet.anchorMax = new Vector2(0.5f, 0);
            sheet.pivot = new Vector2(0.5f, 0);
            sheet.sizeDelta = new Vector2(1080, 400);
            Shadow(sheet, 40, 0.22f, -6);
            var bgi = Img("Background", sheet, sRound, Page, 52);
            Stretch(bgi.rectTransform, 0, -120, 0, 0);
            bgi.raycastTarget = true;   // taps on the sheet must not reach the markers behind it
            bgi.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var vl = sheet.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset((int)Gutter, (int)Gutter, 24, 64);
            vl.spacing = 24;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            sheet.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var handleRow = UiKit.Rect("Handle", sheet);
            handleRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 10;
            Place(Img("Bar", handleRow, sRound, Hairline, 5).rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 10));

            var body = VBox(sheet, 18);
            sheetGroup = body.gameObject.AddComponent<CanvasGroup>();
            sheetTitle = Txt(body, "", Title, Ink, Face.Semi);
            sheetSub = Txt(body, "", Body, Ink2);
            Space(body, 4);
            sheetContent = VBox(body, 14);
            sheetButtons = HRow(body, 136, 20);
            sheetButtons.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            sheet.gameObject.SetActive(false);
        }

        void Sheet(string title, string sub)
        {
            bool first = !sheet.gameObject.activeSelf;
            sheet.gameObject.SetActive(true);
            sheetTitle.text = title;
            sheetSub.text = sub;
            sheetSub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            Clear(sheetContent);
            Clear(sheetButtons);
            if (first) StartCoroutine(SlideUp(sheet, sheetGroup, 160, 0.3f));
            else StartCoroutine(Fade(sheetGroup, 0.3f, 1f, 0.2f));
        }

        Image ProgressLine(string label, bool indeterminate = false)
        {
            var row = VBox(sheetContent, 12);
            Txt(row, label, Small, Ink, Face.Regular);
            var track = Img("Track", row, sRound, Sunken, 6);
            track.gameObject.AddComponent<LayoutElement>().preferredHeight = 10;
            var fill = Img("Fill", track.rectTransform, sRound, Accent, 6);
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0;
            if (indeterminate) StartCoroutine(Indeterminate(fill));
            return fill;
        }

        IEnumerator Indeterminate(Image fill)
        {
            while (fill != null && fill.color != Ok)
            {
                fill.fillAmount = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
                yield return null;
            }
        }

        /// <summary>Eases to 90 % over the expected time and completes only when the work is done.</summary>
        IEnumerator ProgressUntil(Image bar, Func<bool> done, float expectedSeconds)
        {
            float t = 0, dur = expectedSeconds;
            while (!done())
            {
                if (bar != null) bar.fillAmount = 0.9f * Ease(t / dur);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Done(bar);
        }

        IEnumerator ProgressFor(Image bar, float seconds)
        {
            yield return Tween(seconds, k => { if (bar != null) bar.fillAmount = Ease(k); });
            Done(bar);
        }

        void Done(Image bar)
        {
            if (bar == null) return;
            bar.fillAmount = 1f;
            bar.color = Ok;
            var label = bar.transform.parent.parent.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null && !label.text.EndsWith(" · done")) label.text += " · done";
        }

        /// <summary>A flat list row with a thin divider under it: no box around each row, like a feed.</summary>
        RectTransform HCard(RectTransform parent, float height)
        {
            var outer = Img("Row", parent, null, new Color(1, 1, 1, 0), 0);
            var line = Img("Divider", outer.rectTransform, null, Hairline, 0);
            line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.pivot = new Vector2(0.5f, 0);
            line.rectTransform.offsetMin = new Vector2(68, 0); line.rectTransform.offsetMax = new Vector2(0, 2);
            var le = outer.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            var hl = outer.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(0, 4, 0, 0); hl.spacing = 24; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            return outer.rectTransform;
        }

        /// <summary>One tappable row: status badge, terminal name, wire swatch and colour, detail on the right.</summary>
        void Row(Mark state, string name, int colour, string colourName, string detail, Color detailColour, Action onTap)
        {
            var row = HCard(sheetContent, 108);
            MakeButton(row.GetComponent<Image>(), onTap);
            Badge(row, PinMarker.ColourOf(state), PinMarker.IconOf(state), 44, state == Mark.Check ? Ink : (Color?)null);
            Flex(Txt(row, name, Body, Ink, Face.Semi), 80);
            Swatch(row, colour, 30);
            Flex(Txt(row, colourName, Body, Ink), 180);
            Flex(Txt(row, detail, Small, detailColour, Face.Regular, TextAlignmentOptions.Right));
        }

        void ColourLine(RectTransform parent, string key, int colour, string note)
        {
            var row = HRow(parent, 84, 18);
            Flex(Txt(row, key, Body, Ink2), 210);
            Swatch(row, colour, 34);
            Flex(Txt(row, WireColours.Name(colour) + (string.IsNullOrEmpty(note) ? "" : "  <color=" + HexOf(Ink3) + ">" + note + "</color>"), Body, Ink));
        }

        void Callout(RectTransform parent, Color bg, Color fg, string lead, string text)
        {
            var box = Card(parent, 32, 8, bg, bg, 28);
            Txt(box, lead, Body, fg, Face.Semi);
            Txt(box, text, Body, Ink);
        }

        void SetTitle(string title, string step)
        {
            titleText.text = title;
            stepLine.text = step;
        }

        void Instruction(string text, Color dot)
        {
            instructionText.text = text;
            instructionDot.color = dot;
            if (instructionGroup.alpha < 1f) StartCoroutine(Fade(instructionGroup, 0, 1, 0.25f));
            StartCoroutine(Pop(instructionGroup.transform));
        }

        string HelpText()
        {
            if (results != null) return "Red: fix it as shown. Amber: tap and decide. Green: correct.";
            if (cable == null) return "Point the phone at the cable label.";
            if (ZoneOpen()) return "Move the cable to " + zone.Expected.Name + ", the module its label names, or tap \"Not this cable\".";
            if (BundleOpen()) return "Red: a wire that runs to another cable, not to this cable's label. Take it off, or tap \"They are this cable's\".";
            if (atController) return "Point the phone at the " + rule.Connector.Name + " block and the cable label.";
            return "Point the phone at the " + DeviceWord + "'s terminals and the cable label.";
        }


        string LabelWord() { return style.handwrittenLabels ? "cable label" : "QR label"; }
        static string OpinionWord(Opinion o) { return o == Opinion.Match ? "match" : o == Opinion.Mismatch ? "mismatch" : "no answer"; }

        void ShowMissingGuide(bool on)
        {
            PlaceAt(missingGuide, view.LabelHint);
            missingGuide.gameObject.SetActive(on);
        }

        static void PlaceAt(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(pos.x, 1f - pos.y);
            rt.anchoredPosition = Vector2.zero;
        }

        IEnumerator Shutter()
        {
            if (flashGroup == null) yield break;
            flashGroup.alpha = 0.8f;
            yield return Fade(flashGroup, 0.8f, 0, 0.35f);
        }

        void Corners(RectTransform parent, Color c, float len, float thick)
        {
            for (int i = 0; i < 4; i++)
            {
                var a = new Vector2(i % 2, i / 2);
                var horiz = Img("C", parent, sRound, c, thick * 0.5f);
                var vert = Img("C", parent, sRound, c, thick * 0.5f);
                horiz.rectTransform.anchorMin = horiz.rectTransform.anchorMax = a;
                vert.rectTransform.anchorMin = vert.rectTransform.anchorMax = a;
                horiz.rectTransform.pivot = a; vert.rectTransform.pivot = a;
                horiz.rectTransform.sizeDelta = new Vector2(len, thick);
                vert.rectTransform.sizeDelta = new Vector2(thick, len);
            }
        }

        void Dashed(RectTransform parent, float w, float h, Color c)
        {
            const float dash = 26, gap = 16, th = 6;
            for (float x = -w / 2; x < w / 2 - dash / 2; x += dash + gap)
            {
                Seg(parent, new Vector2(x + dash / 2, h / 2), new Vector2(dash, th), c);
                Seg(parent, new Vector2(x + dash / 2, -h / 2), new Vector2(dash, th), c);
            }
            for (float y = -h / 2; y < h / 2 - dash / 2; y += dash + gap)
            {
                Seg(parent, new Vector2(w / 2, y + dash / 2), new Vector2(th, dash), c);
                Seg(parent, new Vector2(-w / 2, y + dash / 2), new Vector2(th, dash), c);
            }
        }

        void Seg(RectTransform parent, Vector2 p, Vector2 size, Color c)
        {
            Place(Img("Dash", parent, sRound, c, 3).rectTransform, new Vector2(0.5f, 0.5f), p, size);
        }

        // ------------------------------------------------------------------ modal bottom sheet

        RectTransform ModalSheet()
        {
            modalOpen = true;
            var dim = Img("Modal", screen, null, Scrim, 0);
            Stretch(dim.rectTransform);
            MakeButton(dim, () => clicked = "dismiss");
            dim.gameObject.AddComponent<CanvasGroup>().alpha = 0;
            dim.rectTransform.SetAsLastSibling();
            var s = UiKit.Rect("Sheet", dim.rectTransform);
            s.anchorMin = s.anchorMax = new Vector2(0.5f, 0);
            s.pivot = new Vector2(0.5f, 0);
            s.sizeDelta = new Vector2(1080, 400);
            var bgi = Img("Background", s, sRound, Page, 52);
            Stretch(bgi.rectTransform, 0, -120, 0, 0);
            bgi.raycastTarget = true;
            bgi.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var vl = s.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset((int)Gutter, (int)Gutter, 24, 72);
            vl.spacing = 24;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            s.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var handleRow = UiKit.Rect("Handle", s);
            handleRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 10;
            Place(Img("Bar", handleRow, sRound, Hairline, 5).rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 10));
            s.gameObject.AddComponent<CanvasGroup>();
            return s;
        }

        IEnumerator OpenModal(RectTransform s)
        {
            StartCoroutine(Fade(s.parent.GetComponent<CanvasGroup>(), 0, 1, 0.2f));
            yield return SlideUp(s, s.GetComponent<CanvasGroup>(), 240, 0.28f);
        }

        IEnumerator CloseModal(RectTransform s)
        {
            yield return Fade(s.parent.GetComponent<CanvasGroup>(), 1, 0, 0.15f);
            Destroy(s.parent.gameObject);
            modalOpen = false;
            clicked = null;
        }

        IEnumerator WaitClickOrTap()
        {
            clicked = null; tapped = null;
            while ((clicked == null || clicked == "dismiss") && tapped == null) yield return null;
        }

        // ================================================================== zone check

        /// <summary>True while the zone check needs the installer: the cable seems to be elsewhere, or the module is out of view.</summary>
        bool ZoneOpen()
        {
            if (zone == null || !zone.NeedsAttention) return false;
            return !(zoneDismissed && zone.Status == ZoneStatus.Elsewhere);
        }

        /// <summary>The zone check speaks before the pins. 'afterCheck': at the second end (check again), else the first (read again).</summary>
        void ZoneSheet(bool afterCheck)
        {
            Sheet(zone.Headline, zone.Text);
            string again = afterCheck ? "recheck" : "rescan";
            if (zone.Status == ZoneStatus.Elsewhere)
            {
                Btn(sheetButtons, "Not this cable", ButtonKind.Secondary, () => clicked = "zone_ignore");
                Btn(sheetButtons, afterCheck ? "Moved · check again" : "Moved · read again", ButtonKind.Primary, () => clicked = again);
                Instruction(zone.ColoursMatch ? "Move the cable to " + zone.Expected.Name
                          : zone.Found != null ? "Cable on " + zone.Found.Name + "?" : zone.Expected.Name + " is empty", Warn);
            }
            else
            {
                Btn(sheetButtons, afterCheck ? "Check again" : "Read again", ButtonKind.Primary, () => clicked = again);
                Instruction("Point at " + zone.Expected.Name, Accent);
            }
        }

        /// <summary>
        /// Anchors the zone hints on the modules themselves: "Reader 2 · 4 wires" where the cable seems to be,
        /// "Reader 1 · move here" where its label says it goes.
        /// </summary>
        void ShowZone(ControllerReading rd, CameraFrame frame)
        {
            zoneShown = false;
            if (zone == null || zone.Status != ZoneStatus.Elsewhere || zoneFrom == null) return;
            if (zone.Found != null)
                foreach (var m in zone.Candidates)
                    if (m.Module == zone.Found && m.Centre.Valid) view.Anchor(CameraKeys.Module(m.Module.Id), m.Centre, frame);
            var here = ModuleReading.Mean(rd.Positions);
            if (here.Valid) view.Anchor(CameraKeys.Module(zone.Expected.Id), here, frame);
            zoneFrom.GetComponentInChildren<TextMeshProUGUI>().text = zone.Found != null
                ? zone.Found.Name + " · " + (zone.WiresFound == 1 ? "1 wire" : zone.WiresFound + " wires") : "";
            zoneTo.GetComponentInChildren<TextMeshProUGUI>().text = zone.Expected.Name + " · move here";
            zoneShown = true;
        }

        RectTransform ZonePill(string name, Color bg, Color fg)
        {
            var p = Pill(markersRT, name, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0.5f, 0.5f), bg);
            Txt(p, "", Small, fg, Face.Semi);
            p.gameObject.SetActive(false);
            return p;
        }

        void PlaceZonePill(RectTransform pill, string key, float inv)
        {
            if (pill == null) return;
            Vector2 p = Vector2.zero;
            bool show = zoneShown && key != null && view.TryGetPosition(key, out p);
            if (pill.gameObject.activeSelf != show) pill.gameObject.SetActive(show);
            if (!show) return;
            PlaceAt(pill, p);
            pill.localScale = Vector3.one * (style.labelTextScale * inv);
        }

        /// <summary>Modules that other cables' labels (scanned this session) plan for: the zone check never suggests them.</summary>
        List<string> ClaimedByOthers()
        {
            var list = new List<string>();
            foreach (var c in cables.Values)
                if (c != current && c.Label != null) list.Add(c.Label.ControllerModule.Code);
            return list;
        }

        void LogZone()
        {
            if (zone == null || zone.Status == ZoneStatus.NotSurveyed || zone.Status == ZoneStatus.InPlace) return;
            log.Add("zone", "status=" + zone.Status + "; expected=" + zone.Expected.Id + "; found=" + (zone.Found != null ? zone.Found.Id : "") +
                "; wires=" + zone.WiresFound + "; colours_match=" + zone.ColoursMatch);
        }

        // ================================================================== bundle check

        /// <summary>True while a wire traced to another cable waits for the installer.</summary>
        bool BundleOpen() { return bundle != null && bundle.NeedsAttention && !bundleDismissed; }

        /// <summary>The bundle check speaks before the pins. 'afterCheck': at the second end (check again), else read again.</summary>
        void BundleSheet(bool afterCheck)
        {
            Sheet(bundle.Headline, bundle.Text);
            Btn(sheetButtons, "They are this cable's", ButtonKind.Secondary, () => clicked = "bundle_ignore");
            Btn(sheetButtons, afterCheck ? "Taken off · check again" : "Taken off · read again", ButtonKind.Primary, () => clicked = afterCheck ? "recheck" : "rescan");
            Instruction(bundle.Foreign.Count == 1 ? "This wire is from another cable" : "These wires are from another cable", Bad);
        }

        /// <summary>Marks the wires traced to another cable in red on the camera view: "12V  other cable".</summary>
        void MarkForeign(int[] colours)
        {
            if (bundle == null || colours == null) return;
            foreach (int i in bundle.Foreign)
            {
                if (i < 0 || i >= marks.Count || i >= colours.Length) continue;
                var m = marks[i];
                if (m.Set(Mark.Wrong, colours[i], m.Name + "  other cable")) StartCoroutine(Pop(m.Vis));
            }
        }

        string BundleNames()
        {
            if (bundle == null) return "";
            var names = new List<string>();
            foreach (int i in bundle.Foreign) if (i >= 0 && i < marks.Count) names.Add(marks[i].Name);
            return string.Join(" ", names.ToArray());
        }

        void LogBundle()
        {
            if (bundle == null || bundle.Status == BundleStatus.NotTraced) return;
            log.Add("bundle", "status=" + bundle.Status + "; traced=" + bundle.Traced + "; foreign=" + BundleNames());
        }

        // ================================================================== markers

        /// <summary>One marker per device terminal (id = terminal index) or per controller pin (id = pin number).</summary>
        void BuildMarks(CableEnd end)
        {
            ClearMarks();
            marksEnd = end;
            int n = end == CableEnd.Controller ? rule.Connector.Count : rule.DeviceTerminals.Length;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                int id = end == CableEnd.Controller ? i + 1 : i;
                string name = end == CableEnd.Controller ? rule.Connector.Pin(i + 1).Print : rule.DeviceTerminals[i].Print;
                Vector2 pos;
                if (!view.TryGetPosition(CameraKeys.Of(end, i), out pos)) pos = new Vector2(0.5f, 0.5f);
                marks.Add(PinMarker.Build(markersRT, id, name, pos, style.markerScale, style.labelTextScale,
                    () => { if (!modalOpen) tapped = marks[idx]; }));
            }
        }

        void ClearMarks()
        {
            foreach (var m in marks) if (m.Root != null) Destroy(m.Root.gameObject);
            marks.Clear();
        }

        PinMarker MarkFor(int pin)
        {
            foreach (var m in marks) if (m.Id == pin) return m;
            return null;
        }

        PinResult ResultFor(int pin)
        {
            if (results == null) return null;
            foreach (var r in results) if (r.Pin == pin) return r;
            return null;
        }

        int Count(Verdict v)
        {
            int n = 0;
            if (results != null) foreach (var r in results) if (r.Verdict == v) n++;
            return n;
        }

        /// <summary>Rows follow the terminals' order on screen, so the list and the board share one layout.</summary>
        List<PinMarker> ScreenOrder(List<PinMarker> list)
        {
            var sorted = new List<PinMarker>(list);
            float sx = 0, sy = 0;
            if (sorted.Count > 1)
            {
                float x0 = 1, x1 = 0, y0 = 1, y1 = 0;
                foreach (var m in sorted) { x0 = Mathf.Min(x0, m.Pos.x); x1 = Mathf.Max(x1, m.Pos.x); y0 = Mathf.Min(y0, m.Pos.y); y1 = Mathf.Max(y1, m.Pos.y); }
                sx = x1 - x0; sy = y1 - y0;
            }
            bool byX = sx >= sy;
            sorted.Sort((a, b) => byX ? a.Pos.x.CompareTo(b.Pos.x) : a.Pos.y.CompareTo(b.Pos.y));
            return sorted;
        }

        List<PinResult> ScreenOrder(PinResult[] rs)
        {
            var ms = new List<PinMarker>();
            foreach (var r in rs) { var m = MarkFor(r.Pin); if (m != null) ms.Add(m); }
            var outList = new List<PinResult>();
            foreach (var m in ScreenOrder(ms)) outList.Add(ResultFor(m.Id));
            return outList;
        }

        // ------------------------------------------------------------------ snapshot panel (linked views)

        static UnityEngine.Rect CropRect(Vector2[] pts, Texture2D tex)
        {
            if (pts == null || pts.Length == 0 || tex == null) return new UnityEngine.Rect(0, 0, 1, 1);
            float x0 = 1, x1 = 0, y0 = 1, y1 = 0;
            foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
            float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            float aspect = tex.width / (float)tex.height;
            float w = Mathf.Max(x1 - x0 + 0.22f, 0.3f);
            float hgt = w * aspect * 3f / 4f;
            hgt = Mathf.Max(hgt, y1 - y0 + 0.12f);
            w = hgt / aspect * 4f / 3f;
            w = Mathf.Min(w, 1f); hgt = Mathf.Min(hgt, 1f);
            float u0 = Mathf.Clamp(cx - w / 2, 0, 1 - w), v0 = Mathf.Clamp(cy - hgt / 2, 0, 1 - hgt);
            return new UnityEngine.Rect(u0, 1f - v0 - hgt, w, hgt);
        }

        static Vector2[] UsedPins(Vector2[] pins, UsageRule r)
        {
            var list = new List<Vector2>();
            foreach (var p in r.Pairs) if (pins != null && p.ControllerPin - 1 < pins.Length) list.Add(pins[p.ControllerPin - 1]);
            return list.Count > 0 ? list.ToArray() : pins;
        }

        void UpdateCheck()
        {
            if (camRoot == null || view == null) return;
            view.Tick();
            float t = Time.unscaledTime;
            float inv = view.MarkerScale;

            // every marker sits where the view says its terminal is now; unknown or out of view = hidden
            for (int k = 0; k < marks.Count; k++)
            {
                var m = marks[k];
                Vector2 p;
                bool known = view.TryGetPosition(CameraKeys.Of(marksEnd, k), out p);
                if (known) m.MoveTo(p);
                bool show = known && m.State != Mark.Hidden;
                if (m.Root.gameObject.activeSelf != show) m.Root.gameObject.SetActive(show);
                m.Animate(t, inv);
            }
            foreach (var a in arrows) a.Update(markersRT, -tagSide, t, inv);
            PlaceZonePill(zoneFrom, zone != null && zone.Found != null ? CameraKeys.Module(zone.Found.Id) : null, inv);
            PlaceZonePill(zoneTo, zone != null ? CameraKeys.Module(zone.Expected.Id) : null, inv);

            if (scanLine != null && scanLine.gameObject.activeSelf)
            {
                float h = canvasRT.rect.height;
                float k = Mathf.Repeat(t * 0.55f, 1f);
                scanLine.anchoredPosition = new Vector2(0, -h * (0.14f + 0.5f * k));
                scanLine.GetComponent<CanvasGroup>().alpha = Mathf.Sin(k * Mathf.PI);
            }
            if (qrBox != null)
            {
                Vector2 lp;
                bool known = labelBoxOn && view.TryGetPosition(CameraKeys.Label, out lp);
                if (qrBox.gameObject.activeSelf != known) qrBox.gameObject.SetActive(known);
                if (known && view.TryGetPosition(CameraKeys.Label, out lp))
                {
                    PlaceAt(qrBox, lp);
                    // the frame covers the label; its corner strokes keep their size on screen
                    qrBox.sizeDelta = labelExtent.x > 0 && labelExtent.y > 0 ? Vector2.Scale(labelExtent, markersRT.rect.size) / inv : new Vector2(300, 300);
                    float pulse = 1f + 0.03f * Mathf.Sin(t * 5f);
                    qrBox.localScale = new Vector3(pulse * inv, pulse * inv, 1f);
                }
            }
            if (missingGuide != null && missingGuide.gameObject.activeSelf)
            {
                PlaceAt(missingGuide, view.LabelHint);
                missingGuide.localScale = Vector3.one * (style.markerScale * inv);
                missingGuide.GetComponent<CanvasGroup>().alpha = 0.65f + 0.35f * Mathf.Sin(t * 4f);
            }
            foreach (var ring in pulseRings)
            {
                if (ring == null || !ring.gameObject.activeInHierarchy) continue;
                float k = Mathf.Repeat(t * 1.4f, 1f);
                ring.localScale = Vector3.one * (1f + 0.6f * k);
                ring.GetComponent<Image>().color = Hex("#FFFFFF", 1f - k);
            }
            if (sheet != null) sheet.sizeDelta = new Vector2(Mathf.Min(canvasRT.rect.width, 1080f), sheet.sizeDelta.y);
        }

        void LateUpdateCheck()
        {
            if (canvasRT != null && markersRT != null && marks.Count > 0)
                tagSide = TagLayout.Apply(marks, canvasRT, FreeArea(), !atController && style.tagsBelowTerminals, style.markerScale, style.labelTextScale);
        }

        /// <summary>
        /// The part of the camera view that nothing covers: below the top controls and above the bottom sheet,
        /// with a margin at the sides. Tags are kept inside it, whatever the phone's aspect ratio.
        /// </summary>
        UnityEngine.Rect FreeArea()
        {
            UnityEngine.Rect r = canvasRT.rect;
            float top = r.yMax - SafeTop() - 290f;
            float bottom = r.yMin + (sheet != null && sheet.gameObject.activeSelf ? sheet.rect.height + 20f : 40f);
            if (top - bottom < 200f) bottom = top - 200f;
            return UnityEngine.Rect.MinMaxRect(r.xMin + 24f, bottom, r.xMax - 24f, top);
        }

        // ------------------------------------------------------------------ snapshot panel (linked views)

        /// <summary>The small view of the other end, top corner of the camera view. Tap to enlarge.</summary>
        void BuildSnapshot(CableEnd of)
        {
            if (snapshot != null) Destroy(snapshot.parent.gameObject);
            snapshotDots.Clear();
            var safe = UiKit.Rect("SnapshotSafe", camRoot);
            ApplySafeArea(safe);
            safe.SetSiblingIndex(sheet.GetSiblingIndex());
            snapshot = UiKit.Rect("Snapshot", safe);
            snapshot.anchorMin = snapshot.anchorMax = new Vector2(style.snapshotOnLeft ? 0 : 1, 1);
            snapshot.pivot = snapshot.anchorMax;
            snapshot.anchoredPosition = new Vector2(style.snapshotOnLeft ? style.snapshotInset : -style.snapshotInset, -style.snapshotTopInset);
            snapshot.sizeDelta = new Vector2(360, 320);
            snapshotExpanded = false;
            snapshotGroup = snapshot.gameObject.AddComponent<CanvasGroup>();
            Shadow(snapshot, 22, 0.22f, 8);
            var frame = Img("Frame", snapshot, sRound, Surface, 28);
            Stretch(frame.rectTransform);
            MakeButton(frame, ToggleSnapshot);
            var title = Txt(snapshot, EndTitle(of), 24, Ink2, Face.Semi);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(22, -26), new Vector2(320, 30), new Vector2(0, 0.5f));
            var holder = UiKit.Rect("Image", snapshot);
            Stretch(holder, 12, 12, 12, 50);
            BuildCrop(holder, of, -1, snapshotDots);
            snapshotHighlight = Img("Highlight", snapshot, sRing, Color.white, 0).rectTransform;
            snapshotHighlight.sizeDelta = new Vector2(70, 70);
            snapshotHighlight.gameObject.SetActive(false);
            pulseRings.Add(snapshotHighlight);
            snapshot.gameObject.SetActive(false);
        }

        /// <summary>
        /// A crop of one end's snapshot with a dot per wire. Device end: one dot per terminal (id = terminal index).
        /// Controller end: one dot per pin that holds a wire in the saved record (id = pin number).
        /// </summary>
        void BuildCrop(RectTransform holder, CableEnd of, int highlight, Dictionary<int, RectTransform> dotsOut)
        {
            holder.gameObject.AddComponent<RectMask2D>();
            var round = Img("Back", holder, sRound, Hex("#111111"), 20);
            Stretch(round.rectTransform);

            var shot = current == null ? null : of == CableEnd.Device ? current.DeviceShot : current.ControllerShot;
            if (shot == null || shot.Image == null)
            {
                var none = Txt(holder, "No image of the " + EndWord(of) + " end", Small, Hex("#D1D5DB"), Face.Regular, TextAlignmentOptions.Center);
                Stretch(none.rectTransform, 24, 0, 24, 0);
                return;
            }
            Texture2D tex = shot.Image;
            var ids = new List<int>(); var pos = new List<Vector2>(); var names = new List<string>(); var colours = new List<int>();
            if (of == CableEnd.Device)
            {
                for (int i = 0; i < rule.DeviceTerminals.Length; i++)
                {
                    ids.Add(i);
                    pos.Add(Get(shot.Points, i, Vector2.one * 0.5f));
                    names.Add(rule.DeviceTerminals[i].Print);
                    colours.Add(record != null ? record.Colours[i] : WireColours.None);
                }
            }
            else
            {
                for (int p = 1; p <= rule.Connector.Count; p++)
                {
                    int c = ctrlRecord != null ? ctrlRecord.PinColours[p - 1] : WireColours.None;
                    if (c == WireColours.None) continue;
                    ids.Add(p);
                    pos.Add(Get(shot.Points, p - 1, Vector2.one * 0.5f));
                    names.Add(rule.Connector.Pin(p).Print);
                    colours.Add(c);
                }
            }
            var uv = CropRect(pos.ToArray(), tex);
            var img = new GameObject("Crop", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(holder, false);
            img.raycastTarget = false;
            img.texture = tex;
            img.uvRect = uv;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 4f / 3f;
            var root = UiKit.Rect("Dots", img.rectTransform);
            Stretch(root);
            for (int k = 0; k < ids.Count; k++)
            {
                Vector2 p = pos[k];
                float u = (p.x - uv.x) / uv.width;
                float vv = ((1f - p.y) - uv.y) / uv.height;
                var dot = UiKit.Rect("Dot " + names[k], root);
                dot.anchorMin = dot.anchorMax = new Vector2(u, vv);
                Img("Ring", dot, sCircle, Surface, 0).rectTransform.sizeDelta = new Vector2(30, 30);
                Img("Sw", dot, sCircle, colours[k] < 0 ? Ink3 : Wire[Mathf.Clamp(colours[k], 0, Wire.Length - 1)], 0).rectTransform.sizeDelta = new Vector2(22, 22);
                var tag = Img("Tag", dot, sRound, Surface, 14);
                Place(tag.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -36), new Vector2(names[k].Length > 2 ? 64 : 46, 32));
                var nm = Txt(tag.rectTransform, names[k], 22, Ink, Face.Semi, TextAlignmentOptions.Center);
                Stretch(nm.rectTransform);
                if (dotsOut != null) dotsOut[ids[k]] = dot;
                if (ids[k] == highlight)
                {
                    var hl = Img("Highlight", dot, sRing, Color.white, 0).rectTransform;
                    hl.sizeDelta = new Vector2(70, 70);
                    pulseRings.Add(hl);
                }
            }
        }

        void ShowSnapshot(bool on)
        {
            if (snapshot == null) return;
            snapshot.gameObject.SetActive(on);
            if (on) { StartCoroutine(Fade(snapshotGroup, 0, 1, 0.3f)); StartCoroutine(Pop(snapshot)); }
        }

        void ToggleSnapshot()
        {
            snapshotExpanded = !snapshotExpanded;
            float inset = snapshotExpanded ? Gutter : style.snapshotInset;
            log.Add("snapshot_" + (snapshotExpanded ? "open" : "close"));
            StartCoroutine(ResizeSnapshot(snapshotExpanded ? new Vector2(984, 820) : new Vector2(360, 320),
                new Vector2(style.snapshotOnLeft ? inset : -inset, snapshotExpanded ? -300 : -style.snapshotTopInset)));
        }

        IEnumerator ResizeSnapshot(Vector2 size, Vector2 pos)
        {
            Vector2 s0 = snapshot.sizeDelta, p0 = snapshot.anchoredPosition;
            snapshot.parent.SetAsLastSibling();
            yield return Tween(0.3f, k =>
            {
                float e = Ease(k);
                snapshot.sizeDelta = Vector2.Lerp(s0, size, e);
                snapshot.anchoredPosition = Vector2.Lerp(p0, pos, e);
            });
            if (!snapshotExpanded) snapshot.parent.SetSiblingIndex(Mathf.Max(0, sheet.GetSiblingIndex() - 1));
        }

        /// <summary>Rings one dot of the other end: a terminal index (reader end) or a pin number (controller end); -1 clears.</summary>
        void HighlightSnapshot(int id)
        {
            if (snapshotHighlight == null) return;
            RectTransform dot;
            if (id < 0 || !snapshotDots.TryGetValue(id, out dot)) { snapshotHighlight.gameObject.SetActive(false); return; }
            snapshotHighlight.SetParent(dot, false);
            snapshotHighlight.anchoredPosition = Vector2.zero;
            snapshotHighlight.gameObject.SetActive(true);
        }
    }
}
