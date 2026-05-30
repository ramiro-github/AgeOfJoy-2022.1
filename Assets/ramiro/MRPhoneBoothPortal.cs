/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Phone booth portal on PF_Payphone — interior volume + handset grab triggers immersive travel.
/// </summary>
public class MRPhoneBoothPortal : MonoBehaviour
{
    const string LogPrefix = "[MRPhoneBoothPortal]";
    const string BoothObjectName = "PF_Payphone";
    const string HandsetObjectName = "PF_Grabbable_Phone";
    const float DefaultTravelDurationSeconds = 3.5f;

    [SerializeField] float travelDurationSeconds = DefaultTravelDurationSeconds;
    [SerializeField] BoxCollider interiorTrigger;
    [SerializeField] AudioSource travelAudioSource;

    bool isTravelerInstance;
    int playersInside;
    bool travelInProgress;
    bool handsetGrabbed;
    PhoneBoothTravelState pendingTravelState;
    XRGrabInteractable handsetGrab;
    Coroutine travelCoroutine;

    public bool IsTravelerInstance
    {
        get => isTravelerInstance;
        set => isTravelerInstance = value;
    }

    public bool TravelInProgress => travelInProgress;
    public bool PlayerInside => playersInside > 0;

    void Awake()
    {
        EnsureInteriorTrigger();
        BindHandsetGrab();
        if (travelAudioSource == null)
            travelAudioSource = GetComponentInChildren<AudioSource>();
    }

    void OnEnable()
    {
        SceneManagerHook.EnsureRegistered();
    }

    void OnDestroy()
    {
        UnbindHandsetGrab();
    }

    public PhoneBoothTravelState CaptureTravelState()
    {
        Transform player = FindPlayerTransform();
        if (player == null)
            return null;

        return PhoneBoothTravelState.Capture(transform, player, handsetGrabbed);
    }

    public void ApplyTravelState(PhoneBoothTravelState state)
    {
        if (state == null)
            return;

        Transform player = FindPlayerTransform();
        state.ApplyToPlayer(transform, player);
        MRTransitionLog.Log($"{LogPrefix} applied travel state inside={PlayerInside} traveler={isTravelerInstance}");
    }

