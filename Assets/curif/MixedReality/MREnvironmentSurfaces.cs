/*
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
*/

using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;

/// <summary>
/// Reads floor / ceiling / walls from the MRUK current room and resolves a safe pose
/// (full position + rotation) for ConfigurationCabinet.
/// Strategy: raycast straight DOWN from the target onto the floor of the current room,
/// validate inside room volume, fall back gracefully.
/// </summary>
public class MREnvironmentSurfaces : MonoBehaviour
{
    const string LogPrefix = "[MREnvironmentSurfaces]";
    const float FloorNormalMinY = 0.75f;
    const float CeilingNormalMaxY = -0.75f;
    const float WallNormalMaxAbsY = 0.35f;

    public static MREnvironmentSurfaces Instance { get; private set; }

    [SerializeField] float editorDefaultCeilingHeight = 2.6f;
    [SerializeField] float editorEstimatedEyeHeightMeters = 1.6f;
    [SerializeField] float wallClearanceMeters = 0.55f;
    [SerializeField] int wallProbeDirections = 8;
    [Tooltip("How far above the target to start the downward floor raycast (meters).")]
    [SerializeField] float floorRayStartAboveMeters = 2.0f;
    [Tooltip("Max length of the floor raycast (meters).")]
    [SerializeField] float floorRayMaxDistanceMeters = 6.0f;

    static int physicsFloorLayerMask = -1;
    static int PhysicsFloorMask
    {
        get
        {
            if (physicsFloorLayerMask < 0)
                physicsFloorLayerMask = LayerMask.GetMask("floor");
            return physicsFloorLayerMask;
        }
    }

    static readonly LabelFilter FloorLabelFilter = new LabelFilter(MRUKAnchor.SceneLabels.FLOOR);
    static readonly LabelFilter CeilingLabelFilter = new LabelFilter(MRUKAnchor.SceneLabels.CEILING);
    static readonly LabelFilter WallLabelFilter = new LabelFilter(MRUKAnchor.SceneLabels.WALL_FACE);

    MRSceneBootstrap sceneBootstrap;
    MRUKRoom room;
    string probeSource = "none";

    public bool IsReady { get; private set; }
    public bool HasFloor { get; private set; }
    public bool HasCeiling { get; private set; }
    public float FloorHeight { get; private set; }
    public float CeilingHeight { get; private set; }
    public Vector3 PlayerFloorPoint { get; private set; }
    public bool UsesMrukAnchors => room != null;
    public MRUKRoom CurrentRoom => room;
    public string ProbeSource => probeSource;

