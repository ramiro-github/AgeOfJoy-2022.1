#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Evita falha de build quando prefabs do Meta XR em Packages/ ficam "dirty"
/// (ex.: [BB] Hand Ray) — bug comum no Unity 2022.3.0f1 + SortingGroup.
/// Corrige definitivamente: atualizar Unity para 2022.3.46f1 ou mais recente.
/// </summary>
public class MetaImmutablePrefabBuildGuard : IPreprocessBuildWithReport
{
    const string LogPrefix = "[MetaImmutablePrefabBuildGuard]";

    static readonly string[] KnownMetaPrefabPaths =
    {
        "Packages/com.meta.xr.sdk.interaction.ovr/Editor/Blocks/Interactors/Prefabs/[BB] Hand Ray.prefab",
        "Packages/com.meta.xr.sdk.interaction.ovr/Editor/Blocks/Interactors/Prefabs/[BB] OVR Camera Rig Interaction.prefab",
    };

    public int callbackOrder => -500;

    [InitializeOnLoadMethod]
    static void ScheduleStartupCleanup()
    {
        EditorApplication.delayCall += () => ClearDirtyImmutablePackagePrefabs(log: false);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        ClearDirtyImmutablePackagePrefabs(log: true);
    }

    [MenuItem("Age of Joy/Fix Meta immutable prefab dirty state (before build)")]
    public static void FixFromMenu()
    {
        int cleared = ClearDirtyImmutablePackagePrefabs(log: true);
        Debug.Log($"{LogPrefix} Concluído — {cleared} asset(s) limpos. Tente Build novamente.");
    }

    static int ClearDirtyImmutablePackagePrefabs(bool log)
    {
        int cleared = 0;
        var paths = new HashSet<string>(KnownMetaPrefabPaths);

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Packages" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }

        foreach (string path in paths)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Packages/"))
                continue;
            if (!System.IO.File.Exists(path))
                continue;

            cleared += ClearDirtyAtPath(path, log);
        }


        return cleared;
    }

    static int ClearDirtyAtPath(string path, bool log)
    {
        int count = 0;
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        if (assets == null || assets.Length == 0)
            assets = new Object[] { AssetDatabase.LoadMainAssetAtPath(path) };

        foreach (Object asset in assets)
        {
            if (asset == null || !EditorUtility.IsDirty(asset))
                continue;

            EditorUtility.ClearDirty(asset);
            count++;
            if (log)
                Debug.LogWarning($"{LogPrefix} ClearDirty: {path} ({asset.name})");
        }

        if (count > 0 && log)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        return count;
    }
}
#endif
