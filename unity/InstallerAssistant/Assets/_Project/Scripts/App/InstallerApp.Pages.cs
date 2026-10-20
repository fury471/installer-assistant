using System;
using System.Collections.Generic;
using System.IO;
using InstallerAssistant.AR;
using InstallerAssistant.Labels;
using InstallerAssistant.UI;
using InstallerAssistant.Verification;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static InstallerAssistant.UI.UiKit;

namespace InstallerAssistant.App
{
    /// <summary>
    /// The app around the check: Home (one action: scan a cable), saved reports and settings.
    /// There is no job list to read first: the label on the cable tells the app what it is, so the
    /// installer starts wherever they are. Every page uses the real rules and records; the one example report
    /// on Reports exists only in the Wizard of Oz. One column, white page, one primary action per screen.
    /// </summary>
    public partial class InstallerApp
    {
        enum Tab { Home, Reports, Settings }

        const float AppBarH = 136, TabBarH = 168, BottomBarH = 232;

        // ================================================================== Home

        /// <summary>
        /// Home, top to bottom: who and when, what happened (a timeline), and the one action docked at the bottom,
        /// where the thumb rests and where it stays put however long the timeline grows.
        /// </summary>
        void ShowHome()
        {
            var pendingList = Pending();
            var pending = pendingList.Count > 0 ? pendingList[0] : null;
            var s = NewScreen(Page);
            // the dock is built first so the timeline above it can scroll clear of it, whatever its height
            var dock = ScanDock(s, pending);
            float dockTop = dock.offsetMax.y;
            var c = Scroll(s, 0, dockTop + 24);
            dock.SetAsLastSibling();

            // 1. header
            var brand = HRow(c, 96, 18);
            var mark = Img("Mark", brand, sRound, Accent, 18);
            Fixed(mark.rectTransform, 60, 60);
            var mi = UiKit.Rect("Icon", mark.rectTransform); Stretch(mi);
            DrawIcon(mi, Icon.Check, 38, OnColour);
            Flex(Txt(brand, "Installer Assistant", Small, Ink, Face.Semi));
            Txt(brand, DateTime.Now.ToString("ddd d MMM"), Caption, Ink3, Face.Regular, TextAlignmentOptions.Right);
            Space(c, 4);
            Txt(c, "Check a cable", DisplaySize, Ink, Face.Semi);
            Txt(c, "Start at either end. The label tells the app which cable it is and what to do next.", Body, Ink2);

            // 2. activity, newest first, as a timeline
            Space(c, 32);
            OverlineText(c, "Activity", Ink);
            Space(c, 4);
            var items = new List<TimelineItem>();
            foreach (var pc in pendingList)
            {
                var c1 = pc;
                items.Add(new TimelineItem
                {
                    Time = c1.SavedAt.ToString("HH:mm"), Title = c1.Title + " · " + EndName(c1, c1.FirstEnd) + " end saved",
                    Line = "Scan the " + EndName(c1, Other(c1.FirstEnd)) + " end to finish the check.", Highlight = true,
                    Chip = p => Chip(p, "1 of 2 ends", WarnSoft, WarnInk), Tap = () => StartCheck(Other(c1.FirstEnd)),
                });
            }
            var sorted = new List<Report>(reports);
            sorted.Sort((x, y) => y.At.CompareTo(x.At));
            for (int i = 0; i < sorted.Count && i < 6; i++)
            {
                var rep = sorted[i];
                items.Add(new TimelineItem
                {
                    Time = When(rep.At), Title = rep.Cable.Title + " verified",
                    Line = rep.Cable.Label.Display + " · " + rep.Wires + "/" + rep.Wires + " wires · " + Plural(rep.Fixes, "fix", "fixes"),
                    Chip = p => Chip(p, "Verified", OkSoft, Ok), Tap = () => ShowReport(rep, false),
                });
            }
            if (items.Count > 0) Timeline(c, items);
            else Txt(c, "Nothing checked yet. Scan a cable to start.", Small, Ink3);

            // 3. the one action, docked above the tab bar (built first, drawn last)
            TabBar(s, Tab.Home);
        }

