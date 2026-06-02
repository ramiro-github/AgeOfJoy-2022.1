/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phone-booth immersive travel: opaque glass, interior glow pulse, light visual mesh shake.
/// </summary>
[DisallowMultipleComponent]
public class MRPhoneBoothTravelVfx : MonoBehaviour
{
    const string LogPrefix = "[MRPhoneBoothTravelVfx]";
    const string LegacyVfxRootName = "VFX_Travel_Runtime";
    const string PayphoneMeshObjectName = "SM_PayPhone";
    const string DoorPhoneboothObjectName = "DoorPhonebooth";
    const string PhoneBoothLightMaterialName = "M_PhoneBooth_Light";
    const string PhoneBoothGlassMaterialName = "M_PhoneBooth_Glass";

    static readonly int GlowMinId = Shader.PropertyToID("_GlowMin");
    static readonly int GlassOpacityId = Shader.PropertyToID("_Opacity");
    static readonly int GlassMrAmountId = Shader.PropertyToID("_MRAmount");

    [Header("Glass (opaque during travel)")]
    [SerializeField] float travelGlassOpacity = 1f;
    [SerializeField] float travelGlassMrAmount = 0f;

    [Header("M_PhoneBooth_Light glow")]
    [SerializeField] float travelGlowMin = 5f;

    [Header("Travel door (optional)")]
    [Tooltip("DoorPhonebooth child on PF_Payphone — off in prefab, on during journey.")]
    [SerializeField] GameObject doorPhonebooth;

    [Header("Cabinet shake (SM_PayPhone + DoorPhonebooth, not booth root)")]
    [SerializeField] Transform shakeTransform;
    [SerializeField] float shakePositionAmplitude = 0.012f;
    [SerializeField] float shakeRotationAmplitude = 0.25f;
    [SerializeField] float shakeFrequency = 11f;

    bool journeyActive;
    PhoneBoothJourneyDirection activeJourneyDirection;
    Coroutine shakeCoroutine;

    MaterialPropertyBlock materialPropertyBlock;
    readonly List<MaterialSlotTarget> glowMaterialTargets = new List<MaterialSlotTarget>();
    readonly List<MaterialSlotTarget> glassMaterialTargets = new List<MaterialSlotTarget>();
    readonly List<GlassMaterialSnapshot> glassMaterialSnapshots = new List<GlassMaterialSnapshot>();
    bool materialTargetsCached;

    struct MaterialSlotTarget
    {
        public Renderer Renderer;
        public int MaterialIndex;
    }

