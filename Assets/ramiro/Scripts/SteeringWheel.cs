/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// VR steering wheel on a cabinet part: hold with one or two hands via the <b>index triggers</b> (not grip).
/// Position stays fixed; only rotates around a local axis. Uses hover + trigger so XR never reparents the wheel.
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

    [Header("Grab (index trigger)")]
    [SerializeField] float triggerPressThreshold = 0.55f;
    [SerializeField] float triggerReleaseThreshold = 0.35f;
    [SerializeField] bool hideHandsOnGrab = true;
    [SerializeField] bool logDebug;

    [Header("Game input")]
    [Tooltip("Maps wheel angle to thumbstick X (right stick / JOYPAD left-right). Invert if turn direction feels wrong.")]
    [SerializeField] bool invertSteering;
    [Tooltip("Keep feeding axis while the wheel springs back to center.")]
    [SerializeField] bool driveAxisWhileReturning = true;

    XRSimpleInteractable hoverInteractable;
    Rigidbody body;
    SphereCollider interactionCollider;
    LibretroControlMap controlMap;

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

    struct HeldHand
    {
        public IXRHoverInteractor Interactor;
        public Transform Follow;
        public bool IsLeft;
    }

    public float AngleDegrees => currentAngle;

    public float NormalizedValue =>
        maxAngleDegrees <= 0.0001f ? 0f : Mathf.Clamp(currentAngle / maxAngleDegrees, -1f, 1f);

    public bool IsGrabbed => heldHands.Count > 0;

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
    }

    void OnDestroy()
    {
        ClearControlMapOverride();

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
    }

    void Update()
    {
        UpdateTriggerHolds();
    }

    void LateUpdate()
    {
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

        // Select unused — grab is driven by index trigger while hovering.
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

    void UpdateTriggerHolds()
    {
        // Start hold: hovering + trigger pressed.
        for (int i = 0; i < hovering.Count; i++)
        {
            IXRHoverInteractor interactor = hovering[i];
            if (interactor == null || IsHolding(interactor))
                continue;

            if (ReadTrigger(interactor) >= triggerPressThreshold)
                BeginHold(interactor);
        }

        // End hold: trigger released (can leave hover while still holding).
        for (int i = heldHands.Count - 1; i >= 0; i--)
        {
            HeldHand held = heldHands[i];
            if (held.Interactor == null || ReadTrigger(held.Interactor) <= triggerReleaseThreshold)
                EndHoldAt(i);
        }
    }

    void BeginHold(IXRHoverInteractor interactor)
    {
        Transform follow = ResolveFollowTransform(interactor);
        if (follow == null)
            return;

        bool isLeft = IsLeftInteractor(interactor);
        heldHands.Add(new HeldHand
        {
            Interactor = interactor,
            Follow = follow,
            IsLeft = isLeft,
        });
        hasPreviousHandsAngle = false;

        if (hideHandsOnGrab)
            StartCoroutine(HideHandVisualsNextFrame(interactor, isLeft));

        Log($"hold start left={isLeft} count={heldHands.Count}");
    }

    void EndHoldAt(int index)
    {
        HeldHand held = heldHands[index];
        heldHands.RemoveAt(index);
        hasPreviousHandsAngle = false;

        if (held.IsLeft)
            RestoreHandVisuals(hiddenLeftRenderers);
        else
            RestoreHandVisuals(hiddenRightRenderers);

        if (!IsGrabbed)
        {
            // Start return from the true continuous angle (avoid leftover smoothed/wrapped display).
            displayedAngle = currentAngle;
            // Do not call PayphoneHandsetGrab.ForceShowPlayerHands() — it runs PlayerMode(false)
            // and exits cabinet play (breaks Quest buttons + can clear ControlMap).
            RestoreAllHandVisuals();
        }

        Log($"hold end left={held.IsLeft} count={heldHands.Count}");
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
        currentAngle = Mathf.Clamp(currentAngle + frameDelta, -maxAngleDegrees, maxAngleDegrees);
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

    static float ReadTrigger(IXRInteractor interactor)
    {
        var behaviour = interactor as MonoBehaviour;
        if (behaviour == null)
            return 0f;

        ActionBasedController controller = behaviour.GetComponentInParent<ActionBasedController>();
        if (controller != null)
        {
            try
            {
                var valueAction = controller.activateActionValue.action;
                if (valueAction != null)
                    return Mathf.Clamp01(valueAction.ReadValue<float>());

                var buttonAction = controller.activateAction.action;
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
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.trigger, out float axis))
            return Mathf.Clamp01(axis);

        return 0f;
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
}
