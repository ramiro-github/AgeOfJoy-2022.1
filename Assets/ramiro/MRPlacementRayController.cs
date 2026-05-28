/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System;
using UnityEngine;

/// <summary>
/// Runtime placement ray used to reposition placed MR objects.
/// Current implementation supports Floor and Wall surfaces.
/// </summary>
public class MRPlacementRayController : MonoBehaviour
{
    const string LogPrefix = "[MRPlacementRayController]";

    [SerializeField] float maxDistanceMeters = 5f;
    [Tooltip("Wall-only fine adjust after auto-facing fix. Keep near zero.")]
    [Range(-30f, 30f)]
    [SerializeField] float wallMountYawOffsetDegrees = 0f;
    [SerializeField] Color validColor = new Color(0.2f, 1f, 0.35f, 1f);
    [SerializeField] Color invalidColor = new Color(1f, 0.25f, 0.25f, 1f);

    GameObject movingTarget;
    PlacementSurfaceType surfaceType;
    PlacementFacingAxis facingAxis = PlacementFacingAxis.PositiveZ;
    Action<Vector3, Quaternion> onConfirmPose;
    Action onCancel;

    LineRenderer line;
    bool isActive;
    Vector3 startPosition;
    Quaternion startRotation;
    Vector3 previewPosition;
    Quaternion previewRotation;
    bool hasValidPreview;

    public bool IsActive => isActive;

    public void BeginMove(
        GameObject target,
        PlacementSurfaceType placementSurfaceType,
        PlacementFacingAxis objectFacingAxis,
        Action<Vector3, Quaternion> confirmCallback,
        Action cancelCallback = null)
    {
        if (target == null || confirmCallback == null)
            return;

        movingTarget = target;
        surfaceType = placementSurfaceType;
        facingAxis = objectFacingAxis;
        onConfirmPose = confirmCallback;
        onCancel = cancelCallback;

        startPosition = target.transform.position;
        startRotation = target.transform.rotation;
        previewPosition = startPosition;
        previewRotation = startRotation;
        hasValidPreview = false;

        EnsureLineRenderer();
        SetLineVisible(true);
        isActive = true;

        ConfigManager.WriteConsole(
            $"{LogPrefix} begin move target={target.name} surface={surfaceType} facing={facingAxis}");
    }

    void Update()
    {
        if (!isActive || movingTarget == null)
            return;

        UpdatePreviewPose();
        DrawRay();

        if (WasCancelPressed())
        {
            movingTarget.transform.SetPositionAndRotation(startPosition, startRotation);
            StopMove(cancelled: true);
            return;
        }

        if (WasConfirmPressed() && hasValidPreview)
        {
            movingTarget.transform.SetPositionAndRotation(previewPosition, previewRotation);
            onConfirmPose?.Invoke(previewPosition, previewRotation);
            StopMove(cancelled: false);
        }
    }

    void UpdatePreviewPose()
    {
        ResolvePointer(out Vector3 rayOrigin, out Vector3 rayDir, out Vector3 viewerPosition);

        bool ok = false;
        Vector3 worldPos = movingTarget.transform.position;
        Quaternion worldRot = movingTarget.transform.rotation;

        MREnvironmentSurfaces surfaces = MREnvironmentSurfaces.Instance;
        switch (surfaceType)
        {
            case PlacementSurfaceType.Wall:
                if (surfaces != null)
                {
                    ok = surfaces.TryGetWallMountedFramePoseFromRay(
                        rayOrigin,
                        rayDir,
                        maxDistanceMeters,
                        0.25f,
                        out worldPos,
                        out worldRot,
                        facingAxis);
                    if (ok)
                    {
                        worldRot *= Quaternion.Euler(0f, wallMountYawOffsetDegrees, 0f);
                    }
                }
                break;

            case PlacementSurfaceType.Floor:
            default:
                Vector3 probe = rayOrigin + rayDir.normalized * maxDistanceMeters;
                if (surfaces != null && surfaces.TryGetFloorPointAt(probe, out Vector3 floorPoint))
                {
                    worldPos = floorPoint;
                    Vector3 look = viewerPosition - worldPos;
                    look.y = 0f;
                    if (look.sqrMagnitude < 0.001f)
                        look = Vector3.forward;
                    worldRot = PlacementOrientation.LookRotationWithFacing(look, facingAxis, Vector3.up);
                    worldRot = PlacementOrientation.EnsureFacingViewer(
                        worldRot, facingAxis, worldPos, viewerPosition);
                    ok = true;
                }
                break;
        }

        hasValidPreview = ok;
        previewPosition = worldPos;
        previewRotation = worldRot;

        if (ok)
            movingTarget.transform.SetPositionAndRotation(previewPosition, previewRotation);
    }

