using Dalamud.Game.ClientState.Objects.SubKinds;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Interop;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace MareSynchronos.Services.CharaData;

public class CharaDataGposeTogetherManager : DisposableMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly IpcCallerBrio _brio;
    private readonly SemaphoreSlim _charaDataCreationSemaphore = new(1, 1);
    private readonly CharaDataFileHandler _charaDataFileHandler;
    private readonly CharaDataManager _charaDataManager;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly Dictionary<string, GposeLobbyUserData> _usersInLobby = [];
    private readonly VfxSpawnManager _vfxSpawnManager;
    private (CharacterData ApiData, CharaDataDownloadDto Dto)? _lastCreatedCharaData;
    private PoseData? _lastDeltaPoseData;
    private PoseData? _lastFullPoseData;
    private WorldData? _lastWorldData;
    private CancellationTokenSource _lobbyCts = new();
    private int _poseGenerationExecutions = 0;

    public CharaDataGposeTogetherManager(ILogger<CharaDataGposeTogetherManager> logger, MareMediator mediator,
            ApiController apiController, IpcCallerBrio brio, DalamudUtilService dalamudUtil, VfxSpawnManager vfxSpawnManager,
        CharaDataFileHandler charaDataFileHandler, CharaDataManager charaDataManager) : base(logger, mediator)
    {
        Mediator.Subscribe<GposeLobbyUserJoin>(this, (msg) =>
        {
            OnUserJoinLobby(msg.UserData);
        });
        Mediator.Subscribe<GPoseLobbyUserLeave>(this, (msg) =>
        {
            OnUserLeaveLobby(msg.UserData);
        });
        Mediator.Subscribe<GPoseLobbyReceiveCharaData>(this, (msg) =>
        {
            OnReceiveCharaData(msg.CharaDataDownloadDto);
        });
        Mediator.Subscribe<GPoseLobbyReceivePoseData>(this, (msg) =>
        {
            OnReceivePoseData(msg.UserData, msg.PoseData);
        });
        Mediator.Subscribe<GPoseLobbyReceiveWorldData>(this, (msg) =>
        {
            OnReceiveWorldData(msg.UserData, msg.WorldData);
        });
        Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            if (_usersInLobby.Count > 0 && !string.IsNullOrEmpty(CurrentGPoseLobbyId))
            {
                JoinGPoseLobby(CurrentGPoseLobbyId, isReconnecting: true);
            }
            else
            {
                LeaveGPoseLobby();
            }
        });
        Mediator.Subscribe<GposeStartMessage>(this, (msg) =>
        {
            OnEnterGpose();
        });
        Mediator.Subscribe<GposeEndMessage>(this, (msg) =>
        {
            OnExitGpose();
        });
        Mediator.Subscribe<FrameworkUpdateMessage>(this, (msg) =>
        {
            OnFrameworkUpdate();
        });
        Mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, (msg) =>
        {
            OnCutsceneFrameworkUpdate();
        });
        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            LeaveGPoseLobby();
        });

        _apiController = apiController;
        _brio = brio;
        _dalamudUtil = dalamudUtil;
        _vfxSpawnManager = vfxSpawnManager;
        _charaDataFileHandler = charaDataFileHandler;
        _charaDataManager = charaDataManager;
    }

    public string? CurrentGPoseLobbyId { get; private set; }
    public string? LastGPoseLobbyId { get; private set; }

    public IEnumerable<GposeLobbyUserData> UsersInLobby => _usersInLobby.Values;

    public (bool SameMap, bool SameServer, bool SameEverything) IsOnSameMapAndServer(GposeLobbyUserData data)
    {
        return (data.Map.RowId == _lastWorldData?.LocationInfo.MapId, data.WorldData?.LocationInfo.ServerId == _lastWorldData?.LocationInfo.ServerId, data.WorldData?.LocationInfo == _lastWorldData?.LocationInfo);
    }

    public async Task PushCharacterDownloadDto()
    {
        var playerData = await _charaDataFileHandler.CreatePlayerData().ConfigureAwait(false);
        if (playerData == null) return;
        if (!string.Equals(playerData.DataHash.Value, _lastCreatedCharaData?.ApiData.DataHash.Value, StringComparison.Ordinal))
        {
            List<GamePathEntry> filegamePaths = [.. playerData.FileReplacements[API.Data.Enum.ObjectKind.Player]
            .Where(u => string.IsNullOrEmpty(u.FileSwapPath)).SelectMany(u => u.GamePaths, (file, path) => new GamePathEntry(file.Hash, path))];
            List<GamePathEntry> fileSwapPaths = [.. playerData.FileReplacements[API.Data.Enum.ObjectKind.Player]
            .Where(u => !string.IsNullOrEmpty(u.FileSwapPath)).SelectMany(u => u.GamePaths, (file, path) => new GamePathEntry(file.FileSwapPath, path))];
            await _charaDataManager.UploadFiles([.. playerData.FileReplacements[API.Data.Enum.ObjectKind.Player]
            .Where(u => string.IsNullOrEmpty(u.FileSwapPath)).SelectMany(u => u.GamePaths, (file, path) => new GamePathEntry(file.Hash, path))])
                .ConfigureAwait(false);

            CharaDataDownloadDto charaDataDownloadDto = new($"GPOSELOBBY:{CurrentGPoseLobbyId}", new(_apiController.UID))
            {
                UpdatedDate = DateTime.UtcNow,
                ManipulationData = playerData.ManipulationData,
                CustomizeData = playerData.CustomizePlusData[API.Data.Enum.ObjectKind.Player],
                FileGamePaths = filegamePaths,
                FileSwaps = fileSwapPaths,
                GlamourerData = playerData.GlamourerData[API.Data.Enum.ObjectKind.Player],
            };

            _lastCreatedCharaData = (playerData, charaDataDownloadDto);
        }

        ForceResendOwnData();

        if (_lastCreatedCharaData != null)
            await _apiController.GposeLobbyPushCharacterData(_lastCreatedCharaData.Value.Dto)
                .ConfigureAwait(false);
    }

    internal void CreateNewLobby()
    {
        _ = Task.Run(async () =>
        {
            ClearLobby();
            CurrentGPoseLobbyId = await _apiController.GposeLobbyCreate().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(CurrentGPoseLobbyId))
            {
                _ = GposeWorldPositionBackgroundTask(_lobbyCts.Token);
                _ = GposePoseDataBackgroundTask(_lobbyCts.Token);
            }
        });
    }

    internal void JoinGPoseLobby(string joinLobbyId, bool isReconnecting = false)
    {
        _ = Task.Run(async () =>
        {
            var otherUsers = await _apiController.GposeLobbyJoin(joinLobbyId).ConfigureAwait(false);
            ClearLobby();
            if (otherUsers.Any())
            {
                LastGPoseLobbyId = string.Empty;

                foreach (var user in otherUsers)
                {
                    OnUserJoinLobby(user);
                }

                CurrentGPoseLobbyId = joinLobbyId;
                _ = GposeWorldPositionBackgroundTask(_lobbyCts.Token);
                _ = GposePoseDataBackgroundTask(_lobbyCts.Token);
            }
            else
            {
                LeaveGPoseLobby();
                LastGPoseLobbyId = string.Empty;
            }
        });
    }

    internal void LeaveGPoseLobby()
    {
        _ = Task.Run(async () =>
        {
            var left = await _apiController.GposeLobbyLeave().ConfigureAwait(false);
            if (left)
            {
                if (_usersInLobby.Count != 0)
                {
                    LastGPoseLobbyId = CurrentGPoseLobbyId;
                }

                ClearLobby(revertCharas: true);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            ClearLobby(revertCharas: true);
        }
    }

    private void ClearLobby(bool revertCharas = false)
    {
        _lobbyCts.Cancel();
        _lobbyCts.Dispose();
        _lobbyCts = new();
        CurrentGPoseLobbyId = string.Empty;
        foreach (var user in _usersInLobby.ToDictionary())
        {
            if (revertCharas)
                _charaDataManager.RevertChara(user.Value.HandledChara);
            OnUserLeaveLobby(user.Value.UserData);
        }
        _usersInLobby.Clear();
    }

    private string CreateJsonFromPoseData(PoseData? poseData)
    {
        if (poseData == null) return "{}";

        var node = new JsonObject();
        node["Bones"] = new JsonObject();
        foreach (var bone in poseData.Value.Bones)
        {
            node["Bones"]![bone.Key] = new JsonObject();
            node["Bones"]![bone.Key]!["Position"] = $"{bone.Value.PositionX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.PositionY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.PositionZ.ToString(CultureInfo.InvariantCulture)}";
            node["Bones"]![bone.Key]!["Scale"] = $"{bone.Value.ScaleX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.ScaleY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.ScaleZ.ToString(CultureInfo.InvariantCulture)}";
            node["Bones"]![bone.Key]!["Rotation"] = $"{bone.Value.RotationX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationZ.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationW.ToString(CultureInfo.InvariantCulture)}";
        }
        node["MainHand"] = new JsonObject();
        foreach (var bone in poseData.Value.MainHand)
        {
            node["MainHand"]![bone.Key] = new JsonObject();
            node["MainHand"]![bone.Key]!["Position"] = $"{bone.Value.PositionX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.PositionY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.PositionZ.ToString(CultureInfo.InvariantCulture)}";
            node["MainHand"]![bone.Key]!["Scale"] = $"{bone.Value.ScaleX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.ScaleY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.ScaleZ.ToString(CultureInfo.InvariantCulture)}";
            node["MainHand"]![bone.Key]!["Rotation"] = $"{bone.Value.RotationX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationZ.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationW.ToString(CultureInfo.InvariantCulture)}";
        }
        node["OffHand"] = new JsonObject();
        foreach (var bone in poseData.Value.OffHand)
        {
            node["OffHand"]![bone.Key] = new JsonObject();
            node["OffHand"]![bone.Key]!["Position"] = $"{bone.Value.PositionX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.PositionY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.PositionZ.ToString(CultureInfo.InvariantCulture)}";
            node["OffHand"]![bone.Key]!["Scale"] = $"{bone.Value.ScaleX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.ScaleY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.ScaleZ.ToString(CultureInfo.InvariantCulture)}";
            node["OffHand"]![bone.Key]!["Rotation"] = $"{bone.Value.RotationX.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationY.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationZ.ToString(CultureInfo.InvariantCulture)}, {bone.Value.RotationW.ToString(CultureInfo.InvariantCulture)}";
        }

        return node.ToJsonString();
    }

    private PoseData CreatePoseDataFromJson(string json, PoseData? fullPoseData = null)
    {
        PoseData output = new();
        output.Bones = new(StringComparer.Ordinal);
        output.MainHand = new(StringComparer.Ordinal);
        output.OffHand = new(StringComparer.Ordinal);

        float getRounded(string number)
        {
            return float.Round(float.Parse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture), 5);
        }

        BoneData createBoneData(JsonNode boneJson)
        {
            BoneData outputBoneData = new();
            outputBoneData.Exists = true;
            var posString = boneJson["Position"]!.ToString();
            var pos = posString.Split(",", StringSplitOptions.TrimEntries);
            outputBoneData.PositionX = getRounded(pos[0]);
            outputBoneData.PositionY = getRounded(pos[1]);
            outputBoneData.PositionZ = getRounded(pos[2]);

            var scaString = boneJson["Scale"]!.ToString();
            var sca = scaString.Split(",", StringSplitOptions.TrimEntries);
            outputBoneData.ScaleX = getRounded(sca[0]);
            outputBoneData.ScaleY = getRounded(sca[1]);
            outputBoneData.ScaleZ = getRounded(sca[2]);

            var rotString = boneJson["Rotation"]!.ToString();
            var rot = rotString.Split(",", StringSplitOptions.TrimEntries);
            outputBoneData.RotationX = getRounded(rot[0]);
            outputBoneData.RotationY = getRounded(rot[1]);
            outputBoneData.RotationZ = getRounded(rot[2]);
            outputBoneData.RotationW = getRounded(rot[3]);
            return outputBoneData;
        }

        var node = JsonNode.Parse(json)!;
        var bones = node["Bones"]!.AsObject();
        foreach (var bone in bones)
        {
            string name = bone.Key;
            var boneJson = bone.Value!.AsObject();
            BoneData outputBoneData = createBoneData(boneJson);

            if (fullPoseData != null)
            {
                if (fullPoseData.Value.Bones.TryGetValue(name, out var prevBoneData) && prevBoneData != outputBoneData)
                {
                    output.Bones[name] = outputBoneData;
                }
            }
            else
            {
                output.Bones[name] = outputBoneData;
            }
        }
        var mainHand = node["MainHand"]!.AsObject();
        foreach (var bone in mainHand)
        {
            string name = bone.Key;
            var boneJson = bone.Value!.AsObject();
            BoneData outputBoneData = createBoneData(boneJson);

            if (fullPoseData != null)
            {
                if (fullPoseData.Value.MainHand.TryGetValue(name, out var prevBoneData) && prevBoneData != outputBoneData)
                {
                    output.MainHand[name] = outputBoneData;
                }
            }
            else
            {
                output.MainHand[name] = outputBoneData;
            }
        }
        var offhand = node["OffHand"]!.AsObject();
        foreach (var bone in offhand)
        {
            string name = bone.Key;
            var boneJson = bone.Value!.AsObject();
            BoneData outputBoneData = createBoneData(boneJson);

            if (fullPoseData != null)
            {
                if (fullPoseData.Value.OffHand.TryGetValue(name, out var prevBoneData) && prevBoneData != outputBoneData)
                {
                    output.OffHand[name] = outputBoneData;
                }
            }
            else
            {
                output.OffHand[name] = outputBoneData;
            }
        }

        if (fullPoseData != null)
            output.IsDelta = true;

        return output;
    }

    private async Task GposePoseDataBackgroundTask(CancellationToken ct)
    {
        _lastFullPoseData = null;
        _lastDeltaPoseData = null;
        _poseGenerationExecutions = 0;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            if (!_dalamudUtil.IsInGpose) continue;
            if (_usersInLobby.Count == 0) continue;

            try
            {
                var chara = await _dalamudUtil.GetPlayerCharacterAsync().ConfigureAwait(false);
                if (_dalamudUtil.IsInGpose)
                {
                    chara = (IPlayerCharacter?)(await _dalamudUtil.GetGposeCharacterFromObjectTableByNameAsync(chara.Name.TextValue, _dalamudUtil.IsInGpose).ConfigureAwait(false));
                }
                if (chara == null || chara.Address == nint.Zero) continue;

                var poseJson = await _brio.GetPoseAsync(chara.Address).ConfigureAwait(false);
                if (string.IsNullOrEmpty(poseJson)) continue;

                var lastFullData = _poseGenerationExecutions++ >= 12 ? null : _lastFullPoseData;
                lastFullData = _forceResendFullPose ? _lastFullPoseData : lastFullData;

                var poseData = CreatePoseDataFromJson(poseJson, lastFullData);
                if (!poseData.IsDelta)
                {
                    _lastFullPoseData = poseData;
                    _lastDeltaPoseData = null;
                    _poseGenerationExecutions = 0;
                }

                bool deltaIsSame = _lastDeltaPoseData != null &&
                    (poseData.Bones.Keys.All(k => _lastDeltaPoseData.Value.Bones.ContainsKey(k)
                        && poseData.Bones.Values.All(k => _lastDeltaPoseData.Value.Bones.ContainsValue(k))));

                if (_forceResendFullPose || ((poseData.Bones.Any() || poseData.MainHand.Any() || poseData.OffHand.Any())
                    && (!poseData.IsDelta || (poseData.IsDelta && !deltaIsSame))))
                {
                    _forceResendFullPose = false;
                    await _apiController.GposeLobbyPushPoseData(poseData).ConfigureAwait(false);
                }

                if (poseData.IsDelta)
                    _lastDeltaPoseData = poseData;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error during Pose Data Generation");
            }
        }
    }

    private async Task GposeWorldPositionBackgroundTask(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_dalamudUtil.IsInGpose ? 10 : 1), ct).ConfigureAwait(false);

            // if there are no players in lobby, don't do anything
            if (_usersInLobby.Count == 0) continue;

            try
            {
                // get own player data
                var player = (Dalamud.Game.ClientState.Objects.Types.ICharacter?)(await _dalamudUtil.GetPlayerCharacterAsync().ConfigureAwait(false));
                if (player == null) continue;
                WorldData worldData;
                if (_dalamudUtil.IsInGpose)
                {
                    player = await _dalamudUtil.GetGposeCharacterFromObjectTableByNameAsync(player.Name.TextValue, true).ConfigureAwait(false);
                    if (player == null) continue;
                    worldData = (await _brio.GetTransformAsync(player.Address).ConfigureAwait(false));
                }
                else
                {
                    var rotQuaternion = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), player.Rotation);
                    worldData = new()
                    {
                        PositionX = player.Position.X,
                        PositionY = player.Position.Y,
                        PositionZ = player.Position.Z,
                        RotationW = rotQuaternion.W,
                        RotationX = rotQuaternion.X,
                        RotationY = rotQuaternion.Y,
                        RotationZ = rotQuaternion.Z,
                        ScaleX = 1,
                        ScaleY = 1,
                        ScaleZ = 1
                    };
                }

                var loc = await _dalamudUtil.GetMapDataAsync().ConfigureAwait(false);
                worldData.LocationInfo = loc;

                if (_forceResendWorldData || worldData != _lastWorldData)
                {
                    _forceResendWorldData = false;
                    await _apiController.GposeLobbyPushWorldData(worldData).ConfigureAwait(false);
                    _lastWorldData = worldData;
                    Logger.LogTrace("WorldData (gpose: {gpose}): {data}", _dalamudUtil.IsInGpose, worldData);
                }

                foreach (var entry in _usersInLobby)
                {
                    if (!entry.Value.HasWorldDataUpdate || _dalamudUtil.IsInGpose || entry.Value.WorldData == null) continue;

                    var entryWorldData = entry.Value.WorldData!.Value;

                    if (worldData.LocationInfo.MapId == entryWorldData.LocationInfo.MapId && worldData.LocationInfo.DivisionId == entryWorldData.LocationInfo.DivisionId
                        && (worldData.LocationInfo.HouseId != entryWorldData.LocationInfo.HouseId
                        || worldData.LocationInfo.WardId != entryWorldData.LocationInfo.WardId
                        || entryWorldData.LocationInfo.ServerId != worldData.LocationInfo.ServerId))
                    {
                        if (entry.Value.SpawnedVfxId == null)
                        {
                            // spawn if it doesn't exist yet
                            entry.Value.LastWorldPosition = new Vector3(entryWorldData.PositionX, entryWorldData.PositionY, entryWorldData.PositionZ);
                            entry.Value.SpawnedVfxId = await _dalamudUtil.RunOnFrameworkThread(() => _vfxSpawnManager.SpawnObject(entry.Value.LastWorldPosition.Value,
                                Quaternion.Identity, Vector3.One, 0.5f, 0.1f, 0.5f, 0.9f)).ConfigureAwait(false);
                        }
                        else
                        {
                            // move object via lerp if it does exist
                            var newPosition = new Vector3(entryWorldData.PositionX, entryWorldData.PositionY, entryWorldData.PositionZ);
                            if (newPosition != entry.Value.LastWorldPosition)
                            {
                                entry.Value.UpdateStart = DateTime.UtcNow;
                                entry.Value.TargetWorldPosition = newPosition;
                            }
                        }
                    }
                    else
                    {
                        await _dalamudUtil.RunOnFrameworkThread(() => _vfxSpawnManager.DespawnObject(entry.Value.SpawnedVfxId)).ConfigureAwait(false);
                        entry.Value.SpawnedVfxId = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error during World Data Generation");
            }
        }
    }

    private void OnCutsceneFrameworkUpdate()
    {
        foreach (var kvp in _usersInLobby)
        {
            if (!string.IsNullOrWhiteSpace(kvp.Value.AssociatedCharaName))
            {
                kvp.Value.Address = _dalamudUtil.GetGposeCharacterFromObjectTableByName(kvp.Value.AssociatedCharaName, true)?.Address ?? nint.Zero;
                if (kvp.Value.Address == nint.Zero)
                {
                    kvp.Value.AssociatedCharaName = string.Empty;
                }
            }

            if (kvp.Value.Address != nint.Zero && (kvp.Value.HasWorldDataUpdate || kvp.Value.HasPoseDataUpdate))
            {
                bool hadPoseDataUpdate = kvp.Value.HasPoseDataUpdate;
                bool hadWorldDataUpdate = kvp.Value.HasWorldDataUpdate;
                kvp.Value.HasPoseDataUpdate = false;
                kvp.Value.HasWorldDataUpdate = false;

                _ = Task.Run(async () =>
                {
                    if (hadPoseDataUpdate && kvp.Value.ApplicablePoseData != null)
                    {
                        await _brio.SetPoseAsync(kvp.Value.Address, CreateJsonFromPoseData(kvp.Value.ApplicablePoseData)).ConfigureAwait(false);
                    }
                    if (hadWorldDataUpdate && kvp.Value.WorldData != null)
                    {
                        await _brio.ApplyTransformAsync(kvp.Value.Address, kvp.Value.WorldData.Value).ConfigureAwait(false);
                    }
                });
            }
        }
    }

    private void OnEnterGpose()
    {
        ForceResendOwnData();
        ResetOwnData();
        foreach (var data in _usersInLobby.Values)
        {
            _ = _dalamudUtil.RunOnFrameworkThread(() => _vfxSpawnManager.DespawnObject(data.SpawnedVfxId));
            data.Reset();
        }
    }

    private void OnExitGpose()
    {
        ForceResendOwnData();
        ResetOwnData();
        foreach (var data in _usersInLobby.Values)
        {
            data.Reset();
        }
    }


    private bool _forceResendFullPose = false;
    private bool _forceResendWorldData = false;

    private void ForceResendOwnData()
    {
        _forceResendFullPose = true;
        _forceResendWorldData = true;
    }

    private void ResetOwnData()
    {
        _poseGenerationExecutions = 0;
        _lastCreatedCharaData = null;
    }

    private void OnFrameworkUpdate()
    {
        var frameworkTime = DateTime.UtcNow;
        foreach (var kvp in _usersInLobby)
        {
            if (kvp.Value.SpawnedVfxId != null && kvp.Value.UpdateStart != null)
            {
                var secondsElasped = frameworkTime.Subtract(kvp.Value.UpdateStart.Value).TotalSeconds;
                if (secondsElasped >= 1)
                {
                    kvp.Value.LastWorldPosition = kvp.Value.TargetWorldPosition;
                    kvp.Value.TargetWorldPosition = null;
                    kvp.Value.UpdateStart = null;
                }
                else
                {
                    var lerp = Vector3.Lerp(kvp.Value.LastWorldPosition ?? Vector3.One, kvp.Value.TargetWorldPosition ?? Vector3.One, (float)secondsElasped);
                    _vfxSpawnManager.MoveObject(kvp.Value.SpawnedVfxId.Value, lerp);
                }
            }
        }
    }

    private void OnReceiveCharaData(CharaDataDownloadDto charaDataDownloadDto)
    {
        if (!_usersInLobby.TryGetValue(charaDataDownloadDto.Uploader.UID, out var lobbyData))
        {
            return;
        }

        lobbyData.CharaData = charaDataDownloadDto;
        if (lobbyData.Address != nint.Zero && !string.IsNullOrEmpty(lobbyData.AssociatedCharaName))
        {
            _ = ApplyCharaData(lobbyData);
        }
    }

    public async Task ApplyCharaData(GposeLobbyUserData userData)
    {
        if (userData.CharaData == null || userData.Address == nint.Zero || string.IsNullOrEmpty(userData.AssociatedCharaName))
            return;

        await _charaDataCreationSemaphore.WaitAsync(_lobbyCts.Token).ConfigureAwait(false);

        try
        {
            await _charaDataManager.ApplyCharaData(userData.CharaData!, userData.AssociatedCharaName).ConfigureAwait(false);
            userData.LastAppliedCharaDataDate = userData.CharaData.UpdatedDate;
            userData.HasPoseDataUpdate = true;
            userData.HasWorldDataUpdate = true;
        }
        finally
        {
            _charaDataCreationSemaphore.Release();
        }
    }

    private readonly SemaphoreSlim _charaDataSpawnSemaphore = new(1, 1);

    internal async Task SpawnAndApplyData(GposeLobbyUserData userData)
    {
        if (userData.CharaData == null)
            return;

        await _charaDataSpawnSemaphore.WaitAsync(_lobbyCts.Token).ConfigureAwait(false);
        try
        {
            userData.HasPoseDataUpdate = false;
            userData.HasWorldDataUpdate = false;
            var chara = await _charaDataManager.SpawnAndApplyData(userData.CharaData).ConfigureAwait(false);
            if (chara == null) return;
            userData.HandledChara = chara;
            userData.AssociatedCharaName = chara.Name;
            userData.HasPoseDataUpdate = true;
            userData.HasWorldDataUpdate = true;
        }
        finally
        {
            _charaDataSpawnSemaphore.Release();
        }
    }

    private void OnReceivePoseData(UserData userData, PoseData poseData)
    {
        if (!_usersInLobby.TryGetValue(userData.UID, out var lobbyData))
        {
            return;
        }

        if (poseData.IsDelta)
            lobbyData.DeltaPoseData = poseData;
        else
            lobbyData.FullPoseData = poseData;
    }

    private void OnReceiveWorldData(UserData userData, WorldData worldData)
    {
        _usersInLobby[userData.UID].WorldData = worldData;
        _ = _usersInLobby[userData.UID].SetWorldDataDescriptor(_dalamudUtil);
    }

    private void OnUserJoinLobby(UserData userData)
    {
        if (_usersInLobby.ContainsKey(userData.UID))
            OnUserLeaveLobby(userData);
        _usersInLobby[userData.UID] = new(userData);
        _ = PushCharacterDownloadDto();
    }

    private void OnUserLeaveLobby(UserData msg)
    {
        _usersInLobby.Remove(msg.UID, out var existingData);
        if (existingData != default)
        {
            _ = _dalamudUtil.RunOnFrameworkThread(() => _vfxSpawnManager.DespawnObject(existingData.SpawnedVfxId));
        }
    }
}
