﻿using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Plugin;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerGlamourer : IIpcCaller
{
    private readonly ApiVersion _glamourerApiVersions;
    private readonly ApplyState? _glamourerApplyAll;
    private readonly GetStateBase64? _glamourerGetAllCustomization;
    private readonly RevertState _glamourerRevert;
    private readonly RevertStateName _glamourerRevertByName;
    private readonly UnlockState _glamourerUnlock;
    private readonly UnlockStateName _glamourerUnlockByName;
    private readonly EventSubscriber<nint> _glamourerStateChanged;
    private readonly ILogger<IpcCallerGlamourer> _logger;
    private readonly DalamudPluginInterface _pi;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;
    private readonly RedrawManager _redrawManager;
    private bool _shownGlamourerUnavailable = false;
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

        _glamourerStateChanged = StateChanged.Subscriber(pi, GlamourerChanged);
        _glamourerStateChanged.Enable();
        _logger = logger;
        _pi = pi;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;
        _redrawManager = redrawManager;
        CheckAPI();
    }

    public bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        bool apiAvailable = false;
        try
        {
            var version = _glamourerApiVersions.Invoke();
            bool versionValid = (_pi.InstalledPlugins
                .FirstOrDefault(p => string.Equals(p.InternalName, "Glamourer", StringComparison.OrdinalIgnoreCase))
                ?.Version ?? new Version(0, 0, 0, 0)) >= new Version(1, 2, 1, 4);
            if (version is { Major: 1, Minor: >= 1 } && versionValid)
            {
                apiAvailable = true;
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
        _glamourerStateChanged.Dispose();
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
                    _glamourerApplyAll!.Invoke(customization, chara.ObjectIndex, LockCode);
                }
                catch (Exception)
                {
                    logger.LogWarning("[{appid}] Failed to apply Glamourer data", applicationId);
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
                    logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                    _glamourerUnlock.Invoke(chara.ObjectIndex, LockCode);
                    logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevert", applicationId);
                    _glamourerRevert.Invoke(chara.ObjectIndex, LockCode);
                    logger.LogDebug("[{appid}] Calling On IPC: PenumbraRedraw", applicationId);
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
            try
            {
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevertByName", applicationId);
                _glamourerRevertByName.Invoke(name, LockCode);
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                _glamourerUnlockByName.Invoke(name, LockCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during Glamourer RevertByName");
            }
        }).ConfigureAwait(false);
    }

    public void RevertByName(ILogger logger, string name, Guid applicationId)
    {
        if ((!APIAvailable) || _dalamudUtil.IsZoning) return;
        try
        {
            logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevertByName", applicationId);
            _glamourerRevertByName.Invoke(name, LockCode);
            logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
            _glamourerUnlockByName.Invoke(name, LockCode);
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
