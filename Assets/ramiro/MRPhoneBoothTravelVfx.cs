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

    [Header("Cabinet shake (visual mesh only — not the booth root)")]
    [SerializeField] Transform shakeTransform;
    [SerializeField] float shakePositionAmplitude = 0.012f;
    [SerializeField] float shakeRotationAmplitude = 0.25f;
    [SerializeField] float shakeFrequency = 11f;

    bool journeyActive;
    Coroutine shakeCoroutine;
    Vector3 shakeBaseLocalPosition;
    Quaternion shakeBaseLocalRotation;

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

    public bool IsJourneyActive => journeyActive;

    void Awake()
    {
        RemoveLegacyRuntimeVfx();
        RemoveScriptSpawnedLights();
        CacheMaterialTargets();
    }

    public void BeginJourneyVisuals()
    {
        if (journeyActive)
            return;

        journeyActive = true;
        CacheMaterialTargets();
        ApplyPhoneBoothTravelGlow();
        ApplyPhoneBoothOpaqueGlass();
        StartCabinetShake();

        ConfigManager.WriteConsole(
            $"{LogPrefix} journey ON glow={glowMaterialTargets.Count} glass={glassMaterialTargets.Count} shake={GetShakeTransform().name}");
        MRTransitionLog.LogStep("MRPhoneBoothTravelVfx", "BeginJourneyVisuals");
    }

    public void EndJourneyVisuals()
    {
        if (!journeyActive)
            return;

        journeyActive = false;
        StopCabinetShake();
        RestorePhoneBoothTravelGlow();
        RestorePhoneBoothGlass();

        ConfigManager.WriteConsole($"{LogPrefix} journey OFF");
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

    Transform GetShakeTransform()
    {
        if (shakeTransform != null)
            return shakeTransform;

        Transform meshRoot = transform.Find(PayphoneMeshObjectName);
        if (meshRoot != null)
            return meshRoot;

        MeshRenderer meshRenderer = GetComponentInChildren<MeshRenderer>(true);
        return meshRenderer != null ? meshRenderer.transform : null;
    }

    void StartCabinetShake()
    {
        Transform target = GetShakeTransform();
        if (target == null || target == transform)
        {
            ConfigManager.WriteConsoleWarning(
                $"{LogPrefix} shake skipped — assign SM_PayPhone mesh transform, not booth root");
            return;
        }

        StopCabinetShake();
        shakeTransform = target;
        shakeBaseLocalPosition = shakeTransform.localPosition;
        shakeBaseLocalRotation = shakeTransform.localRotation;
        shakeCoroutine = StartCoroutine(CabinetShakeRoutine());
    }

    void StopCabinetShake()
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            shakeCoroutine = null;
        }

        if (shakeTransform == null || shakeTransform == transform)
            return;

        shakeTransform.localPosition = shakeBaseLocalPosition;
        shakeTransform.localRotation = shakeBaseLocalRotation;
    }

    IEnumerator CabinetShakeRoutine()
    {
        Transform target = shakeTransform;
        float seed = Random.Range(0f, 100f);
        while (journeyActive && target != null)
        {
            float time = Time.time * shakeFrequency;
            float nx = Mathf.PerlinNoise(seed, time) * 2f - 1f;
            float ny = Mathf.PerlinNoise(seed + 17f, time) * 2f - 1f;
            float nz = Mathf.PerlinNoise(seed + 41f, time) * 2f - 1f;

            target.localPosition = shakeBaseLocalPosition
                + new Vector3(nx, ny, nz) * shakePositionAmplitude;

            target.localRotation = shakeBaseLocalRotation * Quaternion.Euler(
                nx * shakeRotationAmplitude,
                ny * shakeRotationAmplitude,
                nz * shakeRotationAmplitude);

            yield return null;
        }
    }

    void OnDestroy()
    {
        if (journeyActive)
            EndJourneyVisuals();

        materialPropertyBlock = null;
    }
}
