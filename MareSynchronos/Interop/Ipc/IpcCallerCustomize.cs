using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Utility;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerCustomize : IIpcCaller
{
    private readonly ICallGateSubscriber<(int, int)> _customizePlusApiVersion;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _customizePlusGetActiveProfile;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _customizePlusGetProfileById;
    private readonly ICallGateSubscriber<ushort, Guid, object> _customizePlusOnScaleUpdate;
    private readonly ICallGateSubscriber<ushort, int> _customizePlusRevertCharacter;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _customizePlusSetBodyScaleToCharacter;
    private readonly ICallGateSubscriber<Guid, int> _customizePlusDeleteByUniqueId;
    private readonly ILogger<IpcCallerCustomize> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;

    public IpcCallerCustomize(ILogger<IpcCallerCustomize> logger, IDalamudPluginInterface dalamudPluginInterface,
        DalamudUtilService dalamudUtil, MareMediator mareMediator)
    {
        _customizePlusApiVersion = dalamudPluginInterface.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        _customizePlusGetActiveProfile = dalamudPluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        _customizePlusGetProfileById = dalamudPluginInterface.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        _customizePlusRevertCharacter = dalamudPluginInterface.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");
        _customizePlusSetBodyScaleToCharacter = dalamudPluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        _customizePlusOnScaleUpdate = dalamudPluginInterface.GetIpcSubscriber<ushort, Guid, object>("CustomizePlus.Profile.OnUpdate");
        _customizePlusDeleteByUniqueId = dalamudPluginInterface.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");

        _customizePlusOnScaleUpdate.Subscribe(OnCustomizePlusScaleChange);
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;

        CheckAPI();
    }

    public bool APIAvailable { get; private set; } = false;

    public async Task RevertAsync(nint character)
    {
        if (!APIAvailable) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is ICharacter c)
            {
                _logger.LogTrace("CustomizePlus reverting for {chara}", c.Address.ToString("X"));
                _customizePlusRevertCharacter!.InvokeFunc(c.ObjectIndex);
            }
        }).ConfigureAwait(false);
    }

    public async Task<Guid?> SetBodyScaleAsync(nint character, string scale)
    {
        if (!APIAvailable) return null;
        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is ICharacter c)
            {
                string decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(scale));
                _logger.LogTrace("CustomizePlus applying for {chara}", c.Address.ToString("X"));
                if (scale.IsNullOrEmpty())
                {
                    _customizePlusRevertCharacter!.InvokeFunc(c.ObjectIndex);
                    return null;
                }
                else
                {
                    var result = _customizePlusSetBodyScaleToCharacter!.InvokeFunc(c.ObjectIndex, decodedScale);
                    return result.Item2;
                }
            }

            return null;
        }).ConfigureAwait(false);
    }

    public async Task RevertByIdAsync(Guid? profileId)
    {
        if (!APIAvailable || profileId == null) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            _ = _customizePlusDeleteByUniqueId.InvokeFunc(profileId.Value);
        }).ConfigureAwait(false);
    }

    public async Task<string?> GetScaleAsync(nint character)
    {
        if (!APIAvailable) return null;
        var scale = await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is ICharacter c)
            {
                var res = _customizePlusGetActiveProfile.InvokeFunc(c.ObjectIndex);
                _logger.LogTrace("CustomizePlus GetActiveProfile returned {err}", res.Item1);
                if (res.Item1 != 0 || res.Item2 == null) return string.Empty;
                return _customizePlusGetProfileById.InvokeFunc(res.Item2.Value).Item2;
            }

            return string.Empty;
        }).ConfigureAwait(false);
        if (string.IsNullOrEmpty(scale)) return string.Empty;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(scale));
    }

    public void CheckAPI()
    {
        try
        {
            var version = _customizePlusApiVersion.InvokeFunc();
            APIAvailable = (version.Item1 == 6 && version.Item2 >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    private void OnCustomizePlusScaleChange(ushort c, Guid g)
    {
        var obj = _dalamudUtil.GetCharacterFromObjectTableByIndex(c);
        _mareMediator.Publish(new CustomizePlusMessage(obj?.Address ?? null));
    }

    public void Dispose()
    {
        _customizePlusOnScaleUpdate.Unsubscribe(OnCustomizePlusScaleChange);
    }
}