    public void ClearMrukScene()
    {
        MRRoomInfoUI.Instance?.Hide();
        sceneBootstrap?.ClearScene();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        sceneBootstrap = GetComponent<MRSceneBootstrap>();
        if (sceneBootstrap == null)
            sceneBootstrap = gameObject.AddComponent<MRSceneBootstrap>();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public IEnumerator ProbeWhenReady(Transform player)
    {
        IsReady = false;
        HasFloor = false;
        HasCeiling = false;
        room = null;
        probeSource = "none";

        if (sceneBootstrap != null)
            yield return sceneBootstrap.EnsureSceneLoaded();

        room = sceneBootstrap != null ? sceneBootstrap.CurrentRoom : null;
        if (room == null && MRUK.Instance != null)
            room = MRUK.Instance.GetCurrentRoom();

        if (player == null)
            player = FindPlayerTransform();

        if (room != null)
            ProbeFromMrukRoom(player);
        else
            ProbeWithRaycastFallback(player);

        IsReady = true;
        ConfigManager.WriteConsole(
            $"{LogPrefix} ready source={probeSource} floor={HasFloor} y={FloorHeight:F2} " +
            $"ceiling={HasCeiling} y={CeilingHeight:F2} room={(room != null ? room.gameObject.name : "null")}");

        ShowRoomInfoUI();
        yield break;
    }

    void ShowRoomInfoUI()
    {
        MRRoomInfoUI ui = MRRoomInfoUI.Instance;
        if (ui == null)
            ui = GetComponent<MRRoomInfoUI>();
        ui?.Show();
    }

    void ProbeFromMrukRoom(Transform player)
    {
        probeSource = "MRUK";

        MRUKAnchor floorAnchor = room.FloorAnchor;
        if (floorAnchor != null)
        {
            HasFloor = true;
            PlayerFloorPoint = floorAnchor.GetAnchorCenter();
            FloorHeight = PlayerFloorPoint.y;
        }

        MRUKAnchor ceilingAnchor = room.CeilingAnchor;
        if (ceilingAnchor != null)
        {
            HasCeiling = true;
            CeilingHeight = ceilingAnchor.GetAnchorCenter().y;
        }

#if UNITY_EDITOR
        if (!HasFloor && player != null)
            ApplyEditorFallback(player.position);
#endif
    }

    void ProbeWithRaycastFallback(Transform player)
    {
        if (probeSource == "none")
            probeSource = "Fallback";

        if (player == null)
        {
#if UNITY_EDITOR
            ApplyEditorFallback(Vector3.zero);
#endif
            return;
        }

        Vector3 eye = player.position;
        if (TryHitFloor(new Ray(eye + Vector3.up * 0.15f, Vector3.down), 4f, out MRSurfaceHit floorHit))
        {
            HasFloor = true;
            FloorHeight = floorHit.point.y;
            PlayerFloorPoint = floorHit.point;
        }
#if UNITY_EDITOR
        else
            ApplyEditorFallback(eye);
#else
        else
        {
            HasFloor = true;
            FloorHeight = 0f;
            PlayerFloorPoint = new Vector3(eye.x, 0f, eye.z);
        }
#endif

        if (HasFloor && TryHitCeiling(new Ray(new Vector3(PlayerFloorPoint.x, FloorHeight + 0.1f, PlayerFloorPoint.z), Vector3.up), 4f, out MRSurfaceHit ceilingHit))
        {
            HasCeiling = true;
            CeilingHeight = ceilingHit.point.y;
        }
#if UNITY_EDITOR
        else if (HasFloor)
        {
            HasCeiling = true;
            CeilingHeight = FloorHeight + editorDefaultCeilingHeight;
        }
#endif
    }

#if UNITY_EDITOR
    void ApplyEditorFallback(Vector3 eye)
    {
        float floorY = eye.y - editorEstimatedEyeHeightMeters;
        if (TryPhysicsFloor(eye, out Vector3 physicsFloor))
            floorY = physicsFloor.y;

        HasFloor = true;
        FloorHeight = floorY;
        PlayerFloorPoint = new Vector3(eye.x, floorY, eye.z);
        HasCeiling = true;
        CeilingHeight = floorY + editorDefaultCeilingHeight;
        if (probeSource == "none")
            probeSource = "EditorFallback";
    }
#endif

    /// <summary>
    /// Compute full pose (X, Y, Z + rotation facing the player) for ConfigurationCabinet.
    /// Step 1: target = player + forward*distance.
    /// Step 2: project target VERTICALLY onto floor of current MRUK room (or physics fallback).
    /// Step 3: nudge away from walls if needed.
    /// Step 4: clamp inside room volume.
    /// Step 5: rotate to face the player.
    /// </summary>
    public bool TryGetConfigurationCabinetPose(
        Transform player,
        float distanceMeters,
        Vector3 cabinetFootprint,
        out Vector3 worldPosition,
        out Quaternion worldRotation)
    {
        worldPosition = Vector3.zero;
        worldRotation = Quaternion.identity;

        if (player == null)
            return false;

        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 target = player.position + forward * distanceMeters;

        if (!TryGetFloorPointAt(target, out Vector3 floorPoint))
            floorPoint = new Vector3(target.x, HasFloor ? FloorHeight : (player.position.y - editorEstimatedEyeHeightMeters), target.z);

        Vector3 placement = floorPoint;
        placement = NudgeAwayFromWalls(placement, cabinetFootprint);
        placement = ClampInsideRoom(placement, cabinetFootprint);

        if (TryGetFloorPointAt(placement, out Vector3 finalFloor))
            placement.y = finalFloor.y;
        else if (HasFloor)
            placement.y = FloorHeight;

        worldPosition = placement;
        worldRotation = RotationFacingPlayer(worldPosition, player.position);

        ConfigManager.WriteConsole(
            $"{LogPrefix} cabinet pose target={target} placement={worldPosition} " +
            $"hasFloor={HasFloor} floorY={FloorHeight:F2} room={(room != null ? room.gameObject.name : "null")}");
        return true;
    }

    /// <summary>
    /// Vertically project a horizontal point onto the MRUK floor of the current room.
    /// Uses GetBestPoseFromRaycast straight DOWN to stay on the actual floor plane.
    /// </summary>
    public bool TryGetFloorPointAt(Vector3 worldNear, out Vector3 floorPoint)
    {
        floorPoint = default;

        if (room != null && HasFloor)
        {
            float startY = Mathf.Max(worldNear.y, FloorHeight) + floorRayStartAboveMeters;
            Vector3 origin = new Vector3(worldNear.x, startY, worldNear.z);
            Ray downRay = new Ray(origin, Vector3.down);

            Pose pose = room.GetBestPoseFromRaycast(
                downRay,
                floorRayMaxDistanceMeters + floorRayStartAboveMeters,
                FloorLabelFilter,
                out MRUKAnchor hitAnchor,
                out _,
                MRUK.PositioningMethod.DEFAULT);

            if (hitAnchor != null)
            {
                floorPoint = pose.position;
                return true;
            }
        }

        if (room != null)
        {
            float dist = room.TryGetClosestSurfacePosition(
                new Vector3(worldNear.x, HasFloor ? FloorHeight + 0.2f : worldNear.y + 0.35f, worldNear.z),
                out Vector3 closest,
                out _,
                out Vector3 normal,
                FloorLabelFilter);
            if (FoundSurface(dist) && IsFloorNormal(normal))
            {
                floorPoint = new Vector3(worldNear.x, closest.y, worldNear.z);
                return true;
            }
        }

        if (HasFloor)
        {
            floorPoint = new Vector3(worldNear.x, FloorHeight, worldNear.z);
            return true;
        }

        if (TryPhysicsFloor(new Vector3(worldNear.x, worldNear.y + 0.2f, worldNear.z), out Vector3 physicsPoint))
        {
            floorPoint = new Vector3(worldNear.x, physicsPoint.y, worldNear.z);
            return true;
        }

        return false;
    }

    public bool TryGetCeilingPointAt(Vector3 worldNear, out Vector3 ceilingPoint)
    {
        ceilingPoint = default;

        float baseY = HasFloor ? FloorHeight + 0.5f : worldNear.y;
        Vector3 probe = new Vector3(worldNear.x, baseY, worldNear.z);

        if (room != null)
        {
            float dist = room.TryGetClosestSurfacePosition(
                probe, out ceilingPoint, out _, out Vector3 normal, CeilingLabelFilter);
            if (FoundSurface(dist) && IsCeilingNormal(normal))
                return true;
        }

        if (HasCeiling)
        {
            ceilingPoint = new Vector3(worldNear.x, CeilingHeight, worldNear.z);
            return true;
        }

        if (HasFloor)
        {
            ceilingPoint = new Vector3(worldNear.x, FloorHeight + editorDefaultCeilingHeight, worldNear.z);
            return true;
        }

        return false;
    }

    public bool TryGetCeilingPoint(Vector3 horizontalPoint, out Vector3 ceilingPoint)
    {
        return TryGetCeilingPointAt(horizontalPoint, out ceilingPoint);
    }

    public void AlignOriginToFloor(Transform mrSpaceOrigin, Transform player)
    {
        if (mrSpaceOrigin == null)
            return;

        Vector3 pos = player != null ? player.position : mrSpaceOrigin.position;
        pos.y = HasFloor ? FloorHeight : 0f;

        mrSpaceOrigin.position = pos;
        mrSpaceOrigin.rotation = Quaternion.identity;
    }

    Vector3 ClampInsideRoom(Vector3 point, Vector3 cabinetFootprint)
    {
        if (room == null)
            return point;

        if (room.IsPositionInRoom(point, testVerticalBounds: false)
            && !room.IsPositionInSceneVolume(point, out _, testVerticalBounds: true, distanceBuffer: 0.15f))
            return point;

        if (room.GenerateRandomPositionOnSurface(
                MRUK.SurfaceType.FACING_UP,
                wallClearanceMeters,
                FloorLabelFilter,
                out Vector3 randomPos,
                out _)
            && room.IsPositionInRoom(randomPos, testVerticalBounds: false))
        {
            return randomPos;
        }

        float closestDist = room.TryGetClosestSurfacePosition(
            point, out Vector3 closest, out _, out Vector3 normal, FloorLabelFilter);
        if (FoundSurface(closestDist) && IsFloorNormal(normal))
            return closest;

        return point;
    }

    Vector3 NudgeAwayFromWalls(Vector3 point, Vector3 cabinetFootprint)
    {
        float probeHeight = Mathf.Max(0.9f, cabinetFootprint.y * 0.45f);
        float clearance = wallClearanceMeters + Mathf.Max(cabinetFootprint.x, cabinetFootprint.z) * 0.5f;
        Vector3 origin = new Vector3(point.x, (HasFloor ? FloorHeight : point.y) + probeHeight, point.z);

        for (int i = 0; i < wallProbeDirections; i++)
        {
            float angle = i * (360f / wallProbeDirections) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

            if (room != null)
            {
                float wallDist = room.TryGetClosestSurfacePosition(
                    origin + dir * clearance,
                    out Vector3 wallPoint,
                    out _,
                    out Vector3 wallNormal,
                    WallLabelFilter);
                if (FoundSurface(wallDist) && IsWallNormal(wallNormal))
                {
                    float dist = Vector3.Distance(origin, wallPoint);
                    if (dist < clearance)
                        point -= dir * (clearance - dist + 0.05f);
                    continue;
                }
            }

            if (!TryHitWall(new Ray(origin, dir), clearance + 0.6f, out MRSurfaceHit wallHit))
                continue;

            float rayDist = Vector3.Distance(origin, wallHit.point);
            if (rayDist < clearance)
                point -= dir * (clearance - rayDist + 0.05f);
        }

        return point;
    }

    static Quaternion RotationFacingPlayer(Vector3 cabinetPosition, Vector3 playerPosition)
    {
        Vector3 toPlayer = playerPosition - cabinetPosition;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f)
            return Quaternion.identity;

        return Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
    }

