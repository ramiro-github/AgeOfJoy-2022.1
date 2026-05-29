/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections.Generic;
using UnityEngine;
using LC = LibretroControlMapDictionnary;

/// <summary>
/// MR layout menus on ConfigurationCabinet CRT (GenericMenu + MRLayoutRegistry).
/// Replaces VR ConfigurationController menus and uGUI MRConfigurationUI.
/// </summary>
public class MRConfigurationController : MonoBehaviour
{
    const string LogPrefix = "[MRConfigurationController]";
    const string DefaultSkin = "c64";
    const int VisibleCabinetRows = 11;

    enum Screen
    {
        Idle,
        NavMain,
        Cabinets,
        Added,
        Help
    }

    [SerializeField] string systemSkin = DefaultSkin;
    [SerializeField] float navRepeatDelay = 0.16f;
    [SerializeField] float spawnDistanceMeters = 2f;
    [SerializeField] float spawnYOffsetMeters;
    [SerializeField] bool seedExampleCabinetInEditor = true;

    ScreenGenerator screen;
    Renderer display;
    ShaderScreenBase shaderOnline;
    ShaderScreenBase shaderOffline;
    LibretroControlMap libretroControlMap;
    CoinSlotController coinSlot;

    GenericMenu navMenu;
    readonly List<string> catalogNames = new List<string>();
    readonly List<MRCabinetPlacement> placements = new List<MRCabinetPlacement>();

    MRLayoutRegistry registry;
    Transform mrSpaceOrigin;
    MRPlacementRayController placementRay;

    Screen currentScreen = Screen.Idle;
    int selectedListIndex;
    int listScrollOffset;
    float navCooldown;
    bool sessionActive;
    bool placementMoveActive;
    bool placementAddActive;
    string movingPlacementId;
    string pendingAddCabinetName;
    GameObject pendingAddRoot;

    public bool IsSessionActive => sessionActive;

    public void PrepareForCabinet(
        ScreenGenerator screenGenerator,
        LibretroControlMap controlMap,
        CoinSlotController coinSlotController)
    {
        screen = screenGenerator;
        libretroControlMap = controlMap;
        coinSlot = coinSlotController;

        if (screen == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} ScreenGenerator missing");
            return;
        }

        display = screen.GetComponent<Renderer>();
        var shaderConfig = new Dictionary<string, string> { ["damage"] = "none" };
        if (display != null)
        {
            shaderOnline = ShaderScreen.Factory(display, 1, "crt", shaderConfig);
            shaderOffline = ShaderScreen.Factory(display, 1, "crtlod", shaderConfig);
        }

