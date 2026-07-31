/*
This program is free software: you can redistribute it and/or modify it under
the terms of the GNU General Public License as published by the Free Software
Foundation, either version 3 of the License, or (at your option) any later version.
*/

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Retro Turbo Vision–style world HUD to tune steering-wheel settings
/// (rotation-axis, max-angle, steer-gain, steer-anti-deadzone, steer-digital, devices.type).
/// Opened with the Menu button while a play-session wheel is active.
/// Visual language matches <see cref="MRPlacementInfoHUD"/>.
/// </summary>
public class SteeringWheelSettingsHUD : MonoBehaviour
{
    const string LogPrefix = "[SteeringWheelSettingsHUD]";
    const int InnerWidth = 34;
    const string ColorTitle = "FFFF55";
    const string ColorHeader = "55FFFF";
    const string ColorBody = "FFFFFF";
    const string ColorSel = "55FF55";

    enum Row
    {
        InputDevice = 0,
        RotationAxis = 1,
        MaxAngle = 2,
        SteerGain = 3,
        AntiDeadzone = 4,
        SteerDigital = 5,
        ResetAll = 6,
        Count = 7,
    }

    public static SteeringWheelSettingsHUD Instance { get; private set; }

    [SerializeField] Vector2 panelSizePixels = new Vector2(380f, 420f);
    [SerializeField] float worldScale = 0.0006f;
    [SerializeField] Vector3 localOffsetFromController = new Vector3(-0.13f, 0.08f, 0.02f);
    [SerializeField] float borderThicknessPixels = 4f;
    [SerializeField] float cornerRadiusPixels = 8f;
    [SerializeField] Color backgroundColor = new Color(0f, 0f, 0.67f, 1f);
    [SerializeField] Color borderColor = new Color(0.33f, 1f, 1f, 1f);
    [SerializeField] Color textColor = Color.white;
    [SerializeField] int fontSize = 16;
    [SerializeField] float navRepeatDelay = 0.18f;
    [SerializeField] float stickDeadzone = 0.55f;

    Canvas canvas;
    RectTransform canvasRect;
    Text bodyText;
    Sprite roundedSprite;
    bool visible;
    SteeringWheel target;
    Row selected = Row.InputDevice;
    float navCooldown;
    bool stickXWasOut;
    bool stickYWasOut;

    public static bool IsOpen => Instance != null && Instance.visible;

    public SteeringWheel Target => target;

    public static SteeringWheelSettingsHUD Ensure()
    {
        if (Instance != null)
            return Instance;

        GameObject go = new GameObject("SteeringWheelSettingsHUD");
        Instance = go.AddComponent<SteeringWheelSettingsHUD>();
        DontDestroyOnLoad(go);
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildUi();
        Hide();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (roundedSprite != null)
        {
            if (roundedSprite.texture != null)
                Destroy(roundedSprite.texture);
            Destroy(roundedSprite);
            roundedSprite = null;
        }
    }

    void LateUpdate()
    {
        if (!visible || canvas == null)
            return;

        if (target == null || !target.InteractionEnabled)
        {
            Hide();
            return;
        }

        UpdatePoseAboveController();
        HandleInput();
        RefreshText();
    }

    public void Show(SteeringWheel wheel)
    {
        if (wheel == null)
            return;

        target = wheel;
        selected = Row.InputDevice;
        navCooldown = 0f;
        stickXWasOut = false;
        stickYWasOut = false;

        if (canvas == null)
            BuildUi();

        visible = true;
        if (canvas != null)
            canvas.gameObject.SetActive(true);

        RefreshText();
        UpdatePoseAboveController();
        ConfigManager.WriteConsole($"{LogPrefix} open cab={wheel.CabinetKey}");
    }

    public void Hide()
    {
        visible = false;
        target = null;
        if (canvas != null)
            canvas.gameObject.SetActive(false);
    }

    public void Toggle(SteeringWheel wheel)
    {
        if (wheel == null)
            return;

        if (visible && target == wheel)
            Hide();
        else
            Show(wheel);
    }

