/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using UnityEngine;
using UnityEngine.SceneManagement;

public static class MixedRealityBootstrap
{
    const string RootName = "MixedRealitySystem";
    const string BootSceneName = "FixedScene";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (MixedRealityManager.Instance != null)
            return;

        Scene active = SceneManager.GetActiveScene();
        if (active.name != BootSceneName)
            return;

        var root = new GameObject(RootName);
        root.AddComponent<MixedRealityManager>();
        root.AddComponent<MRPassthroughController>();
        root.AddComponent<MRSceneTransition>();
        root.AddComponent<MRModeInput>();
        root.AddComponent<MRLayoutRegistry>();

        Object.DontDestroyOnLoad(root);
        ConfigManager.WriteConsole("[MixedRealityBootstrap] MixedRealitySystem installed");
    }
}