        screen.Init(systemSkin);
        ActivateShader(false);
        BuildNavMenu();
        ShowIdle();
    }

    public void BeginSession()
    {
        if (screen == null)
            return;

        registry = EnsureRegistry();
        mrSpaceOrigin = EnsureMrSpaceOrigin();
        placementRay = EnsurePlacementRayController();
        registry.EnsureLayoutLoaded();
        registry.SpawnAll(mrSpaceOrigin);

        MRCatalogBootstrap.PrepareCatalog(seedExampleCabinetInEditor);
        RefreshCatalog();

        setupActionMap();
        sessionActive = true;
        coinSlot?.insertCoin();

        currentScreen = Screen.NavMain;
        navMenu.selectedIndex = 0;
        navMenu.Deselect();
        ActivateShader(true);
        DrawCurrentScreen();
        ConfigManager.WriteConsole($"{LogPrefix} session started (catalog={catalogNames.Count})");
    }

    public void EndSession()
    {
        sessionActive = false;
        placementMoveActive = false;
        placementAddActive = false;
        movingPlacementId = null;
        CancelPendingAdd(destroyCabinet: true);
        currentScreen = Screen.Idle;
        cleanActionMap();
        ActivateShader(false);
        ShowIdle();
        ConfigManager.WriteConsole($"{LogPrefix} session ended");
    }

    void Update()
    {
        if (!sessionActive || screen == null)
            return;

        if (placementMoveActive || placementAddActive)
            return;

        navCooldown -= Time.deltaTime;

        if (WasBackPressed())
        {
            HandleBack();
            return;
        }

        if (navCooldown <= 0f)
        {
            if (WasMoveUp() || ReadStickY() > 0.55f)
            {
                MoveSelection(-1);
                navCooldown = navRepeatDelay;
                return;
            }

            if (WasMoveDown() || ReadStickY() < -0.55f)
            {
                MoveSelection(1);
                navCooldown = navRepeatDelay;
                return;
            }
        }

        if (WasConfirmPressed())
            HandleConfirm();
    }

    void BuildNavMenu()
    {
        navMenu = new GenericMenu(screen, "MR CONFIGURATION");
        navMenu.AddOption("CABINETS", "Catalog: add or remove in MR space");
        navMenu.AddOption("ADDED", "Cabinets in mr-layout.yaml");
        navMenu.AddOption("MOVE CONFIG", "Reposition ConfigurationCabinetMiniMR");
        navMenu.AddOption("HELP", "Controls");
        navMenu.AddOption("EXIT", "Close panel");
    }

    void RefreshCatalog()
    {
        catalogNames.Clear();
        catalogNames.AddRange(MRLayoutRegistry.GetCatalogCabinetNames());
        catalogNames.Sort();
    }

    void RefreshPlacements()
    {
        placements.Clear();
        if (registry == null)
            return;

        registry.EnsureLayoutLoaded();
        foreach (MRCabinetPlacement placement in registry.Placements)
        {
            if (placement != null && !string.IsNullOrEmpty(placement.Id))
                placements.Add(placement);
        }
    }

    void DrawCurrentScreen()
    {
        screen.Clear();

        switch (currentScreen)
        {
            case Screen.NavMain:
                navMenu.DrawMenu();
                DrawFooter("STICK: move   A: select   B: back");
                break;
            case Screen.Cabinets:
                DrawCabinetsPage();
                break;
            case Screen.Added:
                DrawAddedPage();
                break;
            case Screen.Help:
                DrawHelpPage();
                break;
        }

        screen.DrawScreen();
    }

    void DrawCabinetsPage()
    {
        screen.PrintCentered(0, "CABINETS", true);
        screen.PrintLine(1, false, '-');

        if (catalogNames.Count == 0)
        {
            screen.PrintCentered(8, "Not Found Cabinet", true);
            screen.PrintCentered(10, "Check cabinetsdb/", false);
            DrawFooter("B: back");
            return;
        }

        screen.Print(1, 2, "Name", false);
        screen.Print(22, 2, "In", false);
        screen.Print(28, 2, "Act", false);
        screen.PrintLine(3, false, '-');

        int row = 4;
        for (int i = 0; i < VisibleCabinetRows; i++)
        {
            int idx = listScrollOffset + i;
            if (idx >= catalogNames.Count)
                break;

            string name = Truncate(catalogNames[idx], 18);
            bool inScene = registry != null && registry.IsCabinetInScene(catalogNames[idx]);
            bool selected = idx == selectedListIndex;

            string line = PadRight(name, 20) + (inScene ? "Yes" : " No") + " " + (inScene ? "Rem" : "Add");
            screen.Print(1, row, selected ? "> " + line : "  " + line, selected);
            row++;
        }

        if (selectedListIndex >= 0 && selectedListIndex < catalogNames.Count)
            screen.Print(1, 20, Truncate(catalogNames[selectedListIndex], 36), false);

        DrawFooter("A: Add/Rem   B: back");
    }

    void DrawPlacementRayHint(bool stickRotationEnabled)
    {
        screen.Clear();
        screen.PrintCentered(0, "PLACE OBJECT", true);
        screen.PrintCentered(4, "Point at floor", false);
        screen.PrintCentered(6, "Trigger: confirm", false);
        if (stickRotationEnabled)
            screen.PrintCentered(8, "L stick L/R: rotate", false);
        screen.PrintCentered(stickRotationEnabled ? 10 : 8, "B / Grip: cancel", false);
        screen.DrawScreen();
    }

    void DrawAddedPage()
    {
        screen.PrintCentered(0, "ADDED CABINETS", true);
        screen.PrintLine(1, false, '-');

        RefreshPlacements();

        if (placements.Count == 0)
        {
            screen.PrintCentered(8, "(empty)", true);
            DrawFooter("B: back");
            return;
        }

        int row = 3;
        for (int i = 0; i < VisibleCabinetRows; i++)
        {
            int idx = listScrollOffset + i;
            if (idx >= placements.Count)
                break;

            MRCabinetPlacement p = placements[idx];
            bool selected = idx == selectedListIndex;
            float x = p.Position != null ? p.Position.X : 0f;
            float y = p.Position != null ? p.Position.Y : 0f;
            float z = p.Position != null ? p.Position.Z : 0f;
            string line = Truncate(p.DisplayLabel, 16) + $" {x:F1},{y:F1},{z:F1}";
            screen.Print(1, row, selected ? "> " + line : "  " + line, selected);
            row++;
        }

        DrawFooter("A: move   B: back");
    }

    void DrawHelpPage()
    {
        screen.PrintCentered(0, "HELP", true);
        screen.Print(2, 3, "Up/Down: navigate lists", false);
        screen.Print(2, 5, "A: Add/Remove/Move", false);
        screen.Print(2, 7, "B: back / close panel", false);
        screen.Print(2, 9, "Add/Move: floor ray", false);
        screen.Print(2, 10, "L stick L/R: rotate Y*", false);
        screen.Print(2, 11, "* floor cabinets / prefab", false);
        screen.Print(2, 13, "Catalog = cabinetsdb/", false);
        screen.Print(2, 14, "In Scene = mr-layout.yaml", false);
        DrawFooter("B: back");
    }

    void ShowIdle()
    {
        if (screen == null)
            return;

        screen.Clear();
        screen.PrintCentered(1, "MR CONFIGURATION", true);
        screen.PrintCentered(8, "Cabinet ready", false);
        screen.PrintCentered(11, "Open panel to edit layout", false);
        screen.DrawScreen();
    }

    void DrawFooter(string text)
    {
        screen.Print(1, screen.CharactersYCount - 2, text, false);
    }

    int GetListCount()
    {
        return currentScreen switch
        {
            Screen.Cabinets => catalogNames.Count,
            Screen.Added => placements.Count,
            _ => 0
        };
    }

    void MoveSelection(int delta)
    {
        switch (currentScreen)
        {
            case Screen.NavMain:
                if (delta < 0)
                    navMenu.PreviousOption();
                else
                    navMenu.NextOption();
                DrawCurrentScreen();
                break;

            case Screen.Cabinets:
            case Screen.Added:
                int count = GetListCount();
                if (count == 0)
                    return;

                selectedListIndex += delta;
                if (selectedListIndex < 0)
                    selectedListIndex = count - 1;
                else if (selectedListIndex >= count)
                    selectedListIndex = 0;

                ClampListScroll();
                DrawCurrentScreen();
                break;
        }
    }

    void HandleConfirm()
    {
        switch (currentScreen)
        {
            case Screen.NavMain:
                navMenu.Select();
                HandleNavChoice(navMenu.GetSelectedOption());
                navMenu.Deselect();
                break;

            case Screen.Cabinets:
                ExecuteCabinetToggle(selectedListIndex);
                RefreshCatalog();
                DrawCurrentScreen();
                break;
            case Screen.Added:
                BeginMoveSelectedPlacement();
                break;
        }
    }

    void HandleNavChoice(string choice)
    {
        switch (choice)
        {
            case "CABINETS":
                selectedListIndex = 0;
                listScrollOffset = 0;
                currentScreen = Screen.Cabinets;
                break;
            case "ADDED":
                selectedListIndex = 0;
                listScrollOffset = 0;
                currentScreen = Screen.Added;
                break;
            case "HELP":
                currentScreen = Screen.Help;
                break;
            case "MOVE CONFIG":
                MRConfigurationCabinetController.Instance?.BeginRepositionWithRay();
                return;
            case "EXIT":
                MRConfigurationCabinetController.Instance?.CloseEdit();
                return;
            default:
                return;
        }

        DrawCurrentScreen();
    }

    void HandleBack()
    {
        switch (currentScreen)
        {
            case Screen.Cabinets:
            case Screen.Added:
            case Screen.Help:
                currentScreen = Screen.NavMain;
                navMenu.selectedIndex = 0;
                DrawCurrentScreen();
                break;
            case Screen.NavMain:
                MRConfigurationCabinetController.Instance?.CloseEdit();
                break;
        }
    }

    void ExecuteCabinetToggle(int index)
    {
        if (registry == null || index < 0 || index >= catalogNames.Count)
            return;

        string cabinetName = catalogNames[index];
        bool inScene = registry.IsCabinetInScene(cabinetName);

        if (inScene)
        {
            if (registry.TryRemoveCabinetFromScene(cabinetName))
                ConfigManager.WriteConsole($"{LogPrefix} removed {cabinetName}");
        }
        else
        {
            BeginAddCabinetWithRay(cabinetName);
        }
    }

    void BeginAddCabinetWithRay(string cabinetName)
    {
        if (registry == null || placementRay == null || placementMoveActive || placementAddActive)
            return;

        if (placementRay.IsActive)
            return;

        ComputeInitialFloorPose(out Vector3 worldPos, out Quaternion worldRot);

        if (!registry.TrySpawnTransientCabinet(cabinetName, mrSpaceOrigin, worldPos, worldRot, out GameObject root))
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} add ray spawn failed for {cabinetName}");
            return;
        }

        placementAddActive = true;
        pendingAddCabinetName = cabinetName;
        pendingAddRoot = root;

        MRPlacementProfile profile = MRPlacementProfile.Resolve(root);
        PlacementSurfaceType surfaceType = profile != null
            ? profile.surfaceType
            : PlacementSurfaceType.Floor;
        PlacementFacingAxis facingAxis = profile != null
            ? profile.facingAxis
            : PlacementFacingAxis.PositiveZ;

        DrawPlacementRayHint(MRPlacementRayController.ExpectsStickRotationHint(profile, surfaceType));

        placementRay.BeginMove(
            root,
            surfaceType,
            facingAxis,
            confirmCallback: (finalPos, finalRot, anchorUuid) =>
            {
                placementAddActive = false;
                string name = pendingAddCabinetName;
                GameObject spawned = pendingAddRoot;
                pendingAddCabinetName = null;
                pendingAddRoot = null;

                if (spawned != null)
                    spawned.transform.SetPositionAndRotation(finalPos, finalRot);

                bool saved = registry.TryFinalizeTransientCabinetAdd(
                    name, spawned, mrSpaceOrigin, finalPos, finalRot, anchorUuid);

                if (!saved)
                    ConfigManager.WriteConsoleWarning($"{LogPrefix} add confirm but save failed ({name})");

                DrawCurrentScreen();
            },
            cancelCallback: () =>
            {
                CancelPendingAdd(destroyCabinet: true);
                DrawCurrentScreen();
            });

        ConfigManager.WriteConsole($"{LogPrefix} add ray begin {cabinetName}");
    }

    void CancelPendingAdd(bool destroyCabinet)
    {
        placementAddActive = false;
        if (destroyCabinet && registry != null && pendingAddRoot != null)
            registry.DestroyTransientCabinet(pendingAddRoot);

        pendingAddCabinetName = null;
        pendingAddRoot = null;
    }

    void BeginMoveSelectedPlacement()
    {
        if (registry == null || placementRay == null || placementMoveActive)
            return;

        if (selectedListIndex < 0 || selectedListIndex >= placements.Count)
            return;

        MRCabinetPlacement placement = placements[selectedListIndex];
        if (placement == null || string.IsNullOrEmpty(placement.Id))
            return;

        if (!registry.TryGetSpawnedRoot(placement.Id, out GameObject spawnedRoot) || spawnedRoot == null)
        {
            ConfigManager.WriteConsoleWarning($"{LogPrefix} move skipped, spawned root missing for {placement.Id}");
            return;
        }

        placementMoveActive = true;
        movingPlacementId = placement.Id;

        MRPlacementProfile profile = MRPlacementProfile.Resolve(spawnedRoot);
        DrawPlacementRayHint(MRPlacementRayController.ExpectsStickRotationHint(profile, placement.SurfaceType));

        placementRay.BeginMove(
            spawnedRoot,
            placement.SurfaceType,
            placement.FacingAxis,
            confirmCallback: (worldPos, worldRot, anchorUuid) =>
            {
                placementMoveActive = false;
                bool saved = registry.TryUpdatePlacementPose(
                    movingPlacementId, mrSpaceOrigin, worldPos, worldRot, anchorUuid);
                if (!saved)
                    ConfigManager.WriteConsoleWarning($"{LogPrefix} move confirm but save failed ({movingPlacementId})");
                movingPlacementId = null;
                RefreshPlacements();
                DrawCurrentScreen();
            },
            cancelCallback: () =>
            {
                placementMoveActive = false;
                movingPlacementId = null;
                DrawCurrentScreen();
            });

        ConfigManager.WriteConsole($"{LogPrefix} move begin {placement.DisplayLabel} ({placement.Id})");
    }

    void ClampListScroll()
    {
        int count = GetListCount();
        if (count <= 0)
        {
            selectedListIndex = 0;
            listScrollOffset = 0;
            return;
        }

        selectedListIndex = Mathf.Clamp(selectedListIndex, 0, count - 1);
        int maxOffset = Mathf.Max(0, count - VisibleCabinetRows);
        if (selectedListIndex < listScrollOffset)
            listScrollOffset = selectedListIndex;
        else if (selectedListIndex >= listScrollOffset + VisibleCabinetRows)
            listScrollOffset = selectedListIndex - VisibleCabinetRows + 1;
        listScrollOffset = Mathf.Clamp(listScrollOffset, 0, maxOffset);
    }

    void ComputeInitialFloorPose(out Vector3 worldPos, out Quaternion worldRot)
    {
        ComputeSpawnPose(out worldPos, out worldRot);

        MREnvironmentSurfaces surfaces = MREnvironmentSurfaces.Instance;
        if (surfaces != null && surfaces.TryGetFloorPointAt(worldPos, out Vector3 floorPoint))
            worldPos = floorPoint;
    }

    void ComputeSpawnPose(out Vector3 worldPos, out Quaternion worldRot)
    {
        Transform view = Camera.main != null ? Camera.main.transform : transform;

        Vector3 forward = view.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        forward.Normalize();

        worldPos = view.position + forward * spawnDistanceMeters;
        worldPos.y = (mrSpaceOrigin != null ? mrSpaceOrigin.position.y : 0f) + spawnYOffsetMeters;
        worldRot = Quaternion.LookRotation(-forward, Vector3.up);
    }

    void setupActionMap()
    {
        if (libretroControlMap == null)
            libretroControlMap = GetComponent<LibretroControlMap>();
        if (libretroControlMap == null)
        {
            ConfigManager.WriteConsoleError($"{LogPrefix} LibretroControlMap missing");
            return;
        }

        ControlMapConfiguration conf = new DefaultControlMap();
#if UNITY_EDITOR
        // Override conflicting default button bindings for editor MR menu flow.
        conf.RemoveMaps(LC.JOYPAD_A);
        conf.RemoveMaps(LC.JOYPAD_B);
        conf.RemoveMaps(LC.JOYPAD_X);
        conf.RemoveMaps(LC.JOYPAD_Y);

        conf.AddMap(LC.JOYPAD_UP, ControlMapPathDictionary.KEYBOARD_W);
        conf.AddMap(LC.JOYPAD_DOWN, ControlMapPathDictionary.KEYBOARD_S);
        conf.AddMap(LC.JOYPAD_LEFT, ControlMapPathDictionary.KEYBOARD_A);
        conf.AddMap(LC.JOYPAD_RIGHT, ControlMapPathDictionary.KEYBOARD_D);
        conf.AddMap(LC.JOYPAD_A, ControlMapPathDictionary.KEYBOARD_ENTER);
        conf.AddMap(LC.JOYPAD_B, ControlMapPathDictionary.KEYBOARD_ESC);
        conf.AddMap(LC.JOYPAD_X, ControlMapPathDictionary.KEYBOARD_ESC);
        conf.AddMap(LC.JOYPAD_Y, ControlMapPathDictionary.KEYBOARD_SPACE);
#endif
        libretroControlMap.CreateFromConfiguration(conf, "inputMap_MRConfiguration_" + name);
        libretroControlMap.Enable(true);
    }

    void cleanActionMap()
    {
        if (libretroControlMap != null)
            libretroControlMap.Clean();
    }

    void ActivateShader(bool online)
    {
        if (screen == null)
            return;

        screen.ActivateShader(online ? shaderOnline : shaderOffline);
    }

    bool ControlActive(string mameControl)
    {
        if (libretroControlMap == null)
            return false;

        try
        {
            return libretroControlMap.Active(mameControl) != 0;
        }
        catch
        {
            libretroControlMap.Enable(true);
            return false;
        }
    }

    static MRLayoutRegistry EnsureRegistry()
    {
        if (MRLayoutRegistry.Instance != null)
            return MRLayoutRegistry.Instance;

        var existing = FindObjectOfType<MRLayoutRegistry>();
        if (existing != null)
            return existing;

        var go = new GameObject("MRLayoutRegistry");
        return go.AddComponent<MRLayoutRegistry>();
    }

    static Transform EnsureMrSpaceOrigin()
    {
        if (MixedRealityManager.Instance != null && MixedRealityManager.Instance.MRSpaceOrigin != null)
            return MixedRealityManager.Instance.MRSpaceOrigin;

        var existing = GameObject.Find("MRSpaceOrigin_TestUI");
        if (existing != null)
            return existing.transform;

        var go = new GameObject("MRSpaceOrigin_TestUI");
        return go.transform;
    }

    MRPlacementRayController EnsurePlacementRayController()
    {
        MRPlacementRayController existing = FindObjectOfType<MRPlacementRayController>();
        if (existing != null)
            return existing;

        GameObject host = new GameObject("MRPlacementRayController");
        return host.AddComponent<MRPlacementRayController>();
    }

    static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
            return "(unnamed)";
        if (value.Length <= maxLen)
            return value;
        return value.Substring(0, maxLen - 3) + "...";
    }

    static string PadRight(string value, int width)
    {
        if (value.Length >= width)
            return value.Substring(0, width);
        return value.PadRight(width);
    }

    float ReadStickY()
    {
#if UNITY_EDITOR
        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W))
            return 1f;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S))
            return -1f;
        return Input.GetAxisRaw("Vertical");
#else
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        return stick.y;
#endif
    }

    bool WasMoveUp() =>
#if UNITY_EDITOR
        Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
#else
        false;
#endif

    bool WasMoveDown() =>
#if UNITY_EDITOR
        Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
#else
        false;
#endif

    bool WasConfirmPressed()
    {
        if (ControlActive(LC.JOYPAD_A))
            return true;
#if UNITY_EDITOR
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0);
#else
        return OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);
#endif
    }

    bool WasBackPressed()
    {
        if (ControlActive(LC.JOYPAD_B) || ControlActive(LC.JOYPAD_X))
            return true;
#if UNITY_EDITOR
        return Input.GetKeyDown(KeyCode.Backspace)
            || Input.GetKeyDown(KeyCode.B)
            || Input.GetKeyDown(KeyCode.Escape)
            || Input.GetKeyDown(KeyCode.JoystickButton1);
#else
        return OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);
#endif
    }
}