    struct GlassMaterialSnapshot
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public float DefaultOpacity;
        public float DefaultMrAmount;
    }

    struct ShakeTargetState
    {
        public Transform Transform;
        public Vector3 BaseLocalPosition;
        public Quaternion BaseLocalRotation;
    }

    readonly List<ShakeTargetState> shakeTargets = new List<ShakeTargetState>();

    public bool IsJourneyActive => journeyActive;

    void Awake()
    {
        RemoveLegacyRuntimeVfx();
        RemoveScriptSpawnedLights();
        CacheMaterialTargets();
    }

    public void BeginJourneyVisuals(PhoneBoothJourneyDirection direction)
    {
        if (journeyActive)
            return;

        journeyActive = true;
        activeJourneyDirection = direction;
        CacheMaterialTargets();
        ApplyPhoneBoothTravelGlow();
        ApplyPhoneBoothOpaqueGlass();
        SetDoorPhoneboothActive(true);
        StartCabinetShake();

        if (direction == PhoneBoothJourneyDirection.ToMR)
            MRPhoneBoothExteriorSidewalk.SetSidewalk8Active(false);

        ConfigManager.WriteConsole(
            $"{LogPrefix} journey ON dir={direction} glow={glowMaterialTargets.Count} glass={glassMaterialTargets.Count} shakeTargets={shakeTargets.Count}");
        MRTransitionLog.LogStep("MRPhoneBoothTravelVfx", "BeginJourneyVisuals");
    }

    public void EndJourneyVisuals(PhoneBoothJourneyDirection direction)
    {
        if (!journeyActive)
            return;

        journeyActive = false;
        StopCabinetShake();
        RestorePhoneBoothTravelGlow();
        RestorePhoneBoothGlass();
        SetDoorPhoneboothActive(false);

        ConfigManager.WriteConsole($"{LogPrefix} journey OFF dir={direction}");
        MRTransitionLog.LogStep("MRPhoneBoothTravelVfx", "EndJourneyVisuals");
    }

    void RemoveLegacyRuntimeVfx()
    {
        Transform legacyRoot = transform.Find(LegacyVfxRootName);
        if (legacyRoot != null)
            Destroy(legacyRoot.gameObject);
    }

    void RemoveScriptSpawnedLights()
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "SpeedTravelFillLight" || child.name == "ParticleLightTemplate")
                Destroy(child.gameObject);
        }
    }

    void CacheMaterialTargets()
    {
        glowMaterialTargets.Clear();
        glassMaterialTargets.Clear();
        glassMaterialSnapshots.Clear();

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;

                if (material.name.StartsWith(PhoneBoothLightMaterialName))
                {
                    glowMaterialTargets.Add(new MaterialSlotTarget
                    {
                        Renderer = renderer,
                        MaterialIndex = i
                    });
                }
                else if (material.name.StartsWith(PhoneBoothGlassMaterialName))
                {
                    glassMaterialTargets.Add(new MaterialSlotTarget
                    {
                        Renderer = renderer,
                        MaterialIndex = i
                    });

                    glassMaterialSnapshots.Add(new GlassMaterialSnapshot
                    {
                        Renderer = renderer,
                        MaterialIndex = i,
                        DefaultOpacity = material.HasProperty(GlassOpacityId)
                            ? material.GetFloat(GlassOpacityId)
                            : 0.5f,
                        DefaultMrAmount = material.HasProperty(GlassMrAmountId)
                            ? material.GetFloat(GlassMrAmountId)
                            : 0f
                    });
                }
            }
        }

        materialTargetsCached = true;
    }

    void ApplyPhoneBoothTravelGlow()
    {
        if (glowMaterialTargets.Count == 0)
            return;

        EnsureMaterialPropertyBlock();
        foreach (MaterialSlotTarget target in glowMaterialTargets)
        {
            if (target.Renderer == null)
                continue;

            materialPropertyBlock.Clear();
            materialPropertyBlock.SetFloat(GlowMinId, travelGlowMin);
            target.Renderer.SetPropertyBlock(materialPropertyBlock, target.MaterialIndex);
        }
    }

    void RestorePhoneBoothTravelGlow()
    {
        foreach (MaterialSlotTarget target in glowMaterialTargets)
        {
            if (target.Renderer == null)
                continue;

            target.Renderer.SetPropertyBlock(null, target.MaterialIndex);
        }
    }

    void ApplyPhoneBoothOpaqueGlass()
    {
        foreach (GlassMaterialSnapshot snapshot in glassMaterialSnapshots)
        {
            if (snapshot.Renderer == null)
                continue;

            Material material = snapshot.Renderer.materials[snapshot.MaterialIndex];
            if (!material.HasProperty(GlassOpacityId) || !material.HasProperty(GlassMrAmountId))
                continue;

            material.SetFloat(GlassOpacityId, travelGlassOpacity);
            material.SetFloat(GlassMrAmountId, travelGlassMrAmount);
        }
    }

    void RestorePhoneBoothGlass()
    {
        foreach (GlassMaterialSnapshot snapshot in glassMaterialSnapshots)
        {
            if (snapshot.Renderer == null)
                continue;

            Material material = snapshot.Renderer.materials[snapshot.MaterialIndex];
            if (!material.HasProperty(GlassOpacityId) || !material.HasProperty(GlassMrAmountId))
                continue;

            material.SetFloat(GlassOpacityId, snapshot.DefaultOpacity);
            material.SetFloat(GlassMrAmountId, snapshot.DefaultMrAmount);
        }
    }

    void EnsureMaterialPropertyBlock()
    {
        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();
    }

    GameObject ResolveDoorPhonebooth()
    {
        if (doorPhonebooth != null)
            return doorPhonebooth;

        Transform directChild = transform.Find(DoorPhoneboothObjectName);
        if (directChild != null)
        {
            doorPhonebooth = directChild.gameObject;
            return doorPhonebooth;
        }

        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name != DoorPhoneboothObjectName)
                continue;

            doorPhonebooth = child.gameObject;
            break;
        }

        return doorPhonebooth;
    }

    void SetDoorPhoneboothActive(bool active)
    {
        GameObject door = ResolveDoorPhonebooth();
        if (door != null)
            door.SetActive(active);
    }

    Transform GetCabinetMeshShakeTransform()
    {
        if (shakeTransform != null && shakeTransform != transform)
            return shakeTransform;

        Transform meshRoot = transform.Find(PayphoneMeshObjectName);
        if (meshRoot != null)
            return meshRoot;

        MeshRenderer meshRenderer = GetComponentInChildren<MeshRenderer>(true);
        return meshRenderer != null ? meshRenderer.transform : null;
    }

    void BuildShakeTargets()
    {
        shakeTargets.Clear();
        TryAddShakeTarget(GetCabinetMeshShakeTransform());

        GameObject door = ResolveDoorPhonebooth();
        if (door != null)
            TryAddShakeTarget(door.transform);
    }

    void TryAddShakeTarget(Transform target)
    {
        if (target == null || target == transform)
            return;

        foreach (ShakeTargetState existing in shakeTargets)
        {
            if (existing.Transform == target)
                return;
        }

        shakeTargets.Add(new ShakeTargetState
        {
            Transform = target,
            BaseLocalPosition = target.localPosition,
            BaseLocalRotation = target.localRotation
        });
    }

    void StartCabinetShake()
    {
        StopCabinetShake();
        BuildShakeTargets();

        if (shakeTargets.Count == 0)
        {
            ConfigManager.WriteConsoleWarning(
                $"{LogPrefix} shake skipped — no SM_PayPhone or DoorPhonebooth transform");
            return;
        }

        shakeCoroutine = StartCoroutine(CabinetShakeRoutine());
    }

    void StopCabinetShake()
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            shakeCoroutine = null;
        }

        foreach (ShakeTargetState target in shakeTargets)
        {
            if (target.Transform == null)
                continue;

            target.Transform.localPosition = target.BaseLocalPosition;
            target.Transform.localRotation = target.BaseLocalRotation;
        }

        shakeTargets.Clear();
    }

    IEnumerator CabinetShakeRoutine()
    {
        float seed = Random.Range(0f, 100f);
        while (journeyActive)
        {
            float time = Time.time * shakeFrequency;
            float nx = Mathf.PerlinNoise(seed, time) * 2f - 1f;
            float ny = Mathf.PerlinNoise(seed + 17f, time) * 2f - 1f;
            float nz = Mathf.PerlinNoise(seed + 41f, time) * 2f - 1f;

            Vector3 offset = new Vector3(nx, ny, nz) * shakePositionAmplitude;
            Vector3 euler = new Vector3(
                nx * shakeRotationAmplitude,
                ny * shakeRotationAmplitude,
                nz * shakeRotationAmplitude);

            foreach (ShakeTargetState target in shakeTargets)
            {
                if (target.Transform == null)
                    continue;

                target.Transform.localPosition = target.BaseLocalPosition + offset;
                target.Transform.localRotation = target.BaseLocalRotation * Quaternion.Euler(euler);
            }

            yield return null;
        }
    }

    void OnDestroy()
    {
        if (journeyActive)
            EndJourneyVisuals(activeJourneyDirection);

        materialPropertyBlock = null;
    }
}
