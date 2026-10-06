using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wizard-of-Oz demo of an AR wiring-verification view for the AXIS A1610 door controller.
///
/// SETUP (2 minutes):
///   1. Take a straight-on photo of the opened A1610 (with the reader wired in) and drag it into Assets.
///   2. Create an empty GameObject, add this component, assign the photo to "Board Photo".
///   3. Press Play. No canvas, prefabs or packages needed (uses IMGUI, works with either input system).
///
/// DEMO FLOW:
///   SPACE           "Point the camera at the controller" -> locate
///   click 4 corners  of the board in the photo: top-left, top-right, bottom-right, bottom-left
///                    (or press ENTER to use a default square)
///   -> scan animation -> markers appear (green OK / red X / amber ?)
///   click a marker   detail card: expected vs found + what to do
///   F               "installer fixes it" (Wizard: sets everything to OK)
///   S               rescan -> "Installation verified", controller reports Reader 1 ONLINE
///   1-9             cycle a terminal's result (OK -> X -> ?), R reset, H hide operator hint
///
/// Terminal positions (u,v) are fractions of the board: (0,0) = top-left corner, (1,1) = bottom-right.
/// Tune them in Play mode until markers sit on the real terminals, then right-click the component ->
/// Copy Component, stop Play, right-click -> Paste Component Values (Play-mode edits are otherwise lost).
/// </summary>
public class VerificationDemo : MonoBehaviour
{
    public enum Check { Pass, Fail, Unclear }
    enum Phase { Aim, Locate, Scanning, Results }

    [System.Serializable]
    public class Terminal
    {
        public string label = "Terminal";
        public string expected = "Wire colour";
        public string foundWhenWrong = "Wrong wire";
        [Range(0f, 1f)] public float u = 0.5f;
        [Range(0f, 1f)] public float v = 0.5f;
        public Check result = Check.Pass;
    }

    [Header("Background")]
    [Tooltip("Straight-on photo of the opened A1610. Used unless the webcam is on.")]
    public Texture boardPhoto;
    public bool useWebcam = false;

    [Header("Terminals  (PLACEHOLDERS: set labels/colours from the wiring drawing)")]
    public List<Terminal> terminals = new List<Terminal>
    {
        new Terminal { label = "Reader 1: +12V",   expected = "Red",    foundWhenWrong = "Nothing connected",    u = 0.10f, v = 0.30f },
        new Terminal { label = "Reader 1: GND",    expected = "Black",  foundWhenWrong = "Nothing connected",    u = 0.10f, v = 0.36f },
        new Terminal { label = "Reader 1: A",      expected = "Green",  foundWhenWrong = "Yellow (A/B swapped)", u = 0.10f, v = 0.42f, result = Check.Fail },
        new Terminal { label = "Reader 1: B",      expected = "Yellow", foundWhenWrong = "Green (A/B swapped)",  u = 0.10f, v = 0.48f, result = Check.Fail },
        new Terminal { label = "Door 1: Monitor",  expected = "Blue",   foundWhenWrong = "Loose strand",         u = 0.10f, v = 0.62f, result = Check.Unclear },
        new Terminal { label = "Relay 1: Lock",    expected = "Orange", foundWhenWrong = "Wired to Relay 2",     u = 0.45f, v = 0.12f },
    };

    [Header("Timing")]
    public float scanSeconds = 2.5f;

    Phase phase = Phase.Aim;
    WebCamTexture cam;
    readonly Vector2[] corners = new Vector2[4];
    int cornersSet;
    float scanStart;
    int selected = -1;
    bool showHint = true;
    Check[] seeded;
    Texture2D px;
    GUIStyle titleStyle, textStyle, markStyle, hintStyle;

    static readonly Color Green = new Color(0.20f, 0.80f, 0.35f);
    static readonly Color Red   = new Color(0.90f, 0.25f, 0.25f);
    static readonly Color Amber = new Color(1.00f, 0.70f, 0.10f);
    static readonly Color Cyan  = new Color(0.20f, 0.85f, 1.00f);
    static readonly Color Dark  = new Color(0f, 0f, 0f, 0.72f);

    void Start()
    {
        px = Texture2D.whiteTexture;
        seeded = new Check[terminals.Count];
        for (int i = 0; i < terminals.Count; i++) seeded[i] = terminals[i].result;

        if (useWebcam && WebCamTexture.devices.Length > 0)
        {
            cam = new WebCamTexture(1280, 720);
            cam.Play();
        }
    }

    void OnDestroy()
    {
        if (cam != null) cam.Stop();
    }

