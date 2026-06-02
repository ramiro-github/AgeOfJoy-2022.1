/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Opaque dark tunnel around the phone booth during immersive travel (runtime geometry, no prefab).
/// </summary>
[DisallowMultipleComponent]
public class MRPhoneBoothTravelVfx : MonoBehaviour
{
    const string LogPrefix = "[MRPhoneBoothTravelVfx]";
    const string VfxRootName = "VFX_Travel_Runtime";
    const string WallName = "VFX_TravelDark";
    const string FloorName = "VFX_TravelFloor";
    const float UnityPlaneMeshSize = 10f;

    [SerializeField] float tunnelRadius = 1.2f;
    [SerializeField] float tunnelHeight = 4.5f;
    [SerializeField] float verticalOffset = 0.15f;
    [SerializeField] float floorYOffset = 0.14f;
    [SerializeField] Color tunnelColor = new Color(0f, 0f, 0f, 1f);
    [SerializeField] Color ambientDuringTravel = new Color(0.02f, 0.02f, 0.02f);
    [SerializeField] float boothLightMultiplierDuringTravel = 0f;

    GameObject vfxRoot;
    Material tunnelMaterial;
    bool journeyActive;

    readonly List<LightSnapshot> lightSnapshots = new List<LightSnapshot>();
    AmbientSnapshot ambientSnapshot;
    CameraSnapshot cameraSnapshot;

    struct LightSnapshot
    {
        public Light Light;
        public bool Enabled;
        public float Intensity;
    }

    struct AmbientSnapshot
    {
        public bool Saved;
        public AmbientMode Mode;
        public Color Light;
        public float Intensity;
    }

    struct CameraSnapshot
    {
        public bool Saved;
        public Camera Camera;
        public CameraClearFlags ClearFlags;
        public Color BackgroundColor;
    }

    public bool IsJourneyActive => journeyActive;

    public void BeginJourneyVisuals()
    {
        if (journeyActive)
            return;

        journeyActive = true;
        EnsureVfxRoot();
        BuildOrEnableTunnel();
        ApplyDarkEnvironment();
        vfxRoot.SetActive(true);

        ConfigManager.WriteConsole($"{LogPrefix} journey visuals ON");
        MRTransitionLog.LogStep("MRPhoneBoothTravelVfx", "BeginJourneyVisuals");
    }

    public void EndJourneyVisuals()
    {
        if (!journeyActive)
            return;

        journeyActive = false;
        RestoreDarkEnvironment();

        if (vfxRoot != null)
            vfxRoot.SetActive(false);

        ConfigManager.WriteConsole($"{LogPrefix} journey visuals OFF");
        MRTransitionLog.LogStep("MRPhoneBoothTravelVfx", "EndJourneyVisuals");
    }

    void EnsureVfxRoot()
    {
        if (vfxRoot != null)
            return;

        vfxRoot = new GameObject(VfxRootName);
        vfxRoot.transform.SetParent(transform, false);
        vfxRoot.SetActive(false);
    }

    void BuildOrEnableTunnel()
    {
        Transform existingWall = vfxRoot.transform.Find(WallName);
        if (existingWall != null)
            return;

        float wallCenterY = tunnelHeight * 0.5f + verticalOffset;
        float wallHeightScale = tunnelHeight * 0.5f;
        float floorY = verticalOffset + floorYOffset;
        float planeScale = (tunnelRadius * 2f) / UnityPlaneMeshSize;

        tunnelMaterial = CreateOpaqueTunnelMaterial(tunnelColor);

        var wallGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wallGo.name = WallName;
        wallGo.transform.SetParent(vfxRoot.transform, false);
        wallGo.transform.localPosition = new Vector3(0f, wallCenterY, 0f);
        wallGo.transform.localRotation = Quaternion.identity;
        wallGo.transform.localScale = new Vector3(tunnelRadius * 2f, wallHeightScale, tunnelRadius * 2f);
        ConfigureTunnelRenderer(wallGo, tunnelMaterial, invertForInteriorView: true);

        var floorGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floorGo.name = FloorName;
        floorGo.transform.SetParent(vfxRoot.transform, false);
        floorGo.transform.localPosition = new Vector3(0f, floorY, 0f);
        floorGo.transform.localRotation = Quaternion.identity;
        floorGo.transform.localScale = new Vector3(planeScale, 1f, planeScale);
        ConfigureTunnelRenderer(floorGo, tunnelMaterial, invertForInteriorView: false);
    }

