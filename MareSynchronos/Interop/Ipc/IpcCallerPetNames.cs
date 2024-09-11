using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerPetNames : IIpcCaller
{
    private readonly ILogger<IpcCallerPetNames> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;

    private readonly ICallGateSubscriber<object> _petnamesReady;
    private readonly ICallGateSubscriber<object> _petnamesDisposing;
    private readonly ICallGateSubscriber<(uint, uint)> _apiVersion;
    private readonly ICallGateSubscriber<bool> _enabled;

    private readonly ICallGateSubscriber<string, object> _playerDataChanged;
    private readonly ICallGateSubscriber<string> _getPlayerData;
    private readonly ICallGateSubscriber<string, object> _setPlayerData;
    private readonly ICallGateSubscriber<ushort, object> _clearPlayerData;

    public IpcCallerPetNames(ILogger<IpcCallerPetNames> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil,
        MareMediator mareMediator)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;

        _petnamesReady = pi.GetIpcSubscriber<object>("PetRenamer.Ready");
        _petnamesDisposing = pi.GetIpcSubscriber<object>("PetRenamer.Disposing");
        _apiVersion = pi.GetIpcSubscriber<(uint, uint)>("PetRenamer.ApiVersion");
        _enabled = pi.GetIpcSubscriber<bool>("PetRenamer.Enabled");

        _playerDataChanged = pi.GetIpcSubscriber<string, object>("PetRenamer.PlayerDataChanged");
        _getPlayerData = pi.GetIpcSubscriber<string>("PetRenamer.GetPlayerData");
        _setPlayerData = pi.GetIpcSubscriber<string, object>("PetRenamer.SetPlayerData");
        _clearPlayerData = pi.GetIpcSubscriber<ushort, object>("PetRenamer.ClearPlayerData");

        _petnamesReady.Subscribe(OnPetNicknamesReady);
        _petnamesDisposing.Subscribe(OnPetNicknamesDispose);
        _playerDataChanged.Subscribe(OnLocalPetNicknamesDataChange);

        CheckAPI();
    }

    public bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            APIAvailable = _enabled?.InvokeFunc() ?? false;
            if (APIAvailable)
            {
                APIAvailable = _apiVersion?.InvokeFunc() is { Item1: 3, Item2: >= 1 };
            }
        }
        catch
        {
            APIAvailable = false;
        }
    }

    private void OnPetNicknamesReady()
    {
        CheckAPI();
        _mareMediator.Publish(new PetNamesReadyMessage());
    }

    private void OnPetNicknamesDispose()
    {
        _mareMediator.Publish(new PetNamesMessage(string.Empty));
    }

    public string GetLocalNames()
    {
        if (!APIAvailable) return string.Empty;

        try
        {
            string localNameData = _getPlayerData.InvokeFunc();
            return string.IsNullOrEmpty(localNameData) ? string.Empty : localNameData;
        } 
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not obtain Pet Nicknames data");
        }

        return string.Empty;
    }

    public async Task SetPlayerData(nint character, string playerData)
    {
        if (!APIAvailable) return;

        _logger.LogTrace("Applying Pet Nicknames data to {chara}", character.ToString("X"));

        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() =>
            {
                if (string.IsNullOrEmpty(playerData))
                {
                    var gameObj = _dalamudUtil.CreateGameObject(character);
                    if (gameObj is IPlayerCharacter pc)
                    {
                        _clearPlayerData.InvokeAction(pc.ObjectIndex);
                    }
                }
                else
                {
                    _setPlayerData.InvokeAction(playerData);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not apply Pet Nicknames data");
        }
    }

    public async Task ClearPlayerData(nint characterPointer)
    {
        if (!APIAvailable) return;
        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() =>
            {
                var gameObj = _dalamudUtil.CreateGameObject(characterPointer);
                if (gameObj is IPlayerCharacter pc)
                {
                    _logger.LogTrace("Pet Nicknames removing for {addr}", pc.Address.ToString("X"));
                    _clearPlayerData.InvokeAction(pc.ObjectIndex);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not clear Pet Nicknames data");
        }
    }

    private void OnLocalPetNicknamesDataChange(string data)
    {
        _mareMediator.Publish(new PetNamesMessage(data));
    }

    public void Dispose()
    {
        _petnamesReady.Unsubscribe(OnPetNicknamesReady);
        _petnamesDisposing.Unsubscribe(OnPetNicknamesDispose);
        _playerDataChanged.Unsubscribe(OnLocalPetNicknamesDataChange);
    }
}
