/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns ConfigurationCabinet in the MR environment using a single pose computed
/// by MREnvironmentSurfaces (floor projection + wall clearance + face-player rotation).
/// </summary>
public class MRConfigurationCabinetController : MonoBehaviour
{
    const string LogPrefix = "[MRConfigurationCabinetController]";
    const string CabinetPrefabPath = "UICabinet/ConfigurationCabinet";
    static readonly Vector3 ConfigurationCabinetFootprint = new Vector3(0.54f, 2.38f, 0.70f);

    public static MRConfigurationCabinetController Instance { get; private set; }

    [SerializeField] float spawnDistanceMeters = 1.4f;
    [SerializeField] float spawnYawOffsetDegrees = 0f;
#if UNITY_EDITOR
    [SerializeField] bool editorAutoSpawnInVr = false;
    [SerializeField] float editorAutoSpawnDelaySeconds = 2f;
    [Tooltip("Editor only: re-place cabinet every frame (move MRUK room to test).")]
    [SerializeField] bool editorContinuousFloorSnap = true;
#endif

    GameObject cabinetInstance;
    MRConfigurationController configurationController;
    bool isEditOpen;

    public bool IsEditOpen => isEditOpen;
    public bool HasCabinet => cabinetInstance != null;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void OnEnable()
    {
        if (MixedRealityManager.Instance != null)
            MixedRealityManager.Instance.OnModeChanged += HandleModeChanged;
    }

    void Start()
    {
#if UNITY_EDITOR
        if (editorAutoSpawnInVr)
            StartCoroutine(EditorAutoSpawnWhenReady());
#endif
    }

    void OnDisable()
    {
        if (MixedRealityManager.Instance != null)
            MixedRealityManager.Instance.OnModeChanged -= HandleModeChanged;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

#if UNITY_EDITOR
    void LateUpdate()
    {
        if (!Application.isEditor || !editorContinuousFloorSnap || cabinetInstance == null)
            return;

        if (MixedRealityManager.Instance == null)
            return;

        ExperienceMode mode = MixedRealityManager.Instance.CurrentMode;
        if (mode != ExperienceMode.MR && mode != ExperienceMode.MR_EDIT)
            return;

        ApplyPoseToCabinet(cabinetInstance);
    }
#endif

    void HandleModeChanged(ExperienceMode mode)
    {
        if (mode == ExperienceMode.MR)
            SpawnAtMrOrigin();
        else if (mode == ExperienceMode.VR)
            Despawn();
    }

    public void ToggleEdit()
    {
        if (isEditOpen)
            CloseEdit();
        else
            OpenEdit();
    }

    public void OpenEdit()
    {
        if (isEditOpen)
            return;

        if (MixedRealityManager.Instance == null)
            return;

        ExperienceMode mode = MixedRealityManager.Instance.CurrentMode;
        if (mode != ExperienceMode.MR && mode != ExperienceMode.MR_EDIT)
        {
#if UNITY_EDITOR
            if (mode == ExperienceMode.VR && !HasCabinet)
                SpawnForEditorTest();
#else
            return;
#endif
        }

        if (!HasCabinet)
            SpawnAtMrOrigin();

        if (configurationController == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} open failed — MRConfigurationController missing");
            return;
        }

        if (mode == ExperienceMode.MR)
            MixedRealityManager.Instance.EnterMREdit();

        configurationController.Activate();
        isEditOpen = true;
        ConfigManager.WriteConsole($"{LogPrefix} edit panel open");
    }

    public void CloseEdit()
    {
        if (!isEditOpen && configurationController != null && !configurationController.IsActive)
            return;

        configurationController?.Deactivate();
        isEditOpen = false;

        if (MixedRealityManager.Instance != null
            && MixedRealityManager.Instance.CurrentMode == ExperienceMode.MR_EDIT)
            MixedRealityManager.Instance.ExitMREdit();

        ConfigManager.WriteConsole($"{LogPrefix} edit panel closed");
    }

    public void ForceCloseEdit()
    {
        configurationController?.Deactivate();
        isEditOpen = false;
    }

    public void SpawnAtMrOrigin()
    {
        if (cabinetInstance != null)
            return;

        GameObject prefab = Resources.Load<GameObject>(CabinetPrefabPath);
        if (prefab == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} prefab not found: {CabinetPrefabPath}");
            return;
        }