    void Update()
    {
        if (phase == Phase.Scanning && Time.time - scanStart >= scanSeconds) phase = Phase.Results;
    }

    // ---------------------------------------------------------------- drawing

    void OnGUI()
    {
        InitStyles();
        HandleInput(Event.current);

        Rect screen = new Rect(0, 0, Screen.width, Screen.height);
        Texture bg = cam != null ? (Texture)cam : boardPhoto;
        if (bg != null) GUI.DrawTexture(screen, bg, ScaleMode.ScaleAndCrop);
        else Fill(screen, new Color(0.12f, 0.12f, 0.14f));

        switch (phase)
        {
            case Phase.Aim:
                Banner("Point the camera at the open door controller", Cyan);
                break;
            case Phase.Locate:
                Banner("Locating controller...", Cyan);
                for (int i = 0; i < cornersSet; i++)
                    Fill(new Rect(corners[i].x - 6, corners[i].y - 6, 12, 12), Cyan);
                break;
            case Phase.Scanning:
                DrawScan();
                break;
            case Phase.Results:
                DrawResults();
                break;
        }

        if (showHint) OperatorHint();
    }

    void DrawScan()
    {
        float p = Mathf.Clamp01((Time.time - scanStart) / scanSeconds);
        DrawOutline(Cyan);
        DrawLine(Map(0f, p), Map(1f, p), Cyan, 4f);
        for (int i = 0; i < terminals.Count; i++)
            if (terminals[i].v <= p) DrawMarker(i);
        Banner("Checking wiring... " + Mathf.RoundToInt(p * 100f) + "%", Cyan);
    }

    void DrawResults()
    {
        int ok = 0, bad = 0, unclear = 0;
        foreach (Terminal t in terminals)
        {
            if (t.result == Check.Pass) ok++;
            else if (t.result == Check.Fail) bad++;
            else unclear++;
        }
        bool allGood = bad == 0 && unclear == 0;

        DrawOutline(allGood ? Green : Cyan);
        for (int i = 0; i < terminals.Count; i++) DrawMarker(i);

        if (allGood) Banner("Installation verified", Green);
        else Banner(bad + " wrong     " + unclear + " unclear     " + ok + " OK", bad > 0 ? Red : Amber);

        // Second source of truth: what the controller itself reports.
        // Faked here; later read from the device instead.
        bool readerOnline = ReaderOk();
        Panel(new Rect(Screen.width - 340, 90, 320, 70),
              "Controller reports\nReader 1: " + (readerOnline ? "ONLINE" : "OFFLINE"),
              readerOnline ? Green : Red);

        if (selected >= 0 && selected < terminals.Count) DrawDetail(terminals[selected]);
        else if (!allGood) Panel(new Rect(20, Screen.height - 120, 420, 50), "Tap a red or amber marker for details", Color.white);
    }

    void DrawMarker(int i)
    {
        Terminal t = terminals[i];
        Vector2 p = Map(t.u, t.v);
        float r = i == selected ? 20f : 15f;
        Rect box = new Rect(p.x - r, p.y - r, r * 2f, r * 2f);

        if (i == selected) Fill(new Rect(box.x - 3, box.y - 3, box.width + 6, box.height + 6), Color.white);
        Fill(box, Col(t.result));
        GUI.Label(box, t.result == Check.Pass ? "OK" : t.result == Check.Fail ? "X" : "?", markStyle);

        if (t.result != Check.Pass)
        {
            Rect tag = new Rect(p.x + r + 6, p.y - 13, 200, 26);
            Fill(tag, Dark);
            GUI.Label(new Rect(tag.x + 6, tag.y, tag.width - 6, tag.height), t.label, textStyle);
        }
    }

    void DrawDetail(Terminal t)
    {
        string found = t.result == Check.Pass ? t.expected
                     : t.result == Check.Fail ? t.foundWhenWrong
                     : "Can't see clearly";
        string action = t.result == Check.Pass ? "No action needed"
                      : t.result == Check.Fail ? "Move the wire to the right terminal, then rescan"
                      : "Move closer or add light, then rescan";
        Panel(new Rect(20, Screen.height - 200, 460, 130),
              t.label + "\nExpected: " + t.expected + "\nFound: " + found + "\n" + action,
              Col(t.result));
    }

    void Banner(string text, Color accent)
    {
        Rect r = new Rect(Screen.width * 0.5f - 300, 20, 600, 56);
        Fill(r, Dark);
        Fill(new Rect(r.x, r.yMax - 4, r.width, 4), accent);
        GUI.Label(r, text, titleStyle);
    }

