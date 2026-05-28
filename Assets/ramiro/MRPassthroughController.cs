/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections;
using AOJ.Managers;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

public class MRPassthroughController : MonoBehaviour
{
    const string LogPrefix = "[MRPassthroughController]";
    const float SystemInitTimeoutSeconds = 10f;
    const float LayerReadyTimeoutSeconds = 5f;
    const string FadeSphereName = "SM_FadeSphere";

    OVRPassthroughLayer passthroughLayer;
    Camera xrCamera;
    Animator fadeSphereAnimator;
    readonly System.Collections.Generic.List<Renderer> disabledFadeRenderers = new System.Collections.Generic.List<Renderer>();

    CameraClearFlags savedClearFlags;
    Color savedBackgroundColor;
    OVROverlay.OverlayType savedOverlayType;
    bool savedFog;
    bool savedEyeFovPremultipliedAlpha;
    bool initialized;
    bool passthroughSystemReady;

    public bool IsPassthroughEnabled => passthroughLayer != null && passthroughLayer.enabled;
    public bool PassthroughSystemReady => passthroughSystemReady;

    /// <summary>True when OVR/XR is active enough to query or enable passthrough (Quest build or Editor + Link).</summary>
    public static bool IsPassthroughRuntimeAvailable()
    {
        if (OVRManager.instance == null)
            return false;

#if UNITY_EDITOR
        return XRSettings.enabled && XRSettings.isDeviceActive;
#else
        return true;
#endif
    }

    public void Initialize()
    {
        if (initialized)
            return;

        xrCamera = ResolveXRCamera();
        if (xrCamera == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} XR camera not found.");
            return;
        }

        passthroughLayer = xrCamera.GetComponent<OVRPassthroughLayer>();
        if (passthroughLayer == null)
            passthroughLayer = xrCamera.gameObject.AddComponent<OVRPassthroughLayer>();

        var fadeSphere = GameObject.Find(FadeSphereName);
        if (fadeSphere != null)
            fadeSphereAnimator = fadeSphere.GetComponent<Animator>();