        /// <summary>
        /// The scan action, docked above the tab bar. Its height follows its content, so the text never squeezes:
        /// a header (icon, title, one line), a three-step stepper joined by a line, and the button.
        /// </summary>
        RectTransform ScanDock(RectTransform s, Cable pending)
        {
            var dock = Card(s, 48, 40);
            dock.anchorMin = new Vector2(0, 0); dock.anchorMax = new Vector2(1, 0); dock.pivot = new Vector2(0.5f, 0);
            dock.offsetMin = new Vector2(Gutter, TabBarH + 24); dock.offsetMax = new Vector2(-Gutter, TabBarH + 24 + 600);

            string other = pending != null ? EndName(pending, Other(pending.FirstEnd)) : null;
            var head = HRow(dock, 0, 32);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var ring = Img("Viewfinder", head, sCircle, AccentSoft, 0);
            Fixed(ring.rectTransform, 104, 104);
            var cam = UiKit.Rect("Icon", ring.rectTransform); Stretch(cam);
            DrawIcon(cam, Icon.Camera, 52, Accent);
            var ht = VBox(head, 10); Flex(ht);
            Txt(ht, pending != null ? "Finish " + pending.Title : "Scan a cable label", Head, Ink, Face.Semi);
            Txt(ht, pending != null ? "It is waiting for its " + other + " end." : "At the reader or the controller. The app pairs the two ends.", Small, Ink2).lineSpacing = 10;

            // the three steps, shown once as feedforward; the current one is blue
            int current = pending != null ? 2 : 1;
            var steps = HRow(dock, 0, 0);
            var shl = steps.GetComponent<HorizontalLayoutGroup>();
            shl.childForceExpandWidth = true; shl.childAlignment = TextAnchor.UpperCenter;
            shl.padding = new RectOffset(0, 0, 8, 0);
            var track = Img("Track", steps, null, Hex("#D5D9E0"), 0);   // joins the three numbers; hidden behind them
            track.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            track.rectTransform.anchorMin = new Vector2(1f / 6f, 1); track.rectTransform.anchorMax = new Vector2(5f / 6f, 1);
            track.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0, 3); track.rectTransform.anchoredPosition = new Vector2(0, -8 - 28);
            if (current > 1)
            {
                var doneTrack = Img("Done", steps, null, Ok, 0);
                doneTrack.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                doneTrack.rectTransform.anchorMin = new Vector2(1f / 6f, 1); doneTrack.rectTransform.anchorMax = new Vector2(0.5f, 1);
                doneTrack.rectTransform.sizeDelta = new Vector2(0, 3); doneTrack.rectTransform.anchoredPosition = new Vector2(0, -8 - 28);
            }
            MiniStep(steps, "1", "Scan one end", current == 1, current > 1);
            MiniStep(steps, "2", "Scan the other end", current == 2, false);
            MiniStep(steps, "3", "Fix what is red", false, false);

            var go = HRow(dock, 144);
            go.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Btn(go, pending != null ? "Scan the " + other + " end" : "Scan a cable", ButtonKind.Primary, () => StartCheck(NextEnd()), 144);

            // size the dock to its content now, so the scroll area above can stop where the dock starts
            LayoutRebuilder.ForceRebuildLayoutImmediate(dock);
            float h = LayoutUtility.GetPreferredHeight(dock);
            dock.offsetMax = new Vector2(-Gutter, TabBarH + 24 + h);
            return dock;
        }

        static string EndName(Cable c, CableEnd e) { return e == CableEnd.Controller ? "controller" : (c.Rule != null ? c.Rule.Usage : "device").ToLowerInvariant(); }

