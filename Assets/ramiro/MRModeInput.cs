/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using UnityEngine;

public class MRModeInput : MonoBehaviour
{
    const string LogPrefix = "[MRModeInput]";

    [SerializeField] float holdDurationSeconds = 3f;
    [SerializeField] bool useRightController = true;

    float holdTimer;
    bool wasPressed;

    void Update()
    {
        if (MixedRealityManager.Instance == null || !MixedRealityManager.Instance.CanToggleMode())
        {
            holdTimer = 0f;
            wasPressed = false;
            return;
        }

        bool pressed = IsAButtonPressed();
        if (pressed)
        {
            holdTimer += Time.deltaTime;
            if (!wasPressed)
                ConfigManager.WriteConsole($"{LogPrefix} holding toggle (A / Enter)...");
            if (holdTimer >= holdDurationSeconds)
            {
                ToggleMode();
                holdTimer = 0f;
            }
        }
        else
        {
            holdTimer = 0f;
        }

        wasPressed = pressed;
    }

    void ToggleMode()
    {
        var manager = MixedRealityManager.Instance;
        if (manager.CurrentMode == ExperienceMode.VR)
            manager.EnterMR();
        else
            manager.EnterVR();

        PulseHaptic();
        ConfigManager.WriteConsole($"{LogPrefix} toggle requested (target mode pending)");
    }

    bool IsAButtonPressed()
    {
#if UNITY_EDITOR
        return Input.GetKey(KeyCode.JoystickButton0)
            || Input.GetKey(KeyCode.Return)
            || Input.GetKey(KeyCode.KeypadEnter);
#else
        OVRInput.Controller controller = useRightController
            ? OVRInput.Controller.RTouch
            : OVRInput.Controller.LTouch;
        return OVRInput.Get(OVRInput.Button.One, controller);
#endif
    }

    static void PulseHaptic()
    {
#if !UNITY_EDITOR
        try
        {
            OVRInput.SetControllerVibration(0.4f, 0.6f, OVRInput.Controller.RTouch);
        }
        catch
        {
            // optional feedback
        }
#endif
    }
}