    void HandleInput()
    {
        navCooldown -= Time.unscaledDeltaTime;

        if (WasClosePressed())
        {
            Hide();
            return;
        }

        Vector2 stick = ReadRightStick();
        bool yOut = Mathf.Abs(stick.y) >= stickDeadzone;
        bool xOut = Mathf.Abs(stick.x) >= stickDeadzone;

        if (yOut && !stickYWasOut && navCooldown <= 0f)
        {
            int dir = stick.y > 0f ? -1 : 1;
            selected = (Row)(((int)selected + dir + (int)Row.Count) % (int)Row.Count);
            navCooldown = navRepeatDelay;
        }

        if (xOut && !stickXWasOut && navCooldown <= 0f)
        {
            int dir = stick.x > 0f ? 1 : -1;
            ApplyAdjust(dir);
            navCooldown = navRepeatDelay;
        }

        stickYWasOut = yOut;
        stickXWasOut = xOut;

        if (WasConfirmPressed())
        {
            if (selected == Row.SteerDigital || selected == Row.RotationAxis || selected == Row.InputDevice)
                ApplyAdjust(1);
            else if (selected == Row.ResetAll)
                ResetAll();
        }

        if (WasResetPressed())
            ResetAll();
    }

    void ApplyAdjust(int direction)
    {
        if (target == null || direction == 0)
            return;

        switch (selected)
        {
            case Row.InputDevice:
                target.CycleInputDeviceType(direction);
                break;
            case Row.RotationAxis:
                target.CycleRotationAxis(direction);
                break;
            case Row.MaxAngle:
                target.AdjustMaxAngleDegrees(direction * 5f);
                break;
            case Row.SteerGain:
                target.AdjustSteerGain(direction * 0.1f);
                break;
            case Row.AntiDeadzone:
                target.AdjustSteerAntiDeadzone(direction * 0.05f);
                break;
            case Row.SteerDigital:
                target.ToggleSteerDigital();
                break;
            case Row.ResetAll:
                if (direction != 0)
                    ResetAll();
                break;
        }
    }

    void ResetAll()
    {
        if (target == null)
            return;

        target.ResetToYamlBaseline();
        ConfigManager.WriteConsole($"{LogPrefix} reset to YAML baseline cab={target.CabinetKey}");
    }

    void RefreshText()
    {
        if (bodyText == null)
            return;

        bodyText.text = BuildPanelText();
    }

    string BuildPanelText()
    {
        string bar = "+" + new string('-', InnerWidth) + "+";
        string mid = "+" + new string('-', InnerWidth) + "+";
        string divider = "  " + new string('-', InnerWidth - 2);

        string title = "STEERING SETUP";
        string cab = target != null ? target.CabinetKey : "WHEEL";
        if (cab.Length > InnerWidth - 2)
            cab = cab.Substring(0, InnerWidth - 2);

        float maxAngle = target != null ? target.MaxAngleDegrees : 90f;
        float gain = target != null ? target.SteerGain : 1f;
        float anti = target != null ? target.SteerAntiDeadzone : 0f;
        bool digital = target == null || target.SteerDigital;
        string axis = target != null ? target.RotationAxisLabel : "z";
        string device = target != null ? target.InputDeviceType : "gamepad";
        if (device.Length > 14)
            device = device.Substring(0, 14);

        var sb = new System.Text.StringBuilder(1600);
        sb.Append(Paint(bar, ColorHeader)).Append('\n');
        sb.Append(RowLine(Center(title), ColorTitle)).Append('\n');
        sb.Append(Paint(mid, ColorHeader)).Append('\n');
        sb.Append(RowLine(PadColumns("CONTROLS", "ACTION", 18), ColorHeader)).Append('\n');
        sb.Append(RowLine(divider, ColorHeader)).Append('\n');
        sb.Append(RowLine(PadColumns("[STICK U/D]", "SELECT", 18), ColorBody)).Append('\n');
        sb.Append(RowLine(PadColumns("[STICK L/R]", "ADJUST", 18), ColorBody)).Append('\n');
        sb.Append(RowLine(PadColumns("[A]", "TOGGLE/OK", 18), ColorBody)).Append('\n');
        sb.Append(RowLine(PadColumns("[Y]", "RESET ALL", 18), ColorBody)).Append('\n');
        sb.Append(RowLine(PadColumns("[B]/MENU]", "CLOSE", 18), ColorBody)).Append('\n');
        sb.Append(Paint(mid, ColorHeader)).Append('\n');
        sb.Append(RowLine(Dotted("CABINET", cab.ToUpperInvariant()), ColorBody)).Append('\n');
        sb.Append(OptionLine(Row.InputDevice, "DEVICE", device)).Append('\n');
        sb.Append(OptionLine(Row.RotationAxis, "ROTATION-AXIS", axis)).Append('\n');
        sb.Append(OptionLine(Row.MaxAngle, "MAX-ANGLE", maxAngle.ToString("0") + " deg")).Append('\n');
        sb.Append(OptionLine(Row.SteerGain, "STEER-GAIN", gain.ToString("0.0"))).Append('\n');
        sb.Append(OptionLine(Row.AntiDeadzone, "ANTI-DEADZONE", anti.ToString("0.00"))).Append('\n');
        sb.Append(OptionLine(Row.SteerDigital, "STEER-DIGITAL", digital ? "ON" : "OFF")).Append('\n');
        sb.Append(OptionLine(Row.ResetAll, "RESET", "YAML DEFAULTS")).Append('\n');
        sb.Append(Paint(bar, ColorHeader));
        return sb.ToString();
    }