        void MiniStep(RectTransform parent, string n, string text, bool now, bool done)
        {
            var box = VBox(parent, 14);
            box.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            box.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            var le = Flex(box);
            le.preferredWidth = 0; le.minWidth = 0;   // three equal columns, so the numbers sit on the joining line
            var num = Img("Num", box, sCircle, now ? Accent : done ? Ok : Surface, 0);
            Fixed(num.rectTransform, 56, 56);
            var holder = UiKit.Rect("Mark", num.rectTransform); Stretch(holder);
            if (done) DrawIcon(holder, Icon.Check, 32, OnColour);
            else { var t = Txt(holder, n, Caption, now ? OnColour : Ink2, Face.Semi, TextAlignmentOptions.Center); Stretch(t.rectTransform); }
            var label = Txt(box, text, Small - 2, now ? Accent : done ? Ink : Ink2, now ? Face.Semi : Face.Regular, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;   // one line each, so the three labels line up
            label.enableAutoSizing = true; label.fontSizeMin = 24; label.fontSizeMax = Small - 2;
        }

        class TimelineItem
        {
            public string Time, Title, Line;
            public bool Highlight;
            public Func<RectTransform, RectTransform> Chip;
            public Action Tap;
        }

        /// <summary>
        /// A vertical timeline: a thin rail with a dot per event, the time above a bold title and one quiet line.
        /// Reads top to bottom like a news feed; the newest event is first.
        /// </summary>
        void Timeline(RectTransform parent, List<TimelineItem> items)
        {
            var list = VBox(parent, 0);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                bool last = i == items.Count - 1;
                var row = Img("Event", list, null, new Color(0, 0, 0, 0), 0);
                var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hl.spacing = 24;
                hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
                if (it.Tap != null) MakeButton(row, it.Tap);

                var rail = UiKit.Rect("Rail", row.rectTransform);
                rail.gameObject.AddComponent<LayoutElement>().preferredWidth = 24;
                if (!last)
                {
                    // the rail runs from this event's dot down to the next one
                    var line = Img("Line", rail, null, Hairline, 0).rectTransform;
                    line.anchorMin = new Vector2(0.5f, 0); line.anchorMax = new Vector2(0.5f, 1);
                    line.offsetMin = new Vector2(-1, -24); line.offsetMax = new Vector2(1, -24);
                }
                var dot = Img("Dot", rail, sCircle, it.Highlight ? Accent : Hex("#C7CCD4"), 0);
                Place(dot.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(16, 16));

                var body = VBox(row.rectTransform, 6);
                body.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 0, last ? 8 : 44);
                Flex(body);
                Txt(body, it.Time, Caption, Ink3);
                var titleRow = HRow(body, 0, 16);
                Flex(Txt(titleRow, it.Title, Body, Ink, Face.Semi));
                if (it.Chip != null) it.Chip(titleRow);
                if (!string.IsNullOrEmpty(it.Line)) Txt(body, it.Line, Small, Ink2);
            }
        }

        // ================================================================== reports

        void ShowReports()
        {
            var s = NewScreen(Page);
            var c = Scroll(s, 0, TabBarH);
            Space(c, 40);
            Txt(c, "Reports", DisplaySize, Ink, Face.Semi);
            Txt(c, "Installation records saved on this phone, newest first.", Body, Ink2);
            Space(c, 16);
            var sorted = new List<Report>(reports);
            sorted.Sort((a, b) => b.At.CompareTo(a.At));
            var items = new List<TimelineItem>();
            foreach (var r in sorted)
            {
                var rep = r;
                items.Add(new TimelineItem
                {
                    Time = When(r.At), Title = r.Cable.Title, Highlight = r.Live,
                    Line = r.Cable.Label.Display + " · " + r.Wires + "/" + r.Wires + " wires · " + Plural(r.Fixes, "fix", "fixes") + " · " + Duration(r.Seconds),
                    Chip = p => Chip(p, "Verified", OkSoft, Ok), Tap = () => ShowReport(rep, false),
                });
            }
            Timeline(c, items);
            TabBar(s, Tab.Reports);
        }

        void ShowReport(Report r, bool fromCheck)
        {
            var s = NewScreen(Page);
            if (!fromCheck) AppBar(s, "Report", ShowReports);
            var c = Scroll(s, fromCheck ? 40 : AppBarH, fromCheck ? BottomBarH : 0);

            var head = VBox(c, 10);
            head.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            head.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            Space(head, 24);
            Badge(head, Ok, Icon.Check, 132);
            Space(head, 12);
            Txt(head, "Installation verified", Title + 6, Ink, Face.Semi, TextAlignmentOptions.Center);
            Txt(head, r.Cable.Label.Display + " · " + r.Cable.Title + " · " + When(r.At), Small, Ink2, Face.Regular, TextAlignmentOptions.Center);
            Space(c, 8);

            var stats = HRow(c, 190, 20);
            stats.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            Stat(stats, r.Wires + "/" + r.Wires, "wires correct");
            Stat(stats, r.Fixes.ToString(), r.Fixes == 1 ? "fix made" : "fixes made");
            Stat(stats, r.CheckedByYou.ToString(), "checked by you");

            if (r.Live)
            {
                var ds = r.Cable.DeviceShot; var cs = r.Cable.ControllerShot;
                bool hasD = ds != null && ds.Image != null, hasC = cs != null && cs.Image != null;
                if (hasD || hasC)
                {
                    OverlineText(c, "Evidence", Ink3);
                    var thumbs = HRow(c, 330, 20);
                    thumbs.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
                    if (hasD) Thumb(thumbs, ds.Image, CropRect(ds.Points, ds.Image), EndTitleOf(r.Cable.Rule, CableEnd.Device));
                    if (hasC) Thumb(thumbs, cs.Image, CropRect(UsedPins(cs.Points, r.Record != null ? r.Record.Rule : rule), cs.Image), "Controller end");
                }

                if (r.Record != null)
                {
                    var ru = r.Record.Rule;
                    OverlineText(c, "Wiring", Ink3);
                    var w = Card(c, 40, 6);
                    for (int i = 0; i < ru.Pairs.Length; i++)
                    {
                        if (i > 0) Divider(w);
                        var p = ru.Pairs[i];
                        var row = HRow(w, 84, 18);
                        int col = r.Record.ColourOn(p.DeviceTerminal);
                        Swatch(row, col, 34);
                        Flex(Txt(row, WireColours.Name(col), Body, Ink), 170);
                        Flex(Txt(row, EndTitleOf(r.Cable.Rule, CableEnd.Device).Replace(" end", "") + " " + ru.Terminal(p.DeviceTerminal).Print +
                            "  →  pin " + p.ControllerPin + " (" + ru.Connector.Pin(p.ControllerPin).Print + ")", Small, Ink2));
                        Badge(row, Ok, Icon.Check, 40);
                    }
                }
            }

            if (!string.IsNullOrEmpty(r.FixText))
            {
                OverlineText(c, r.Fixes > 0 ? "Fixed during the check" : "Result", Ink3);
                var f = Card(c, 40, 10);
                Txt(f, r.FixText, Body, Ink);
            }

            var next = Card(c, 40, 10, WarnSoft, WarnSoft);
            Txt(next, "Next on the device", Body, Ink, Face.Semi);
            Txt(next, "Power the controller and set up the reader in the A1610's web page. A reader LED that keeps flashing red is still waiting for the controller.", Small, Ink2);
            Txt(c, "Checked in " + Duration(r.Seconds) + (r.Live ? " · session log kept on the phone" : " · example record"), Caption, Ink3, Face.Regular, TextAlignmentOptions.Center);

            if (fromCheck)
            {
                var bar = BottomBar(s);
                Btn(bar, "Save report", ButtonKind.Secondary, () => Toast(SaveReport()));
                Btn(bar, "Done", ButtonKind.Primary, ShowHome);
            }
        }

        string SaveReport()
        {
            if (log == null) return "Nothing to save yet";
            log.Add("report_saved");
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "InstallerAssistant");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "session_" + log.SessionId + ".csv");
                File.WriteAllText(file, log.ToCsv());
                Debug.Log("[InstallerAssistant] Session log saved to " + file);
                return "Report saved on this phone";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[InstallerAssistant] Could not save the session log: " + e.Message);
                return "Could not save the report on this device";
            }
        }

        // ================================================================== settings

        void ShowSettings()
        {
            var s = NewScreen(Page);
            var c = Scroll(s, 0, TabBarH);
            Space(c, 40);
            Txt(c, "Settings", DisplaySize, Ink, Face.Semi);
            Space(c, 8);

            OverlineText(c, "Checking", Ink3);
            var chk = Card(c, 40, 24);
            SettingRow(chk, "AI second opinion", "An independent AI compares both ends. Off: the rules decide alone.",
                row => Switch(row, useSecondOpinion, v => useSecondOpinion = v));
            Divider(chk);
            SettingRow(chk, "Ask me below", "Readings less certain than this turn amber for you to check.", null);
            int th = confidenceThreshold < 0.75f ? 0 : confidenceThreshold < 0.85f ? 1 : 2;
            Segmented(chk, new[] { "70 %", "80 %", "90 %" }, th, i => confidenceThreshold = 0.7f + 0.1f * i);

            if (op != null)
            {
                OverlineText(c, "Wizard of Oz (operator)", Ink3);
                var demo = Card(c, 40, 24);
                SettingRow(demo, "Participant starts at", "The end shown when \"Scan a cable\" is pressed first. The app itself does not need to know.", null);
                Segmented(demo, new[] { "Reader end", "Controller end" }, op.StartEnd == CableEnd.Controller ? 1 : 0,
                    i => { op.StartEnd = i == 0 ? CableEnd.Device : CableEnd.Controller; ShowSettings(); });
                Divider(demo);
                SettingRow(demo, "Planted fault", "Restarts the site visit with this fault on the bench.", null);
                var names = new List<string>();
                for (int i = 0; op.FaultNames != null && i < op.FaultNames.Length && i < 3; i++) names.Add(op.FaultNames[i]);
                Segmented(demo, names.ToArray(), op.Fault, i => { op.ChooseFault(i); ResetSession(); ShowSettings(); });
                Divider(demo);
                SettingRow(demo, "Start without the label", "At the controller, the app first asks for a missing label.",
                    row => Switch(row, op.StartWithoutLabel, v => op.StartWithoutLabel = v));
                Divider(demo);
                SettingRow(demo, "Operator keys", "Show the key line at the top of the screen.",
                    row => Switch(row, op.ShowOperatorKeys, v => op.ShowOperatorKeys = v));
                Divider(demo);
                var reset = HRow(demo, 0);
                Btn(reset, "Reset demo data", ButtonKind.Secondary, () => { ResetSession(); Toast("Demo data reset"); ShowSettings(); }, 112);
            }

            OverlineText(c, "About", Ink3);
            var about = Card(c, 40, 6);
            KeyRow(about, "Version", "0.1 · MVP");
            Divider(about);
            KeyRow(about, "Wiring rules", "AXIS A1610 · A4020-E");
            Divider(about);
            KeyRow(about, "Sensing", op != null ? "Wizard of Oz (scripted)" : "Live (camera and perception)");
            TabBar(s, Tab.Settings);
        }

        void SettingRow(RectTransform parent, string title, string desc, Action<RectTransform> control)
        {
            var row = HRow(parent, 0, 24);
            var t = VBox(row, 6); Flex(t);
            Txt(t, title, Body, Ink, Face.Semi);
            if (!string.IsNullOrEmpty(desc)) Txt(t, desc, Caption, Ink2);
            if (control != null) control(row);
        }

        void Segmented(RectTransform parent, string[] options, int selected, Action<int> onPick)
        {
            var bg = Img("Segmented", parent, sRound, Sunken, 30);
            bg.gameObject.AddComponent<LayoutElement>().preferredHeight = 96;
            var hl = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(8, 8, 8, 8); hl.spacing = 8;
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                bool on = i == selected;
                var seg = Img("Option", bg.rectTransform, sRound, on ? Surface : new Color(0, 0, 0, 0), 24);
                MakeButton(seg, () => onPick(k));
                var t = Txt(seg.rectTransform, options[i], Small, on ? Accent : Ink2, on ? Face.Semi : Face.Regular, TextAlignmentOptions.Center);
                Stretch(t.rectTransform, 12, 0, 12, 0);
                t.enableAutoSizing = true; t.fontSizeMin = 22; t.fontSizeMax = Small;
            }
        }

        // ================================================================== shared page parts

        /// <summary>Top bar: a back button and an optional centred title.</summary>
        void AppBar(RectTransform s, string title, Action back)
        {
            var bar = UiKit.Rect("AppBar", s);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(0, AppBarH); bar.anchoredPosition = Vector2.zero;
            if (back != null)
            {
                var b = Img("Back", bar, sCircle, new Color(0, 0, 0, 0), 0);
                Place(b.rectTransform, new Vector2(0, 0.5f), new Vector2(24 + 56, 0), new Vector2(112, 112));
                MakeButton(b, back);
                var ic = UiKit.Rect("Icon", b.rectTransform); Stretch(ic);
                DrawIcon(ic, Icon.ChevronLeft, 48, Ink);
            }
            if (!string.IsNullOrEmpty(title))
            {
                var t = Txt(bar, title, Body, Ink, Face.Semi, TextAlignmentOptions.Center);
                Place(t.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 60));
            }
        }

        /// <summary>Pinned action area at the bottom of a page.</summary>
        RectTransform BottomBar(RectTransform s)
        {
            var bar = Img("BottomBar", s, null, Page, 0);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(0, BottomBarH); rt.anchoredPosition = Vector2.zero;
            var line = Img("Line", rt, null, Hairline, 0);
            line.rectTransform.anchorMin = new Vector2(0, 1); line.rectTransform.anchorMax = new Vector2(1, 1);
            line.rectTransform.sizeDelta = new Vector2(0, 2); line.rectTransform.anchoredPosition = Vector2.zero;
            var row = UiKit.Rect("Buttons", rt);
            Stretch(row, Gutter, 48, Gutter, 48);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 20; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
            return row;
        }

        void TabBar(RectTransform s, Tab active)
        {
            var bar = Img("TabBar", s, null, Surface, 0);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(0, TabBarH); rt.anchoredPosition = Vector2.zero;
            var line = Img("Line", rt, null, Hairline, 0);
            line.rectTransform.anchorMin = new Vector2(0, 1); line.rectTransform.anchorMax = new Vector2(1, 1);
            line.rectTransform.sizeDelta = new Vector2(0, 2); line.rectTransform.anchoredPosition = Vector2.zero;
            var row = UiKit.Rect("Tabs", rt);
            Stretch(row, 24, 0, 24, 2);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
            TabItem(row, "Check", Icon.Camera, active == Tab.Home, ShowHome);
            TabItem(row, "Reports", Icon.Reports, active == Tab.Reports, ShowReports);
            TabItem(row, "Settings", Icon.Settings, active == Tab.Settings, ShowSettings);
        }

        void TabItem(RectTransform row, string label, Icon icon, bool on, Action go)
        {
            var item = Img("Tab " + label, row, null, new Color(0, 0, 0, 0), 0);
            MakeButton(item, go);
            var ic = UiKit.Rect("Icon", item.rectTransform);
            Place(ic, new Vector2(0.5f, 0.5f), new Vector2(0, 24), new Vector2(64, 64));
            DrawIcon(ic, icon, 56, on ? Accent : Ink3);
            var t = Txt(item.rectTransform, label, Caption, on ? Accent : Ink3, on ? Face.Semi : Face.Regular, TextAlignmentOptions.Center);
            Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -38), new Vector2(300, 40));
        }

        void KeyRow(RectTransform parent, string key, string value)
        {
            var row = HRow(parent, 84, 20);
            Flex(Txt(row, key, Body, Ink2), 300);
            Flex(Txt(row, value, Body, Ink, Face.Regular, TextAlignmentOptions.Right));
        }

        void StepLine(RectTransform parent, string n, string text)
        {
            var row = HRow(parent, 0, 26);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
            var num = Img("Num", row, sCircle, Surface, 0);
            Fixed(num.rectTransform, 56, 56);
            var t = Txt(num.rectTransform, n, Small, Ink, Face.Semi, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
            Flex(Txt(row, text, Body, Ink));
        }

        void Stat(RectTransform parent, string big, string small)
        {
            var box = Card(parent, 24, 4);
            Flex(box);
            box.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Txt(box, big, 60, Ink, Face.Semi, TextAlignmentOptions.Center);
            Txt(box, small, Caption, Ink2, Face.Regular, TextAlignmentOptions.Center);
        }

        void Thumb(RectTransform parent, Texture2D tex, Rect uv, string caption)
        {
            var box = Card(parent, 12, 10);
            Flex(box);
            var holder = UiKit.Rect("Mask", box);
            holder.gameObject.AddComponent<LayoutElement>().preferredHeight = 240;
            holder.gameObject.AddComponent<RectMask2D>();
            var img = new GameObject("Img", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(holder, false);
            Stretch(img.rectTransform);
            img.texture = tex; img.uvRect = uv;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 4f / 3f;
            Txt(box, caption, Caption, Ink2, Face.Regular, TextAlignmentOptions.Center);
        }

        /// <summary>A full-width child row of a padding-free card, so dividers can be inset like the rows.</summary>
        RectTransform InsetRow(RectTransform card)
        {
            var r = UiKit.Rect("Inset", card);
            var vl = r.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(128, 0, 0, 0);
            vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandWidth = true;
            return r;
        }

        static string Plural(int n, string one, string many) { return n + " " + (n == 1 ? one : many); }
        static string Duration(float secs) { return Mathf.FloorToInt(secs / 60f) + ":" + Mathf.FloorToInt(secs % 60f).ToString("00"); }
    }
}
