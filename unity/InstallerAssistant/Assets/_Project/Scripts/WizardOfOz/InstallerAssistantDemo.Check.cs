using System;
using System.Collections;
using System.Collections.Generic;
using InstallerAssistant.AI;
using InstallerAssistant.Labels;
using InstallerAssistant.Records;
using InstallerAssistant.UI;
using InstallerAssistant.Verification;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static InstallerAssistant.UI.UiKit;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// The check itself, on the camera view. The installer starts at whichever end of the cable is in front of
    /// them. The label tells the app which cable it is and which end; the app records that end, sends the installer
    /// to the other end, pairs the two scans by the label, and shows the verdict where the installer stands.
    /// Fixes are always shown on the controller block, where the A1610 defines what each pin does.
    ///
    /// Light floating controls over the camera image, one bottom sheet that always says what to do next,
    /// and markers, tags and fix arrows anchored on the terminals.
    /// </summary>
    public partial class InstallerAssistantDemo
    {
        // ------------------------------------------------------------------ view state of the check

        bool atController, checkAborted, needsFixAtController;
        DemoLayout.PhotoView view;
        readonly Dictionary<DemoLayout.PhotoView, Texture2D> mockCache = new Dictionary<DemoLayout.PhotoView, Texture2D>();
        readonly List<PinMarker> marks = new List<PinMarker>();
        readonly List<FixArrow> arrows = new List<FixArrow>();
        readonly Dictionary<int, RectTransform> snapshotDots = new Dictionary<int, RectTransform>();
        readonly List<RectTransform> pulseRings = new List<RectTransform>();
        readonly List<PinMarker> hiddenBeforeCalibration = new List<PinMarker>();
        int tagSide;
        PinMarker tapped;
        bool modalOpen, calibrating, snapshotExpanded;
        int calibStep;
        Vector2 calibPin1;

        RectTransform camRoot, mover, photoRT, markersRT, scanLine, qrBox, missingGuide;
        RawImage photo;
        AspectRatioFitter photoFit;
        TextMeshProUGUI titleText, stepLine, instructionText, sheetTitle, sheetSub, qrCaption;
        Image instructionDot;
        CanvasGroup instructionGroup, sheetGroup, snapshotGroup, flashGroup;
        RectTransform sheet, sheetContent, sheetButtons, snapshot, snapshotHighlight;

        void ClearCheckViews()
        {
            foreach (var a in arrows) a.Destroy();
            arrows.Clear(); marks.Clear(); snapshotDots.Clear(); pulseRings.Clear(); hiddenBeforeCalibration.Clear();
            camRoot = mover = photoRT = markersRT = scanLine = qrBox = missingGuide = null;
            sheet = sheetContent = sheetButtons = snapshot = snapshotHighlight = null;
            photo = null; view = null; qrCaption = null;
            modalOpen = false; snapshotExpanded = false;
            if (calibrating) { calibrating = false; if (calibGroup != null) calibGroup.gameObject.SetActive(false); }
        }

        // ------------------------------------------------------------------ words the app derives from the label

        static CableEnd Other(CableEnd e) { return e == CableEnd.Controller ? CableEnd.Device : CableEnd.Controller; }
        string DeviceWord { get { return cable != null ? cable.Usage.ToLowerInvariant() : "reader"; } }
        string EndWord(CableEnd e) { return e == CableEnd.Controller ? "controller" : DeviceWord; }
        string EndTitle(CableEnd e) { return e == CableEnd.Controller ? "Controller end" : char.ToUpperInvariant(DeviceWord[0]) + DeviceWord.Substring(1) + " end"; }
        string CableTitle { get { return cable.DoorName + " " + DeviceWord; } }
        /// <summary>Where the given end is, from the label and the wiring rule: the installer never has to look it up.</summary>
        string WhereIs(CableEnd e)
        {
            return e == CableEnd.Controller ? Short(rule.ControllerModel) + " · " + rule.Connector.Name + " block"
                                            : cable.DoorName + " · " + Short(rule.DeviceModel) + " " + DeviceWord;
        }
        /// <summary>The end to scan next when the installer presses "Scan a cable".</summary>
        CableEnd NextEnd()
        {
            if (record != null && ctrlRecord == null) return CableEnd.Controller;
            if (ctrlRecord != null && record == null) return CableEnd.Device;
            if (record != null && ctrlRecord != null) return CableEnd.Controller;
            return demoStart;
        }

        // ================================================================== start, flow, exit

        /// <param name="end">The end the installer is at now.</param>
        /// <param name="fillOtherEnd">Operator shortcut: pretend the other end was already recorded as configured.</param>
        void StartCheck(CableEnd end, bool fillOtherEnd = false)
        {
            StopAllCoroutines();
            toastCo = null;
            world = new WozWorld { Layout = L, Scenario = Scenario, Speed = speed, LabelAttached = !plantMissingLabel };
            labels = new WozLabelScanner(world);
            wires = new WozWireReader(world);
            secondOpinion = new WozSecondOpinion(world);
            log = new SessionLog(() => Time.unscaledTime - startTime, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            check = null; results = null;
            fixesOnFirstCheck = 0; confirmedByYou = 0; firstFixText = null; needsFixAtController = false;
            checkAborted = false; clicked = null; tapped = null;
            readColours = null; readConfidence = null; readEdited = null;

            if (fillOtherEnd) FillEnd(Other(end));
            world.Record = record;
            if (ctrlRecord != null) world.LabelAttached = true;   // the controller label was already scanned on an earlier visit

            BuildCheckScreen();
            startTime = Time.unscaledTime;
            log.Add("start", "scenario=" + Scenario.name + "; at=" + CableLabel.EndCode(end) +
                "; saved=" + (record != null ? "DEV" : "") + (ctrlRecord != null ? "CTRL" : "") + "; second_opinion=" + useSecondOpinion);
            StartCoroutine(CheckFlow(end));
        }

        /// <summary>Operator shortcut: records one end as the layout describes it, without the camera.</summary>
        void FillEnd(CableEnd e)
        {
            var now = DateTime.Now;
            if (e == CableEnd.Device)
            {
                int n = rule.DeviceTerminals.Length;
                var c = new int[n];
                for (int i = 0; i < n; i++) c[i] = Mathf.Clamp(Get(L.readerColours, i, i), 0, WireColours.Names.Length - 1);
                record = new CableRecord(cable.AtEnd(CableEnd.Device), rule, c, new bool[n], now);
                records.Save(record);
            }
            else
            {
                int n = rule.Connector.Count;
                var pins = new int[n]; var conf = new float[n];
                for (int p = 0; p < n; p++) { pins[p] = Get(Scenario.pinColours, p, WireColours.None); conf[p] = 0.95f; }
                ctrlRecord = new ControllerRecord(cable.AtEnd(CableEnd.Controller), rule, pins, conf, new bool[n], now);
                records.Save(ctrlRecord);
            }
            MarkOneEnd(e, now);
        }

        void MarkOneEnd(CableEnd e, DateTime at)
        {
            if (liveCable.State == CableState.Verified) return;
            if (liveCable.State != CableState.OneEnd) { liveCable.FirstEnd = e; liveCable.SavedAt = at; }
            liveCable.State = CableState.OneEnd;
        }

        IEnumerator CheckFlow(CableEnd end)
        {
            bool otherEndSaved = end == CableEnd.Controller ? record != null : ctrlRecord != null;
            if (!otherEndSaved)
            {
                // first end: find out what this is, and record it
                if (end == CableEnd.Device) yield return RecordReaderEnd();
                else yield return RecordControllerEnd();
                if (checkAborted) yield break;
                yield return WalkScreen(Other(end), false);
                if (checkAborted) yield break;
                end = Other(end);
            }

            // second end: pair by the label, check, fix
            if (end == CableEnd.Controller) yield return ControllerSecond();
            else
            {
                yield return ReaderSecond();
                if (checkAborted) yield break;
                if (needsFixAtController)
                {
                    yield return WalkScreen(CableEnd.Controller, true);
                    if (checkAborted) yield break;
                    yield return ControllerFix();
                }
            }
            if (checkAborted || pendingRestart >= 0) yield break;

            float secs = Time.unscaledTime - startTime;
            liveCable.State = CableState.Verified;
            liveCable.When = "today";
            var rep = new Report
            {
                Cable = liveCable, At = DateTime.Now, Fixes = fixesOnFirstCheck, CheckedByYou = confirmedByYou,
                Wires = rule.DeviceTerminals.Length, Seconds = secs, Live = true,
                FixText = firstFixText ?? "No fixes needed: both ends match.",
            };
            reports.RemoveAll(x => x.Cable == liveCable);
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

        /// <summary>Shows the camera on this end and scans its label. The label says which cable and which end.</summary>
        IEnumerator Arrive(CableEnd end, string step, string why)
        {
            atController = end == CableEnd.Controller;
            world.AtController = atController;
            readColours = null; readConfidence = null; readEdited = null;   // a new end: nothing read here yet
            foreach (var a in arrows) a.Destroy();
            arrows.Clear();
            if (atController)
            {
                var sc = Scenario;
                bool noLabelPhoto = !world.LabelAttached && (sc.noLabel.photo != null || L.UsesMockImages);
                SetView(noLabelPhoto ? sc.noLabel : sc.wiring,
                        noLabelPhoto ? ControllerTexture(sc.noLabel, sc.pinColours, false) : ControllerTexture(sc.wiring, sc.pinColours, true));
                BuildControllerMarks();
            }
            else
            {
                SetView(L.reader, ReaderTexture());
                BuildReaderMarks();
            }
            SetTitle(EndTitle(end), step);
            yield return ScanLabel(why);
        }

        IEnumerator ScanLabel(string why)
        {
            var sc = Scenario;
            while (true)
            {
                Instruction("Point the camera at the cable label", Accent);
                Sheet("Scan the cable label", why);
                ProgressLine("Looking for the " + LabelWord(), true);
                LabelScan scan = null;
                scanLine.gameObject.SetActive(true);
                yield return labels.Scan(s => scan = s);
                scanLine.gameObject.SetActive(false);
                if (scan.Status != LabelScanStatus.Found)
                {
                    // without the label there is no pairing, so the app asks for it instead of guessing
                    log.Add("label_missing");
                    Instruction("No label on this cable", Warn);
                    ShowMissingGuide(true);
                    bool known = record != null || ctrlRecord != null;
                    Sheet("Label missing", "The app can't tell which cable this is. Attach " +
                        (known ? "the label " + cable.Display : "the cable's label") + " to this end, then scan again.");
                    Btn(sheetButtons, "Label attached · scan again", ButtonKind.Primary, () => clicked = "labelled");
                    yield return WaitClick();
                    ShowMissingGuide(false);
                    world.LabelAttached = true;
                    if (atController) SetView(sc.wiring, ControllerTexture(sc.wiring, sc.pinColours, true));
                    continue;
                }
                cable = scan.Label;
                var r = AxisCatalog.Lookup(cable.Usage);
                if (r != null) rule = r;
                if (qrCaption != null) qrCaption.text = cable.Display;
                ShowQr(view.labelPos);
                yield return Wait(0.25f);
                Instruction("Label found · hold steady", Ok);
                log.Add("label_found", cable.Display + " · " + CableLabel.EndCode(cable.End));
                yield return Wait(0.9f);
                qrBox.gameObject.SetActive(false);
                yield break;
            }
        }

        // ================================================================== first end: record what is there

        IEnumerator RecordReaderEnd()
        {
            yield return Arrive(CableEnd.Device, "End 1 of 2", "The label tells the app which cable this is and which end you are at.");
            SetTitle(EndTitle(CableEnd.Device), CableTitle + " · end 1 of 2");
            yield return ConfirmWires("Save " + DeviceWord + " end");

            record = new CableRecord(cable, rule, (int[])readColours.Clone(), (bool[])readEdited.Clone(), DateTime.Now);
            records.Save(record);
            world.Record = record;
            int edits = CountEdits();
            confirmedByYou += edits;
            log.Add("reader_saved", "colours=" + string.Join(" ", Array.ConvertAll(readColours, WireColours.Name)) + "; edited=" + edits);
            MarkOneEnd(CableEnd.Device, DateTime.Now);
            yield return Shutter();
            Toast(EndTitle(CableEnd.Device) + " saved");
            yield return Wait(0.6f);
        }

        IEnumerator RecordControllerEnd()
        {
            yield return Arrive(CableEnd.Controller, "End 1 of 2", "The label tells the app which cable this is and which end you are at.");
            SetTitle(EndTitle(CableEnd.Controller), CableTitle + " · end 1 of 2");
            yield return ConfirmWires("Save controller end");

            ctrlRecord = new ControllerRecord(cable, rule, (int[])readColours.Clone(), (float[])readConfidence.Clone(), (bool[])readEdited.Clone(), DateTime.Now);
            records.Save(ctrlRecord);
            int edits = CountEdits();
            confirmedByYou += edits;
            log.Add("controller_saved", "pins=" + string.Join(" ", Array.ConvertAll(readColours, WireColours.Name)) + "; edited=" + edits);
            MarkOneEnd(CableEnd.Controller, DateTime.Now);
            yield return Shutter();
            Toast("Controller end saved");
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
                if (clicked == "rescan")
                {
                    log.Add("rescan");
                    Sheet("Reading again", "Hold the phone still for a moment.");
                    var b2 = ProgressLine(ReadingWhat());
                    yield return ReadWires(b2);
                    continue;
                }
                if (clicked == "save")
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
            foreach (var m in marks) if (m.State != Mark.Edited) m.Set(atController ? Mark.Hidden : Mark.Reading, m.Colour, null);
            int[] colours = null; float[] conf = null;
            scanLine.gameObject.SetActive(true);
            if (atController) StartCoroutine(wires.ReadControllerEnd(rule, r => { colours = r.Colours; conf = r.Confidence; }));
            else StartCoroutine(wires.ReadDeviceEnd(rule, r => { colours = r.Colours; conf = r.Confidence; }));
            yield return ProgressUntil(bar, () => colours != null, 2.4f);
            scanLine.gameObject.SetActive(false);

            int n = marks.Count;
            if (readColours == null || readColours.Length != n)
            {
                readColours = new int[n]; readConfidence = new float[n]; readEdited = new bool[n];
            }
            for (int i = 0; i < n; i++)
            {
                if (!readEdited[i]) { readColours[i] = colours[i]; readConfidence[i] = conf[i]; }
                SetReadMark(i);
            }
            log.Add(atController ? "controller_read" : "reader_read", "unsure=" + marks.FindAll(x => x.State == Mark.Check).Count);
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
            yield return Arrive(CableEnd.Controller, "End 2 of 2", "The label pairs this end with the " + DeviceWord + " end you saved.");
            SetTitle(EndTitle(CableEnd.Controller), CableTitle + " · end 2 of 2");
            BuildSnapshot(CableEnd.Device);
            ShowSnapshot(true);
            yield return RunCheck(true);
            yield return ControllerResultsLoop();
        }

        /// <summary>Back at the controller after the check at the reader found a fault: the same verdicts, now on the pins.</summary>
        IEnumerator ControllerFix()
        {
            yield return Arrive(CableEnd.Controller, "Fix", "Scan the label to show the fix on this block.");
            SetTitle(EndTitle(CableEnd.Controller), CableTitle + " · fix");
            BuildSnapshot(CableEnd.Device);
            ShowSnapshot(true);
            foreach (var m in marks) m.Set(Mark.Hidden, -1, null);
            foreach (var r in results) ApplyResult(r);
            DrawFixArrows();
            log.Add("fix_shown_at_controller");
            yield return ControllerResultsLoop();
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
                if (clicked == "recheck")
                {
                    world.Rewired = true;   // Wizard of Oz: the operator has re-wired the bench
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
            if (!first) SetView(L.controllerFixed, ControllerTexture(L.controllerFixed, null, true));

            Sheet(first ? "Checking the wiring" : "Checking again", "Comparing " + rule.Connector.Name + " with what you saved at the " + DeviceWord + ".");
            var readBar = ProgressLine(ReadingWhat());
            var rulesBar = ProgressLine("Comparing with the " + DeviceWord + " end");
            var aiBar = useSecondOpinion ? ProgressLine("AI second opinion") : null;
            Instruction("Hold steady", Accent);
            foreach (var m in marks) m.Set(record.ExpectedOnPin(m.Id) != WireColours.None ? Mark.Reading : Mark.Hidden, -1, null);

            ControllerReading rd = null;
            Opinion[] ops = null;
            scanLine.gameObject.SetActive(true);
            StartCoroutine(wires.ReadControllerEnd(rule, r => rd = r));
            if (useSecondOpinion)
            {
                StartCoroutine(secondOpinion.Compare(record, o => ops = o));    // layer 3 runs in parallel, independently
                StartCoroutine(ProgressUntil(aiBar, () => ops != null, 2.8f));
            }
            yield return ProgressUntil(readBar, () => rd != null, 2.2f);
            scanLine.gameObject.SetActive(false);
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

        void DrawFixArrows()
        {
            foreach (var a in arrows) a.Destroy();
            arrows.Clear();
            if (!atController) return;
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
            yield return Arrive(CableEnd.Device, "End 2 of 2", "The label pairs this end with the controller end you saved.");
            SetTitle(EndTitle(CableEnd.Device), CableTitle + " · end 2 of 2");
            BuildSnapshot(CableEnd.Controller);
            ShowSnapshot(true);
            yield return ConfirmWires("Check the wiring");

            record = new CableRecord(cable, rule, (int[])readColours.Clone(), (bool[])readEdited.Clone(), DateTime.Now);
            records.Save(record);
            world.Record = record;
            confirmedByYou += CountEdits();
            log.Add("reader_saved", "colours=" + string.Join(" ", Array.ConvertAll(readColours, WireColours.Name)));
            yield return RunCheckAtReader();

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
                if (clicked == "finish") yield break;
            }
        }

        int PinOfTerminal(int terminalIndex) { return rule.PinFor(rule.DeviceTerminals[terminalIndex].Id); }

        /// <summary>The same rule check and verdict table, run against the controller end saved earlier.</summary>
        IEnumerator RunCheckAtReader()
        {
            Sheet("Checking the wiring", "Comparing the " + DeviceWord + " end with the controller end you saved.");
            var rulesBar = ProgressLine("Comparing with the controller end");
            var aiBar = useSecondOpinion ? ProgressLine("AI second opinion") : null;
            Instruction("Hold steady", Accent);
            for (int i = 0; i < marks.Count; i++) marks[i].Set(Mark.Reading, record.Colours[i], null);

            var rd = ctrlRecord.AsReading();
            Opinion[] ops = null;
            if (useSecondOpinion)
            {
                StartCoroutine(secondOpinion.Compare(record, o => ops = o));
                StartCoroutine(ProgressUntil(aiBar, () => ops != null, 2.8f));
            }
            check = RuleChecker.Check(record, rd);
            yield return ProgressFor(rulesBar, 0.6f);
            if (useSecondOpinion) { while (ops == null) yield return null; }
            else ops = RulesOnly();

            results = VerdictCombiner.Evaluate(check, rd, ops, confidenceThreshold);
            ApplyReaderVerdicts();
            NoteFirstFixes(true);
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
            if (wrong > 0)
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

            if (wrong > 0) Btn(sheetButtons, "Go to the controller", ButtonKind.Primary, () => clicked = "fix");
            else if (amber == 0) Btn(sheetButtons, "Finish", ButtonKind.Primary, () => clicked = "finish");

            if (wrong > 0) Instruction("Wiring does not match the controller end", Bad);
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
                Callout(md, BadSoft, Bad, atController ? "Fix" : "Fix at the controller", f.Fix.Text + (f.Fix.TouchesPower ? " Do this with the power off." : ""));
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
            if (atController) { ApplyResult(r); DrawFixArrows(); }
            else ApplyReaderVerdicts();
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

            // camera view (a photo here; the AR camera background in the app)
            var bg = Img("Camera", camRoot, null, Hex("#111111"), 0);
            Stretch(bg.rectTransform);
            bg.gameObject.AddComponent<RectMask2D>();
            mover = UiKit.Rect("Mover", bg.rectTransform);
            Stretch(mover);
            photo = new GameObject("Photo", typeof(RectTransform)).AddComponent<RawImage>();
            photo.transform.SetParent(mover, false);
            photo.raycastTarget = false;
            photoRT = photo.rectTransform;
            Stretch(photoRT);
            photoFit = photo.gameObject.AddComponent<AspectRatioFitter>();
            photoFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            markersRT = UiKit.Rect("Markers", photoRT);
            Stretch(markersRT);

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

            // label detection frame and missing-label guide (anchored on the photo)
            qrBox = UiKit.Rect("LabelBox", markersRT);
            qrBox.sizeDelta = new Vector2(300, 300);
            Corners(qrBox, Surface, 64 * L.markerScale, 8 * L.markerScale);
            var qrLbl = Pill(qrBox, "LabelText", new Vector2(0.5f, 1), new Vector2(0, 54), new Vector2(0.5f, 0.5f), Surface);
            qrLbl.localScale = Vector3.one * L.labelTextScale;
            Fixed(Img("Dot", qrLbl, sCircle, Ok, 0).rectTransform, 18, 18);
            qrCaption = Txt(qrLbl, cable.Display, Caption, Ink, Face.Semi);
            qrBox.gameObject.SetActive(false);

            missingGuide = UiKit.Rect("MissingLabel", markersRT);
            missingGuide.sizeDelta = new Vector2(330, 300);
            missingGuide.gameObject.AddComponent<CanvasGroup>();
            Dashed(missingGuide, 330, 300, Warn);
            var mg = Pill(missingGuide, "MissingText", new Vector2(0.5f, 0), new Vector2(0, -64), new Vector2(0.5f, 0.5f), WarnSoft);
            mg.localScale = Vector3.one * (L.labelTextScale / L.markerScale);
            Txt(mg, "Attach the label here", Small, WarnInk, Face.Semi);
            missingGuide.gameObject.SetActive(false);

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
            float t = 0, dur = expectedSeconds / Mathf.Max(0.1f, speed);
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
            if (atController) return "Point the phone at the " + rule.Connector.Name + " block and the cable label.";
            return "Point the phone at the " + DeviceWord + "'s terminals and the cable label.";
        }

        string LabelWord() { return L.handwrittenLabels ? "cable label" : "QR label"; }
        static string OpinionWord(Opinion o) { return o == Opinion.Match ? "match" : o == Opinion.Mismatch ? "mismatch" : "no answer"; }

        void ShowQr(Vector2 pos)
        {
            qrBox.anchorMin = qrBox.anchorMax = new Vector2(pos.x, 1f - pos.y);
            qrBox.anchoredPosition = Vector2.zero;
            var caption = (RectTransform)qrBox.Find("LabelText");
            float edge = pos.x > 0.65f ? 1f : pos.x < 0.35f ? 0f : 0.5f;
            caption.anchorMin = caption.anchorMax = new Vector2(edge, 1);
            caption.pivot = new Vector2(edge, 0.5f);
            qrBox.gameObject.SetActive(true);
        }

        void ShowMissingGuide(bool on)
        {
            Vector2 pos = view.labelPos;
            missingGuide.anchorMin = missingGuide.anchorMax = new Vector2(pos.x, 1f - pos.y);
            missingGuide.anchoredPosition = Vector2.zero;
            missingGuide.gameObject.SetActive(on);
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

        // ================================================================== markers and photos

        void BuildReaderMarks()
        {
            ClearMarks();
            for (int i = 0; i < rule.DeviceTerminals.Length; i++)
            {
                int id = i;
                marks.Add(PinMarker.Build(markersRT, id, rule.DeviceTerminals[i].Print, Get(L.reader.points, i, new Vector2(0.4f + 0.06f * i, 0.42f)),
                    L.markerScale, L.labelTextScale, () => { if (!modalOpen && !calibrating) tapped = marks[id]; }));
            }
        }

        void BuildControllerMarks()
        {
            ClearMarks();
            for (int p = 1; p <= rule.Connector.Count; p++)
            {
                int idx = p - 1;
                marks.Add(PinMarker.Build(markersRT, p, rule.Connector.Pin(p).Print, Get(view.points, idx, new Vector2(0.3f + 0.05f * idx, 0.45f)),
                    L.markerScale, L.labelTextScale, () => { if (!modalOpen && !calibrating) tapped = marks[idx]; }));
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

        Texture2D ReaderTexture()
        {
            if (L.reader.photo != null) return L.reader.photo;
            Texture2D t;
            if (!mockCache.TryGetValue(L.reader, out t))
            {
                var c = new Color[4];
                for (int i = 0; i < 4; i++) c[i] = Wire[Mathf.Clamp(Get(L.readerColours, i, i), 0, Wire.Length - 1)];
                t = MockPhotos.Reader(c);
                mockCache[L.reader] = t;
            }
            return t;
        }

        /// <summary>Photo of a controller view; with no photo, a mock showing pinColours (null = correctly wired).</summary>
        Texture2D ControllerTexture(DemoLayout.PhotoView v, int[] pinColours, bool withLabel)
        {
            if (v.photo != null) return v.photo;
            Texture2D t;
            if (mockCache.TryGetValue(v, out t)) return t;
            var c = new Color[8];
            for (int p = 1; p <= 8; p++)
            {
                int colour = pinColours != null ? Get(pinColours, p - 1, -1)
                    : rule.TerminalFor(p) != null ? Get(L.readerColours, rule.TerminalIndex(rule.TerminalFor(p)), -1) : -1;
                c[p - 1] = colour >= 0 ? Wire[Mathf.Clamp(colour, 0, Wire.Length - 1)] : new Color(0, 0, 0, 0);
            }
            t = MockPhotos.Controller(c, withLabel);
            mockCache[v] = t;
            return t;
        }

        void SetView(DemoLayout.PhotoView v, Texture2D tex)
        {
            view = v;
            photo.texture = tex;
            photoFit.aspectRatio = tex.width / (float)tex.height;
            if (atController)
                foreach (var m in marks) m.MoveTo(Get(v.points, m.Id - 1, m.Pos));
        }

        Vector2[] UsedPins(Vector2[] pins)
        {
            var list = new List<Vector2>();
            foreach (var p in rule.Pairs) if (pins != null && p.ControllerPin - 1 < pins.Length) list.Add(pins[p.ControllerPin - 1]);
            return list.Count > 0 ? list.ToArray() : pins;
        }

        float Zoom() { return mover != null ? mover.localScale.x : 1f; }

        void UpdateCheck()
        {
            if (mover == null || view == null) return;
            float t = Time.unscaledTime;
            // A camera feed always fills the screen: never scale the photo below the screen
            // (that showed the dark background as a border) and never pan past its edges.
            Vector2 pan = new Vector2(Mathf.Clamp(view.pan.x, -0.4f, 0.4f), Mathf.Clamp(view.pan.y, -0.4f, 0.4f));
            float cover = Mathf.Max(1f / (1f - 2f * Mathf.Abs(pan.x)), 1f / (1f - 2f * Mathf.Abs(pan.y)));
            float s = Mathf.Max(view.zoom, cover) * (calibrating ? 1f : 1.02f);
            Vector2 size = photoRT.rect.size;
            Vector2 sway = calibrating ? Vector2.zero
                : new Vector2(Mathf.PerlinNoise(t * 0.35f, 1.3f) - 0.5f, Mathf.PerlinNoise(2.1f, t * 0.3f) - 0.5f) * 18f;
            Vector2 room = (s - 1f) * 0.5f * size - Vector2.one * 9f;   // keep 9 px for the hand sway
            Vector2 off = new Vector2(-pan.x * size.x * s, pan.y * size.y * s);
            off.x = Mathf.Clamp(off.x, -Mathf.Max(0f, room.x), Mathf.Max(0f, room.x));
            off.y = Mathf.Clamp(off.y, -Mathf.Max(0f, room.y), Mathf.Max(0f, room.y));
            mover.localScale = new Vector3(s, s, 1f);
            mover.anchoredPosition = off + sway;

            float inv = 1f / Zoom();
            foreach (var m in marks) m.Animate(t, inv);
            foreach (var a in arrows) a.Update(markersRT, -tagSide, t, inv);

            if (scanLine != null && scanLine.gameObject.activeSelf)
            {
                float h = canvasRT.rect.height;
                float k = Mathf.Repeat(t * 0.55f * speed, 1f);
                scanLine.anchoredPosition = new Vector2(0, -h * (0.14f + 0.5f * k));
                scanLine.GetComponent<CanvasGroup>().alpha = Mathf.Sin(k * Mathf.PI);
            }
            if (qrBox != null && qrBox.gameObject.activeSelf)
            {
                Vector2 frame = view.labelSize;
                qrBox.sizeDelta = frame.x > 0 && frame.y > 0 ? Vector2.Scale(frame, photoRT.rect.size) * Zoom() : new Vector2(300, 300);
                float p = 1f + 0.03f * Mathf.Sin(t * 5f);
                qrBox.localScale = new Vector3(p / Zoom(), p / Zoom(), 1f);
            }
            if (missingGuide != null && missingGuide.gameObject.activeSelf)
            {
                missingGuide.localScale = Vector3.one * (L.markerScale / Zoom());
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
                tagSide = TagLayout.Apply(marks, canvasRT, FreeArea(), !atController && L.readerTagsBelowTerminals, L.markerScale, L.labelTextScale);
        }

        /// <summary>
        /// The part of the camera view that nothing covers: below the top controls and above the bottom sheet,
        /// with a margin at the sides. Tags are kept inside it, whatever the phone's aspect ratio.
        /// </summary>
        Rect FreeArea()
        {
            Rect r = canvasRT.rect;
            float top = r.yMax - SafeTop() - 290f;
            float bottom = r.yMin + (sheet != null && sheet.gameObject.activeSelf ? sheet.rect.height + 20f : 40f);
            if (top - bottom < 200f) bottom = top - 200f;
            return UnityEngine.Rect.MinMaxRect(r.xMin + 24f, bottom, r.xMax - 24f, top);
        }

        // ------------------------------------------------------------------ snapshot panel (linked views)

        Rect CropRect(Vector2[] pts, Texture2D tex)
        {
            if (pts == null || pts.Length == 0) return new Rect(0, 0, 1, 1);
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
            return new Rect(u0, 1f - v0 - hgt, w, hgt);
        }

        /// <summary>The small live-linked view of the other end, top corner of the camera view. Tap to enlarge.</summary>
        void BuildSnapshot(CableEnd of)
        {
            if (snapshot != null) Destroy(snapshot.parent.gameObject);
            snapshotDots.Clear();
            var safe = UiKit.Rect("SnapshotSafe", camRoot);
            ApplySafeArea(safe);
            safe.SetSiblingIndex(sheet.GetSiblingIndex());
            snapshot = UiKit.Rect("Snapshot", safe);
            snapshot.anchorMin = snapshot.anchorMax = new Vector2(L.snapshotOnLeft ? 0 : 1, 1);
            snapshot.pivot = snapshot.anchorMax;
            snapshot.anchoredPosition = new Vector2(L.snapshotOnLeft ? L.snapshotInset : -L.snapshotInset, -L.snapshotTopInset);
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
        /// A crop of one end's photo with a dot per wire. Device end: one dot per terminal (id = terminal index).
        /// Controller end: one dot per pin that holds a wire in the saved record (id = pin number).
        /// </summary>
        void BuildCrop(RectTransform holder, CableEnd of, int highlight, Dictionary<int, RectTransform> dotsOut)
        {
            holder.gameObject.AddComponent<RectMask2D>();
            var round = Img("Back", holder, sRound, Hex("#111111"), 20);
            Stretch(round.rectTransform);

            Texture2D tex;
            var ids = new List<int>(); var pos = new List<Vector2>(); var names = new List<string>(); var colours = new List<int>();
            if (of == CableEnd.Device)
            {
                tex = ReaderTexture();
                for (int i = 0; i < rule.DeviceTerminals.Length; i++)
                {
                    ids.Add(i);
                    pos.Add(Get(L.reader.points, i, Vector2.one * 0.5f));
                    names.Add(rule.DeviceTerminals[i].Print);
                    colours.Add(record != null ? record.Colours[i] : readColours != null && !atController && i < readColours.Length ? readColours[i] : i);
                }
            }
            else
            {
                var v = Scenario.wiring;
                tex = ControllerTexture(v, Scenario.pinColours, true);
                for (int p = 1; p <= rule.Connector.Count; p++)
                {
                    int c = ctrlRecord != null ? ctrlRecord.PinColours[p - 1] : Get(Scenario.pinColours, p - 1, WireColours.None);
                    if (c == WireColours.None) continue;
                    ids.Add(p);
                    pos.Add(Get(v.points, p - 1, Vector2.one * 0.5f));
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
                Img("Sw", dot, sCircle, Wire[Mathf.Clamp(colours[k], 0, Wire.Length - 1)], 0).rectTransform.sizeDelta = new Vector2(22, 22);
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
            snapshot.gameObject.SetActive(on);
            if (on) { StartCoroutine(Fade(snapshotGroup, 0, 1, 0.3f)); StartCoroutine(Pop(snapshot)); }
        }

        void ToggleSnapshot()
        {
            snapshotExpanded = !snapshotExpanded;
            float inset = snapshotExpanded ? Gutter : L.snapshotInset;
            log.Add("snapshot_" + (snapshotExpanded ? "open" : "close"));
            StartCoroutine(ResizeSnapshot(snapshotExpanded ? new Vector2(984, 820) : new Vector2(360, 320),
                new Vector2(L.snapshotOnLeft ? inset : -inset, snapshotExpanded ? -300 : -L.snapshotTopInset)));
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

        // ------------------------------------------------------------------ calibration (operator)

        int CalibCount { get { return atController ? 3 : 1 + rule.DeviceTerminals.Length; } }

        void ToggleCalibration()
        {
            if (photo == null || photo.texture == null || view == null) return;
            calibrating = !calibrating;
            calibStep = 0;
            calibGroup.gameObject.SetActive(calibrating);
            calibGroup.transform.SetAsLastSibling();
            if (calibrating)
            {
                hiddenBeforeCalibration.Clear();
                foreach (var m in marks)
                    if (m.State == Mark.Hidden) { hiddenBeforeCalibration.Add(m); m.Set(Mark.Correct, -1, m.Name); }
            }
            else
            {
                foreach (var m in hiddenBeforeCalibration) m.Set(Mark.Hidden, -1, null);
                hiddenBeforeCalibration.Clear();
            }
            UpdateCalibText();
        }

        void UpdateCalibText()
        {
            string what = calibStep == 0 ? "the cable LABEL"
                : atController ? (calibStep == 1 ? "pin 1 (" + rule.Connector.Pin(1).Print + ")" : "pin " + rule.Connector.Count + " (" + rule.Connector.Pin(rule.Connector.Count).Print + ")")
                : "terminal " + rule.DeviceTerminals[calibStep - 1].Print;
            calibText.text = (atController ? "Controller" : "Reader") + " · click " + what + "  (" + (calibStep + 1) + "/" + CalibCount + ") · Esc to stop";
        }

        void OnCalibrationClick(PointerEventData e)
        {
            if (!calibrating) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(photoRT, e.position, null, out local)) return;
            Rect r = photoRT.rect;
            var p = new Vector2(Mathf.Clamp01((local.x - r.xMin) / r.width), Mathf.Clamp01(1f - (local.y - r.yMin) / r.height));
            if (calibStep == 0) { view.labelPos = p; ShowQr(p); }
            else if (!atController)
            {
                int i = calibStep - 1;
                if (view.points == null || view.points.Length < rule.DeviceTerminals.Length)
                {
                    var n = new Vector2[rule.DeviceTerminals.Length];
                    if (view.points != null) Array.Copy(view.points, n, view.points.Length);
                    view.points = n;
                }
                view.points[i] = p;
                marks[i].MoveTo(p);
            }
            else if (calibStep == 1) calibPin1 = p;
            else
            {
                view.points = DemoLayout.Interpolate(calibPin1, p);
                foreach (var m in marks) m.MoveTo(view.points[m.Id - 1]);
            }
#if UNITY_EDITOR
            if (layout != null) EditorUtility.SetDirty(layout);
#endif
            calibStep++;
            if (calibStep >= CalibCount)
            {
                ToggleCalibration();
                qrBox.gameObject.SetActive(false);
                Toast(layout != null ? "Positions saved to " + layout.name : "Positions set (assign a DemoLayout asset to keep them)");
#if UNITY_EDITOR
                if (layout != null) AssetDatabase.SaveAssets();
#endif
                return;
            }
            UpdateCalibText();
        }
    }
}
