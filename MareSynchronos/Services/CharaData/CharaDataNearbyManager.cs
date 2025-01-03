using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Interop;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.Services;

internal sealed class CharaDataNearbyManager : DisposableMediatorSubscriberBase
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly Dictionary<CharaDataMetaInfoDto, List<PoseEntry>> _nearbyData = [];
    private readonly Dictionary<long, Guid> _poseVfx = [];
    private readonly Dictionary<UserData, List<CharaDataMetaInfoDto>> _sharedWithYouData = [];
    private readonly VfxSpawnManager _vfxSpawnManager;

    public CharaDataNearbyManager(ILogger<CharaDataNearbyManager> logger, MareMediator mediator, DalamudUtilService dalamudUtilService, VfxSpawnManager vfxSpawnManager) : base(logger, mediator)
    {
        mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => HandleFrameworkUpdate());
        _dalamudUtilService = dalamudUtilService;
        _vfxSpawnManager = vfxSpawnManager;
    }

    public void UpdateSharedData(Dictionary<UserData, List<CharaDataMetaInfoDto>> newData)
    {
        _sharedWithYouData.Clear();
        foreach (var kvp in newData)
        {
            _sharedWithYouData[kvp.Key] = kvp.Value;
        }
    }

    private void HandleFrameworkUpdate()
    {
        var previousPoses = _nearbyData.Values.SelectMany(k => k).Select(k => k.Id!.Value).ToList();
        _nearbyData.Clear();

        var map = _dalamudUtilService.GetMapData();
        var pos = _dalamudUtilService.GetPlayerCharacter().Position;
        var data = _sharedWithYouData.SelectMany(v => v.Value)
            .SelectMany(v => v.PoseData, (MetaInfo, PoseData) => (MetaInfo, PoseData))
            .Where(p => p.PoseData.WorldData != null && p.PoseData.WorldData != default(WorldData)
                && p.PoseData.WorldData.Value.LocationInfo.MapId == map.MapId && p.PoseData.WorldData.Value.LocationInfo.TerritoryId == map.TerritoryId)
            .ToList();

        foreach (var entry in data)
        {
            var dist = Vector3.Distance(pos, new Vector3(entry.PoseData.WorldData.Value.PositionX, entry.PoseData.WorldData.Value.PositionY, entry.PoseData.WorldData.Value.PositionZ));
            Logger.LogDebug("Distance from player to data {data} is {dist}", entry.MetaInfo.Id, dist);
            if (dist < 50)
            {
                if (!_nearbyData.TryGetValue(entry.MetaInfo, out var poseList))
                {
                    _nearbyData[entry.MetaInfo] = [entry.PoseData];

                }
                else
                {
                    poseList.Add(entry.PoseData);
                }
            }
        }

        foreach (var prevPose in previousPoses.Except(_nearbyData.Values.SelectMany(k => k).Select(k => k.Id!.Value)))
        {
            if (_poseVfx.TryGetValue(prevPose, out Guid vfx))
            {
                _vfxSpawnManager.DespawnObject(vfx);
                _poseVfx.Remove(prevPose);
            }
        }

        foreach (var newPoseList in _nearbyData)
        {
            foreach (var pose in newPoseList.Value)
            {
                if (!_poseVfx.TryGetValue(pose.Id!.Value, out Guid vfx))
                {
                    var guid = _vfxSpawnManager.SpawnObject(new Vector3(pose.WorldData.Value.PositionX, pose.WorldData.Value.PositionY, pose.WorldData.Value.PositionZ),
                        new Quaternion(pose.WorldData.Value.RotationX, pose.WorldData.Value.RotationY, pose.WorldData.Value.RotationZ, pose.WorldData.Value.RotationW));
                    if (guid != null)
                    {
                        _poseVfx[pose.Id!.Value] = guid.Value;
                    }
                }
            }
        }
    }
}
