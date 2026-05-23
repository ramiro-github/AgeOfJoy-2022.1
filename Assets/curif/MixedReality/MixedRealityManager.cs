/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System;
using System.Collections;
using UnityEngine;

public class MixedRealityManager : MonoBehaviour
{
    public const string MrRoomName = "MR";
    const string LogPrefix = "[MixedRealityManager]";

    public static MixedRealityManager Instance { get; private set; }

    public ExperienceMode CurrentMode { get; private set; } = ExperienceMode.VR;
    public Transform MRSpaceOrigin { get; private set; }
    public event Action<ExperienceMode> OnModeChanged;

    MRPassthroughController passthrough;
    MRSceneTransition sceneTransition;
    MRLayoutRegistry layoutRegistry;
    bool transitionInProgress;

    /// <summary>True when VR gallery rules (CabinetsController slots, registry rooms) should run.</summary>
    public static bool IsVrExperience =>
        Instance == null || Instance.CurrentMode == ExperienceMode.VR;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        passthrough = GetComponent<MRPassthroughController>();
        if (passthrough == null)
            passthrough = gameObject.AddComponent<MRPassthroughController>();

        sceneTransition = GetComponent<MRSceneTransition>();
        if (sceneTransition == null)
            sceneTransition = gameObject.AddComponent<MRSceneTransition>();

        layoutRegistry = GetComponent<MRLayoutRegistry>();
        if (layoutRegistry == null)
            layoutRegistry = gameObject.AddComponent<MRLayoutRegistry>();

        passthrough.Initialize();
        ConfigManager.WriteConsole($"{LogPrefix} ready (mode={CurrentMode})");
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool CanToggleMode() => !transitionInProgress && (sceneTransition == null || !sceneTransition.IsTransitionRunning);

    public void EnterMR()
    {
        if (!CanToggleMode() || CurrentMode != ExperienceMode.VR)
            return;
        StartCoroutine(EnterMRCoroutine());
    }

    public void EnterVR()
    {
        if (!CanToggleMode() || CurrentMode == ExperienceMode.VR)
            return;
        StartCoroutine(EnterVRCoroutine());
    }

    public void EnterMREdit()
    {
        if (CurrentMode != ExperienceMode.MR)
            return;
        SetMode(ExperienceMode.MR_EDIT);
    }

    public void ExitMREdit()
    {
        if (CurrentMode != ExperienceMode.MR_EDIT)
            return;
        SetMode(ExperienceMode.MR);
    }

    IEnumerator EnterMRCoroutine()
    {
        transitionInProgress = true;
        ConfigManager.WriteConsole($"{LogPrefix} EnterMR start");

        MRVrSystemsGate.SuspendForMR();

        // Passthrough primeiro, com cenas VR ainda carregadas (padrão Meta).
        yield return passthrough.EnablePassthroughWhenReady();
        if (!passthrough.PassthroughSystemReady)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} EnterMR aborted — passthrough system not ready");
            transitionInProgress = false;
            yield break;
        }

        yield return sceneTransition.UnloadVrScenes();

        EnsureMRSpaceOrigin();
        layoutRegistry?.SpawnAll(MRSpaceOrigin);
        SetMode(ExperienceMode.MR);

        transitionInProgress = false;
        ConfigManager.WriteConsole($"{LogPrefix} EnterMR done");
    }

    IEnumerator EnterVRCoroutine()
    {
        transitionInProgress = true;
        ConfigManager.WriteConsole($"{LogPrefix} EnterVR start");

        layoutRegistry?.DespawnAll();
        passthrough.DisablePassthrough();
        DestroyMRSpaceOrigin();
        SetMode(ExperienceMode.VR);

        yield return sceneTransition.ReloadVrScenes();
        MRVrSystemsGate.ResumeForVR();

        transitionInProgress = false;
        ConfigManager.WriteConsole($"{LogPrefix} EnterVR done");
    }

    void EnsureMRSpaceOrigin()
    {
        if (MRSpaceOrigin != null)
            return;

        var player = FindPlayerTransform();
        Vector3 originPosition = player != null ? player.position : Vector3.zero;
        originPosition.y = 0f;

        var originGo = new GameObject("MRSpaceOrigin");
        originGo.transform.position = originPosition;
        originGo.transform.rotation = Quaternion.identity;
        MRSpaceOrigin = originGo.transform;
        ConfigManager.WriteConsole($"{LogPrefix} MRSpaceOrigin at {originPosition}");
    }

    void DestroyMRSpaceOrigin()
    {
        if (MRSpaceOrigin == null)
            return;
        Destroy(MRSpaceOrigin.gameObject);
        MRSpaceOrigin = null;
    }

    static Transform FindPlayerTransform()
    {
        var pc = FindObjectOfType<PlayerController>();
        if (pc != null && pc.PlayerControllerGameObject != null)
            return pc.PlayerControllerGameObject.transform;
        if (pc != null)
            return pc.transform;

        var tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged != null ? tagged.transform : null;
    }

    void SetMode(ExperienceMode mode)
    {
        if (CurrentMode == mode)
            return;
        CurrentMode = mode;
        ConfigManager.WriteConsole($"{LogPrefix} mode={mode}");
        OnModeChanged?.Invoke(mode);
    }
}
