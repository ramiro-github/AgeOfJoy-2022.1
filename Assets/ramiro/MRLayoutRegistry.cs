/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// MR cabinet layout (mr-layout.yaml). Phase 2b: load/save, spawn, delete.
/// Placement Position/Rotation are anchor-local when AnchorUuid is set (v3+), otherwise world space (v2).
/// VR registry.yaml is never modified by this class.
/// </summary>
public class MRLayoutRegistry : MonoBehaviour
{
    const string LogPrefix = "[MRLayoutRegistry]";
    public const string LayoutFileName = "mr-layout.yaml";
    public const int WorldSpaceLayoutVersion = 2;
    public const int AnchorRelativeLayoutVersion = 3;

    public static MRLayoutRegistry Instance { get; private set; }

    readonly Dictionary<string, GameObject> spawnedById = new Dictionary<string, GameObject>();
    MRLayout layout;

    public string LayoutFilePath => Path.Combine(ConfigManager.CabinetsDB, LayoutFileName);

    public int SpawnedCount => spawnedById.Count;

    public IReadOnlyList<MRCabinetPlacement> Placements =>
        layout != null ? layout.GetCabinets() : System.Array.Empty<MRCabinetPlacement>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        MRLibretroWarmup.EnsureOnMainThread();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void EnsureLayoutLoaded()
    {
        if (layout != null)
            return;

        layout = MRLayout.LoadOrCreate(LayoutFilePath);
        ConfigManager.WriteConsole($"{LogPrefix} layout loaded ({layout.Cabinets.Count} entries)");
    }

    /// <summary>Spawn all entries from mr-layout.yaml under MRSpaceOrigin.</summary>
    public void SpawnAll(Transform mrSpaceOrigin)
    {
        DespawnAll();
        EnsureLayoutLoaded();

        if (mrSpaceOrigin == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} SpawnAll skipped — MRSpaceOrigin is null");
            return;
        }

        if (layout.Cabinets.Count == 0)
        {
            ConfigManager.WriteConsole($"{LogPrefix} SpawnAll: layout empty ({LayoutFilePath})");
            return;
        }

        EnsureWorldSpaceLayout(mrSpaceOrigin);

        int index = 0;
        foreach (MRCabinetPlacement placement in layout.GetCabinets())
        {
            if (placement == null || string.IsNullOrEmpty(placement.Id))
                continue;

            if (TrySpawnPlacement(placement, mrSpaceOrigin, index))
                index++;
        }

