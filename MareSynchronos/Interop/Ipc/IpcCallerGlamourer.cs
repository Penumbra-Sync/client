using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerGlamourer : IIpcCaller
{
    private readonly ICallGateSubscriber<(int, int)> _glamourerApiVersions;
    private readonly ICallGateSubscriber<string, GameObject?, uint, object>? _glamourerApplyAll;
    private readonly ICallGateSubscriber<GameObject?, string>? _glamourerGetAllCustomization;
    private readonly ICallGateSubscriber<Character?, uint, object?> _glamourerRevert;
    private readonly ICallGateSubscriber<string, uint, object?> _glamourerRevertByName;
    private readonly ICallGateSubscriber<string, uint, bool> _glamourerUnlock;
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
        _glamourerApiVersions = pi.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersions");
        _glamourerGetAllCustomization = pi.GetIpcSubscriber<GameObject?, string>("Glamourer.GetAllCustomizationFromCharacter");
        _glamourerApplyAll = pi.GetIpcSubscriber<string, GameObject?, uint, object>("Glamourer.ApplyAllToCharacterLock");
        _glamourerRevert = pi.GetIpcSubscriber<Character?, uint, object?>("Glamourer.RevertCharacterLock");
        _glamourerRevertByName = pi.GetIpcSubscriber<string, uint, object?>("Glamourer.RevertLock");
        _glamourerUnlock = pi.GetIpcSubscriber<string, uint, bool>("Glamourer.UnlockName");

        pi.GetIpcSubscriber<int, nint, Lazy<string>, object?>("Glamourer.StateChanged").Subscribe((type, address, customize) => GlamourerChanged(address));
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
            var version = _glamourerApiVersions.InvokeFunc();
            bool versionValid = (_pi.InstalledPlugins
                .FirstOrDefault(p => string.Equals(p.InternalName, "Glamourer", StringComparison.OrdinalIgnoreCase))
                ?.Version ?? new Version(0, 0, 0, 0)) >= new Version(1, 0, 6, 1);
            if (version.Item1 == 0 && version.Item2 >= 1 && versionValid)
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
        _pi.GetIpcSubscriber<int, nint, Lazy<string>, object?>("Glamourer.StateChanged").Unsubscribe((type, address, customize) => GlamourerChanged(address));
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
                    _glamourerApplyAll!.InvokeAction(customization, chara, LockCode);
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
                    return _glamourerGetAllCustomization!.InvokeFunc(c);
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
                    _glamourerUnlock.InvokeFunc(name, LockCode);
                    logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevert", applicationId);
                    _glamourerRevert.InvokeAction(chara, LockCode);
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
                _glamourerRevertByName.InvokeAction(name, LockCode);
                logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
                _glamourerUnlock.InvokeFunc(name, LockCode);
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
            _glamourerRevertByName.InvokeAction(name, LockCode);
            logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
            _glamourerUnlock.InvokeFunc(name, LockCode);
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
