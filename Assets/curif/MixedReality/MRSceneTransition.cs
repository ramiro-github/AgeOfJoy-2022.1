/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MRSceneTransition : MonoBehaviour
{
    const string LogPrefix = "[MRSceneTransition]";
    const string FixedSceneName = "FixedScene";

    static readonly string[] ScenesToReloadOnExitMr =
    {
        "IntroGalleryExterior",
        "IntroGallery",
    };

    readonly List<string> unloadedSceneNames = new List<string>();
    bool transitionRunning;

    public bool IsTransitionRunning => transitionRunning;

    public IEnumerator UnloadVrScenes()
    {
        transitionRunning = true;
        unloadedSceneNames.Clear();

        var scenesToUnload = new List<Scene>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || scene.name == FixedSceneName)
                continue;
            scenesToUnload.Add(scene);
        }

        foreach (Scene scene in scenesToUnload)
        {
            ConfigManager.WriteConsole($"{LogPrefix} unloading {scene.name}");
            unloadedSceneNames.Add(scene.name);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        transitionRunning = false;
    }

    public IEnumerator ReloadVrScenes()
    {
        transitionRunning = true;

        foreach (string sceneName in ScenesToReloadOnExitMr)
        {
            if (SceneManager.GetSceneByName(sceneName).isLoaded)
                continue;

            ConfigManager.WriteConsole($"{LogPrefix} loading {sceneName}");
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        }

        unloadedSceneNames.Clear();
        transitionRunning = false;
    }
}