    string OptionLine(Row row, string key, string value)
    {
        bool on = selected == row;
        string prefix = on ? "> " : "  ";
        string hex = on ? ColorSel : ColorBody;
        return RowLine(Dotted(prefix + key, value), hex);
    }

    static string Paint(string content, string hex) =>
        "<color=#" + hex + ">" + content + "</color>";

    static string RowLine(string content, string hex)
    {
        if (content == null)
            content = string.Empty;
        if (content.Length > InnerWidth)
            content = content.Substring(0, InnerWidth);
        content = content.PadRight(InnerWidth);
        return Paint("|", ColorHeader) + Paint(content, hex) + Paint("|", ColorHeader);
    }

    static string Center(string content)
    {
        if (content == null)
            content = string.Empty;
        if (content.Length >= InnerWidth)
            return content.Substring(0, InnerWidth);

        int pad = InnerWidth - content.Length;
        int left = pad / 2;
        return new string(' ', left) + content + new string(' ', pad - left);
    }

    static string PadColumns(string left, string right, int leftWidth)
    {
        left = "  " + (left ?? string.Empty);
        right = right ?? string.Empty;
        if (left.Length < leftWidth)
            left = left.PadRight(leftWidth);
        else if (left.Length > leftWidth)
            left = left.Substring(0, leftWidth);
        return left + right;
    }

    static string Dotted(string key, string value)
    {
        const int keyWidth = 18;
        key = key ?? string.Empty;
        if (!key.StartsWith(" ") && !key.StartsWith(">"))
            key = "  " + key;

        if (key.Length < keyWidth)
            key = key.PadRight(keyWidth, '.');
        else
            key = key.Substring(0, keyWidth - 1) + ".";

        value = value ?? string.Empty;
        int maxValue = Mathf.Max(1, InnerWidth - keyWidth - 1);
        if (value.Length > maxValue)
            value = value.Substring(0, maxValue);

        return key + " " + value;
    }

    void UpdatePoseAboveController()
    {
        if (canvasRect == null)
            return;

        ResolveAnchor(out Vector3 pos, out Vector3 lookFrom);
        canvasRect.position = pos;

        Vector3 toViewer = lookFrom - pos;
        if (toViewer.sqrMagnitude > 0.0001f)
            canvasRect.rotation = Quaternion.LookRotation(-toViewer.normalized, Vector3.up);
    }

