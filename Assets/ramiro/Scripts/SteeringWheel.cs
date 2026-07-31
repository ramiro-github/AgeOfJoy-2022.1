/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// VR steering wheel on a cabinet part: latch with one or two hands via the <b>grip</b> buttons
/// (not index trigger — free for game inputs like GT rear-view).
/// Press grip once while hovering to hold; press again to release (toggle, no need to keep squeezing).
/// Position stays fixed; only rotates around a local axis. Uses hover + grip so XR never reparents the wheel.
/// Haptics (default on): grab/release pulse, center notch, near-lock cue, hard-lock stop.
/// Wired onto the existing GLB <c>steering-wheel</c> mesh by <see cref="CabinetSteeringWheelSpawner"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(1000)]
public class SteeringWheel : MonoBehaviour
{
    const string LogPrefix = "[SteeringWheel]";
    static readonly string[] GrabInteractionLayers = { "InteractablePart" };
    const string GrabPhysicsLayerName = "InteractablePart";

    /// <summary>True while any steering wheel in the scene has at least one hand holding it.</summary>
    public static bool IsAnyHandHolding { get; private set; }

    static int holdSessionCount;
    bool holdSessionActive;

    [Header("Axis")]
    [Tooltip("Local axis the wheel spins around (Forward = face of a typical rim).")]
    [SerializeField] Vector3 localRotationAxis = Vector3.forward;

    [Header("Limits")]
    [Tooltip("Maximum turn from center, in degrees (e.g. 90 each way).")]
    [SerializeField] float maxAngleDegrees = 90f;
    [Tooltip("Degrees per second back to center when released. 0 = leave the wheel where it was.")]
    [SerializeField] float returnSpeedDegrees = 180f;
    [Tooltip("Smooth applied rotation (0 = instant).")]
    [SerializeField] float rotationSmoothing = 18f;

    [Header("Grab (grip toggle)")]
    [Tooltip("Grip axis above this counts as pressed (rising edge toggles grab/release).")]
    [SerializeField] float gripPressThreshold = 0.55f;
    [Tooltip("After a press, grip must fall below this before the next toggle edge is armed.")]
    [SerializeField] float gripReleaseThreshold = 0.35f;
    [SerializeField] bool hideHandsOnGrab = true;
    [SerializeField] bool logDebug;
    [Header("Grab marker")]
    [Tooltip("Small sphere snapped onto the wheel rim at grab; stays parented and rotates with the wheel.")]
    [SerializeField] bool showGrabMarkers = true;
    [SerializeField] float grabMarkerRadius = 0.01f;
    [SerializeField] Color grabMarkerColor = new Color(1f, 0.85f, 0.15f, 0.95f);

    [Header("Game input")]
    [Tooltip("Maps wheel angle to thumbstick X (right stick / JOYPAD left-right). Invert if turn direction feels wrong.")]
    [SerializeField] bool invertSteering;
    [Tooltip("Multiplies normalized wheel axis before clamp ±1. >1 reaches full stick sooner (YAML steer-gain).")]
    [SerializeField] float steerGain = 1f;
    [Tooltip("When >0, remaps non-zero axis to start at this magnitude (0–1) to beat game stick deadzones (YAML steer-anti-deadzone).")]
    [SerializeField] [Range(0f, 0.95f)] float steerAntiDeadzone = 0f;
    [Tooltip("Also press JOYPAD left/right from the wheel. Disable for NeGcon / proportional racers (YAML steer-digital).")]
    [SerializeField] bool steerDigital = true;
    [Tooltip("Keep feeding axis while the wheel springs back to center.")]
    [SerializeField] bool driveAxisWhileReturning = true;

    [Header("Haptics")]
    [SerializeField] bool enableHaptics = true;
    [Tooltip("Minimum seconds between discrete haptic events (center / near-lock).")]
    [SerializeField] float hapticEventCooldown = 0.1f;
    [Tooltip("Fraction of max-angle where the soft near-lock cue fires.")]
    [SerializeField] [Range(0.5f, 0.98f)] float nearLockRatio = 0.88f;

    /// <summary>When false, XR grab and game axis are off (cabinet idle until play session).</summary>
    bool interactionEnabled = true;

    XRSimpleInteractable hoverInteractable;
    Rigidbody body;
    SphereCollider interactionCollider;
    LibretroControlMap controlMap;
    LibretroControlMap localControlMap;

    /// <summary>World-space outer rim radius from mesh (cached); interaction collider is larger for hover.</summary>
    float visualRimRadiusWorld = -1f;

    Vector3 homeLocalPosition;
    Quaternion homeLocalRotation;
    Vector3 homeLocalScale;

    float currentAngle;
    float displayedAngle;
    bool hasPreviousHandsAngle;
    float previousHandsAngle;

    readonly List<IXRHoverInteractor> hovering = new List<IXRHoverInteractor>();
    readonly List<HeldHand> heldHands = new List<HeldHand>();
    readonly List<Renderer> hiddenLeftRenderers = new List<Renderer>();
    readonly List<Renderer> hiddenRightRenderers = new List<Renderer>();

    // Edge latch for grip toggle (per hand) — press once = hold, press again = release.
    bool leftGripWasDown;
    bool rightGripWasDown;

    // Discrete steering haptics (grab / center / near-lock / hard lock).
    float hapticCooldownUntil;
    bool nearLockLatchedPositive;
    bool nearLockLatchedNegative;
    bool hardLockLatchedPositive;
    bool hardLockLatchedNegative;
    float leftHapticUntil;
    float rightHapticUntil;

