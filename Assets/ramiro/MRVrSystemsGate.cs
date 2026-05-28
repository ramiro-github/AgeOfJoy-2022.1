/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using AOJ.Managers;
using UnityEngine;

/// <summary>
/// Stops VR arcade systems (LibRetro, deployed cabinets) before MR.
/// MR cabinets are placed later via UI + MRLayoutRegistry (see MIXED_REALITY_DESIGN.md).
/// </summary>
public static class MRVrSystemsGate
{
    const string LogPrefix = "[MRVrSystemsGate]";

    public static void SuspendForMR()
    {
        ConfigManager.WriteConsole($"{LogPrefix} SuspendForMR");
        StopActiveLibretroGames();
        TeardownDeployedVrCabinets();
        DisableVrCabinetControllers();
        EnsureHandModelsVisible();
        ResetLegacyPassthroughState();
    }

    public static void ResumeForVR()
    {
        ConfigManager.WriteConsole($"{LogPrefix} ResumeForVR (VR scenes reload separately)");
        ResetLegacyPassthroughState();
    }

    static void StopActiveLibretroGames()
    {
        if (!LibretroMameCore.GameLoaded)
            return;

        var screens = Object.FindObjectsOfType<LibretroScreenController>(true);
        foreach (LibretroScreenController screen in screens)
        {
            if (screen == null)
                continue;
            if (LibretroMameCore.isRunning(screen.ScreenName, screen.GameFile))
            {
                ConfigManager.WriteConsole($"{LogPrefix} ending LibRetro on {screen.name}");
                LibretroMameCore.End(screen.ScreenName, screen.GameFile);
            }
        }
    }

    static void TeardownDeployedVrCabinets()
    {
        var replacements = Object.FindObjectsOfType<CabinetReplace>(true);
        foreach (CabinetReplace replace in replacements)
        {
            if (replace == null)
                continue;

            ConfigManager.WriteConsole($"{LogPrefix} removing deployed cabinet {replace.name}");
            if (replace.outOfOrderCabinet != null)
                replace.outOfOrderCabinet.SetActive(true);

            Object.Destroy(replace.gameObject);
        }
    }

    static void DisableVrCabinetControllers()
    {
        var controllers = Object.FindObjectsOfType<CabinetsController>(true);
        foreach (CabinetsController controller in controllers)
        {
            if (controller == null)
                continue;
            controller.StopAllCoroutines();
            controller.enabled = false;
            ConfigManager.WriteConsole($"{LogPrefix} disabled CabinetsController on {controller.gameObject.name} (room={controller.Room})");
        }
    }

    static void ResetLegacyPassthroughState()
    {
        if (EventManager.Instance != null)
            EventManager.Instance.IsPassthrough = false;
    }

    static void EnsureHandModelsVisible()
    {
        var changeControls = Object.FindObjectOfType<ChangeControls>(true);
        if (changeControls == null)
            return;

        changeControls.PlayerMode(false);
        ConfigManager.WriteConsole($"{LogPrefix} hand models restored for MR");
    }
}