    void ResolveAnchor(out Vector3 worldPos, out Vector3 viewerPos)
    {
        Transform pointer = ResolveRightControllerTransform();
        Transform viewer = Camera.main != null ? Camera.main.transform : null;
        viewerPos = viewer != null ? viewer.position : (pointer != null ? pointer.position : transform.position);

        if (pointer != null)
        {
            worldPos = pointer.position
                + pointer.right * localOffsetFromController.x
                + pointer.up * localOffsetFromController.y
                + pointer.forward * localOffsetFromController.z;
            return;
        }

        if (viewer != null)
        {
            worldPos = viewer.position
                + viewer.right * localOffsetFromController.x
                + viewer.up * (0.05f + localOffsetFromController.y)
                + viewer.forward * (0.55f + localOffsetFromController.z);
            return;
        }

        worldPos = transform.position + localOffsetFromController;
    }

    void BuildUi()
    {
        if (canvas != null)
            return;

        GameObject canvasGo = new GameObject("SteeringSettingsCanvas");
        canvasGo.transform.SetParent(transform, false);

        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 85;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        canvasGo.AddComponent<GraphicRaycaster>().enabled = false;

        canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = panelSizePixels;
        canvasRect.localScale = Vector3.one * worldScale;

        GameObject borderGo = new GameObject("Border");
        borderGo.transform.SetParent(canvasRect, false);
        RectTransform borderRect = borderGo.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;
        Image borderImage = borderGo.AddComponent<Image>();
        borderImage.color = borderColor;
        borderImage.sprite = EnsureRoundedSprite();
        borderImage.type = Image.Type.Sliced;

        GameObject panelGo = new GameObject("Background");
        panelGo.transform.SetParent(borderRect, false);
        RectTransform panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        float t = Mathf.Max(1f, borderThicknessPixels);
        panelRect.offsetMin = new Vector2(t, t);
        panelRect.offsetMax = new Vector2(-t, -t);
        Image panelImage = panelGo.AddComponent<Image>();
        panelImage.color = backgroundColor;
        panelImage.sprite = EnsureRoundedSprite();
        panelImage.type = Image.Type.Sliced;
        panelGo.AddComponent<RectMask2D>();

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(panelRect, false);
        RectTransform textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 12f);
        textRect.offsetMax = new Vector2(-10f, -12f);

        bodyText = textGo.AddComponent<Text>();
        bodyText.font = ResolveMonospaceFont();
        bodyText.fontSize = fontSize;
        bodyText.color = textColor;
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.horizontalOverflow = HorizontalWrapMode.Overflow;
        bodyText.verticalOverflow = VerticalWrapMode.Truncate;
        bodyText.supportRichText = true;
        bodyText.lineSpacing = 0.95f;