    public void BeginTravelToMR()
    {
        if (travelInProgress || MixedRealityManager.Instance == null)
            return;

        if (MixedRealityManager.Instance.CurrentMode != ExperienceMode.VR)
            return;

        if (!PlayerInside)
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} BeginTravelToMR ignored — player not inside");
            return;
        }

        if (!MixedRealityManager.Instance.CanToggleMode())
            return;

        pendingTravelState = CaptureTravelState();
        if (pendingTravelState == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} BeginTravelToMR failed — no travel state");
            return;
        }

        MRTransitionLog.LogStep("MRPhoneBoothPortal", "BeginTravelToMR");
        travelCoroutine = StartCoroutine(PlayTravelThen(() =>
            MixedRealityManager.Instance.EnterMRFromPhoneBooth(this)));
    }

    public void BeginTravelToVR()
    {
        if (travelInProgress || MixedRealityManager.Instance == null)
            return;

        if (!MixedRealityManager.IsVrExperience && MixedRealityManager.Instance.CurrentMode != ExperienceMode.MR_EDIT)
        {
            if (MixedRealityManager.Instance.CurrentMode != ExperienceMode.MR)
                return;
        }

        if (!PlayerInside)
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} BeginTravelToVR ignored — player not inside");
            return;
        }

        if (!MixedRealityManager.Instance.CanToggleMode())
            return;

        pendingTravelState = CaptureTravelState();
        if (pendingTravelState == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} BeginTravelToVR failed — no travel state");
            return;
        }

        MRTransitionLog.LogStep("MRPhoneBoothPortal", "BeginTravelToVR");
        travelCoroutine = StartCoroutine(PlayTravelThen(() =>
            MixedRealityManager.Instance.EnterVRFromPhoneBooth(this)));
    }

    public PhoneBoothTravelState ConsumePendingTravelState()
    {
        PhoneBoothTravelState state = pendingTravelState;
        pendingTravelState = null;
        return state;
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    void EnsureInteriorTrigger()
    {
        if (interiorTrigger != null)
            return;

        var volumeGo = new GameObject("InteriorVolume");
        volumeGo.transform.SetParent(transform, false);
        volumeGo.transform.localPosition = new Vector3(0f, 1f, -0.15f);
        volumeGo.transform.localRotation = Quaternion.identity;

        interiorTrigger = volumeGo.AddComponent<BoxCollider>();
        interiorTrigger.isTrigger = true;
        interiorTrigger.size = new Vector3(1.1f, 2.2f, 1.1f);
        interiorTrigger.center = Vector3.zero;

        var relay = volumeGo.AddComponent<MRPhoneBoothInteriorRelay>();
        relay.Portal = this;
    }

    void BindHandsetGrab()
    {
        Transform handsetRoot = transform.Find(HandsetObjectName);
        if (handsetRoot == null)
            handsetGrab = GetComponentInChildren<XRGrabInteractable>(true);
        else
            handsetGrab = handsetRoot.GetComponentInChildren<XRGrabInteractable>(true);

        if (handsetGrab == null)
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} handset XRGrabInteractable not found on {name}");
            return;
        }

        handsetGrab.selectEntered.AddListener(OnHandsetGrabbed);
        handsetGrab.selectExited.AddListener(OnHandsetReleased);
    }

    void UnbindHandsetGrab()
    {
        if (handsetGrab == null)
            return;

        handsetGrab.selectEntered.RemoveListener(OnHandsetGrabbed);
        handsetGrab.selectExited.RemoveListener(OnHandsetReleased);
    }

    void OnHandsetGrabbed(SelectEnterEventArgs _)
    {
        handsetGrabbed = true;
        TryStartTravelFromHandset();
    }

    void OnHandsetReleased(SelectExitEventArgs _)
    {
        handsetGrabbed = false;
    }

    void TryStartTravelFromHandset()
    {
        if (!PlayerInside || travelInProgress)
            return;

        if (MixedRealityManager.Instance == null)
            return;

        if (MixedRealityManager.Instance.CurrentMode == ExperienceMode.VR)
            BeginTravelToMR();
        else if (MixedRealityManager.Instance.IsMrEnvironmentActive())
            BeginTravelToVR();
    }

    internal void NotifyPlayerEntered()
    {
        playersInside++;
    }

    internal void NotifyPlayerExited()
    {
        playersInside = Mathf.Max(0, playersInside - 1);
    }

    IEnumerator PlayTravelThen(System.Action onComplete)
    {
        travelInProgress = true;
        MRTransitionLog.LogStep("MRPhoneBoothPortal", "PlayTravelEffect start");

        if (travelAudioSource != null && travelAudioSource.clip != null)
            travelAudioSource.Play();

        var fadeSphere = GameObject.Find("SM_FadeSphere");
        Animator fadeAnimator = fadeSphere != null ? fadeSphere.GetComponent<Animator>() : null;
        if (fadeAnimator != null)
            fadeAnimator.SetTrigger("FadeInTrigger");

        float duration = Mathf.Max(1f, travelDurationSeconds);
        yield return new WaitForSeconds(duration);

        travelInProgress = false;
        travelCoroutine = null;
        MRTransitionLog.LogStep("MRPhoneBoothPortal", "PlayTravelEffect end");
        onComplete?.Invoke();
    }

    static Transform FindPlayerTransform()
    {
        var pc = FindObjectOfType<PlayerController>();
        if (pc != null && pc.PlayerControllerGameObject != null)
            return pc.PlayerControllerGameObject.transform;

        var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
        return xrOrigin != null ? xrOrigin.transform : null;
    }

    /// <summary>Attach portal to scene booths that lack one.</summary>
    public static MRPhoneBoothPortal EnsureOn(GameObject boothRoot)
    {
        if (boothRoot == null)
            return null;

        var portal = boothRoot.GetComponent<MRPhoneBoothPortal>();
        if (portal == null)
            portal = boothRoot.AddComponent<MRPhoneBoothPortal>();

        return portal;
    }

    static class SceneManagerHook
    {
        static bool registered;

        public static void EnsureRegistered()
        {
            if (registered)
                return;

            registered = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.name != "IntroGalleryExterior")
                return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != BoothObjectName && !root.name.StartsWith(BoothObjectName))
                    continue;

                EnsureOn(root);
            }
        }
    }
}

/// <summary>Forwards trigger events from the interior volume child to the portal.</summary>
public class MRPhoneBoothInteriorRelay : MonoBehaviour
{
    public MRPhoneBoothPortal Portal;

    void OnTriggerEnter(Collider other)
    {
        if (Portal == null || !IsPlayerCollider(other))
            return;

        Portal.NotifyPlayerEntered();
    }

    void OnTriggerExit(Collider other)
    {
        if (Portal == null || !IsPlayerCollider(other))
            return;

        Portal.NotifyPlayerExited();
    }

    static bool IsPlayerCollider(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() != null)
            return true;

        Transform t = other.transform;
        while (t != null)
        {
            string n = t.name;
            if (n.Contains("OVRPlayerController") || n == "GrabVolumeSmall" || n == "GrabVolumeBig")
                return true;
            t = t.parent;
        }

        return false;
    }
}
