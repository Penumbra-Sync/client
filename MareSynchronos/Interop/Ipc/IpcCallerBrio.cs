using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Services;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.Json.Nodes;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerBrio : IIpcCaller
{
    private readonly ILogger<IpcCallerBrio> _logger;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly ICallGateSubscriber<(int, int)> _brioApiVersion;

    private readonly ICallGateSubscriber<bool, bool, bool, Task<IGameObject>> _brioSpawnActorAsync;
    private readonly ICallGateSubscriber<IGameObject, bool> _brioDespawnActor;
    private readonly ICallGateSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool> _brioSetModelTransform;
    private readonly ICallGateSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)> _brioGetModelTransform;
    private readonly ICallGateSubscriber<IGameObject, string> _brioGetPoseAsJson;
    private readonly ICallGateSubscriber<IGameObject, string, bool, bool> _brioSetPoseFromJson;
    private readonly ICallGateSubscriber<IGameObject, bool> _brioFreezeActor;
    private readonly ICallGateSubscriber<bool> _brioFreezePhysics;


    public bool APIAvailable { get; private set; }

    public IpcCallerBrio(ILogger<IpcCallerBrio> logger, IDalamudPluginInterface dalamudPluginInterface,
        DalamudUtilService dalamudUtilService)
    {
        _logger = logger;
        _dalamudUtilService = dalamudUtilService;

        _brioApiVersion = dalamudPluginInterface.GetIpcSubscriber<(int, int)>("Brio.ApiVersion");
        _brioSpawnActorAsync = dalamudPluginInterface.GetIpcSubscriber<bool, bool, bool, Task<IGameObject>>("Brio.Actor.SpawnExAsync");
        _brioDespawnActor = dalamudPluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.Actor.Despawn");
        _brioSetModelTransform = dalamudPluginInterface.GetIpcSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool>("Brio.Actor.SetModelTransform");
        _brioGetModelTransform = dalamudPluginInterface.GetIpcSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)>("Brio.Actor.GetModelTransform");
        _brioGetPoseAsJson = dalamudPluginInterface.GetIpcSubscriber<IGameObject, string>("Brio.Actor.Pose.GetPoseAsJson");
        _brioSetPoseFromJson = dalamudPluginInterface.GetIpcSubscriber<IGameObject, string, bool, bool>("Brio.Actor.Pose.LoadFromJson");
        _brioFreezeActor = dalamudPluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.Actor.Freeze");
        _brioFreezePhysics = dalamudPluginInterface.GetIpcSubscriber<bool>("Brio.FreezePhysics");

        CheckAPI();
    }

    public void CheckAPI()
    {
        try
        {
            var version = _brioApiVersion.InvokeFunc();
            APIAvailable = (version.Item1 == 2 && version.Item2 >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public async Task<IGameObject?> SpawnActorAsync()
    {
        if (!APIAvailable) return null;
        _logger.LogDebug("Spawning Brio Actor");
        return await _brioSpawnActorAsync.InvokeFunc(false, false, true).ConfigureAwait(false);
    }

    public async Task<bool> DespawnActorAsync(nint address)
    {
        if (!APIAvailable) return false;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return false;
        _logger.LogDebug("Despawning Brio Actor {actor}", gameObject.Name.TextValue);
        return await _dalamudUtilService.RunOnFrameworkThread(() => _brioDespawnActor.InvokeFunc(gameObject)).ConfigureAwait(false);
    }

    public async Task<bool> ApplyTransformAsync(nint address, WorldData data)
    {
        if (!APIAvailable) return false;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return false;
        _logger.LogDebug("Applying Transform to Actor {actor}", gameObject.Name.TextValue);

        return await _dalamudUtilService.RunOnFrameworkThread(() => _brioSetModelTransform.InvokeFunc(gameObject,
            new Vector3(data.PositionX, data.PositionY, data.PositionZ),
            new Quaternion(data.RotationX, data.RotationY, data.RotationZ, data.RotationW),
            new Vector3(data.ScaleX, data.ScaleY, data.ScaleZ), false)).ConfigureAwait(false);
    }

    public async Task<WorldData> GetTransformAsync(nint address)
    {
        if (!APIAvailable) return default;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return default;
        var data = await _dalamudUtilService.RunOnFrameworkThread(() => _brioGetModelTransform.InvokeFunc(gameObject)).ConfigureAwait(false);
        //_logger.LogDebug("Getting Transform from Actor {actor}", gameObject.Name.TextValue);

        return new WorldData()
        {
            PositionX = data.Item1.Value.X,
            PositionY = data.Item1.Value.Y,
            PositionZ = data.Item1.Value.Z,
            RotationX = data.Item2.Value.X,
            RotationY = data.Item2.Value.Y,
            RotationZ = data.Item2.Value.Z,
            RotationW = data.Item2.Value.W,
            ScaleX = data.Item3.Value.X,
            ScaleY = data.Item3.Value.Y,
            ScaleZ = data.Item3.Value.Z
        };
    }

    public async Task<string?> GetPoseAsync(nint address)
    {
        if (!APIAvailable) return null;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return null;
        _logger.LogDebug("Getting Pose from Actor {actor}", gameObject.Name.TextValue);

        return await _dalamudUtilService.RunOnFrameworkThread(() => _brioGetPoseAsJson.InvokeFunc(gameObject)).ConfigureAwait(false);
    }

    public async Task<bool> SetPoseAsync(nint address, string pose)
    {
        if (!APIAvailable) return false;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return false;
        _logger.LogDebug("Setting Pose to Actor {actor}", gameObject.Name.TextValue);

        var applicablePose = JsonNode.Parse(pose)!;
        var currentPose = await _dalamudUtilService.RunOnFrameworkThread(() => _brioGetPoseAsJson.InvokeFunc(gameObject)).ConfigureAwait(false);
        applicablePose["ModelDifference"] = JsonNode.Parse(JsonNode.Parse(currentPose)!["ModelDifference"]!.ToJsonString());

        await _dalamudUtilService.RunOnFrameworkThread(() =>
        {
            _brioFreezeActor.InvokeFunc(gameObject);
            _brioFreezePhysics.InvokeFunc();
        }).ConfigureAwait(false);
        return await _dalamudUtilService.RunOnFrameworkThread(() => _brioSetPoseFromJson.InvokeFunc(gameObject, applicablePose.ToJsonString(), false)).ConfigureAwait(false);
    }

    public void Dispose()
    {
    }
}
