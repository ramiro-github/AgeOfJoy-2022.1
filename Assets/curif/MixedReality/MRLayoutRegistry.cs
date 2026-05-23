/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MR cabinet layout (mr-layout.yaml). Phase 1: no spawn — empty real-world space.
/// Phase 2a+: load YAML and spawn via CabinetFactory; UI add/delete in 2b/2c.
/// VR registry.yaml is never modified by this class.
/// </summary>
public class MRLayoutRegistry : MonoBehaviour
{
    const string LogPrefix = "[MRLayoutRegistry]";
    public const string LayoutFileName = "mr-layout.yaml";

    public static MRLayoutRegistry Instance { get; private set; }

    readonly List<GameObject> spawnedCabinets = new List<GameObject>();

    public string LayoutFilePath => System.IO.Path.Combine(ConfigManager.CabinetsDB, LayoutFileName);

    public int SpawnedCount => spawnedCabinets.Count;

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

    /// <summary>Phase 2a+: spawn all entries from mr-layout.yaml under MRSpaceOrigin.</summary>
    public void SpawnAll(Transform mrSpaceOrigin)
    {
        DespawnAll();

        if (mrSpaceOrigin == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} SpawnAll skipped — MRSpaceOrigin is null");
            return;
        }

        // Phase 1 — MR enters with no cabinets; placement is via hand UI (phases 2b/2c).
        ConfigManager.WriteConsole($"{LogPrefix} SpawnAll: phase 1 — no cabinets (layout file: {LayoutFilePath})");
    }

    /// <summary>Removes all MR-spawned cabinets from the scene.</summary>
    public void DespawnAll()
    {
        for (int i = spawnedCabinets.Count - 1; i >= 0; i--)
        {
            if (spawnedCabinets[i] != null)
                Destroy(spawnedCabinets[i]);
        }

        spawnedCabinets.Clear();
        ConfigManager.WriteConsole($"{LogPrefix} DespawnAll done");
    }

    internal void TrackSpawned(GameObject cabinetRoot)
    {
        if (cabinetRoot != null && !spawnedCabinets.Contains(cabinetRoot))
            spawnedCabinets.Add(cabinetRoot);
    }
}
