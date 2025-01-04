using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using MareSynchronos.API.Data;
using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.Services;

internal sealed class CharaDataNearbyManager : DisposableMediatorSubscriberBase
{
    internal record NearbyCharaDataEntry
    {
        public float Direction { get; init; }
        public float Distance { get; init; }
    }

    private readonly DalamudUtilService _dalamudUtilService;
    private readonly Dictionary<PoseEntryExtended, NearbyCharaDataEntry> _nearbyData = [];
    private readonly Dictionary<PoseEntryExtended, Guid> _poseVfx = [];
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly CharaDataConfigService _charaDataConfigService;
    private readonly Dictionary<UserData, List<CharaDataMetaInfoExtendedDto>> _sharedWithYouData = [];
    private readonly VfxSpawnManager _vfxSpawnManager;
    private Task? _filterEntriesRunningTask;
    private (Guid VfxId, PoseEntryExtended Pose)? _hoveredVfx = null;
    private DateTime _lastExecutionTime = DateTime.UtcNow;
    public CharaDataNearbyManager(ILogger<CharaDataNearbyManager> logger, MareMediator mediator,
        DalamudUtilService dalamudUtilService, VfxSpawnManager vfxSpawnManager,
        ServerConfigurationManager serverConfigurationManager,
        CharaDataConfigService charaDataConfigService) : base(logger, mediator)
    {
        mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => HandleFrameworkUpdate());
        mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, (_) => HandleFrameworkUpdate());
        _dalamudUtilService = dalamudUtilService;
        _vfxSpawnManager = vfxSpawnManager;
        _serverConfigurationManager = serverConfigurationManager;
        _charaDataConfigService = charaDataConfigService;
        mediator.Subscribe<GposeStartMessage>(this, (_) => ClearAllVfx());
    }

    public bool ComputeNearbyData { get; set; } = false;

    public IDictionary<PoseEntryExtended, NearbyCharaDataEntry> NearbyData => _nearbyData;

    public string UserNoteFilter { get; set; } = string.Empty;

    public void UpdateSharedData(Dictionary<UserData, List<CharaDataMetaInfoExtendedDto>> newData)
    {
        _sharedWithYouData.Clear();
        foreach (var kvp in newData)
        {
            _sharedWithYouData[kvp.Key] = kvp.Value;
        }
    }

    internal void SetHoveredVfx(PoseEntryExtended? hoveredPose)
    {
        if (hoveredPose == null && _hoveredVfx == null)
            return;

        if (hoveredPose == null)
        {
            _vfxSpawnManager.DespawnObject(_hoveredVfx!.Value.VfxId);
            _hoveredVfx = null;
            return;
        }

        if (_hoveredVfx == null)
        {
            var vfxGuid = _vfxSpawnManager.SpawnObject(hoveredPose.Position, hoveredPose.Rotation, Vector3.One * 4, 1, 0.2f, 0.2f, 1f);
            if (vfxGuid != null)
                _hoveredVfx = (vfxGuid.Value, hoveredPose);
            return;
        }

        if (hoveredPose != _hoveredVfx!.Value.Pose)
        {
            _vfxSpawnManager.DespawnObject(_hoveredVfx.Value.VfxId);
            var vfxGuid = _vfxSpawnManager.SpawnObject(hoveredPose.Position, hoveredPose.Rotation, Vector3.One * 4, 1, 0.2f, 0.2f, 1f);
            if (vfxGuid != null)
                _hoveredVfx = (vfxGuid.Value, hoveredPose);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ClearAllVfx();
    }

    private static float CalculateYawDegrees(Vector3 directionXZ)
    {
        // Calculate yaw angle in radians using Atan2 (X, Z)
        float yawRadians = (float)Math.Atan2(-directionXZ.X, directionXZ.Z);
        float yawDegrees = yawRadians * (180f / (float)Math.PI);

        // Normalize to [0, 360)
        if (yawDegrees < 0)
            yawDegrees += 360f;

        return yawDegrees;
    }

    private static float GetAngleToTarget(Vector3 cameraPosition, float cameraYawDegrees, Vector3 targetPosition)
    {
        // Step 4: Calculate the direction vector from camera to target
        Vector3 directionToTarget = targetPosition - cameraPosition;

        // Step 5: Project the directionToTarget onto the XZ plane (ignore Y)
        Vector3 directionToTargetXZ = new Vector3(directionToTarget.X, 0, directionToTarget.Z);

        // Handle the case where the target is directly above or below the camera
        if (directionToTargetXZ.LengthSquared() < 1e-10f)
        {
            return 0; // Default direction
        }

        directionToTargetXZ = Vector3.Normalize(directionToTargetXZ);

        // Step 6: Calculate the target's yaw angle
        float targetYawDegrees = CalculateYawDegrees(directionToTargetXZ);

        // Step 7: Calculate relative angle
        float relativeAngle = targetYawDegrees - cameraYawDegrees;
        if (relativeAngle < 0)
            relativeAngle += 360f;

        // Step 8: Map relative angle to ArrowDirection
        return relativeAngle;
    }

    private static float GetCameraYaw(Vector3 cameraPosition, Vector3 lookAtVector)
    {
        // Step 1: Calculate the direction vector from camera to LookAtPoint
        Vector3 directionFacing = lookAtVector - cameraPosition;

        // Step 2: Project the directionFacing onto the XZ plane (ignore Y)
        Vector3 directionFacingXZ = new Vector3(directionFacing.X, 0, directionFacing.Z);

        // Handle the case where the LookAtPoint is directly above or below the camera
        if (directionFacingXZ.LengthSquared() < 1e-10f)
        {
            // Default to facing forward along the Z-axis if LookAtPoint is directly above or below
            directionFacingXZ = new Vector3(0, 0, 1);
        }
        else
        {
            directionFacingXZ = Vector3.Normalize(directionFacingXZ);
        }

        // Step 3: Calculate the camera's yaw angle based on directionFacingXZ
        return (CalculateYawDegrees(directionFacingXZ));
    }

    private void ClearAllVfx()
    {
        foreach (var vfx in _poseVfx)
        {
            _vfxSpawnManager.DespawnObject(vfx.Value);
        }
        _poseVfx.Clear();
    }

    private async Task FilterEntriesAsync(Vector3 cameraPos, Vector3 cameraLookAt)
    {
        var previousPoses = _nearbyData.Keys.ToList();
        _nearbyData.Clear();

        var locationInfo = await _dalamudUtilService.RunOnFrameworkThread(() => _dalamudUtilService.GetMapData()).ConfigureAwait(false);
        var player = await _dalamudUtilService.RunOnFrameworkThread(() => _dalamudUtilService.GetPlayerCharacter()).ConfigureAwait(false);
        var currentServer = player.CurrentWorld;
        var playerPos = player.Position;

        var cameraYaw = GetCameraYaw(cameraPos, cameraLookAt);

        // initial filter on name
        foreach (var data in _sharedWithYouData.Where(d => (string.IsNullOrWhiteSpace(UserNoteFilter)
            || ((d.Key.Alias ?? string.Empty).Contains(UserNoteFilter, StringComparison.OrdinalIgnoreCase)
            || d.Key.UID.Contains(UserNoteFilter, StringComparison.OrdinalIgnoreCase)
            || (_serverConfigurationManager.GetNoteForUid(UserNoteFilter) ?? string.Empty).Contains(UserNoteFilter, StringComparison.OrdinalIgnoreCase))))
            .ToDictionary(k => k.Key, k => k.Value))
        {
            // filter all poses based on territory, that always must be correct
            foreach (var pose in data.Value.Where(v => v.HasPoses && v.HasWorldData).SelectMany(k => k.PoseExtended)
                .Where(p => p.HasPoseData
                    && p.HasWorldData
                    && p.WorldData!.Value.LocationInfo.TerritoryId == locationInfo.TerritoryId
                    && (!_charaDataConfigService.Current.NearbyOwnServerOnly || p.WorldData!.Value.LocationInfo.ServerId == currentServer.RowId))
                .ToList())
            {
                var poseLocation = pose.WorldData!.Value.LocationInfo;

                bool isInHousing = poseLocation.WardId != 0;
                var distance = Vector3.Distance(playerPos, pose.Position);
                if (distance > _charaDataConfigService.Current.NearbyDistanceFilter) continue;

                bool addEntry = (!isInHousing && poseLocation.MapId == locationInfo.MapId)
                    || (isInHousing
                        && ((poseLocation.HouseId == 0 && poseLocation.DivisionId == locationInfo.DivisionId
                                && (_charaDataConfigService.Current.NearbyIgnoreHousingLimitations || poseLocation.WardId == locationInfo.WardId))
                            || (poseLocation.HouseId > 0
                                && (_charaDataConfigService.Current.NearbyIgnoreHousingLimitations || (poseLocation.HouseId == locationInfo.HouseId && poseLocation.WardId == locationInfo.WardId && poseLocation.DivisionId == locationInfo.DivisionId && poseLocation.RoomId == locationInfo.RoomId)))
                           ));

                if (addEntry)
                    _nearbyData[pose] = new() { Direction = GetAngleToTarget(cameraPos, cameraYaw, pose.Position), Distance = distance };
            }
        }

        if (_charaDataConfigService.Current.NearbyDrawWisps && !_dalamudUtilService.IsInGpose)
            await _dalamudUtilService.RunOnFrameworkThread(() => ManageWispsNearby(previousPoses)).ConfigureAwait(false);
    }

    private unsafe void HandleFrameworkUpdate()
    {
        if (_lastExecutionTime.AddSeconds(0.5) > DateTime.UtcNow) return;
        _lastExecutionTime = DateTime.UtcNow;
        if (!ComputeNearbyData)
        {
            if (_nearbyData.Any())
                _nearbyData.Clear();
            if (_poseVfx.Any())
                ClearAllVfx();
            return;
        }

        if (!_charaDataConfigService.Current.NearbyDrawWisps || _dalamudUtilService.IsInGpose)
            ClearAllVfx();

        var camera = CameraManager.Instance()->CurrentCamera;
        Vector3 cameraPos = new(camera->Position.X, camera->Position.Y, camera->Position.Z);
        Vector3 lookAt = new(camera->LookAtVector.X, camera->LookAtVector.Y, camera->LookAtVector.Z);

        if (_filterEntriesRunningTask?.IsCompleted ?? true)
            _filterEntriesRunningTask = FilterEntriesAsync(cameraPos, lookAt);
    }

    private void ManageWispsNearby(List<PoseEntryExtended> previousPoses)
    {
        foreach (var data in _nearbyData.Keys)
        {
            if (_poseVfx.TryGetValue(data, out var _)) continue;

            var vfxGuid = _vfxSpawnManager.SpawnObject(data.Position, data.Rotation, Vector3.One * 2);
            if (vfxGuid != null)
            {
                _poseVfx[data] = vfxGuid.Value;
            }
        }

        foreach (var data in previousPoses.Except(_nearbyData.Keys))
        {
            if (_poseVfx.Remove(data, out var guid))
            {
                _vfxSpawnManager.DespawnObject(guid);
            }
        }
    }
}
