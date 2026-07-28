/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using UnityEngine;

/// <summary>
/// If cabinet <c>description.yaml</c> lists <c>steering-wheel</c>, finds that part on the
/// cabinet GLB and makes it interactive (adds <see cref="SteeringWheel"/>). No prefab spawn —
/// the cabinet mesh itself is the wheel. Legacy alias <c>steeringwheel</c> is still accepted.
/// Optional part fields: <c>rotation-axis</c> (x/y/z), <c>max-angle</c> (degrees each way),
/// <c>steer-gain</c> (axis multiplier, default 1), <c>steer-digital</c> (JOYPAD L/R, default true).
/// </summary>
public static class CabinetSteeringWheelSpawner
{
    const string LogPrefix = "[CabinetSteeringWheelSpawner]";
    const string WheelPartName = "steering-wheel";
    const string WheelPartNameLegacy = "steeringwheel";

    /// <summary>Attach after the cabinet GLB exists (factory or skinning).</summary>
    public static void TryAttach(Cabinet cabinet, CabinetInformation cabInfo)
    {
        if (cabinet == null || cabInfo == null)
            return;

        CabinetInformation.Part wheelPart = FindWheelPart(cabInfo);
        if (wheelPart == null)
            return;

        if (AlreadyAttached(cabinet.gameObject))
            return;

        Transform wheel = ResolveWheelPart(cabinet);
        if (wheel == null)
        {
            ConfigManager.WriteConsoleWarning(
                $"{LogPrefix} description has '{WheelPartName}' but object was not found on '{cabInfo.name}'");
            return;
        }

        // Ensure Rigidbody exists before SteeringWheel.Awake (RequireComponent also adds one).
        if (wheel.GetComponent<Rigidbody>() == null)
            wheel.gameObject.AddComponent<Rigidbody>();

        SteeringWheel steering = wheel.GetComponent<SteeringWheel>();
        if (steering == null)
            steering = wheel.gameObject.AddComponent<SteeringWheel>();

        steering.SetLocalRotationAxisFromYaml(wheelPart.rotationAxis);
        steering.SetMaxAngleDegreesFromYaml(wheelPart.maxAngle);
        steering.SetSteerGainFromYaml(wheelPart.steerGain);
        steering.SetSteerDigitalFromYaml(wheelPart.steerDigital);
        steering.CaptureHomePose();

        ConfigManager.WriteConsole(
            $"{LogPrefix} using cabinet part '{wheel.name}' on '{cabInfo.name}' " +
            $"axis={wheelPart.rotationAxis ?? "forward"} maxAngle={wheelPart.maxAngle?.ToString() ?? "default"} " +
            $"steerGain={wheelPart.steerGain?.ToString() ?? "default"} " +
            $"steerDigital={wheelPart.steerDigital?.ToString() ?? "default"} " +
            $"pos={wheel.position} rot={wheel.eulerAngles}");
    }

    static bool IsWheelPartName(string name)
    {
        return !string.IsNullOrEmpty(name)
            && (string.Equals(name, WheelPartName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, WheelPartNameLegacy, System.StringComparison.OrdinalIgnoreCase));
    }

    static CabinetInformation.Part FindWheelPart(CabinetInformation cabInfo)
    {
        if (cabInfo.Parts == null)
            return null;

        for (int i = 0; i < cabInfo.Parts.Count; i++)
        {
            CabinetInformation.Part part = cabInfo.Parts[i];
            if (part != null && IsWheelPartName(part.name))
                return part;
        }

        return null;
    }

    static Transform ResolveWheelPart(Cabinet cabinet)
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
