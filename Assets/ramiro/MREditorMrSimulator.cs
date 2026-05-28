/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Editor-only passthrough-like camera backdrop.
/// </summary>
public class MREditorMrSimulator : MonoBehaviour
{
    const string LogPrefix = "[MREditorMrSimulator]";

#if UNITY_EDITOR
    [SerializeField] Color passthroughBackdropColor = new Color(0.38f, 0.4f, 0.43f, 1f);
    [SerializeField] bool logControlsOnStart = true;

    Camera simulatedCamera;
    CameraClearFlags savedClearFlags;
    Color savedBackgroundColor;
    bool savedFog;
    bool backdropActive;
#endif

    public static MREditorMrSimulator Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void Start()
    {
#if UNITY_EDITOR
        if (logControlsOnStart && Application.isEditor)
        {
            ConfigManager.WriteConsole($"{LogPrefix} VR mode — hold Enter 3s → MR (MRUK room + walls)");
            ConfigManager.WriteConsole($"{LogPrefix} In MR: Y 3s or M = ConfigurationCabinet panel");
        }
#endif
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ApplyPassthroughBackdrop(Camera camera)
    {
#if UNITY_EDITOR
        if (!Application.isEditor || camera == null || backdropActive)
            return;

        simulatedCamera = camera;
        savedClearFlags = camera.clearFlags;
        savedBackgroundColor = camera.backgroundColor;
        savedFog = RenderSettings.fog;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = passthroughBackdropColor;
        RenderSettings.fog = false;
        backdropActive = true;

        ConfigManager.WriteConsole($"{LogPrefix} passthrough backdrop (editor simulation)");
#endif
    }

    public void ClearPassthroughBackdrop()
    {
#if UNITY_EDITOR
        if (!backdropActive || simulatedCamera == null)
            return;

        simulatedCamera.clearFlags = savedClearFlags;
        simulatedCamera.backgroundColor = savedBackgroundColor;
        RenderSettings.fog = savedFog;
        simulatedCamera = null;
        backdropActive = false;
#endif
    }

}