        cabinetInstance = Instantiate(prefab);
        cabinetInstance.name = "MRConfigurationCabinet";
        // World root: MRUK floor is world-locked; parenting to MRSpaceOrigin skews Y on device.
        cabinetInstance.transform.SetParent(null, true);
        PrepareCabinetInstance(cabinetInstance);
        ApplyPoseToCabinet(cabinetInstance);
        ConfigManager.WriteConsole($"{LogPrefix} spawned at {cabinetInstance.transform.position} rot={cabinetInstance.transform.eulerAngles}");
    }

    public void Despawn()
    {
        ForceCloseEdit();
        if (cabinetInstance != null)
            Destroy(cabinetInstance);
        cabinetInstance = null;
        configurationController = null;
    }

    void PrepareCabinetInstance(GameObject root)
    {
        foreach (ConfigurationController config in root.GetComponentsInChildren<ConfigurationController>(true))
            config.enabled = false;

        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is ScreenGenerator || behaviour is MRConfigurationController)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName == "ConfigurationController"
                || typeName == "Teleportation"
                || typeName == "basicAGE"
                || typeName == "BehaviorTree")
                behaviour.enabled = false;
        }

        foreach (AudioSource audio in root.GetComponentsInChildren<AudioSource>(true))
            audio.enabled = false;

        ScreenGenerator screen = root.GetComponentInChildren<ScreenGenerator>(true);
        if (screen == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} ScreenGenerator not found on ConfigurationCabinet");
            return;
        }

        configurationController = root.GetComponent<MRConfigurationController>();
        if (configurationController == null)
            configurationController = root.AddComponent<MRConfigurationController>();

        configurationController.Initialize(screen);
    }

    /// <summary>
    /// Single source of truth for cabinet placement:
    /// 1) Ask MREnvironmentSurfaces for (worldPos, worldRot) — already floor-projected + room-clamped.
    /// 2) Lift by pivot-to-bottom so the cabinet base lands exactly on the floor plane.
    /// 3) Apply yaw offset and write the pose.
    /// </summary>
    void ApplyPoseToCabinet(GameObject root)
    {
        if (root == null)
            return;

        Transform player = FindPlayerTransform();
        MREnvironmentSurfaces surfaces = MREnvironmentSurfaces.Instance;

        Vector3 worldPos;
        Quaternion worldRot;
        if (surfaces != null && player != null
            && surfaces.TryGetConfigurationCabinetPose(
                player, spawnDistanceMeters, ConfigurationCabinetFootprint, out worldPos, out worldRot))
        {
            worldRot *= Quaternion.Euler(0f, spawnYawOffsetDegrees, 0f);
        }
        else
        {
            // Fallback when MR surfaces are not ready: face the player, use player Y minus eye height.
            Vector3 forward = player != null ? player.forward : root.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 basePos = player != null ? player.position : root.transform.position;
            worldPos = basePos + forward * spawnDistanceMeters;
            worldPos.y = basePos.y - 1.6f;

            Vector3 toPlayer = (player != null ? player.position : worldPos + forward) - worldPos;
            toPlayer.y = 0f;
            worldRot = toPlayer.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(toPlayer.normalized) * Quaternion.Euler(0f, spawnYawOffsetDegrees, 0f)
                : Quaternion.LookRotation(-forward) * Quaternion.Euler(0f, spawnYawOffsetDegrees, 0f);
        }

        root.transform.SetPositionAndRotation(worldPos, worldRot);

        // Now lift by pivot-to-bottom so the cabinet base lands on the floor.
        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.GetComponentInChildren<BoxCollider>();
        if (box != null)
        {
            float bottomY = PlaceOnFloorFromBoxCollider.CalculateLowerPointY(root.transform, box);
            float pivotToBottom = root.transform.position.y - bottomY;
            root.transform.position = new Vector3(worldPos.x, worldPos.y + pivotToBottom, worldPos.z);
        }
    }

#if UNITY_EDITOR
    IEnumerator EditorAutoSpawnWhenReady()
    {
        yield return new WaitForSeconds(editorAutoSpawnDelaySeconds);

        MREnvironmentSurfaces surfaces = MREnvironmentSurfaces.Instance;
        if (surfaces != null && !surfaces.IsReady)
            yield return surfaces.ProbeWhenReady(FindPlayerTransform());

        if (HasCabinet)
            yield break;

        if (MixedRealityManager.Instance != null
            && MixedRealityManager.Instance.CurrentMode == ExperienceMode.MR)
        {
            SpawnAtMrOrigin();
            yield break;
        }

        SpawnForEditorTest();
        OpenEdit();
    }

    void SpawnForEditorTest()
    {
        if (HasCabinet)
            return;

        GameObject prefab = Resources.Load<GameObject>(CabinetPrefabPath);
        if (prefab == null)
            return;

        cabinetInstance = Instantiate(prefab);
        cabinetInstance.name = "MRConfigurationCabinet_EditorTest";
        cabinetInstance.transform.SetParent(null, true);
        PrepareCabinetInstance(cabinetInstance);
        ApplyPoseToCabinet(cabinetInstance);
        ConfigManager.WriteConsole($"{LogPrefix} editor test cabinet spawned at {cabinetInstance.transform.position}");
    }
#endif

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
}
