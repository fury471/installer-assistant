using System;
using System.Collections.Generic;
using System.IO;
using InstallerAssistant.Labels;
using InstallerAssistant.UI;
using InstallerAssistant.Verification;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static InstallerAssistant.UI.UiKit;

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// The app around the check: Home (one action: scan a cable), saved reports and settings.
    /// There is no job list to read first: the label on the cable tells the app what it is, so the
    /// installer starts wherever they are. Past checks are demo data; every page uses the real rules and records.
    /// One column, cards on a light page, one primary action per screen.
    /// </summary>
    public partial class InstallerAssistantDemo
    {
        enum Tab { Home, Reports, Settings }

        const float AppBarH = 136, TabBarH = 168, BottomBarH = 232;

        // ================================================================== Home

        void ShowHome()
        {
            bool pending = liveCable.State == CableState.OneEnd;
            var s = NewScreen(Page);
            var c = Scroll(s, 0, TabBarH + (pending ? 156 : 0));

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
            Space(c, 8);

            // the one action, with its three steps shown once as feedforward
            var hero = Card(c, 40, 32);
            var head = HRow(hero, 0, 28);
            var ring = Img("Viewfinder", head, sCircle, AccentSoft, 0);
            Fixed(ring.rectTransform, 112, 112);
            var cam = UiKit.Rect("Icon", ring.rectTransform); Stretch(cam);
            DrawIcon(cam, Icon.Camera, 56, Accent);
            var ht = VBox(head, 4); Flex(ht);
            Txt(ht, "Scan a cable label", Head, Ink, Face.Semi);
            Txt(ht, "At the reader or the controller. The app pairs the two ends.", Small, Ink2);

            var steps = HRow(hero, 0, 12);
            steps.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            steps.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            MiniStep(steps, "1", "Scan one end");
            MiniStep(steps, "2", "Scan the other end");
            MiniStep(steps, "3", "Fix what is red");

            var go = HRow(hero, 144);
            go.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Btn(go, "Scan a cable", ButtonKind.Primary, () => StartCheck(NextEnd()), 144);

            // what happened today, newest first, as a timeline
            var items = new List<TimelineItem>();
            if (pending)
                items.Add(new TimelineItem
                {
                    Time = liveCable.SavedAt.ToString("HH:mm"), Title = liveCable.Title + " · " + EndName(liveCable.FirstEnd) + " end saved",
                    Line = "Scan the " + EndName(Other(liveCable.FirstEnd)) + " end to finish the check.", Highlight = true,
                    Chip = p => Chip(p, "1 of 2 ends", WarnSoft, WarnInk), Tap = () => StartCheck(NextEnd()),
                });
            var sorted = new List<Report>(reports);
            sorted.Sort((x, y) => y.At.CompareTo(x.At));
            for (int i = 0; i < sorted.Count && i < 4; i++)
            {
                var rep = sorted[i];
                items.Add(new TimelineItem
                {
                    Time = When(rep.At), Title = rep.Cable.Title + " verified",
                    Line = rep.Cable.Label.Display + " · " + rep.Wires + "/" + rep.Wires + " wires · " + Plural(rep.Fixes, "fix", "fixes"),
                    Chip = p => Chip(p, "Verified", OkSoft, Ok), Tap = () => ShowReport(rep, false),
                });
            }
            if (items.Count > 0)
            {
                Space(c, 16);
                OverlineText(c, "Activity", Ink);
                Space(c, 4);
                Timeline(c, items);
            }

            TabBar(s, Tab.Home);
            if (pending) PendingBar(s);
        }

        string EndName(CableEnd e) { return e == CableEnd.Controller ? "controller" : cable.Usage.ToLowerInvariant(); }

        void MiniStep(RectTransform parent, string n, string text)
        {
            var box = VBox(parent, 10);
            box.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            box.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            Flex(box);
            var num = Img("Num", box, sCircle, Surface, 0);
            Fixed(num.rectTransform, 52, 52);
            var t = Txt(num.rectTransform, n, Caption, Ink, Face.Semi, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
            Txt(box, text, Caption, Ink2, Face.Regular, TextAlignmentOptions.Center);
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

        /// <summary>A floating bar above the tab bar for the cable that waits for its other end: always one tap away.</summary>
        void PendingBar(RectTransform s)
        {
            var holder = UiKit.Rect("PendingBar", s);
            holder.anchorMin = new Vector2(0, 0); holder.anchorMax = new Vector2(1, 0); holder.pivot = new Vector2(0.5f, 0);
            holder.offsetMin = new Vector2(Gutter, TabBarH + 24); holder.offsetMax = new Vector2(-Gutter, TabBarH + 24 + 112);
            Shadow(holder, 24, 0.10f, 6);
            var edge = Img("Edge", holder, sRound, Hairline, 56);
            Stretch(edge.rectTransform);
            var bar = Img("Bar", edge.rectTransform, sRound, Surface, 54);
            Stretch(bar.rectTransform, 2, 2, 2, 2);
            MakeButton(bar, () => StartCheck(NextEnd()));
            var hl = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(40, 32, 0, 0); hl.spacing = 18; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            var dot = Img("Dot", bar.rectTransform, sCircle, Warn, 0); Fixed(dot.rectTransform, 18, 18);
            Txt(bar.rectTransform, liveCable.Title, Small, Ink, Face.Semi).textWrappingMode = TextWrappingModes.NoWrap;
            Flex(Txt(bar.rectTransform, "1 of 2 ends", Caption, Ink3));
            Txt(bar.rectTransform, "Scan the " + EndName(Other(liveCable.FirstEnd)) + " end", Small, Accent, Face.Semi).textWrappingMode = TextWrappingModes.NoWrap;
            var chev = UiKit.Rect("Chevron", bar.rectTransform); Fixed(chev, 24, 36);
            DrawIcon(chev, Icon.ChevronRight, 34, Accent);
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
                OverlineText(c, "Evidence", Ink3);
                var thumbs = HRow(c, 330, 20);
                thumbs.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
                Thumb(thumbs, ReaderTexture(), CropRect(L.reader.points, ReaderTexture()), "Reader end");
                var ft = ControllerTexture(L.controllerFixed, null, true);
                Thumb(thumbs, ft, CropRect(UsedPins(L.controllerFixed.points), ft), "Controller end");

                OverlineText(c, "Wiring", Ink3);
                var w = Card(c, 40, 6);
                for (int i = 0; i < rule.Pairs.Length; i++)
                {
                    if (i > 0) Divider(w);
                    var p = rule.Pairs[i];
                    var row = HRow(w, 84, 18);
                    int col = record != null ? record.ColourOn(p.DeviceTerminal) : -1;
                    Swatch(row, col, 34);
                    Flex(Txt(row, WireColours.Name(col), Body, Ink), 170);
                    Flex(Txt(row, "Reader " + rule.Terminal(p.DeviceTerminal).Print + "  →  pin " + p.ControllerPin + " (" + rule.Connector.Pin(p.ControllerPin).Print + ")", Small, Ink2));
                    Badge(row, Ok, Icon.Check, 40);
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

            OverlineText(c, "Demo (operator)", Ink3);
            var demo = Card(c, 40, 24);
            SettingRow(demo, "Participant starts at", "The end shown when \"Scan a cable\" is pressed first. The app itself does not need to know.", null);
            Segmented(demo, new[] { "Reader end", "Controller end" }, demoStart == CableEnd.Device ? 0 : 1,
                i => { demoStart = i == 0 ? CableEnd.Device : CableEnd.Controller; ShowSettings(); });
            Divider(demo);
            SettingRow(demo, "Planted fault", "Restarts the site visit with this fault on the bench.", null);
            var names = new List<string>();
            for (int i = 0; L.scenarios != null && i < L.scenarios.Length && i < 3; i++) names.Add(L.scenarios[i].name);
            Segmented(demo, names.ToArray(), scenario, i => { ChooseScenario(i, false); ResetDemoData(); ShowSettings(); });
            Divider(demo);
            SettingRow(demo, "Start without the label", "At the controller, the app first asks for a missing label.",
                row => Switch(row, plantMissingLabel, v => plantMissingLabel = v));
            Divider(demo);
            SettingRow(demo, "Operator keys", "Show the key line at the top of the screen.",
                row => Switch(row, showOperatorHint, v => { showOperatorHint = v; UpdateHint(); }));
            Divider(demo);
            var reset = HRow(demo, 0);
            Btn(reset, "Reset demo data", ButtonKind.Secondary, () => { ResetDemoData(); Toast("Demo data reset"); ShowSettings(); }, 112);

            OverlineText(c, "About", Ink3);
            var about = Card(c, 40, 6);
            KeyRow(about, "Version", "0.1 · MVP");
            Divider(about);
            KeyRow(about, "Wiring rules", "AXIS A1610 · A4020-E");
            Divider(about);
            KeyRow(about, "Sensing", "Wizard of Oz (scripted)");
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