    static void ConfigureTunnelRenderer(GameObject go, Material material, bool invertForInteriorView)
    {
        var collider = go.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        if (invertForInteriorView)
        {
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
                meshFilter.mesh = CreateInteriorViewMesh(meshFilter.sharedMesh);
        }

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    static Material CreateOpaqueTunnelMaterial(Color color)
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        var material = new Material(shader) { name = "MRTravelTunnel_Opaque" };
        material.color = color;

        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", (float)CullMode.Front);

        material.renderQueue = (int)RenderQueue.Geometry + 10;
        return material;
    }

    static Mesh CreateInteriorViewMesh(Mesh source)
    {
        var mesh = Instantiate(source);
        mesh.name = source.name + "_Interior";

        Vector3[] normals = mesh.normals;
        for (int i = 0; i < normals.Length; i++)
            normals[i] = -normals[i];
        mesh.normals = normals;

        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            int[] triangles = mesh.GetTriangles(subMesh);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int tmp = triangles[i];
                triangles[i] = triangles[i + 1];
                triangles[i + 1] = tmp;
            }

            mesh.SetTriangles(triangles, subMesh);
        }

        return mesh;
    }

    void ApplyDarkEnvironment()
    {
        if (!ambientSnapshot.Saved)
        {
            ambientSnapshot = new AmbientSnapshot
            {
                Saved = true,
                Mode = RenderSettings.ambientMode,
                Light = RenderSettings.ambientLight,
                Intensity = RenderSettings.ambientIntensity
            };
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientDuringTravel;
        RenderSettings.ambientIntensity = 1f;

        lightSnapshots.Clear();
        foreach (Light light in GetComponentsInChildren<Light>(true))
        {
            if (light == null)
                continue;

            lightSnapshots.Add(new LightSnapshot
            {
                Light = light,
                Enabled = light.enabled,
                Intensity = light.intensity
            });

            light.enabled = true;
            light.intensity *= boothLightMultiplierDuringTravel;
        }

        Camera camera = ResolveXrCamera();
        if (camera != null && !cameraSnapshot.Saved)
        {
            cameraSnapshot = new CameraSnapshot
            {
                Saved = true,
                Camera = camera,
                ClearFlags = camera.clearFlags,
                BackgroundColor = camera.backgroundColor
            };

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }
    }

    void RestoreDarkEnvironment()
    {
        if (ambientSnapshot.Saved)
        {
            RenderSettings.ambientMode = ambientSnapshot.Mode;
            RenderSettings.ambientLight = ambientSnapshot.Light;
            RenderSettings.ambientIntensity = ambientSnapshot.Intensity;
            ambientSnapshot.Saved = false;
        }

        foreach (LightSnapshot snapshot in lightSnapshots)
        {
            if (snapshot.Light == null)
                continue;

            snapshot.Light.enabled = snapshot.Enabled;
            snapshot.Light.intensity = snapshot.Intensity;
        }

        lightSnapshots.Clear();

        if (cameraSnapshot.Saved && cameraSnapshot.Camera != null)
        {
            cameraSnapshot.Camera.clearFlags = cameraSnapshot.ClearFlags;
            cameraSnapshot.Camera.backgroundColor = cameraSnapshot.BackgroundColor;
            cameraSnapshot.Saved = false;
        }
    }

    static Camera ResolveXrCamera()
    {
        var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
            return xrOrigin.Camera;

        return Camera.main;
    }

    void OnDestroy()
    {
        if (journeyActive)
            RestoreDarkEnvironment();

        if (tunnelMaterial != null)
            Destroy(tunnelMaterial);
    }
}