    void DrawRay()
    {
        if (line == null)
            return;

        ResolvePointer(out Vector3 rayOrigin, out Vector3 rayDir, out _);
        line.positionCount = 2;
        line.SetPosition(0, rayOrigin);
        line.SetPosition(1, hasValidPreview ? previewPosition : rayOrigin + rayDir * maxDistanceMeters);
        line.startColor = hasValidPreview ? validColor : invalidColor;
        line.endColor = hasValidPreview ? validColor : invalidColor;
    }

    void ResolvePointer(out Vector3 rayOrigin, out Vector3 rayDirection, out Vector3 viewerPosition)
    {
        Transform pointer = ResolveRightControllerTransform();
        Transform viewer = Camera.main != null ? Camera.main.transform : pointer;

        if (pointer != null)
        {
            rayOrigin = pointer.position;
            rayDirection = pointer.forward;
        }
        else if (viewer != null)
        {
            rayOrigin = viewer.position;
            rayDirection = viewer.forward;
        }
        else
        {
            rayOrigin = transform.position;
            rayDirection = transform.forward;
        }

        viewerPosition = viewer != null ? viewer.position : rayOrigin;
        if (rayDirection.sqrMagnitude < 0.001f)
            rayDirection = Vector3.forward;
        rayDirection.Normalize();
    }

    static Transform ResolveRightControllerTransform()
    {
        ChangeControls controls = FindObjectOfType<ChangeControls>();
        if (controls != null && controls.RightHand != null)
            return controls.RightHand.transform;

        PlayerController pc = FindObjectOfType<PlayerController>();
        if (pc != null && pc.xrorigin != null && pc.xrorigin.transform != null)
        {
            Transform fallback = FindDeepChildByNameContains(pc.xrorigin.transform, "right");
            if (fallback != null)
                return fallback;
        }

        return null;
    }

    static Transform FindDeepChildByNameContains(Transform root, string token)
    {
        if (root == null || string.IsNullOrEmpty(token))
            return null;

        string normalized = token.ToLowerInvariant();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string name = t.name.ToLowerInvariant();
            if (name.Contains(normalized) && (name.Contains("controller") || name.Contains("hand")))
                return t;
        }

        return null;
    }

    void StopMove(bool cancelled)
    {
        if (cancelled)
            onCancel?.Invoke();

        SetLineVisible(false);
        isActive = false;
        movingTarget = null;
        onConfirmPose = null;
        onCancel = null;
        hasValidPreview = false;

        ConfigManager.WriteConsole($"{LogPrefix} end move cancelled={cancelled}");
    }

    void EnsureLineRenderer()
    {
        if (line != null)
            return;

        line = GetComponent<LineRenderer>();
        if (line == null)
            line = gameObject.AddComponent<LineRenderer>();

        line.useWorldSpace = true;
        line.widthMultiplier = 0.008f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.positionCount = 0;
    }

    void SetLineVisible(bool visible)
    {
        if (line != null)
            line.enabled = visible;
    }

    static bool WasConfirmPressed()
    {
#if UNITY_EDITOR
        return Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0);
#else
        return OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
#endif
    }

    static bool WasCancelPressed()
    {
#if UNITY_EDITOR
        return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace);
#else
        return OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)
            || OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
#endif
    }
}