        ConfigManager.WriteConsole($"{LogPrefix} panel built");
    }

    static Font ResolveMonospaceFont()
    {
        string[] candidates =
#if UNITY_ANDROID && !UNITY_EDITOR
        {
            "Droid Sans Mono",
            "Noto Sans Mono",
            "DroidSansMono",
            "Courier",
            "monospace"
        };
#else
        {
            "Consolas",
            "Courier New",
            "Lucida Console",
            "Liberation Mono",
            "Droid Sans Mono",
            "Courier"
        };
#endif

        Font font = Font.CreateDynamicFontFromOSFont(candidates, 18);
        if (font != null)
            return font;

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    Sprite EnsureRoundedSprite()
    {
        if (roundedSprite != null)
            return roundedSprite;

        int size = 64;
        int radius = Mathf.Clamp(Mathf.RoundToInt(cornerRadiusPixels), 4, size / 2 - 1);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "SteerHudRounded";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 fill = new Color32(255, 255, 255, 255);
        float r = radius;
        float rSq = r * r;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inside = true;
                float cx = x < radius ? r - 0.5f - x : (x >= size - radius ? x - (size - r - 0.5f) : 0f);
                float cy = y < radius ? r - 0.5f - y : (y >= size - radius ? y - (size - r - 0.5f) : 0f);
                if ((x < radius || x >= size - radius) && (y < radius || y >= size - radius))
                    inside = (cx * cx + cy * cy) <= rSq;

                pixels[y * size + x] = inside ? fill : clear;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        float border = radius;
        roundedSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        return roundedSprite;
    }

    static Transform ResolveRightControllerTransform()
    {
        ChangeControls controls = FindObjectOfType<ChangeControls>();
        if (controls != null && controls.RightHand != null)
            return controls.RightHand.transform;

        PlayerController pc = FindObjectOfType<PlayerController>();
        if (pc != null && pc.xrorigin != null)
        {
            foreach (Transform t in pc.xrorigin.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name.ToLowerInvariant();
                if (name.Contains("right") && (name.Contains("controller") || name.Contains("hand")))
                    return t;
            }
        }

        return null;
    }

    static Vector2 ReadRightStick()
    {
#if UNITY_EDITOR
        Vector2 editor = Vector2.zero;
        if (MREditorInput.IsHeld(KeyCode.UpArrow) || MREditorInput.IsHeld(KeyCode.W))
            editor.y += 1f;
        if (MREditorInput.IsHeld(KeyCode.DownArrow) || MREditorInput.IsHeld(KeyCode.S))
            editor.y -= 1f;
        if (MREditorInput.IsHeld(KeyCode.RightArrow) || MREditorInput.IsHeld(KeyCode.D))
            editor.x += 1f;
        if (MREditorInput.IsHeld(KeyCode.LeftArrow) || MREditorInput.IsHeld(KeyCode.A))
            editor.x -= 1f;
        if (editor.sqrMagnitude > 0.01f)
            return editor;
#endif
#if !UNITY_EDITOR
        try
        {
            return OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        }
        catch
        {
            // fall through
        }
#endif
        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
            return axis;

        return Vector2.zero;
    }

    static bool WasConfirmPressed()
    {
#if UNITY_EDITOR
        if (MREditorInput.WasPressed(KeyCode.Return) || MREditorInput.WasPressed(KeyCode.KeypadEnter)
            || MREditorInput.WasPressed(KeyCode.Space))
            return true;
#endif
#if !UNITY_EDITOR
        try
        {
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
                return true;
        }
        catch
        {
        }
#endif
        return false;
    }

    static bool WasResetPressed()
    {
#if UNITY_EDITOR
        if (MREditorInput.WasPressed(KeyCode.R))
            return true;
#endif
#if !UNITY_EDITOR
        try
        {
            // Left Y
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
                return true;
        }
        catch
        {
        }
#endif
        return false;
    }

    static bool WasClosePressed()
    {
#if UNITY_EDITOR
        if (MREditorInput.WasPressed(KeyCode.Escape) || MREditorInput.WasPressed(KeyCode.B))
            return true;
#endif
#if !UNITY_EDITOR
        try
        {
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
                return true;
        }
        catch
        {
        }
#endif
        return false;
    }

    /// <summary>Menu / Oculus button — used by <see cref="SteeringWheel"/> to toggle this HUD.</summary>
    public static bool WasMenuButtonPressed()
    {
        if (menuPollFrame == Time.frameCount)
            return menuPressedThisFrame;

        menuPollFrame = Time.frameCount;
        menuPressedThisFrame = PollMenuButtonEdge();
        return menuPressedThisFrame;
    }

    static int menuPollFrame = -1;
    static bool menuPressedThisFrame;
    static bool menuButtonWasDown;

    static bool PollMenuButtonEdge()
    {
#if UNITY_EDITOR
        if (MREditorInput.WasPressed(KeyCode.M))
            return true;
#endif
#if !UNITY_EDITOR
        try
        {
            // Quest menu (≡) on left controller.
            if (OVRInput.GetDown(OVRInput.Button.Start))
                return true;
        }
        catch
        {
        }
#endif
        InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (left.isValid && left.TryGetFeatureValue(CommonUsages.menuButton, out bool pressed) && pressed)
        {
            if (!menuButtonWasDown)
            {
                menuButtonWasDown = true;
                return true;
            }

            return false;
        }

        menuButtonWasDown = false;
        return false;
    }
}
