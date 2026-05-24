/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// MR cabinet layout (mr-layout.yaml). Phase 2b: load/save, spawn, delete.
/// VR registry.yaml is never modified by this class.
/// </summary>
public class MRLayoutRegistry : MonoBehaviour
{
    const string LogPrefix = "[MRLayoutRegistry]";
    public const string LayoutFileName = "mr-layout.yaml";

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

    bool TrySpawnPlacement(MRCabinetPlacement placement, Transform mrSpaceOrigin, int index)
    {
        if (string.IsNullOrEmpty(placement.CabinetDBName))
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} skip entry {placement.Id}: missing cabinetDBName");
            return false;
        }

        CabinetInformation cabInfo;
        try
        {
            cabInfo = CabinetInformation.fromYaml(Path.Combine(ConfigManager.CabinetsDB, placement.CabinetDBName));
        }
        catch (System.Exception e)
        {
            ConfigManager.WriteConsoleException($"{LogPrefix} yaml load failed for {placement.CabinetDBName}", e);
            return false;
        }

        if (cabInfo == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} no description for {placement.CabinetDBName}");
            return false;
        }

        Vector3 localPos = placement.Position != null ? placement.Position.ToVector3() : Vector3.zero;
        Quaternion localRot = placement.Rotation != null ? placement.Rotation.ToQuaternion() : Quaternion.identity;
        Vector3 worldPos = mrSpaceOrigin.TransformPoint(localPos);
        Quaternion worldRot = mrSpaceOrigin.rotation * localRot;

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
                agentPlayerPositions: null,
                backgroundSoundController: null);
        }
        catch (System.Exception e)
        {
            ConfigManager.WriteConsoleException($"{LogPrefix} spawn failed {placement.CabinetDBName}", e);
            return false;
        }

        if (cabinet == null)
            return false;

        float scale = placement.Scale > 0f ? placement.Scale : 1f;
        cabinet.gameObject.transform.localScale = Vector3.one * scale;

        var marker = cabinet.gameObject.GetComponent<MRPlacedCabinet>();
        if (marker == null)
            marker = cabinet.gameObject.AddComponent<MRPlacedCabinet>();
        marker.Initialize(placement.Id, placement.CabinetDBName);

        spawnedById[placement.Id] = cabinet.gameObject;
        ConfigManager.WriteConsole($"{LogPrefix} spawned {placement.DisplayLabel} at {worldPos}");
        return true;
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
