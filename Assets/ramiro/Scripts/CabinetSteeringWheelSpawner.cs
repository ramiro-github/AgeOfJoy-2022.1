/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using UnityEngine;

/// <summary>
/// If cabinet <c>description.yaml</c> lists <c>steering-wheel</c>, finds the mesh/object with that name
/// and instantiates the interactive steering-wheel prefab at the same world position.
/// Marker local euler Z is ignored so GLB roll twist does not pre-turn the prefab.
/// Legacy alias <c>steeringwheel</c> (no hyphen) is still accepted.
/// </summary>
public static class CabinetSteeringWheelSpawner
{
    const string LogPrefix = "[CabinetSteeringWheelSpawner]";
    const string WheelPartName = "steering-wheel";
    const string WheelPartNameLegacy = "steeringwheel";
    const string PrefabResourcesPath = "ramiro/SteeringWheel";
    const string SpawnedObjectName = "SteeringWheel";

    /// <summary>Attach after the cabinet GLB exists (factory or skinning).</summary>
    public static void TryAttach(Cabinet cabinet, CabinetInformation cabInfo)
    {
        if (cabinet == null || cabInfo == null)
            return;

        if (!HasWheelPart(cabInfo))
            return;

        if (AlreadyAttached(cabinet.gameObject))
            return;

        Transform marker = ResolveWheelMarker(cabinet);
        if (marker == null)
        {
            ConfigManager.WriteConsoleWarning(
                $"{LogPrefix} description has '{WheelPartName}' but object was not found on '{cabInfo.name}'");
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcesPath);
        if (prefab == null)
        {
            ConfigManager.WriteConsoleWarning(
                $"{LogPrefix} prefab missing at Resources/{PrefabResourcesPath}");
            return;
        }

        Vector3 worldPosition = marker.position;
        Quaternion worldRotation = RotationIgnoringMarkerZ(marker);

        GameObject instance = Object.Instantiate(prefab);
        instance.name = SpawnedObjectName;
        instance.transform.SetPositionAndRotation(worldPosition, worldRotation);
        instance.transform.SetParent(cabinet.gameObject.transform, worldPositionStays: true);

        // Hide static GLB marker so only the interactive wheel is visible.
        HideMarkerVisuals(marker);

        SteeringWheel steering = instance.GetComponent<SteeringWheel>();
        if (steering == null)
            steering = instance.AddComponent<SteeringWheel>();
        steering.CaptureHomePose();

        ConfigManager.WriteConsole(
            $"{LogPrefix} attached on '{cabInfo.name}' at '{marker.name}' pos={worldPosition} rot={worldRotation.eulerAngles}");
    }

    /// <summary>Copy marker pose but force its local euler Z to 0 (ignore GLB roll/twist).</summary>
    static Quaternion RotationIgnoringMarkerZ(Transform marker)
    {
        Vector3 localEuler = marker.localEulerAngles;
        localEuler.z = 0f;
        Quaternion localRotation = Quaternion.Euler(localEuler);

        if (marker.parent == null)
            return localRotation;

        return marker.parent.rotation * localRotation;
    }

    static void HideMarkerVisuals(Transform marker)
    {
        if (marker == null)
            return;

        Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }

        Collider[] colliders = marker.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    static bool IsWheelPartName(string name)
    {
        return !string.IsNullOrEmpty(name)
            && (string.Equals(name, WheelPartName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, WheelPartNameLegacy, System.StringComparison.OrdinalIgnoreCase));
    }

    static bool HasWheelPart(CabinetInformation cabInfo)
    {
        if (cabInfo.Parts == null)
            return false;

        for (int i = 0; i < cabInfo.Parts.Count; i++)
        {
            CabinetInformation.Part part = cabInfo.Parts[i];
            if (part != null && IsWheelPartName(part.name))
                return true;
        }

        return false;
    }

    static Transform ResolveWheelMarker(Cabinet cabinet)
    {
        GameObject direct = cabinet.PartsOrNull(WheelPartName)
            ?? cabinet.PartsOrNull(WheelPartNameLegacy);
        if (direct != null)
            return direct.transform;

        Transform[] children = cabinet.gameObject.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && IsWheelPartName(child.name))
                return child;
        }

        return null;
    }

    static bool AlreadyAttached(GameObject cabinetRoot)
    {
        if (cabinetRoot == null)
            return false;

        return cabinetRoot.GetComponentInChildren<SteeringWheel>(true) != null;
    }
}
