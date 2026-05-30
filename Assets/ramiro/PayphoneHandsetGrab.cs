/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Payphone handset grab for the IntroGalleryExterior setup:
///   SM_Payphone_Handset (parent) = Rigidbody + Collider + XRGrabInteractable + this script
///   child mesh(es) = visible geometry (follow parent automatically)
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class PayphoneHandsetGrab : MonoBehaviour
{
    const string LogPrefix = "[PayphoneHandsetGrab]";
    const string HandsetObjectName = "SM_Payphone_Handset";
    static readonly string[] GrabInteractionLayers = { "InteractablePart" };
    const string GrabPhysicsLayerName = "InteractablePart";

    [SerializeField] bool hideHandOnGrab = true;
    [Tooltip("Seconds to ease back to the cradle. 0 = instant snap.")]
    [SerializeField] float returnDurationSeconds;
    [SerializeField] bool logDebug;

    Transform grabRoot;
    XRGrabInteractable grabInteractable;
    Rigidbody body;

    Transform homeParent;
    Vector3 homeLocalPosition;
    Quaternion homeLocalRotation;
    Vector3 homeLocalScale;
    Vector3 lockedWorldScale;

    readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    Transform followTransform;
    Vector3 followLocalGrabOffset;
    Quaternion followLocalGrabRotationOffset;
    bool isGrabbed;

    void Awake()
    {
        SetupGrab();
        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);
    }

    void Start()
    {
        CaptureHomePose();
        SetDockedPhysics(true);
        LogSetup();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        Transform root = ResolveGrabRoot();
        if (root != null)
            ClearStaticFlags(root);
    }