    void Panel(Rect r, string text, Color accent)
    {
        Fill(r, Dark);
        Fill(new Rect(r.x, r.y, 6, r.height), accent);
        GUI.Label(new Rect(r.x + 16, r.y + 6, r.width - 22, r.height - 12), text, textStyle);
    }

    void OperatorHint()
    {
        string h = "Operator:  SPACE start   click 4 corners (or ENTER)   1-" + terminals.Count +
                   " change terminal   F fix all   S rescan   R reset   H hide";
        GUI.color = new Color(1f, 1f, 1f, 0.6f);
        GUI.Label(new Rect(10, Screen.height - 28, Screen.width - 20, 24), h, hintStyle);
        GUI.color = Color.white;
    }

    void DrawOutline(Color c)
    {
        for (int i = 0; i < 4; i++) DrawLine(corners[i], corners[(i + 1) % 4], c, 3f);
    }

    void DrawLine(Vector2 a, Vector2 b, Color c, float width)
    {
        Matrix4x4 saved = GUI.matrix;
        float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(angle, a);
        Fill(new Rect(a.x, a.y - width * 0.5f, Vector2.Distance(a, b), width), c);
        GUI.matrix = saved;
    }

    void Fill(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, px);
        GUI.color = Color.white;
    }

    // ---------------------------------------------------------------- input

    void HandleInput(Event e)
    {
        if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
        {
            KeyCode k = e.keyCode;
            if (k == KeyCode.Space && phase == Phase.Aim) { phase = Phase.Locate; cornersSet = 0; }
            else if ((k == KeyCode.Return || k == KeyCode.KeypadEnter) && phase == Phase.Locate) { DefaultCorners(); StartScan(); }
            else if (k == KeyCode.S && cornersSet == 4) StartScan();
            else if (k == KeyCode.F) { foreach (Terminal t in terminals) t.result = Check.Pass; }
            else if (k == KeyCode.R) ResetDemo();
            else if (k == KeyCode.H) showHint = !showHint;
            else if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9)
            {
                int i = k - KeyCode.Alpha1;
                if (i < terminals.Count)
                {
                    terminals[i].result = (Check)(((int)terminals[i].result + 1) % 3);
                    selected = i;
                }
            }
            e.Use();
        }
        else if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (phase == Phase.Locate && cornersSet < 4)
            {
                corners[cornersSet++] = e.mousePosition;
                if (cornersSet == 4) StartScan();
                e.Use();
            }
            else if (phase == Phase.Results)
            {
                selected = -1;
                float best = 45f;
                for (int i = 0; i < terminals.Count; i++)
                {
                    float d = Vector2.Distance(e.mousePosition, Map(terminals[i].u, terminals[i].v));
                    if (d < best) { best = d; selected = i; }
                }
                e.Use();
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    void StartScan()
    {
        selected = -1;
        scanStart = Time.time;
        phase = Phase.Scanning;
    }

    void ResetDemo()
    {
        for (int i = 0; i < terminals.Count && i < seeded.Length; i++) terminals[i].result = seeded[i];
        phase = Phase.Aim;
        cornersSet = 0;
        selected = -1;
    }

    void DefaultCorners()
    {
        float s = Mathf.Min(Screen.width, Screen.height) * 0.6f;
        float x = (Screen.width - s) * 0.5f, y = (Screen.height - s) * 0.5f;
        corners[0] = new Vector2(x, y);
        corners[1] = new Vector2(x + s, y);
        corners[2] = new Vector2(x + s, y + s);
        corners[3] = new Vector2(x, y + s);
        cornersSet = 4;
    }

    // Bilinear map from board coordinates (u,v) to screen, using the 4 clicked corners.
    Vector2 Map(float u, float v)
    {
        Vector2 top = Vector2.Lerp(corners[0], corners[1], u);
        Vector2 bottom = Vector2.Lerp(corners[3], corners[2], u);
        return Vector2.Lerp(top, bottom, v);
    }

    bool ReaderOk()
    {
        foreach (Terminal t in terminals)
            if (t.label.StartsWith("Reader") && t.result == Check.Fail) return false;
        return true;
    }

    static Color Col(Check c)
    {
        return c == Check.Pass ? Green : c == Check.Fail ? Red : Amber;
    }

    void InitStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        titleStyle.normal.textColor = Color.white;
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = true };
        textStyle.normal.textColor = Color.white;
        markStyle = new GUIStyle(titleStyle) { fontSize = 13 };
        hintStyle = new GUIStyle(textStyle) { fontSize = 12 };
    }
}
