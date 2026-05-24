/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections.Generic;
using UnityEngine;

/// <summary>Retro CRT menus on the MR ConfigurationCabinet (GenericMenu + mr-layout.yaml).</summary>
public class MRConfigurationController : MonoBehaviour
{
    const string LogPrefix = "[MRConfigurationController]";
    const string DefaultSkin = "c64";

    enum Screen
    {
        Idle,
        Main,
        InRoom,
        ConfirmRemove
    }

    [SerializeField] string systemSkin = DefaultSkin;
    [SerializeField] float navRepeatDelay = 0.18f;

    ScreenGenerator screen;
    Renderer display;
    ShaderScreenBase shaderOffline;

    GenericMenu mainMenu;
    GenericMenu inRoomMenu;
    GenericMenu confirmMenu;

    readonly List<MRCabinetPlacement> placements = new List<MRCabinetPlacement>();
    string pendingRemoveId;
    string pendingRemoveLabel;

    Screen currentScreen = Screen.Idle;
    float navCooldown;
    bool isActive;

    public bool IsActive => isActive;

    public void Initialize(ScreenGenerator screenGenerator)
    {
        screen = screenGenerator;
        if (screen == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} ScreenGenerator is null");
            return;
        }

        display = screen.GetComponent<Renderer>();
        if (display != null)
        {
            var shaderConfig = new Dictionary<string, string> { ["damage"] = "none" };
            shaderOffline = ShaderScreen.Factory(display, 1, "crtlod", shaderConfig);
        }

        screen.Init(systemSkin);
        if (shaderOffline != null)
            screen.ActivateShader(shaderOffline);