        ConfigManager.WriteConsole($"{LogPrefix} SpawnAll done ({spawnedById.Count} cabinets)");
    }

    public void DespawnAll()
    {
        foreach (GameObject root in spawnedById.Values)
        {
            if (root != null)
                Destroy(root);
        }

        spawnedById.Clear();
        ConfigManager.WriteConsole($"{LogPrefix} DespawnAll done");
    }

    public bool RemovePlacement(string placementId)
    {
        EnsureLayoutLoaded();
        if (string.IsNullOrEmpty(placementId))
            return false;

        MRCabinetPlacement placement = layout.FindById(placementId);
        if (placement == null)
            return false;

        DestroySpawnedInstance(placementId);
        layout.RemoveById(placementId);
        layout.Save(LayoutFilePath);
        ConfigManager.WriteConsole($"{LogPrefix} removed {placement.DisplayLabel} ({placementId})");
        return true;
    }

    public bool TryGetSpawnedRoot(string placementId, out GameObject root)
    {
        if (string.IsNullOrEmpty(placementId))
        {
            root = null;
            return false;
        }

        return spawnedById.TryGetValue(placementId, out root) && root != null;
    }

    /// <summary>True when the cabinet has an entry in mr-layout (placed in MR space).</summary>
    public bool IsCabinetInScene(string cabinetDBName) =>
        FindPlacementByCabinetDBName(cabinetDBName) != null;

    public MRCabinetPlacement FindPlacementByCabinetDBName(string cabinetDBName)
    {
        EnsureLayoutLoaded();
        return layout?.FindByCabinetDBName(cabinetDBName);
    }

    public bool TryGetPlacementById(string placementId, out MRCabinetPlacement placement)
    {
        EnsureLayoutLoaded();
        placement = layout?.FindById(placementId);
        return placement != null;
    }

    /// <summary>All cabinet folders under cabinetsdb (same rule as GameRegistry: every subfolder).</summary>
    public static List<string> GetCatalogCabinetNames()
    {
        var names = new List<string>();
        string dbPath = ConfigManager.CabinetsDB;

        ConfigManager.CreateFolder(ConfigManager.BaseDir);
        ConfigManager.CreateFolder(dbPath);

        GameRegistry.ReloadCabinetDirectoriesFromDisk();
        if (GameRegistry.cabinetDirectories != null && GameRegistry.cabinetDirectories.Length > 0)
        {
            names.AddRange(GameRegistry.cabinetDirectories);
            ConfigManager.WriteConsole($"{LogPrefix} catalog {names.Count} cabinet(s) (GameRegistry) from {dbPath}");
            return names;
        }

        if (!Directory.Exists(dbPath))
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} CabinetsDB missing: {dbPath}");
            return names;
        }

        foreach (string dir in Directory.GetDirectories(dbPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string folderName = Path.GetFileName(dir);
            if (!IsCatalogFolderName(folderName))
                continue;
            names.Add(folderName);
        }

        ConfigManager.WriteConsole($"{LogPrefix} catalog {names.Count} cabinet(s) (scan) from {dbPath}");
        return names;
    }

    static bool IsCatalogFolderName(string folderName)
    {
        if (string.IsNullOrEmpty(folderName))
            return false;
        if (folderName.StartsWith("."))
            return false;
        if (string.Equals(folderName, Path.GetFileNameWithoutExtension(LayoutFileName), StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>Add to mr-layout.yaml and spawn in the MR space (MVP: one instance per cabinetDBName).</summary>
    public bool TryAddCabinetToScene(
        string cabinetDBName,
        Transform mrSpaceOrigin,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Guid anchorUuid = default)
    {
        EnsureLayoutLoaded();
        if (layout == null || string.IsNullOrEmpty(cabinetDBName) || mrSpaceOrigin == null)
            return false;

        if (FindPlacementByCabinetDBName(cabinetDBName) != null)
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} already in layout: {cabinetDBName}");
            return false;
        }

        var placement = new MRCabinetPlacement
        {
            Id = $"{cabinetDBName}-{Guid.NewGuid():N}".Substring(0, Mathf.Min(48, cabinetDBName.Length + 33)),
            CabinetDBName = cabinetDBName,
            Scale = 1f,
            SurfaceType = PlacementSurfaceType.Floor
        };
        WriteStoredPose(placement, PlacementSurfaceType.Floor, worldPosition, worldRotation, anchorUuid);
        layout.AddPlacement(placement);
        layout.Version = AnchorRelativeLayoutVersion;
        layout.Save(LayoutFilePath);

        int index = spawnedById.Count;
        if (!TrySpawnPlacement(placement, mrSpaceOrigin, index))
        {
            layout.RemoveById(placement.Id);
            layout.Save(LayoutFilePath);
            return false;
        }

        ConfigManager.WriteConsole($"{LogPrefix} added {cabinetDBName} ({placement.Id})");
        return true;
    }

    /// <summary>Spawn a game cabinet for floor placement ray — not saved to mr-layout until finalized.</summary>
    public bool TrySpawnTransientCabinet(
        string cabinetDBName,
        Transform mrSpaceOrigin,
        Vector3 worldPosition,
        Quaternion worldRotation,
        out GameObject spawnedRoot)
    {
        spawnedRoot = null;
        if (string.IsNullOrEmpty(cabinetDBName) || mrSpaceOrigin == null)
            return false;

        if (FindPlacementByCabinetDBName(cabinetDBName) != null)
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} already in layout: {cabinetDBName}");
            return false;
        }

        int index = spawnedById.Count;
        if (!TrySpawnCabinetAtWorldPose(
                cabinetDBName,
                mrSpaceOrigin,
                worldPosition,
                worldRotation,
                index,
                registerSpawned: false,
                out spawnedRoot))
            return false;

        ConfigManager.WriteConsole($"{LogPrefix} transient spawn {cabinetDBName} for placement ray");
        return true;
    }

    /// <summary>TestMRmanager / editor: spawn without mr-layout entry or duplicate check.</summary>
    public bool TrySpawnValidationCabinet(
        string cabinetDBName,
        Transform mrSpaceOrigin,
        Vector3 worldPosition,
        Quaternion worldRotation,
        out GameObject spawnedRoot)
    {
        spawnedRoot = null;
        if (string.IsNullOrEmpty(cabinetDBName) || mrSpaceOrigin == null)
            return false;

        return TrySpawnCabinetAtWorldPose(
            cabinetDBName,
            mrSpaceOrigin,
            worldPosition,
            worldRotation,
            spawnedById.Count,
            registerSpawned: false,
            out spawnedRoot);
    }

    /// <summary>Commit a transient cabinet after floor placement ray confirm.</summary>
    public bool TryFinalizeTransientCabinetAdd(
        string cabinetDBName,
        GameObject root,
        Transform mrSpaceOrigin,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Guid anchorUuid = default)
    {
        EnsureLayoutLoaded();
        if (layout == null || string.IsNullOrEmpty(cabinetDBName) || root == null || mrSpaceOrigin == null)
            return false;

        if (FindPlacementByCabinetDBName(cabinetDBName) != null)
        {
            DestroyTransientCabinet(root);
            ConfigManager.WriteConsoleWarning($"{LogPrefix} finalize skipped, already in layout: {cabinetDBName}");
            return false;
        }

        var placement = new MRCabinetPlacement
        {
            Id = $"{cabinetDBName}-{Guid.NewGuid():N}".Substring(0, Mathf.Min(48, cabinetDBName.Length + 33)),
            CabinetDBName = cabinetDBName,
            Scale = 1f,
            SurfaceType = PlacementSurfaceType.Floor,
            FacingAxis = PlacementFacingAxis.PositiveZ
        };
        WriteStoredPose(placement, PlacementSurfaceType.Floor, worldPosition, worldRotation, anchorUuid);

        layout.AddPlacement(placement);
        layout.Version = AnchorRelativeLayoutVersion;
        layout.Save(LayoutFilePath);

        MRPlacedCabinet marker = root.GetComponent<MRPlacedCabinet>();
        if (marker == null)
            marker = root.AddComponent<MRPlacedCabinet>();
        marker.Initialize(placement.Id, cabinetDBName);

        spawnedById[placement.Id] = root;
        ConfigManager.WriteConsole($"{LogPrefix} added {cabinetDBName} ({placement.Id}) after placement ray");
        return true;
    }

    public void DestroyTransientCabinet(GameObject root)
    {
        if (root == null)
            return;

        StopLibretroOnCabinet(root);

        CabinetReplace replace = root.GetComponent<CabinetReplace>();
        if (replace != null)
            Destroy(replace.gameObject);
        else
            Destroy(root);

        ConfigManager.WriteConsole($"{LogPrefix} destroyed transient cabinet {root.name}");
    }

    public bool TryUpdatePlacementPose(
        string placementId,
        Transform mrSpaceOrigin,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Guid anchorUuid = default)
    {
        EnsureLayoutLoaded();
        if (layout == null || string.IsNullOrEmpty(placementId) || mrSpaceOrigin == null)
            return false;

        MRCabinetPlacement placement = layout.FindById(placementId);
        if (placement == null)
            return false;

        WriteStoredPose(placement, placement.SurfaceType, worldPosition, worldRotation, anchorUuid);
        layout.Version = AnchorRelativeLayoutVersion;
        layout.Save(LayoutFilePath);

        if (TryGetSpawnedRoot(placementId, out GameObject root))
            root.transform.SetPositionAndRotation(worldPosition, worldRotation);

        ConfigManager.WriteConsole($"{LogPrefix} updated pose {placement.DisplayLabel} ({placementId})");
        return true;
    }

    /// <summary>Remove from layout and despawn if present.</summary>
    public bool TryRemoveCabinetFromScene(string cabinetDBName)
    {
        EnsureLayoutLoaded();
        MRCabinetPlacement placement = FindPlacementByCabinetDBName(cabinetDBName);
        if (placement == null || string.IsNullOrEmpty(placement.Id))
            return false;

        return RemovePlacement(placement.Id);
    }

    bool TrySpawnPlacement(MRCabinetPlacement placement, Transform mrSpaceOrigin, int index)
    {
        if (string.IsNullOrEmpty(placement.CabinetDBName))
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} skip entry {placement.Id}: missing cabinetDBName");
            return false;
        }

        ReadWorldPose(placement, out Vector3 worldPos, out Quaternion worldRot);

        if (!TrySpawnCabinetAtWorldPose(
                placement.CabinetDBName,
                mrSpaceOrigin,
                worldPos,
                worldRot,
                index,
                registerSpawned: true,
                out GameObject root))
            return false;

        float scale = placement.Scale > 0f ? placement.Scale : 1f;
        root.transform.localScale = Vector3.one * scale;

        MRPlacedCabinet marker = root.GetComponent<MRPlacedCabinet>();
        if (marker == null)
            marker = root.AddComponent<MRPlacedCabinet>();
        marker.Initialize(placement.Id, placement.CabinetDBName);

        spawnedById[placement.Id] = root;
        ConfigManager.WriteConsole($"{LogPrefix} spawned {placement.DisplayLabel} at {worldPos}");
        return true;
    }

    bool TrySpawnCabinetAtWorldPose(
        string cabinetDBName,
        Transform mrSpaceOrigin,
        Vector3 worldPos,
        Quaternion worldRot,
        int index,
        bool registerSpawned,
        out GameObject spawnedRoot)
    {
        spawnedRoot = null;
        if (string.IsNullOrEmpty(cabinetDBName) || mrSpaceOrigin == null)
            return false;

        CabinetInformation cabInfo;
        try
        {
            cabInfo = CabinetInformation.fromYaml(Path.Combine(ConfigManager.CabinetsDB, cabinetDBName));
        }
        catch (System.Exception e)
        {
            ConfigManager.WriteConsoleException($"{LogPrefix} yaml load failed for {cabinetDBName}", e);
            return false;
        }

        if (cabInfo == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} no description for {cabinetDBName}");
            return false;
        }

        MRLibretroWarmup.EnsureOnMainThread();

        Cabinet cabinet;
        try
        {
            cabinet = CabinetFactory.fromInformation(
                cabInfo,
                MixedRealityManager.MrRoomName,
                index,
                worldPos,
                worldRot,
                mrSpaceOrigin,
                agentPlayerPositions: new List<AgentScenePosition>(),
                backgroundSoundController: null);
        }
        catch (System.Exception e)
        {
            ConfigManager.WriteConsoleException($"{LogPrefix} spawn failed {cabinetDBName}", e);
            return false;
        }

        if (cabinet == null)
            return false;

        ApplyCabinetSkinning(cabinet, cabInfo);

        spawnedRoot = cabinet.gameObject;
        DisableAutoFloorSnap(spawnedRoot);

        if (!registerSpawned)
            ConfigManager.WriteConsole($"{LogPrefix} spawned transient {cabinetDBName} at {worldPos}");

        return true;
    }

    static void ApplyCabinetSkinning(Cabinet cabinet, CabinetInformation cabInfo)
    {
        if (cabinet == null || cabInfo?.Parts == null || cabInfo.Parts.Count == 0)
            return;

        try
        {
            CabinetFactory.skinFromInformation(cabinet, cabInfo);
            ConfigManager.WriteConsole($"{LogPrefix} skinned {cabInfo.name} ({cabInfo.Parts.Count} parts)");
        }
        catch (Exception e)
        {
            ConfigManager.WriteConsoleException($"{LogPrefix} skinning failed for {cabInfo.name}", e);
        }
    }

    static void WriteStoredPose(
        MRCabinetPlacement placement,
        PlacementSurfaceType surfaceType,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Guid anchorUuid)
    {
        Meta.XR.MRUtilityKit.MRUKRoom room = MREnvironmentSurfaces.Instance?.CurrentRoom;
        if (MRAnchorPoseResolver.TryWriteAnchorRelativePose(
                room,
                surfaceType,
                worldPosition,
                worldRotation,
                anchorUuid,
                out string anchorUuidText,
                out MRVector3 storedPosition,
                out MRQuaternion storedRotation))
        {
            placement.AnchorUuid = anchorUuidText;
            placement.Position = storedPosition;
            placement.Rotation = storedRotation;
            return;
        }

        placement.AnchorUuid = null;
        placement.Position = MRVector3.From(worldPosition);
        placement.Rotation = MRQuaternion.From(worldRotation);
    }

    static void ReadWorldPose(MRCabinetPlacement placement, out Vector3 worldPosition, out Quaternion worldRotation)
    {
        Vector3 storedPosition = placement.Position != null ? placement.Position.ToVector3() : Vector3.zero;
        Quaternion storedRotation = placement.Rotation != null ? placement.Rotation.ToQuaternion() : Quaternion.identity;

        Meta.XR.MRUtilityKit.MRUKRoom room = MREnvironmentSurfaces.Instance?.CurrentRoom;
        MRAnchorPoseResolver.TryResolveWorldPose(
            room,
            placement.AnchorUuid,
            storedPosition,
            storedRotation,
            placement.SurfaceType,
            out worldPosition,
            out worldRotation);
    }

    void EnsureWorldSpaceLayout(Transform mrSpaceOrigin)
    {
        if (layout == null || layout.Version >= WorldSpaceLayoutVersion)
            return;

        ConfigManager.WriteConsoleWarning(
            $"{LogPrefix} mr-layout version {layout.Version} uses legacy local coords — re-place cabinets once to fix saved poses");

        if (mrSpaceOrigin != null)
        {
            foreach (MRCabinetPlacement placement in layout.GetCabinets())
            {
                if (placement?.Position == null || placement.Rotation == null)
                    continue;

                Vector3 localPos = placement.Position.ToVector3();
                Quaternion localRot = placement.Rotation.ToQuaternion();
                Vector3 worldPos = mrSpaceOrigin.TransformPoint(localPos);
                Quaternion worldRot = mrSpaceOrigin.rotation * localRot;
                placement.AnchorUuid = null;
                placement.Position = MRVector3.From(worldPos);
                placement.Rotation = MRQuaternion.From(worldRot);
            }
        }

        layout.Version = WorldSpaceLayoutVersion;
        layout.Save(LayoutFilePath);
    }

    static void DisableAutoFloorSnap(GameObject cabinetRoot)
    {
        if (cabinetRoot == null)
            return;

        PutOnFloor[] floorSnaps = cabinetRoot.GetComponentsInChildren<PutOnFloor>(true);
        foreach (PutOnFloor floorSnap in floorSnaps)
        {
            if (floorSnap != null)
                Destroy(floorSnap);
        }
    }

    void DestroySpawnedInstance(string placementId)
    {
        if (!spawnedById.TryGetValue(placementId, out GameObject root) || root == null)
            return;

        StopLibretroOnCabinet(root);

        var replace = root.GetComponent<CabinetReplace>();
        if (replace != null)
            Destroy(replace.gameObject);
        else
            Destroy(root);

        spawnedById.Remove(placementId);
    }

    static void StopLibretroOnCabinet(GameObject cabinetRoot)
    {
        var screens = cabinetRoot.GetComponentsInChildren<LibretroScreenController>(true);
        foreach (LibretroScreenController screen in screens)
        {
            if (screen == null)
                continue;
            if (LibretroMameCore.isRunning(screen.ScreenName, screen.GameFile))
                LibretroMameCore.End(screen.ScreenName, screen.GameFile);
        }
    }
}
