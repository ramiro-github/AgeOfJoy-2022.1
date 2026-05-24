#if UNITY_EDITOR
using System;
using Meta.XR.MRUtilityKit;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Aplica configurações Android recomendadas para Meta Quest 3 antes de cada build.
/// Complementa ProjectSettings.asset (pastas Assets/XR e Assets/Oculus podem ser locais/gitignored).
/// </summary>
public class Quest3AndroidBuildSettings : IPreprocessBuildWithReport
{
    const int MinApiLevel = 29;
    const int TargetApiLevel = 32;

    public int callbackOrder => -100;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
            return;

        ApplyPlayerSettings(log: true);
        ApplyOculusQuestTargets(log: true);
        ApplyPassthroughManifest(log: true);
        EnsureMrukRuntimePrefab(log: true);
    }

    [MenuItem("Age of Joy/Configure Android for Meta Quest 3")]
    public static void ApplyFromMenu()
    {
        ApplyPlayerSettings(log: true);
        ApplyOculusQuestTargets(log: true);
        ApplyPassthroughManifest(log: true);
        EnsureMrukRuntimePrefab(log: true);
        Debug.Log("[Quest3AndroidBuildSettings] Configuração Quest 3 aplicada. Salve o projeto (Ctrl+S).");
    }

    static void ApplyPlayerSettings(bool log)
    {
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.curif.AgeOfJoy");

        // Unity 2022.3.0f1 não expõe AndroidApiLevel32 no enum; cast é válido (API 32 = Meta Quest Store).
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)MinApiLevel;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)TargetApiLevel;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.colorSpace = ColorSpace.Linear;

        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;

        // Sustained Performance: AndroidEnableSustainedPerformanceMode em ProjectSettings.asset (API não existe no 2022.3.0f1).

        if (log)
            Debug.Log("[Quest3AndroidBuildSettings] Player: API 29/32, ARM64, IL2CPP, Linear, Vulkan, Instancing");
    }

    static void ApplyOculusQuestTargets(bool log)
    {
        var oculusSettingsType = System.Type.GetType("Unity.XR.Oculus.OculusSettings, Unity.XR.Oculus");
        if (oculusSettingsType == null)
        {
            if (log)
                Debug.LogWarning("[Quest3AndroidBuildSettings] Unity.XR.Oculus não encontrado — use Meta > Tools > Project Setup no Editor.");
            return;
        }

        var getSettings = oculusSettingsType.GetMethod("GetSettings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (getSettings == null)
            return;

        var settings = getSettings.Invoke(null, null);
        if (settings == null)
        {
            if (log)
                Debug.LogWarning("[Quest3AndroidBuildSettings] OculusSettings asset ausente — crie via XR Plug-in Management (Oculus).");
            return;
        }

        SetBool(oculusSettingsType, settings, "TargetQuest2", true);
        SetBool(oculusSettingsType, settings, "TargetQuest3", true);
        SetBool(oculusSettingsType, settings, "TargetQuestPro", false);
        SetBool(oculusSettingsType, settings, "TargetQuest", false);

        EditorUtility.SetDirty(settings as UnityEngine.Object);
        AssetDatabase.SaveAssets();

        if (log)
            Debug.Log("[Quest3AndroidBuildSettings] Oculus: Quest 2 + Quest 3 no manifest (quest2|quest3).");
    }

    /// <summary>
    /// Garante com.oculus.feature.PASSTHROUGH no AndroidManifest via OVRProjectConfig.
    /// Sem isto, IsInsightPassthroughInitialized() falha no dispositivo (ecrã preto).
    /// </summary>
    static void ApplyPassthroughManifest(bool log)
    {
        var config = OVRProjectConfig.CachedProjectConfig;
        if (config == null)
        {
            if (log)
                Debug.LogWarning("[Quest3AndroidBuildSettings] OVRProjectConfig ausente — execute Meta > Tools > Project Setup.");
            return;
        }

        if (config.insightPassthroughSupport == OVRProjectConfig.FeatureSupport.None)
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;

        if (config.sceneSupport == OVRProjectConfig.FeatureSupport.None)
            config.sceneSupport = OVRProjectConfig.FeatureSupport.Required;

        if (config.anchorSupport == OVRProjectConfig.AnchorSupport.Disabled)
            config.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;

        // MR: splash do sistema em passthrough contextual (obrigatório Meta quando passthrough ativo).
        config.systemLoadingScreenBackground =
            OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;

        // MR: sem splash Unity/VR extra por cima do passthrough de arranque.
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.virtualRealitySplashScreen = null;

        OVRProjectConfig.CommitProjectConfig(config);
        AssetDatabase.SaveAssets();

        if (log)
            Debug.Log("[Quest3AndroidBuildSettings] MR: passthrough=Supported, scene=Required, anchors=Enabled, splash=off");

        EnsureSceneSpatialPermissions(log);
    }

    /// <summary>MRUK LoadSceneFromDevice requires com.oculus.permission.USE_SCENE at runtime.</summary>
    static void EnsureSceneSpatialPermissions(bool log)
    {
        const string manifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        string manifest = System.IO.File.Exists(manifestPath)
            ? System.IO.File.ReadAllText(manifestPath)
            : "";

        bool hasScene = manifest.Contains("com.oculus.permission.USE_SCENE", StringComparison.Ordinal);
        bool hasAnchor = manifest.Contains("com.oculus.permission.USE_ANCHOR_API", StringComparison.Ordinal);

        if (!hasScene || !hasAnchor)
        {
            Debug.LogError(
                "[Quest3AndroidBuildSettings] AndroidManifest missing USE_SCENE or USE_ANCHOR_API — " +
                "spatial permission dialog will NOT appear. Add to Assets/Plugins/Android/AndroidManifest.xml");
        }
        else if (log)
        {
            Debug.Log("[Quest3AndroidBuildSettings] AndroidManifest: USE_SCENE + USE_ANCHOR_API present.");
        }
    }

    /// <summary>Copies Meta MRUK prefab into Resources so Quest builds can instantiate it.</summary>
    static void EnsureMrukRuntimePrefab(bool log)
    {
        const string resourcesFolder = "Assets/Resources/MR";
        const string destPath = resourcesFolder + "/MRUK.prefab";

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(resourcesFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "MR");

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
        if (existing != null && existing.GetComponent<MRUK>() != null)
        {
            if (log)
                Debug.Log("[Quest3AndroidBuildSettings] Resources/MR/MRUK.prefab OK.");
            return;
        }

        GameObject source = FindPackageMrukPrefab();
        if (source == null)
        {
            Debug.LogError(
                "[Quest3AndroidBuildSettings] MRUK package prefab not found — install com.meta.xr.mrutilitykit.");
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (existing != null)
            AssetDatabase.DeleteAsset(destPath);

        if (!AssetDatabase.CopyAsset(sourcePath, destPath))
        {
            Debug.LogError("[Quest3AndroidBuildSettings] failed to copy MRUK prefab to Resources/MR/MRUK.prefab");
            return;
        }

        AssetDatabase.SaveAssets();
        if (log)
            Debug.Log($"[Quest3AndroidBuildSettings] copied MRUK prefab to {destPath}");
    }

    static GameObject FindPackageMrukPrefab()
    {
        string[] paths =
        {
            "Packages/com.meta.xr.mrutilitykit/Core/Prefabs/MRUK.prefab",
            "Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab",
        };

        foreach (string path in paths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.GetComponent<MRUK>() != null)
                return prefab;
        }

        foreach (string guid in AssetDatabase.FindAssets("MRUK t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("mrutilitykit", StringComparison.OrdinalIgnoreCase))
                continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.GetComponent<MRUK>() != null)
                return prefab;
        }

        return null;
    }

    static void SetBool(System.Type type, object instance, string fieldName, bool value)
    {
        var field = type.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(bool))
            field.SetValue(instance, value);
    }
}
#endif