        BuildMenus();
        ShowIdle();
    }

    public void Activate()
    {
        if (screen == null)
            return;

        isActive = true;
        currentScreen = Screen.Main;
        mainMenu.selectedIndex = 0;
        mainMenu.Deselect();
        DrawCurrentScreen();
        ConfigManager.WriteConsole($"{LogPrefix} active");
    }

    public void Deactivate()
    {
        isActive = false;
        currentScreen = Screen.Idle;
        pendingRemoveId = null;
        ShowIdle();
        ConfigManager.WriteConsole($"{LogPrefix} idle");
    }

    void Update()
    {
        if (!isActive || screen == null)
            return;

        navCooldown -= Time.deltaTime;

        if (WasBackPressed())
        {
            HandleBack();
            return;
        }

        if (navCooldown <= 0f)
        {
            float stickY = ReadStickY();
            if (stickY > 0.55f)
            {
                MoveSelection(-1);
                navCooldown = navRepeatDelay;
            }
            else if (stickY < -0.55f)
            {
                MoveSelection(1);
                navCooldown = navRepeatDelay;
            }
        }

        if (WasConfirmPressed())
            HandleConfirm();
    }

    void BuildMenus()
    {
        mainMenu = new GenericMenu(screen, "HOME LAYOUT STATION");
        mainMenu.AddOption("IN ROOM", "Cabinets placed in your space");
        mainMenu.AddOption("ADD CABINET", "Coming soon — placement ray");
        mainMenu.AddOption("EXIT", "Close maintenance panel");

        inRoomMenu = new GenericMenu(screen, "IN ROOM");
        confirmMenu = new GenericMenu(screen, "EJECT MACHINE?");
    }

    void RefreshPlacements()
    {
        placements.Clear();
        MRLayoutRegistry registry = MRLayoutRegistry.Instance;
        if (registry == null)
            return;

        registry.EnsureLayoutLoaded();
        foreach (MRCabinetPlacement placement in registry.Placements)
        {
            if (placement != null && !string.IsNullOrEmpty(placement.Id))
                placements.Add(placement);
        }
    }

    void RebuildInRoomMenu()
    {
        RefreshPlacements();
        inRoomMenu = new GenericMenu(screen, "IN ROOM");
        if (placements.Count == 0)
        {
            inRoomMenu.AddOption("(empty)", "No cabinets in mr-layout.yaml");
        }
        else
        {
            foreach (MRCabinetPlacement placement in placements)
                inRoomMenu.AddOption(Truncate(placement.DisplayLabel, 26), "Select to remove");
        }

        inRoomMenu.AddOption("BACK", "Return to main menu");
    }

    void RebuildConfirmMenu(string label)
    {
        confirmMenu = new GenericMenu(screen, "EJECT MACHINE?");
        confirmMenu.AddOption("YES", $"Remove {Truncate(label, 22)}");
        confirmMenu.AddOption("NO", "Keep cabinet");
    }

    void DrawCurrentScreen()
    {
        screen.Clear();

        switch (currentScreen)
        {
            case Screen.Main:
                mainMenu.DrawMenu();
                DrawFooter("STICK: move   A: select   B: back");
                break;
            case Screen.InRoom:
                inRoomMenu.DrawMenu();
                DrawFooter("A: remove   B: back");
                break;
            case Screen.ConfirmRemove:
                confirmMenu.DrawMenu();
                if (!string.IsNullOrEmpty(pendingRemoveLabel))
                    screen.PrintCentered(10, Truncate(pendingRemoveLabel, 30), false);
                DrawFooter("A: confirm   B: cancel");
                break;
        }

        screen.DrawScreen();
    }

    void ShowIdle()
    {
        if (screen == null)
            return;

        screen.Clear();
        screen.PrintCentered(1, "HOME LAYOUT STATION", true);
        screen.PrintCentered(8, "Maintenance cabinet ready", false);
        screen.PrintCentered(12, "Open panel to edit layout", false);
        screen.DrawScreen();
    }

    void DrawFooter(string text)
    {
        screen.Print(2, screen.CharactersYCount - 2, text, false);
    }

    void MoveSelection(int delta)
    {
        GenericMenu menu = GetActiveMenu();
        if (menu == null || menu.options.Count == 0)
            return;

        if (delta < 0)
        {
            for (int i = 0; i < Mathf.Abs(delta); i++)
                menu.PreviousOption();
        }
        else
        {
            for (int i = 0; i < delta; i++)
                menu.NextOption();
        }

        DrawCurrentScreen();
    }

    void HandleConfirm()
    {
        GenericMenu menu = GetActiveMenu();
        if (menu == null)
            return;

        menu.Select();
        string choice = menu.GetSelectedOption();
        menu.Deselect();

        switch (currentScreen)
        {
            case Screen.Main:
                HandleMainChoice(choice);
                break;
            case Screen.InRoom:
                HandleInRoomChoice(choice);
                break;
            case Screen.ConfirmRemove:
                HandleConfirmRemoveChoice(choice);
                break;
        }
    }

    void HandleMainChoice(string choice)
    {
        switch (choice)
        {
            case "IN ROOM":
                RebuildInRoomMenu();
                currentScreen = Screen.InRoom;
                inRoomMenu.selectedIndex = 0;
                break;
            case "ADD CABINET":
                ShowMessageScreen("ADD CABINET", "Phase 2c — placement ray", "B: back");
                currentScreen = Screen.Main;
                return;
            case "EXIT":
                MRConfigurationCabinetController.Instance?.CloseEdit();
                return;
            default:
                return;
        }

        DrawCurrentScreen();
    }

    void HandleInRoomChoice(string choice)
    {
        if (choice == "BACK")
        {
            currentScreen = Screen.Main;
            mainMenu.selectedIndex = 0;
            DrawCurrentScreen();
            return;
        }

        if (choice == "(empty)" || placements.Count == 0)
            return;

        int index = inRoomMenu.selectedIndex;
        if (index < 0 || index >= placements.Count)
            return;

        MRCabinetPlacement selected = placements[index];
        pendingRemoveId = selected.Id;
        pendingRemoveLabel = selected.DisplayLabel;
        RebuildConfirmMenu(pendingRemoveLabel);
        currentScreen = Screen.ConfirmRemove;
        confirmMenu.selectedIndex = 1;
        DrawCurrentScreen();
    }

    void HandleConfirmRemoveChoice(string choice)
    {
        if (choice == "YES" && !string.IsNullOrEmpty(pendingRemoveId))
        {
            MRLayoutRegistry registry = MRLayoutRegistry.Instance;
            registry?.RemovePlacement(pendingRemoveId);
            ConfigManager.WriteConsole($"{LogPrefix} removed {pendingRemoveLabel}");
        }

        pendingRemoveId = null;
        pendingRemoveLabel = null;
        RebuildInRoomMenu();
        currentScreen = Screen.InRoom;
        inRoomMenu.selectedIndex = 0;
        DrawCurrentScreen();
    }

    void HandleBack()
    {
        switch (currentScreen)
        {
            case Screen.ConfirmRemove:
                pendingRemoveId = null;
                pendingRemoveLabel = null;
                currentScreen = Screen.InRoom;
                DrawCurrentScreen();
                break;
            case Screen.InRoom:
                currentScreen = Screen.Main;
                mainMenu.selectedIndex = 0;
                DrawCurrentScreen();
                break;
            case Screen.Main:
                MRConfigurationCabinetController.Instance?.CloseEdit();
                break;
        }
    }

    void ShowMessageScreen(string title, string message, string footer)
    {
        screen.Clear();
        screen.PrintCentered(1, title, true);
        screen.PrintCentered(8, message, false);
        DrawFooter(footer);
        screen.DrawScreen();
    }

    GenericMenu GetActiveMenu()
    {
        return currentScreen switch
        {
            Screen.Main => mainMenu,
            Screen.InRoom => inRoomMenu,
            Screen.ConfirmRemove => confirmMenu,
            _ => null
        };
    }

    static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
            return "(unnamed)";
        if (value.Length <= maxLen)
            return value;
        return value.Substring(0, maxLen - 3) + "...";
    }

    static float ReadStickY()
    {
#if UNITY_EDITOR
        if (Input.GetKey(KeyCode.UpArrow))
            return 1f;
        if (Input.GetKey(KeyCode.DownArrow))
            return -1f;
        return Input.GetAxisRaw("Vertical");
#else
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        return stick.y;
#endif
    }

    static bool WasConfirmPressed()
    {
#if UNITY_EDITOR
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0);
#else
        return OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);
#endif
    }

    static bool WasBackPressed()
    {
#if UNITY_EDITOR
        return Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.JoystickButton1);
#else
        return OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);
#endif
    }
}
