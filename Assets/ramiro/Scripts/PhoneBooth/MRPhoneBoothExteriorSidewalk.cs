/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using UnityEngine;

/// <summary>
/// IntroGalleryExterior sidewalk (8) — hidden during VR→MR travel, restored after MR→VR load.
/// </summary>
public static class MRPhoneBoothExteriorSidewalk
{
    const string LogPrefix = "[MRPhoneBoothExteriorSidewalk]";
    const string SidewalkObjectName = "sidewalk (8)";

    public static void SetSidewalk8Active(bool active)
    {
        GameObject sidewalk = FindSidewalk8();
        if (sidewalk == null)
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} '{SidewalkObjectName}' not found");
            return;
        }

        sidewalk.SetActive(active);
        ConfigManager.WriteConsole($"{LogPrefix} '{SidewalkObjectName}' active={active}");
    }

    static GameObject FindSidewalk8()
    {
        GameObject activeSearch = GameObject.Find(SidewalkObjectName);
        if (activeSearch != null)
            return activeSearch;

        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate == null || candidate.name != SidewalkObjectName)
                continue;

            GameObject go = candidate.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded)
                continue;

            return go;
        }

        return null;
    }
}