    struct HeldHand
    {
        public IXRHoverInteractor Interactor;
        public Transform Follow;
        public bool IsLeft;
        public Transform Marker;
    }

    public float AngleDegrees => currentAngle;

    public float NormalizedValue =>
        maxAngleDegrees <= 0.0001f ? 0f : Mathf.Clamp(currentAngle / maxAngleDegrees, -1f, 1f);

    public bool IsGrabbed => heldHands.Count > 0;

    public bool InteractionEnabled => interactionEnabled;

    /// <summary>
    /// Enable/disable grab + axis output. Components stay attached; idle cabinets keep the wheel decorative.
    /// Driven from this cabinet's control-map enable (play session) — no hooks in curif controllers.
    /// </summary>
    public void SetInteractionEnabled(bool enabled)
    {
        if (interactionEnabled == enabled)
        {
            ApplyInteractionHardware(enabled);
            return;
        }

        interactionEnabled = enabled;

        if (!enabled)
            ForceReleaseAndReset();

        ApplyInteractionHardware(enabled);

        if (logDebug)
            ConfigManager.WriteConsole($"{LogPrefix} interaction={(enabled ? "on" : "off")}");
    }

    void ApplyInteractionHardware(bool enabled)
    {
        if (hoverInteractable != null)
            hoverInteractable.enabled = enabled;

        if (interactionCollider != null)
            interactionCollider.enabled = enabled;
    }

    void ForceReleaseAndReset()
    {
        for (int i = heldHands.Count - 1; i >= 0; i--)
            EndHoldAt(i);

        hovering.Clear();
        hasPreviousHandsAngle = false;
        leftGripWasDown = false;
        rightGripWasDown = false;
        ResetHapticState();
        StopAllHaptics();
        ClearControlMapOverride();
        currentAngle = 0f;
        displayedAngle = 0f;
        ApplyPose(0f);
        RestoreAllHandVisuals();
    }

    /// <summary>
    /// Mirror this cabinet's <see cref="LibretroControlMap"/> enable flag (set when a coin starts play).
    /// </summary>
    void SyncInteractionToPlaySession()
    {
        if (localControlMap == null)
            localControlMap = FindLocalControlMap();

        bool playing = localControlMap != null
            && localControlMap.actionMap != null
            && localControlMap.actionMap.enabled;

        if (playing != interactionEnabled)
            SetInteractionEnabled(playing);
    }