#endif

    void Update()
    {
        if (!isGrabbed || followTransform == null || grabRoot == null)
            return;

        if (grabInteractable != null && !grabInteractable.isSelected)
        {
            EndGrabImmediate();
            return;
        }

        FollowHand();
    }

    void OnDisable()
    {
        RestoreHiddenHand();
        StopAllCoroutines();
        isGrabbed = false;
        followTransform = null;
    }

    void OnDestroy()
    {
        if (grabInteractable == null)
            return;

        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }

    void SetupGrab()
    {
        grabRoot = ResolveGrabRoot();
        EnsureNotStatic();

        body = EnsureRigidbody();
        grabInteractable = EnsureGrabInteractable();
        DisableMisplacedGrabComponents();

        ConfigureGrabInteractable();
        ConfigureGrabLayers();
        RebuildGrabColliders();

        ConfigManager.WriteConsole(
            $"{LogPrefix} grabRoot='{grabRoot.name}' script='{name}' meshChildren={CountMeshChildren(grabRoot)}");
    }

    /// <summary>
    /// Grab the Rigidbody owner (SM_Payphone_Handset), not the mesh child.
    /// </summary>
    Transform ResolveGrabRoot()
    {
        if (GetComponent<Rigidbody>() != null)
            return transform;

        Transform handset = FindDeepChild(transform, HandsetObjectName);
        if (handset != null && handset.GetComponent<Rigidbody>() != null)
            return handset;

        Rigidbody rb = GetComponentInChildren<Rigidbody>(includeInactive: false);
        if (rb != null)
            return rb.transform;

        return transform;
    }

    static Transform FindDeepChild(Transform root, string childName)
    {
        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    static int CountMeshChildren(Transform root)
    {
        int count = 0;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(includeInactive: false))
        {
            if (r.transform != root)
                count++;
        }

        return count;
    }

    /// <summary>
    /// If XRGrabInteractable was left on a mesh child by mistake, disable it.
    /// </summary>
    void DisableMisplacedGrabComponents()
    {
        foreach (XRGrabInteractable grab in GetComponentsInChildren<XRGrabInteractable>(includeInactive: true))
        {
            if (grab == null || grab == grabInteractable)
                continue;

            grab.enabled = false;
            ConfigManager.WriteConsoleWarning(
                $"{LogPrefix} disabled extra XRGrabInteractable on '{grab.name}' — grab stays on '{grabRoot.name}'");
        }
    }

    XRGrabInteractable EnsureGrabInteractable()
    {
        XRGrabInteractable onRoot = grabRoot.GetComponent<XRGrabInteractable>();
        if (onRoot != null)
            return onRoot;

        if (transform == grabRoot)
        {
            XRGrabInteractable onSelf = GetComponent<XRGrabInteractable>();
            if (onSelf != null)
                return onSelf;
        }

        return grabRoot.gameObject.AddComponent<XRGrabInteractable>();
    }

    Rigidbody EnsureRigidbody()
    {
        Rigidbody rb = grabRoot.GetComponent<Rigidbody>();
        if (rb == null)
            rb = grabRoot.gameObject.AddComponent<Rigidbody>();

        rb.useGravity = false;
        rb.detectCollisions = true;
        return rb;
    }

    void EnsureNotStatic()
    {
        ClearStaticFlags(grabRoot);

        Transform parent = grabRoot.parent;
        while (parent != null)
        {
            ClearStaticFlags(parent);
            if (parent.name == "PF_Grabbable_Phone" || parent.name == "PF_Payphone")
                break;
            parent = parent.parent;
        }
    }

    static void ClearStaticFlags(Transform root)
    {
        if (root == null)
            return;

        if (root.gameObject.isStatic)
            root.gameObject.isStatic = false;

        for (int i = 0; i < root.childCount; i++)
            ClearStaticFlags(root.GetChild(i));
    }

    void ConfigureGrabInteractable()
    {
        grabInteractable.throwOnDetach = false;
        grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grabInteractable.trackPosition = true;
        grabInteractable.trackRotation = true;
        grabInteractable.trackScale = false;
        grabInteractable.retainTransformParent = false;
    }

    void ConfigureGrabLayers()
    {
        int physicsLayer = LayerMask.NameToLayer(GrabPhysicsLayerName);
        if (physicsLayer >= 0)
            grabRoot.gameObject.layer = physicsLayer;

        grabInteractable.interactionLayers = InteractionLayerMask.GetMask(GrabInteractionLayers);
    }

    void RebuildGrabColliders()
    {
        grabInteractable.colliders.Clear();

        Collider col = grabRoot.GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.enabled = true;
            grabInteractable.colliders.Add(col);
        }

        if (grabInteractable.colliders.Count == 0)
        {
            BoxCollider box = grabRoot.gameObject.AddComponent<BoxCollider>();
            FitBoxColliderToRenderers(box, grabRoot);
            grabInteractable.colliders.Add(box);
        }
    }

    static void FitBoxColliderToRenderers(BoxCollider box, Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        box.center = root.InverseTransformPoint(bounds.center);
        Vector3 lossy = root.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, lossy.x),
            bounds.size.y / Mathf.Max(0.0001f, lossy.y),
            bounds.size.z / Mathf.Max(0.0001f, lossy.z));
    }

    void CaptureHomePose()
    {
        homeParent = grabRoot.parent;
        homeLocalPosition = grabRoot.localPosition;
        homeLocalRotation = grabRoot.localRotation;
        homeLocalScale = grabRoot.localScale;
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        isGrabbed = true;
        EnsureNotStatic();
        grabRoot.SetParent(null, worldPositionStays: true);
        lockedWorldScale = grabRoot.lossyScale;

        followTransform = ResolveFollowTransform(args.interactorObject);
        CacheFollowOffset();

        if (body != null)
        {
            body.isKinematic = true;
            body.WakeUp();
        }

        Log($"grabbed '{grabRoot.name}' follow='{followTransform?.name}'");

        if (hideHandOnGrab)
            StartCoroutine(HideHandVisualsNextFrame(args.interactorObject));
    }

    IEnumerator HideHandVisualsNextFrame(IXRSelectInteractor interactor)
    {
        yield return null;
        if (!isGrabbed)
            yield break;

        HideHandVisuals(ResolveHandModel(interactor));
    }

    void CacheFollowOffset()
    {
        if (followTransform == null)
            return;

        followLocalGrabOffset = followTransform.InverseTransformPoint(grabRoot.position);
        followLocalGrabRotationOffset = Quaternion.Inverse(followTransform.rotation) * grabRoot.rotation;
    }

    void FollowHand()
    {
        if (followTransform == null)
            return;

        Vector3 worldPos = followTransform.TransformPoint(followLocalGrabOffset);
        Quaternion worldRot = followTransform.rotation * followLocalGrabRotationOffset;
        grabRoot.SetPositionAndRotation(worldPos, worldRot);

        if (grabRoot.parent == null)
            grabRoot.localScale = lockedWorldScale;
    }

    void OnReleased(SelectExitEventArgs _)
    {
        isGrabbed = false;
        followTransform = null;
        RestoreHiddenHand();

        if (returnDurationSeconds > 0f)
            StartCoroutine(ReturnHomeSmooth());
        else
            SnapHome();
    }

    void EndGrabImmediate()
    {
        isGrabbed = false;
        followTransform = null;
        RestoreHiddenHand();
        SnapHome();
    }

    void SnapHome()
    {
        grabRoot.SetParent(homeParent, worldPositionStays: false);
        grabRoot.localPosition = homeLocalPosition;
        grabRoot.localRotation = homeLocalRotation;
        grabRoot.localScale = homeLocalScale;
        SetDockedPhysics(true);
    }

    IEnumerator ReturnHomeSmooth()
    {
        Vector3 startPos = grabRoot.position;
        Quaternion startRot = grabRoot.rotation;

        Vector3 endPos = homeParent != null
            ? homeParent.TransformPoint(homeLocalPosition)
            : homeLocalPosition;
        Quaternion endRot = homeParent != null
            ? homeParent.rotation * homeLocalRotation
            : homeLocalRotation;

        grabRoot.SetParent(homeParent, worldPositionStays: true);

        float duration = returnDurationSeconds;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            grabRoot.position = Vector3.Lerp(startPos, endPos, t);
            grabRoot.rotation = Quaternion.Slerp(startRot, endRot, t);
            yield return null;
        }

        SnapHome();
    }

    void HideHandVisuals(GameObject handRoot)
    {
        RestoreHiddenHand();
        if (handRoot == null)
            return;

        foreach (Renderer renderer in handRoot.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (renderer == null || !renderer.enabled)
                continue;

            renderer.enabled = false;
            hiddenRenderers.Add(renderer);
        }
    }

    void RestoreHiddenHand()
    {
        for (int i = 0; i < hiddenRenderers.Count; i++)
        {
            Renderer renderer = hiddenRenderers[i];
            if (renderer != null)
                renderer.enabled = true;
        }

        hiddenRenderers.Clear();
    }

    void SetDockedPhysics(bool docked)
    {
        if (body == null)
            return;

        body.useGravity = false;
        body.isKinematic = true;

        if (docked)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    static Transform ResolveFollowTransform(IXRSelectInteractor interactor)
    {
        var interactorBehaviour = interactor as MonoBehaviour;
        return interactorBehaviour != null ? interactorBehaviour.transform : null;
    }

    static GameObject ResolveHandModel(IXRSelectInteractor interactor)
    {
        var interactorBehaviour = interactor as MonoBehaviour;
        if (interactorBehaviour == null)
            return null;

        ActionBasedController controller = interactorBehaviour.GetComponentInParent<ActionBasedController>();
        if (controller != null && controller.model != null)
            return controller.model.gameObject;

        ChangeControls controls = FindObjectOfType<ChangeControls>();
        if (controls == null)
            return null;

        Transform interactorTransform = interactorBehaviour.transform;
        if (controls.leftHandXRControl != null
            && interactorTransform.IsChildOf(controls.leftHandXRControl.transform))
            return controls.LeftHand;

        if (controls.rightHandXRControl != null
            && interactorTransform.IsChildOf(controls.rightHandXRControl.transform))
            return controls.RightHand;

        return null;
    }

    void LogSetup()
    {
        if (!logDebug)
            return;

        Log($"setup grabRoot='{grabRoot.name}' colliders={grabInteractable.colliders.Count}");
    }

    void Log(string message)
    {
        if (!logDebug)
            return;

        ConfigManager.WriteConsole($"{LogPrefix} {message}");
    }

    public void RecaptureHomePose()
    {
        if (isGrabbed)
            return;

        CaptureHomePose();
        SnapHome();
    }
}