        savedClearFlags = xrCamera.clearFlags;
        savedBackgroundColor = xrCamera.backgroundColor;
        savedOverlayType = passthroughLayer.overlayType;
        initialized = true;
        ConfigManager.WriteConsole($"{LogPrefix} initialized on {xrCamera.name}, layerType={savedOverlayType}");
    }

    public IEnumerator EnablePassthroughWhenReady()
    {
        if (!initialized)
            Initialize();
        if (passthroughLayer == null || xrCamera == null)
            yield break;

        if (!IsPassthroughRuntimeAvailable())
        {
#if UNITY_EDITOR
            ConfigManager.WriteConsoleWarning(
                $"{LogPrefix} Editor: XR/OVR not initialized — skipping passthrough (MR layout test mode)");
            passthroughSystemReady = true;
            MREditorMrSimulator.Instance?.ApplyPassthroughBackdrop(xrCamera);
#else
            ConfigManager.WriteConsoleError($"{LogPrefix} passthrough unavailable — OVR/XR not ready");
            passthroughSystemReady = false;
#endif
            yield break;
        }

        LogPassthroughDiagnostics("before enable");

        yield return WaitForPassthroughSystemReady();
        if (!passthroughSystemReady)
            yield break;

        ApplyPassthroughRendering();
        yield return WaitUntilPassthroughLayerVisible();

        LogPassthroughDiagnostics("after enable");
    }

    public void DisablePassthrough()
    {
        if (passthroughLayer != null)
        {
            passthroughLayer.enabled = false;
            passthroughLayer.overlayType = savedOverlayType;
        }

        if (xrCamera != null)
        {
            xrCamera.clearFlags = savedClearFlags;
            xrCamera.backgroundColor = savedBackgroundColor;

            Transform psMotes = xrCamera.transform.Find("PS_Motes");
            if (psMotes != null)
                psMotes.gameObject.SetActive(true);
        }

        RenderSettings.fog = savedFog;

        if (OVRManager.instance != null)
            OVRManager.eyeFovPremultipliedAlphaModeEnabled = savedEyeFovPremultipliedAlpha;

        RestoreFadeSphereVisuals();

        MREditorMrSimulator.Instance?.ClearPassthroughBackdrop();

        if (EventManager.Instance != null)
            EventManager.Instance.IsPassthrough = false;

        ConfigManager.WriteConsole($"{LogPrefix} passthrough OFF");
    }

    void ApplyPassthroughRendering()
    {
        if (!IsPassthroughRuntimeAvailable())
            return;

        EnsureInsightPassthroughEnabled();
        SuppressFadeSphereVisuals();

        savedOverlayType = passthroughLayer.overlayType;
        savedEyeFovPremultipliedAlpha = OVRManager.eyeFovPremultipliedAlphaModeEnabled;

        // Underlay = passthrough atrás; mãos/controladores VR renderizam por cima.
        // Overlay (opacity=1) tapava as mãos 3D quando o passthrough passou a funcionar.
        OVRManager.eyeFovPremultipliedAlphaModeEnabled = false;
        passthroughLayer.enabled = false;
        passthroughLayer.hidden = false;
        passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
        passthroughLayer.textureOpacity = 1f;
        passthroughLayer.enabled = true;

        xrCamera.clearFlags = CameraClearFlags.SolidColor;
        xrCamera.backgroundColor = Color.clear;

        savedFog = RenderSettings.fog;
        RenderSettings.fog = false;

        Transform psMotes = xrCamera.transform.Find("PS_Motes");
        if (psMotes != null)
            psMotes.gameObject.SetActive(false);

        if (EventManager.Instance != null)
            EventManager.Instance.IsPassthrough = true;

        if (fadeSphereAnimator != null)
            fadeSphereAnimator.SetTrigger("FadeInTrigger");

        ConfigManager.WriteConsole($"{LogPrefix} passthrough ON (underlay, hands on top)");
    }

    IEnumerator WaitForPassthroughSystemReady()
    {
        passthroughSystemReady = false;

        if (!IsPassthroughRuntimeAvailable())
            yield break;

        EnsureInsightPassthroughEnabled();

        float elapsed = 0f;
        while (!OVRManager.IsInsightPassthroughInitialized()
               && !OVRManager.HasInsightPassthroughInitFailed()
               && elapsed < SystemInitTimeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (OVRManager.HasInsightPassthroughInitFailed())
        {
            ConfigManager.WriteConsoleError(
                $"{LogPrefix} Insight Passthrough init FAILED — verifique OVRProjectConfig (Passthrough Support) e Meta > Tools > Update Android Manifest");
            yield break;
        }

        if (!OVRManager.IsInsightPassthroughInitialized())
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} Insight Passthrough init timeout ({SystemInitTimeoutSeconds}s)");
            yield break;
        }

        passthroughSystemReady = true;
        ConfigManager.WriteConsole($"{LogPrefix} Insight Passthrough system ready");
    }

    IEnumerator WaitUntilPassthroughLayerVisible()
    {
        if (passthroughLayer == null || !passthroughLayer.enabled)
            yield break;

        bool resumed = false;
        UnityAction<OVRPassthroughLayer> onResumed = _ => resumed = true;
        passthroughLayer.passthroughLayerResumed.AddListener(onResumed);

        float elapsed = 0f;
        while (!resumed && elapsed < LayerReadyTimeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        passthroughLayer.passthroughLayerResumed.RemoveListener(onResumed);

        if (resumed)
            ConfigManager.WriteConsole($"{LogPrefix} passthrough layer resumed (visible on HMD)");
        else
            ConfigManager.WriteConsoleWarning($"{LogPrefix} passthrough layer resume timeout ({LayerReadyTimeoutSeconds}s)");
    }

    void SuppressFadeSphereVisuals()
    {
        disabledFadeRenderers.Clear();
        var fadeSphere = GameObject.Find(FadeSphereName);
        if (fadeSphere == null)
            return;

        foreach (Renderer renderer in fadeSphere.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
                continue;
            disabledFadeRenderers.Add(renderer);
            renderer.enabled = false;
        }

        if (disabledFadeRenderers.Count > 0)
            ConfigManager.WriteConsole($"{LogPrefix} disabled {disabledFadeRenderers.Count} fade sphere renderer(s)");
    }

    void RestoreFadeSphereVisuals()
    {
        foreach (Renderer renderer in disabledFadeRenderers)
        {
            if (renderer != null)
                renderer.enabled = true;
        }
        disabledFadeRenderers.Clear();
    }

    void LogPassthroughDiagnostics(string stage)
    {
        if (!IsPassthroughRuntimeAvailable())
        {
            ConfigManager.WriteConsole($"{LogPrefix} diag [{stage}] passthrough runtime unavailable (XR not initialized)");
            return;
        }

        bool supported = OVRManager.IsInsightPassthroughSupported();
        bool initialized = OVRManager.IsInsightPassthroughInitialized();
        bool pending = OVRManager.IsInsightPassthroughInitPending();
        bool failed = OVRManager.HasInsightPassthroughInitFailed();
        bool managerEnabled = OVRManager.instance != null && OVRManager.instance.isInsightPassthroughEnabled;
        bool layerEnabled = passthroughLayer != null && passthroughLayer.enabled;

        ConfigManager.WriteConsole(
            $"{LogPrefix} diag [{stage}] supported={supported} init={initialized} pending={pending} failed={failed} manager={managerEnabled} layer={layerEnabled}");
    }

    static void EnsureInsightPassthroughEnabled()
    {
        if (!IsPassthroughRuntimeAvailable())
            return;

        if (OVRManager.instance == null)
            return;

        if (!OVRManager.instance.isInsightPassthroughEnabled)
            OVRManager.instance.isInsightPassthroughEnabled = true;
    }

    static Camera ResolveXRCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        var origin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null && origin.Camera != null)
            return origin.Camera;

        var tagged = GameObject.FindGameObjectWithTag("MainCamera");
        return tagged != null ? tagged.GetComponent<Camera>() : null;
    }
}