    bool TryHitFloor(Ray ray, float maxDistance, out MRSurfaceHit hit)
    {
        if (TryCast(ray, maxDistance, out hit) && IsFloorNormal(hit.normal))
            return true;

        if (TryPhysicsFloor(ray.origin, out Vector3 physicsPoint))
        {
            hit = new MRSurfaceHit { ok = true, point = physicsPoint, normal = Vector3.up };
            return true;
        }

        hit = default;
        return false;
    }

    bool TryHitCeiling(Ray ray, float maxDistance, out MRSurfaceHit hit)
    {
        if (TryCast(ray, maxDistance, out hit) && IsCeilingNormal(hit.normal))
            return true;

        hit = default;
        return false;
    }

    bool TryHitWall(Ray ray, float maxDistance, out MRSurfaceHit hit)
    {
        if (TryCast(ray, maxDistance, out hit) && IsWallNormal(hit.normal))
            return true;

        hit = default;
        return false;
    }

    bool TryCast(Ray ray, float maxDistance, out MRSurfaceHit hit)
    {
        hit = default;

        if (Physics.Raycast(ray, out RaycastHit physicsHit, maxDistance, PhysicsFloorMask))
        {
            hit = new MRSurfaceHit { ok = true, point = physicsHit.point, normal = physicsHit.normal };
            return true;
        }

        return false;
    }

    static bool TryPhysicsFloor(Vector3 nearPoint, out Vector3 floorPoint)
    {
        Ray ray = new Ray(nearPoint + Vector3.up * 0.2f, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 5f, PhysicsFloorMask))
        {
            floorPoint = hit.point;
            return true;
        }

        floorPoint = default;
        return false;
    }

    static bool FoundSurface(float distance) => !float.IsInfinity(distance);

    static bool IsFloorNormal(Vector3 normal) => normal.y >= FloorNormalMinY;

    static bool IsCeilingNormal(Vector3 normal) => normal.y <= CeilingNormalMaxY;

    static bool IsWallNormal(Vector3 normal) => Mathf.Abs(normal.y) <= WallNormalMaxAbsY;

    static Transform FindPlayerTransform()
    {
        var pc = FindObjectOfType<PlayerController>();
        if (pc != null && pc.PlayerControllerGameObject != null)
            return pc.PlayerControllerGameObject.transform;
        if (pc != null)
            return pc.transform;

        var tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged != null ? tagged.transform : null;
    }

    struct MRSurfaceHit
    {
        public bool ok;
        public Vector3 point;
        public Vector3 normal;
    }
}
