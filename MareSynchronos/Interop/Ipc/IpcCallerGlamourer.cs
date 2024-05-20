using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerGlamourer : IIpcCaller
{
    private readonly ILogger<IpcCallerGlamourer> _logger;
    private readonly DalamudPluginInterface _pi;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;
    private readonly RedrawManager _redrawManager;

    private readonly ApiVersion _glamourerApiVersions;
    private readonly ApplyState? _glamourerApplyAll;
    private readonly GetStateBase64? _glamourerGetAllCustomization;
    private readonly RevertState _glamourerRevert;
    private readonly RevertStateName _glamourerRevertByName;
    private readonly UnlockState _glamourerUnlock;
    private readonly UnlockStateName _glamourerUnlockByName;
    private readonly EventSubscriber<nint>? _glamourerStateChanged;

    private readonly Glamourer.Api.IpcSubscribers.Legacy.ApiVersions _glamourerApiVersionLegacy;
    private readonly ICallGateSubscriber<string, GameObject?, uint, object>? _glamourerApplyAllLegacy;
    private readonly ICallGateSubscriber<GameObject?, string>? _glamourerGetAllCustomizationLegacy;
    private readonly ICallGateSubscriber<Character?, uint, object?> _glamourerRevertLegacy;
    private readonly Glamourer.Api.IpcSubscribers.Legacy.RevertLock _glamourerRevertByNameLegacy;
    private readonly Glamourer.Api.IpcSubscribers.Legacy.UnlockName _glamourerUnlockLegacy;
    private readonly EventSubscriber<int, nint, Lazy<string>>? _glamourerStateChangedLegacy;

    private bool _shownGlamourerUnavailable = false;
    private bool _useLegacyGlamourer = false;
    private readonly uint LockCode = 0x6D617265;

    public IpcCallerGlamourer(ILogger<IpcCallerGlamourer> logger, DalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mareMediator,
        RedrawManager redrawManager)
    {
        _glamourerApiVersions = new ApiVersion(pi);
        _glamourerGetAllCustomization = new GetStateBase64(pi);
        _glamourerApplyAll = new ApplyState(pi);
        _glamourerRevert = new RevertState(pi);
        _glamourerRevertByName = new RevertStateName(pi);
        _glamourerUnlock = new UnlockState(pi);
        _glamourerUnlockByName = new UnlockStateName(pi);

        _glamourerApiVersionLegacy = new(pi);
        _glamourerApplyAllLegacy = pi.GetIpcSubscriber<string, GameObject?, uint, object>("Glamourer.ApplyAllToCharacterLock");
        _glamourerGetAllCustomizationLegacy = pi.GetIpcSubscriber<GameObject?, string>("Glamourer.GetAllCustomizationFromCharacter");
        _glamourerRevertLegacy = pi.GetIpcSubscriber<Character?, uint, object?>("Glamourer.RevertCharacterLock");
        _glamourerRevertByNameLegacy = new(pi);
        _glamourerUnlockLegacy = new(pi);

        _logger = logger;
        _pi = pi;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;
        _redrawManager = redrawManager;
        CheckAPI();

        if (_useLegacyGlamourer)
        {
            _glamourerStateChangedLegacy = Glamourer.Api.IpcSubscribers.Legacy.StateChanged.Subscriber(pi, (t, a, c) => GlamourerChanged(a));
            _glamourerStateChangedLegacy.Enable();
        }
        else
        {
            _glamourerStateChanged = StateChanged.Subscriber(pi, GlamourerChanged);
            _glamourerStateChanged.Enable();
        }
    }

    public bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        bool apiAvailable = false;
        try
        {
            bool versionValid = (_pi.InstalledPlugins
                .FirstOrDefault(p => string.Equals(p.InternalName, "Glamourer", StringComparison.OrdinalIgnoreCase))
                ?.Version ?? new Version(0, 0, 0, 0)) >= new Version(1, 0, 6, 1);
            try
            {
                var version = _glamourerApiVersions.Invoke();
                if (version is { Major: 1, Minor: >= 1 } && versionValid)
                {
                    apiAvailable = true;
                }
            }
            catch
            {
                var version = _glamourerApiVersionLegacy.Invoke();
                if (version is { Major: 0, Minor: >= 1 } && versionValid)
                {
                    apiAvailable = true;
                    _useLegacyGlamourer = true;
                }
            }
            _shownGlamourerUnavailable = _shownGlamourerUnavailable && !apiAvailable;

            APIAvailable = apiAvailable;
        }
        catch
        {
            APIAvailable = apiAvailable;
        }
        finally
        {
            if (!apiAvailable && !_shownGlamourerUnavailable)
            {
                _shownGlamourerUnavailable = true;
                _mareMediator.Publish(new NotificationMessage("Glamourer inactive", "Your Glamourer installation is not active or out of date. Update Glamourer to continue to use Mare. If you just updated Glamourer, ignore this message.",
                    NotificationType.Error));
            }
        }
    }

    public void Dispose()
    {
        _glamourerStateChanged?.Dispose();
        _glamourerStateChangedLegacy?.Dispose();
    }

    public async Task ApplyAllAsync(ILogger logger, GameObjectHandler handler, string? customization, Guid applicationId, CancellationToken token, bool fireAndForget = false)
    {
        if (!APIAvailable || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning) return;

        await _redrawManager.RedrawSemaphore.WaitAsync(token).ConfigureAwait(false);

        try
        {

            await _redrawManager.PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                try
                {
                    logger.LogDebug("[{appid}] Calling on IPC: GlamourerApplyAll", applicationId);
                    if (_useLegacyGlamourer)
                    {
                        _glamourerApplyAllLegacy.InvokeAction(customization, chara, LockCode);
                    }
                    else
                    {
                        _glamourerApplyAll!.Invoke(customization, chara.ObjectIndex, LockCode);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{appid}] Failed to apply Glamourer data", applicationId);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _redrawManager.RedrawSemaphore.Release();
        }
    }

    public async Task<string> GetCharacterCustomizationAsync(IntPtr character)
    {
        if (!APIAvailable) return string.Empty;
        try
        {
            return await _dalamudUtil.RunOnFrameworkThread(() =>
            {
                var gameObj = _dalamudUtil.CreateGameObject(character);
                if (gameObj is Character c)
                {
                    if (_useLegacyGlamourer)
                        return _glamourerGetAllCustomizationLegacy.InvokeFunc(c) ?? string.Empty;
                    else
                        return _glamourerGetAllCustomization!.Invoke(c.ObjectIndex).Item2 ?? string.Empty;
                }
                return string.Empty;
            }).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task RevertAsync(ILogger logger, string name, GameObjectHandler handler, Guid applicationId, CancellationToken token)
    {
        if ((!APIAvailable) || _dalamudUtil.IsZoning) return;
        try
        {
            await _redrawManager.RedrawSemaphore.WaitAsync(token).ConfigureAwait(false);
            await _redrawManager.PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                try
                {
                    if (_useLegacyGlamourer)
                    {
                        logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                        _glamourerUnlockLegacy.Invoke(name, LockCode);
                        logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevert", applicationId);
                        _glamourerRevertLegacy.InvokeAction(chara, LockCode);
                        logger.LogDebug("[{appid}] Calling On IPC: PenumbraRedraw", applicationId);
                    }
                    else
                    {
                        logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                        _glamourerUnlock.Invoke(chara.ObjectIndex, LockCode);
                        logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevert", applicationId);
                        _glamourerRevert.Invoke(chara.ObjectIndex, LockCode);
                        logger.LogDebug("[{appid}] Calling On IPC: PenumbraRedraw", applicationId);
                    }

                    _mareMediator.Publish(new PenumbraRedrawCharacterMessage(chara));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{appid}] Error during GlamourerRevert", applicationId);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _redrawManager.RedrawSemaphore.Release();
        }
    }

    public async Task RevertByNameAsync(ILogger logger, string name, Guid applicationId)
    {
        if ((!APIAvailable) || _dalamudUtil.IsZoning) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            RevertByName(logger, name, applicationId);

        }).ConfigureAwait(false);
    }

    public void RevertByName(ILogger logger, string name, Guid applicationId)
    {
        if ((!APIAvailable) || _dalamudUtil.IsZoning) return;

        try
        {
            if (_useLegacyGlamourer)
            {
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevertByName", applicationId);
                _glamourerRevertByNameLegacy.Invoke(name, LockCode);
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                _glamourerUnlockLegacy.Invoke(name, LockCode);
            }
            else
            {
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevertByName", applicationId);
                _glamourerRevertByName.Invoke(name, LockCode);
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                _glamourerUnlockByName.Invoke(name, LockCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Glamourer RevertByName");
        }
    }

    private void GlamourerChanged(nint address)
    {
        _mareMediator.Publish(new GlamourerChangedMessage(address));
    }

}