    LibretroControlMap FindLocalControlMap()
    {
        // Prefer the map on this cabinet's screen — not the global active-core pointer.
        Transform node = transform;
        while (node != null)
        {
            LibretroScreenController screen = node.GetComponentInChildren<LibretroScreenController>(true);
            if (screen != null)
            {
                LibretroControlMap onScreen = screen.GetComponent<LibretroControlMap>();
                if (onScreen != null)
                    return onScreen;
            }

            AGEBasicScreenController ageScreen = node.GetComponentInChildren<AGEBasicScreenController>(true);
            if (ageScreen != null)
            {
                LibretroControlMap onAge = ageScreen.GetComponent<LibretroControlMap>();
                if (onAge != null)
                    return onAge;
            }

            AGEBasicCabinetController ageCab = node.GetComponentInChildren<AGEBasicCabinetController>(true);
            if (ageCab != null)
            {
                LibretroControlMap onAgeCab = ageCab.GetComponent<LibretroControlMap>();
                if (onAgeCab != null)
                    return onAgeCab;
            }

            LibretroControlMap any = node.GetComponentInChildren<LibretroControlMap>(true);
            if (any != null)
                return any;

            node = node.parent;
        }

        return GetComponentInParent<LibretroControlMap>();
    }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        ConfigureRigidbody();
        EnsureInteractionCollider();
        hoverInteractable = EnsureHoverInteractable();
        hoverInteractable.hoverEntered.AddListener(OnHoverEntered);
        hoverInteractable.hoverExited.AddListener(OnHoverExited);
    }

    void Start()
    {
        CaptureHomePose();
        ApplyPose(displayedAngle);
        localControlMap = FindLocalControlMap();
        SyncInteractionToPlaySession();
    }

    void OnDestroy()
    {
        for (int i = heldHands.Count - 1; i >= 0; i--)
            DestroyGrabMarker(heldHands[i].Marker);
        heldHands.Clear();

        ClearControlMapOverride();
        NotifyHoldSessionEnded();
        StopAllHaptics();

        if (hoverInteractable == null)
            return;

        hoverInteractable.hoverEntered.RemoveListener(OnHoverEntered);
        hoverInteractable.hoverExited.RemoveListener(OnHoverExited);
    }

    void PushSteeringToControlMap()
    {
        bool publish = IsGrabbed
            || (driveAxisWhileReturning && Mathf.Abs(NormalizedValue) > 0.001f);

        if (!publish)
        {
            ClearControlMapOverride();
            return;
        }

        float axisX = invertSteering ? NormalizedValue : -NormalizedValue;
        axisX = ApplyAxisResponse(axisX);

        // VR/MR: only one libretro game runs at a time. Always write the maps the cores poll
        // (deviceIdsJoypad.controlMap == LibretroMameCore.ControlMap). Do not gate on cabinet
        // hierarchy — free cabinets may lack CabinetReplace and IsUnderSameCabinet fails in VR rooms.
        ApplyAxisOverride(LibretroMameCore.ControlMap, axisX);
        ApplyAxisOverride(LibretroFlycastCore.ControlMap, axisX);

        controlMap = ResolveControlMap();
        if (controlMap != null
            && controlMap != LibretroMameCore.ControlMap
            && controlMap != LibretroFlycastCore.ControlMap)
            ApplyAxisOverride(controlMap, axisX);

        if (logDebug)
        {
            ConfigManager.WriteConsole(
                $"{LogPrefix} axis={axisX:F2} angle={currentAngle:F1} " +
                $"mame={(LibretroMameCore.ControlMap != null)} flycast={(LibretroFlycastCore.ControlMap != null)} " +
                $"resolved={(controlMap != null)}");
        }
    }

    void ApplyAxisOverride(LibretroControlMap map, float axisX)
    {
        if (map == null)
            return;

        map.externalSteerX = axisX;
        map.externalSteerActive = true;
        map.externalSteerDigital = steerDigital;
    }

    void ClearControlMapOverride()
    {
        ClearAxisOverride(controlMap);
        ClearAxisOverride(LibretroMameCore.ControlMap);
        ClearAxisOverride(LibretroFlycastCore.ControlMap);
    }

    static void ClearAxisOverride(LibretroControlMap map)
    {
        if (map == null)
            return;

        map.externalSteerX = 0f;
        map.externalSteerActive = false;
        map.externalSteerDigital = true;
    }

    LibretroControlMap ResolveControlMap()
    {
        if (LibretroMameCore.ControlMap != null)
            return LibretroMameCore.ControlMap;

        if (LibretroFlycastCore.ControlMap != null)
            return LibretroFlycastCore.ControlMap;

        CabinetReplace cabinetReplace = GetComponentInParent<CabinetReplace>();
        if (cabinetReplace != null)
        {
            LibretroControlMap onCabinet = cabinetReplace.GetComponentInChildren<LibretroControlMap>(true);
            if (onCabinet != null)
                return onCabinet;
        }

        // Walk up and search children — CRT screen holds the map in VR.
        Transform node = transform;
        while (node != null)
        {
            LibretroScreenController screen = node.GetComponentInChildren<LibretroScreenController>(true);
            if (screen != null)
            {
                LibretroControlMap onScreen = screen.GetComponent<LibretroControlMap>();
                if (onScreen != null)
                    return onScreen;
            }

            LibretroControlMap any = node.GetComponentInChildren<LibretroControlMap>(true);
            if (any != null)
                return any;

            node = node.parent;
        }

        return GetComponentInParent<LibretroControlMap>();
    }

    void OnDisable()
    {
        ClearControlMapOverride();
        StopAllHaptics();
    }

    void Update()
    {
        SyncInteractionToPlaySession();
        UpdateHapticDecay();

        if (!interactionEnabled)
            return;

        UpdateGripHolds();
    }

    void LateUpdate()
    {
        if (!interactionEnabled)
            return;

        if (IsGrabbed)
        {
            UpdateHeldAngle();
        }
        else if (returnSpeedDegrees > 0f && Mathf.Abs(currentAngle) > 0.01f)
        {
            ReturnTowardCenter();
        }
        else if (!IsGrabbed)
        {
            currentAngle = 0f;
        }

        // Use linear Lerp — LerpAngle wraps at ±180 and breaks multi-turn wheels / return-to-center.
        if (IsGrabbed && rotationSmoothing > 0f)
        {
            float t = 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime);
            displayedAngle = Mathf.Lerp(displayedAngle, currentAngle, t);
        }
        else
        {
            displayedAngle = currentAngle;
        }

        if (!IsGrabbed && Mathf.Abs(displayedAngle) <= 0.01f)
        {
            displayedAngle = 0f;
            currentAngle = 0f;
        }

        ApplyPose(displayedAngle);
        PushSteeringToControlMap();
    }

    public void CaptureHomePose()
    {
        homeLocalPosition = transform.localPosition;
        homeLocalRotation = transform.localRotation;
        homeLocalScale = transform.localScale;
        currentAngle = 0f;
        displayedAngle = 0f;
    }

    /// <summary>
    /// YAML <c>rotation-axis</c>: x/y/z or right/up/forward. Empty keeps current axis (default Forward/Z).
    /// </summary>
    public void SetLocalRotationAxisFromYaml(string axisName)
    {
        if (string.IsNullOrWhiteSpace(axisName))
            return;

        switch (axisName.Trim().ToLowerInvariant())
        {
            case "x":
            case "right":
                localRotationAxis = Vector3.right;
                break;
            case "y":
            case "up":
                localRotationAxis = Vector3.up;
                break;
            case "z":
            case "forward":
                localRotationAxis = Vector3.forward;
                break;
            default:
                ConfigManager.WriteConsoleWarning(
                    $"{LogPrefix} unknown rotation-axis '{axisName}' (use x/y/z or right/up/forward)");
                break;
        }
    }

    /// <summary>
    /// YAML <c>max-angle</c>: max degrees from center each way. Ignored if null or &lt;= 0.
    /// </summary>
    public void SetMaxAngleDegreesFromYaml(float? degrees)
    {
        if (!degrees.HasValue || degrees.Value <= 0f)
            return;

        maxAngleDegrees = degrees.Value;
        currentAngle = Mathf.Clamp(currentAngle, -maxAngleDegrees, maxAngleDegrees);
        displayedAngle = Mathf.Clamp(displayedAngle, -maxAngleDegrees, maxAngleDegrees);
    }

    /// <summary>
    /// YAML <c>steer-gain</c>: multiplies normalized axis before clamp ±1. Ignored if null or &lt;= 0.
    /// </summary>
    public void SetSteerGainFromYaml(float? gain)
    {
        if (!gain.HasValue || gain.Value <= 0f)
            return;

        steerGain = gain.Value;
    }

    /// <summary>
    /// YAML <c>steer-anti-deadzone</c>: 0–1. Remaps non-zero axis to start at this magnitude.
    /// Ignored if null or &lt;= 0.
    /// </summary>
    public void SetSteerAntiDeadzoneFromYaml(float? antiDeadzone)
    {
        if (!antiDeadzone.HasValue || antiDeadzone.Value <= 0f)
            return;

        steerAntiDeadzone = Mathf.Clamp(antiDeadzone.Value, 0f, 0.95f);
    }

    /// <summary>
    /// YAML <c>steer-digital</c>: when true, wheel also asserts JOYPAD left/right.
    /// Set false for NeGcon / DualShock proportional steering.
    /// </summary>
    public void SetSteerDigitalFromYaml(bool? digital)
    {
        if (!digital.HasValue)
            return;

        steerDigital = digital.Value;
    }

    /// <summary>
    /// Gain then optional anti-deadzone: jumps past the game's stick deadzone near center.
    /// </summary>
    float ApplyAxisResponse(float normalizedSigned)
    {
        float axis = Mathf.Clamp(normalizedSigned * steerGain, -1f, 1f);
        float mag = Mathf.Abs(axis);
        if (mag <= 0.0001f)
            return 0f;

        if (steerAntiDeadzone > 0.0001f)
            mag = steerAntiDeadzone + (1f - steerAntiDeadzone) * mag;

        return Mathf.Sign(axis) * Mathf.Clamp01(mag);
    }

    void ConfigureRigidbody()
    {
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
    }

    void EnsureInteractionCollider()
    {
        // MeshCollider (even convex) is unreliable for XR Direct hover on detailed rims.
        // Cabinet GLBs often put the mesh collider on a child of the named part.
        MeshCollider[] meshes = GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < meshes.Length; i++)
        {
            if (meshes[i] != null)
                meshes[i].enabled = false;
        }

        interactionCollider = GetComponent<SphereCollider>();
        if (interactionCollider == null)
            interactionCollider = gameObject.AddComponent<SphereCollider>();

        interactionCollider.isTrigger = false;
        interactionCollider.enabled = true;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
            Vector3 localExtents = transform.InverseTransformVector(bounds.extents);
            float radius = Mathf.Max(Mathf.Abs(localExtents.x), Mathf.Abs(localExtents.y), Mathf.Abs(localExtents.z));
            interactionCollider.center = localCenter;
            interactionCollider.radius = Mathf.Max(0.05f, radius * 0.85f);
        }
        else
        {
            interactionCollider.center = Vector3.zero;
            interactionCollider.radius = 0.12f;
        }

        visualRimRadiusWorld = -1f;
    }

    XRSimpleInteractable EnsureHoverInteractable()
    {
        XRGrabInteractable legacyGrab = GetComponent<XRGrabInteractable>();
        if (legacyGrab != null)
        {
            legacyGrab.enabled = false;
            Destroy(legacyGrab);
        }

        XRSimpleInteractable interactable = GetComponent<XRSimpleInteractable>();
        if (interactable == null)
            interactable = gameObject.AddComponent<XRSimpleInteractable>();

        int physicsLayer = LayerMask.NameToLayer(GrabPhysicsLayerName);
        if (physicsLayer >= 0)
            gameObject.layer = physicsLayer;

        // Select unused — grab is driven by grip toggle while hovering.
        interactable.selectMode = InteractableSelectMode.Multiple;
        interactable.interactionLayers = InteractionLayerMask.GetMask(GrabInteractionLayers);
        interactable.colliders.Clear();
        interactable.colliders.Add(interactionCollider);
        return interactable;
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (args.interactorObject == null)
            return;
        if (!hovering.Contains(args.interactorObject))
            hovering.Add(args.interactorObject);
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        if (args.interactorObject == null)
            return;
        hovering.Remove(args.interactorObject);
    }

    void UpdateGripHolds()
    {
        bool leftDown = ResolveGripDown(isLeft: true, leftGripWasDown);
        bool rightDown = ResolveGripDown(isLeft: false, rightGripWasDown);
        // Rising-edge flags; cleared when a hand consumes the toggle this frame
        // so the same squeeze cannot release and re-grab (or vice versa).
        bool leftEdge = leftDown && !leftGripWasDown;
        bool rightEdge = rightDown && !rightGripWasDown;

        // Toggle release: second grip press while holding (hover not required).
        for (int i = heldHands.Count - 1; i >= 0; i--)
        {
            HeldHand held = heldHands[i];
            if (held.Interactor == null)
            {
                EndHoldAt(i);
                continue;
            }

            if (held.IsLeft)
            {
                if (!leftEdge)
                    continue;
                leftEdge = false;
                EndHoldAt(i);
            }
            else
            {
                if (!rightEdge)
                    continue;
                rightEdge = false;
                EndHoldAt(i);
            }
        }

        // Toggle grab: first grip press while hovering.
        for (int i = 0; i < hovering.Count; i++)
        {
            IXRHoverInteractor interactor = hovering[i];
            if (interactor == null || IsHolding(interactor))
                continue;

            bool isLeft = IsLeftInteractor(interactor);
            if (isLeft)
            {
                if (!leftEdge)
                    continue;
                leftEdge = false;
                BeginHold(interactor);
            }
            else
            {
                if (!rightEdge)
                    continue;
                rightEdge = false;
                BeginHold(interactor);
            }
        }

        leftGripWasDown = leftDown;
        rightGripWasDown = rightDown;
    }

    /// <summary>
    /// Schmitt-trigger grip level so a single squeeze yields one rising-edge toggle.
    /// </summary>
    bool ResolveGripDown(bool isLeft, bool wasDown)
    {
        float grip = ReadGripForHand(isLeft);
        if (wasDown)
            return grip > gripReleaseThreshold;
        return grip >= gripPressThreshold;
    }

    float ReadGripForHand(bool isLeft)
    {
        for (int i = 0; i < heldHands.Count; i++)
        {
            if (heldHands[i].IsLeft == isLeft && heldHands[i].Interactor != null)
                return ReadGrip(heldHands[i].Interactor);
        }

        for (int i = 0; i < hovering.Count; i++)
        {
            IXRHoverInteractor interactor = hovering[i];
            if (interactor != null && IsLeftInteractor(interactor) == isLeft)
                return ReadGrip(interactor);
        }

        XRNode node = isLeft ? XRNode.LeftHand : XRNode.RightHand;
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.grip, out float axis))
            return Mathf.Clamp01(axis);

        return 0f;
    }

    void BeginHold(IXRHoverInteractor interactor)
    {
        Transform follow = ResolveFollowTransform(interactor);
        if (follow == null)
            return;

        bool wasEmpty = heldHands.Count == 0;
        bool isLeft = IsLeftInteractor(interactor);
        Transform marker = showGrabMarkers ? CreateGrabMarker(isLeft) : null;
        heldHands.Add(new HeldHand
        {
            Interactor = interactor,
            Follow = follow,
            IsLeft = isLeft,
            Marker = marker,
        });
        hasPreviousHandsAngle = false;

        if (marker != null && follow != null)
            PlaceGrabMarker(marker, follow.position);

        if (wasEmpty)
            NotifyHoldSessionStarted();

        if (hideHandsOnGrab)
            StartCoroutine(HideHandVisualsNextFrame(interactor, isLeft));

        PulseHaptic(isLeft, frequency: 0.35f, amplitude: 0.45f, duration: 0.05f);
        Log($"hold start left={isLeft} count={heldHands.Count}");
    }

    void EndHoldAt(int index)
    {
        HeldHand held = heldHands[index];
        heldHands.RemoveAt(index);
        hasPreviousHandsAngle = false;

        DestroyGrabMarker(held.Marker);

        if (held.IsLeft)
            RestoreHandVisuals(hiddenLeftRenderers);
        else
            RestoreHandVisuals(hiddenRightRenderers);

        PulseHaptic(held.IsLeft, frequency: 0.3f, amplitude: 0.28f, duration: 0.04f);

        if (!IsGrabbed)
        {
            // Start return from the true continuous angle (avoid leftover smoothed/wrapped display).
            displayedAngle = currentAngle;
            // Do not call PayphoneHandsetGrab.ForceShowPlayerHands() — it runs PlayerMode(false)
            // and exits cabinet play (breaks Quest buttons + can clear ControlMap).
            RestoreAllHandVisuals();
            NotifyHoldSessionEnded();
            ResetHapticState();
        }

        Log($"hold end left={held.IsLeft} count={heldHands.Count}");
    }

    Transform CreateGrabMarker(bool isLeft)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = isLeft ? "SteeringGrabMarker_L" : "SteeringGrabMarker_R";
        go.transform.SetParent(transform, false);

        // Constant world size regardless of cabinet / wheel scale.
        float parentScale = Mathf.Max(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));
        float localDiameter = (grabMarkerRadius * 2f) / Mathf.Max(parentScale, 0.0001f);
        go.transform.localScale = Vector3.one * localDiameter;

        Collider col = go.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Unlit/Color")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Standard");
            if (shader != null)
            {
                Material mat = new Material(shader);
                if (mat.HasProperty("_Color"))
                    mat.color = grabMarkerColor;
                else if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", grabMarkerColor);
                renderer.sharedMaterial = mat;
            }
        }

        return go.transform;
    }

    /// <summary>
    /// Snap marker onto the visual wheel rim at the hand's angular position and parent it so it
    /// stays flush and rotates with the wheel (does not follow the hand afterward).
    /// </summary>
    void PlaceGrabMarker(Transform marker, Vector3 handWorld)
    {
        Vector3 pivot = PivotWorldPosition();
        Vector3 axis = AxisWorld();
        Vector3 flat = Vector3.ProjectOnPlane(handWorld - pivot, axis);
        if (flat.sqrMagnitude < 0.0001f)
            flat = Vector3.ProjectOnPlane(RestWorldRotation() * GetPerpendicular(localRotationAxis), axis);
        if (flat.sqrMagnitude < 0.0001f)
            flat = Vector3.ProjectOnPlane(transform.right, axis);

        if (flat.sqrMagnitude < 0.0001f)
        {
            // Stay on the rim plane — never at raw hand (floats in front of the wheel).
            marker.position = pivot;
            return;
        }

        // Visual mesh rim (not the oversized hover SphereCollider), inset by marker radius.
        float rimRadius = Mathf.Max(0.02f, GetVisualRimRadiusWorld() - grabMarkerRadius);
        marker.position = pivot + flat.normalized * rimRadius;
    }

    /// <summary>
    /// Outer rim radius in world units from mesh bounds in the spin plane.
    /// </summary>
    float GetVisualRimRadiusWorld()
    {
        if (visualRimRadiusWorld > 0f)
            return visualRimRadiusWorld;

        Vector3 axisLocal = NormalizedLocalAxis();
        float rimLocal = EstimateRimFromLocalExtents(axisLocal);
        if (rimLocal < 0.01f)
            rimLocal = interactionCollider != null ? interactionCollider.radius * 0.75f : 0.12f;

        visualRimRadiusWorld = rimLocal * InPlaneLossyScale(axisLocal);
        return visualRimRadiusWorld;
    }

    float EstimateRimFromLocalExtents(Vector3 axisLocal)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool started = false;
        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;
            if (renderer.gameObject.name.StartsWith("SteeringGrabMarker", System.StringComparison.Ordinal))
                continue;

            Bounds world = renderer.bounds;
            Vector3 c = world.center;
            Vector3 e = world.extents;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 local = transform.InverseTransformPoint(
                    c + new Vector3(sx * e.x, sy * e.y, sz * e.z));
                if (!started)
                {
                    min = max = local;
                    started = true;
                }
                else
                {
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }
        }

        if (!started)
            return 0f;

        return InPlaneRadiusFromExtents((max - min) * 0.5f, axisLocal);
    }

    static float InPlaneRadiusFromExtents(Vector3 extents, Vector3 axisUnit)
    {
        Vector3 a = axisUnit.normalized;
        float absX = Mathf.Abs(a.x);
        float absY = Mathf.Abs(a.y);
        float absZ = Mathf.Abs(a.z);

        // Half-size of the disc in the plane perpendicular to the spin axis.
        if (absZ >= absX && absZ >= absY)
            return Mathf.Max(extents.x, extents.y);
        if (absY >= absX && absY >= absZ)
            return Mathf.Max(extents.x, extents.z);
        return Mathf.Max(extents.y, extents.z);
    }

    float InPlaneLossyScale(Vector3 axisLocal)
    {
        Vector3 a = axisLocal.normalized;
        Vector3 s = transform.lossyScale;
        float absX = Mathf.Abs(a.x);
        float absY = Mathf.Abs(a.y);
        float absZ = Mathf.Abs(a.z);

        if (absZ >= absX && absZ >= absY)
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
        if (absY >= absX && absY >= absZ)
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        return Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    static void DestroyGrabMarker(Transform marker)
    {
        if (marker == null)
            return;

        if (Application.isPlaying)
            Destroy(marker.gameObject);
        else
            DestroyImmediate(marker.gameObject);
    }

    bool IsHolding(IXRHoverInteractor interactor)
    {
        for (int i = 0; i < heldHands.Count; i++)
        {
            if (heldHands[i].Interactor == interactor)
                return true;
        }

        return false;
    }

    void UpdateHeldAngle()
    {
        if (!TryGetHandsAngle(out float handsAngle))
            return;

        if (!hasPreviousHandsAngle)
        {
            previousHandsAngle = handsAngle;
            hasPreviousHandsAngle = true;
            return;
        }

        float frameDelta = Mathf.DeltaAngle(previousHandsAngle, handsAngle);
        previousHandsAngle = handsAngle;
        float previousAngle = currentAngle;
        currentAngle = Mathf.Clamp(currentAngle + frameDelta, -maxAngleDegrees, maxAngleDegrees);
        UpdateSteeringHaptics(previousAngle, currentAngle);
    }

    void ReturnTowardCenter()
    {
        currentAngle = Mathf.MoveTowards(currentAngle, 0f, returnSpeedDegrees * Time.deltaTime);
    }

    bool TryGetHandsAngle(out float angle)
    {
        angle = 0f;
        if (heldHands.Count == 0)
            return false;

        if (heldHands.Count == 1)
        {
            Transform hand = heldHands[0].Follow;
            return hand != null && TryAngleOfPoint(hand.position, out angle);
        }

        Transform a = heldHands[0].Follow;
        Transform b = heldHands[1].Follow;
        if (a == null || b == null)
            return false;

        Vector3 mid = (a.position + b.position) * 0.5f;
        Vector3 across = b.position - a.position;
        Vector3 axis = AxisWorld();
        Vector3 flat = Vector3.ProjectOnPlane(across, axis);
        if (flat.sqrMagnitude < 0.0001f)
            return TryAngleOfPoint(mid, out angle);

        return TryAngleOfDirection(flat, out angle);
    }

    bool TryAngleOfPoint(Vector3 worldPoint, out float angle)
    {
        Vector3 axis = AxisWorld();
        Vector3 flat = Vector3.ProjectOnPlane(worldPoint - PivotWorldPosition(), axis);
        if (flat.sqrMagnitude < 0.0001f)
        {
            angle = 0f;
            return false;
        }

        return TryAngleOfDirection(flat, out angle);
    }

    bool TryAngleOfDirection(Vector3 flatDirection, out float angle)
    {
        Vector3 axis = AxisWorld();
        Quaternion restWorld = RestWorldRotation();
        Vector3 reference = Vector3.ProjectOnPlane(restWorld * GetPerpendicular(localRotationAxis), axis);
        if (reference.sqrMagnitude < 0.0001f)
            reference = Vector3.ProjectOnPlane(restWorld * Vector3.up, axis);

        if (reference.sqrMagnitude < 0.0001f)
        {
            angle = 0f;
            return false;
        }

        angle = Vector3.SignedAngle(reference.normalized, flatDirection.normalized, axis);
        return true;
    }

    void ApplyPose(float angleDegrees)
    {
        transform.localPosition = homeLocalPosition;
        transform.localRotation = homeLocalRotation * Quaternion.AngleAxis(angleDegrees, NormalizedLocalAxis());
        transform.localScale = homeLocalScale;
    }

    Vector3 PivotWorldPosition()
    {
        return transform.parent != null
            ? transform.parent.TransformPoint(homeLocalPosition)
            : homeLocalPosition;
    }

    Quaternion RestWorldRotation()
    {
        return transform.parent != null
            ? transform.parent.rotation * homeLocalRotation
            : homeLocalRotation;
    }

    Vector3 AxisWorld() => (RestWorldRotation() * NormalizedLocalAxis()).normalized;

    Vector3 NormalizedLocalAxis()
    {
        Vector3 axis = localRotationAxis.sqrMagnitude > 0.0001f ? localRotationAxis : Vector3.forward;
        return axis.normalized;
    }

    static Vector3 GetPerpendicular(Vector3 axis)
    {
        Vector3 n = axis.normalized;
        Vector3 candidate = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
        return Vector3.Cross(n, candidate).normalized;
    }

    static float ReadGrip(IXRInteractor interactor)
    {
        var behaviour = interactor as MonoBehaviour;
        if (behaviour == null)
            return 0f;

        ActionBasedController controller = behaviour.GetComponentInParent<ActionBasedController>();
        if (controller != null)
        {
            try
            {
                var valueAction = controller.selectActionValue.action;
                if (valueAction != null)
                    return Mathf.Clamp01(valueAction.ReadValue<float>());

                var buttonAction = controller.selectAction.action;
                if (buttonAction != null && buttonAction.IsPressed())
                    return 1f;
            }
            catch
            {
                // unbound during transitions
            }
        }

        bool isLeft = IsLeftInteractor(interactor);
        XRNode node = isLeft ? XRNode.LeftHand : XRNode.RightHand;
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.grip, out float axis))
            return Mathf.Clamp01(axis);

        return 0f;
    }

    void NotifyHoldSessionStarted()
    {
        if (holdSessionActive)
            return;

        holdSessionActive = true;
        holdSessionCount++;
        IsAnyHandHolding = holdSessionCount > 0;
    }

    void NotifyHoldSessionEnded()
    {
        if (!holdSessionActive)
            return;

        holdSessionActive = false;
        holdSessionCount = Mathf.Max(0, holdSessionCount - 1);
        IsAnyHandHolding = holdSessionCount > 0;
    }

    static Transform ResolveFollowTransform(IXRInteractor interactor)
    {
        if (interactor == null)
            return null;

        var behaviour = interactor as MonoBehaviour;
        if (behaviour != null)
        {
            ActionBasedController controller = behaviour.GetComponentInParent<ActionBasedController>();
            if (controller != null)
                return controller.transform;
        }

        Transform attach = interactor.GetAttachTransform(null);
        return attach != null ? attach : behaviour != null ? behaviour.transform : null;
    }

    System.Collections.IEnumerator HideHandVisualsNextFrame(IXRInteractor interactor, bool isLeft)
    {
        yield return null;
        if (!IsHolding(interactor as IXRHoverInteractor) && !IsGrabbed)
            yield break;

        GameObject handModel = ResolveHandModel(interactor);
        if (handModel == null)
            yield break;
        if (handModel.transform == transform || handModel.transform.IsChildOf(transform))
            yield break;

        List<Renderer> list = isLeft ? hiddenLeftRenderers : hiddenRightRenderers;
        list.Clear();
        foreach (Renderer renderer in handModel.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
                continue;
            if (renderer.transform.IsChildOf(transform))
                continue;

            renderer.enabled = false;
            list.Add(renderer);
        }
    }

    void RestoreAllHandVisuals()
    {
        RestoreHandVisuals(hiddenLeftRenderers);
        RestoreHandVisuals(hiddenRightRenderers);
    }

    static void RestoreHandVisuals(List<Renderer> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null)
                list[i].enabled = true;
        }

        list.Clear();
    }

    static bool IsLeftInteractor(IXRInteractor interactor)
    {
        var behaviour = interactor as MonoBehaviour;
        if (behaviour == null)
            return false;

        ChangeControls controls = Object.FindObjectOfType<ChangeControls>();
        if (controls != null && controls.leftHandXRControl != null
            && behaviour.transform.IsChildOf(controls.leftHandXRControl.transform))
            return true;

        return behaviour.name.ToLowerInvariant().Contains("left");
    }

    static GameObject ResolveHandModel(IXRInteractor interactor)
    {
        var behaviour = interactor as MonoBehaviour;
        if (behaviour == null)
            return null;

        ActionBasedController controller = behaviour.GetComponentInParent<ActionBasedController>();
        if (controller != null && controller.model != null)
            return controller.model.gameObject;

        ChangeControls controls = Object.FindObjectOfType<ChangeControls>();
        if (controls == null)
            return null;

        Transform t = behaviour.transform;
        if (controls.leftHandXRControl != null && t.IsChildOf(controls.leftHandXRControl.transform))
            return controls.LeftHand;
        if (controls.rightHandXRControl != null && t.IsChildOf(controls.rightHandXRControl.transform))
            return controls.RightHand;

        return null;
    }

    void Log(string message)
    {
        if (logDebug)
            ConfigManager.WriteConsole($"{LogPrefix} {message}");
    }

    void ResetHapticState()
    {
        nearLockLatchedPositive = false;
        nearLockLatchedNegative = false;
        hardLockLatchedPositive = false;
        hardLockLatchedNegative = false;
        hapticCooldownUntil = 0f;
    }

    void UpdateSteeringHaptics(float previousAngle, float nextAngle)
    {
        if (!enableHaptics || !IsGrabbed)
            return;

        // Center notch: crossed 0° while turning.
        if ((previousAngle < 0f && nextAngle >= 0f) || (previousAngle > 0f && nextAngle <= 0f))
        {
            if (TryConsumeHapticCooldown())
                PulseHapticAllHeld(frequency: 0.45f, amplitude: 0.32f, duration: 0.03f);
        }

        float near = Mathf.Max(1f, maxAngleDegrees * nearLockRatio);
        float releaseNear = near * 0.82f;

        if (nextAngle >= near && previousAngle < near && !nearLockLatchedPositive)
        {
            nearLockLatchedPositive = true;
            if (TryConsumeHapticCooldown())
                PulseHapticAllHeld(frequency: 0.4f, amplitude: 0.3f, duration: 0.04f);
        }
        else if (nextAngle < releaseNear)
        {
            nearLockLatchedPositive = false;
            hardLockLatchedPositive = false;
        }

        if (nextAngle <= -near && previousAngle > -near && !nearLockLatchedNegative)
        {
            nearLockLatchedNegative = true;
            if (TryConsumeHapticCooldown())
                PulseHapticAllHeld(frequency: 0.4f, amplitude: 0.3f, duration: 0.04f);
        }
        else if (nextAngle > -releaseNear)
        {
            nearLockLatchedNegative = false;
            hardLockLatchedNegative = false;
        }

        // Hard stop at max lock.
        float lockEps = 0.05f;
        if (nextAngle >= maxAngleDegrees - lockEps && previousAngle < maxAngleDegrees - lockEps
            && !hardLockLatchedPositive)
        {
            hardLockLatchedPositive = true;
            PulseHapticAllHeld(frequency: 0.55f, amplitude: 0.55f, duration: 0.06f);
        }

        if (nextAngle <= -maxAngleDegrees + lockEps && previousAngle > -maxAngleDegrees + lockEps
            && !hardLockLatchedNegative)
        {
            hardLockLatchedNegative = true;
            PulseHapticAllHeld(frequency: 0.55f, amplitude: 0.55f, duration: 0.06f);
        }
    }

    bool TryConsumeHapticCooldown()
    {
        float now = Time.unscaledTime;
        if (now < hapticCooldownUntil)
            return false;

        hapticCooldownUntil = now + Mathf.Max(0.02f, hapticEventCooldown);
        return true;
    }

    void PulseHapticAllHeld(float frequency, float amplitude, float duration)
    {
        bool pulsedLeft = false;
        bool pulsedRight = false;
        for (int i = 0; i < heldHands.Count; i++)
        {
            if (heldHands[i].IsLeft)
            {
                if (pulsedLeft)
                    continue;
                pulsedLeft = true;
            }
            else
            {
                if (pulsedRight)
                    continue;
                pulsedRight = true;
            }

            PulseHaptic(heldHands[i].IsLeft, frequency, amplitude, duration);
        }
    }

    void PulseHaptic(bool isLeft, float frequency, float amplitude, float duration)
    {
        if (!enableHaptics)
            return;

#if UNITY_EDITOR
        return;
#else
        try
        {
            OVRInput.Controller controller = isLeft
                ? OVRInput.Controller.LTouch
                : OVRInput.Controller.RTouch;
            OVRInput.SetControllerVibration(frequency, amplitude, controller);
            float until = Time.unscaledTime + Mathf.Max(0.02f, duration);
            if (isLeft)
                leftHapticUntil = Mathf.Max(leftHapticUntil, until);
            else
                rightHapticUntil = Mathf.Max(rightHapticUntil, until);
        }
        catch
        {
            // optional feedback
        }
#endif
    }

    void UpdateHapticDecay()
    {
#if !UNITY_EDITOR
        float now = Time.unscaledTime;
        try
        {
            if (leftHapticUntil > 0f && now >= leftHapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                leftHapticUntil = 0f;
            }

            if (rightHapticUntil > 0f && now >= rightHapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                rightHapticUntil = 0f;
            }
        }
        catch
        {
            leftHapticUntil = 0f;
            rightHapticUntil = 0f;
        }
#endif
    }

    void StopAllHaptics()
    {
        leftHapticUntil = 0f;
        rightHapticUntil = 0f;
#if !UNITY_EDITOR
        try
        {
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }
        catch
        {
            // optional feedback
        }
#endif
    }
}
