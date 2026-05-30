/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using UnityEngine;
using UnityEngine.SceneManagement;

public static class MixedRealityBootstrap
{
    const string RootName = "MixedRealitySystem";
    static bool ShouldInstallForScene(string sceneName) =>
        sceneName == "FixedScene" || sceneName == "TestMRmanager";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (MixedRealityManager.Instance != null)
            return;

        Scene active = SceneManager.GetActiveScene();
        if (!ShouldInstallForScene(active.name))
            return;

        var root = new GameObject(RootName);
        root.AddComponent<MixedRealityManager>();

        if (active.name == "TestMRmanager")
            root.AddComponent<MRTestGameCabinetSpawn>();

        Object.DontDestroyOnLoad(root);
        ConfigManager.WriteConsole("[MixedRealityBootstrap] MixedRealitySystem installed");
    }
}
