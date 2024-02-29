using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Plugin;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using System.Collections.Concurrent;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerPenumbra : DisposableMediatorSubscriberBase, IIpcCaller
{
    private readonly DalamudPluginInterface _pi;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;
    private readonly RedrawManager _redrawManager;
    private bool _shownPenumbraUnavailable = false;
    private string? _penumbraModDirectory;
    public string? ModDirectory
    {
        get => _penumbraModDirectory;
        private set
        {
            if (!string.Equals(_penumbraModDirectory, value, StringComparison.Ordinal))
            {
                _penumbraModDirectory = value;
                _mareMediator.Publish(new PenumbraDirectoryChangedMessage(_penumbraModDirectory));
            }
        }
    }

    private readonly FuncSubscriber<string, string, Dictionary<string, string>, string, int, PenumbraApiEc> _penumbraAddTemporaryMod;
    private readonly FuncSubscriber<string, int, bool, PenumbraApiEc> _penumbraAssignTemporaryCollection;
    private readonly FuncSubscriber<string, string, TextureType, bool, Task> _penumbraConvertTextureFile;
    private readonly FuncSubscriber<string, PenumbraApiEc> _penumbraCreateNamedTemporaryCollection;
    private readonly EventSubscriber _penumbraDispose;
    private readonly FuncSubscriber<bool> _penumbraEnabled;
    private readonly EventSubscriber<nint, string, string> _penumbraGameObjectResourcePathResolved;
    private readonly FuncSubscriber<string> _penumbraGetMetaManipulations;
    private readonly EventSubscriber _penumbraInit;
    private readonly EventSubscriber<ModSettingChange, string, string, bool> _penumbraModSettingChanged;
    private readonly EventSubscriber<nint, int> _penumbraObjectIsRedrawn;
    private readonly ActionSubscriber<string, RedrawType> _penumbraRedraw;
    private readonly ActionSubscriber<GameObject, RedrawType> _penumbraRedrawObject;
    private readonly ConcurrentDictionary<IntPtr, bool> _penumbraRedrawRequests = new();
    private readonly FuncSubscriber<string, PenumbraApiEc> _penumbraRemoveTemporaryCollection;
    private readonly FuncSubscriber<string, string, int, PenumbraApiEc> _penumbraRemoveTemporaryMod;
    private readonly FuncSubscriber<string> _penumbraResolveModDir;
    private readonly FuncSubscriber<string[], string[], Task<(string[], string[][])>> _penumbraResolvePaths;
    private readonly ParamsFuncSubscriber<ushort, IReadOnlyDictionary<string, string[]>?[]> _penumbraResourcePaths;

    public IpcCallerPenumbra(ILogger<IpcCallerPenumbra> logger, DalamudPluginInterface pi, DalamudUtilService dalamudUtil,
        MareMediator mareMediator, RedrawManager redrawManager) : base(logger, mareMediator)
    {
        _pi = pi;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;
        _redrawManager = redrawManager;
        _penumbraInit = Penumbra.Api.Ipc.Initialized.Subscriber(pi, PenumbraInit);
        _penumbraDispose = Penumbra.Api.Ipc.Disposed.Subscriber(pi, PenumbraDispose);
        _penumbraResolveModDir = Penumbra.Api.Ipc.GetModDirectory.Subscriber(pi);
        _penumbraRedraw = Penumbra.Api.Ipc.RedrawObjectByName.Subscriber(pi);
        _penumbraRedrawObject = Penumbra.Api.Ipc.RedrawObject.Subscriber(pi);
        _penumbraObjectIsRedrawn = Penumbra.Api.Ipc.GameObjectRedrawn.Subscriber(pi, RedrawEvent);
        _penumbraGetMetaManipulations = Penumbra.Api.Ipc.GetPlayerMetaManipulations.Subscriber(pi);
        _penumbraRemoveTemporaryMod = Penumbra.Api.Ipc.RemoveTemporaryMod.Subscriber(pi);
        _penumbraAddTemporaryMod = Penumbra.Api.Ipc.AddTemporaryMod.Subscriber(pi);
        _penumbraCreateNamedTemporaryCollection = Penumbra.Api.Ipc.CreateNamedTemporaryCollection.Subscriber(pi);
        _penumbraRemoveTemporaryCollection = Penumbra.Api.Ipc.RemoveTemporaryCollectionByName.Subscriber(pi);
        _penumbraAssignTemporaryCollection = Penumbra.Api.Ipc.AssignTemporaryCollection.Subscriber(pi);
        _penumbraResolvePaths = Penumbra.Api.Ipc.ResolvePlayerPathsAsync.Subscriber(pi);
        _penumbraEnabled = Penumbra.Api.Ipc.GetEnabledState.Subscriber(pi);
        _penumbraModSettingChanged = Penumbra.Api.Ipc.ModSettingChanged.Subscriber(pi, (change, arg1, arg, b) =>
        {
            if (change == ModSettingChange.EnableState)
                _mareMediator.Publish(new PenumbraModSettingChangedMessage());
        });
        _penumbraConvertTextureFile = Penumbra.Api.Ipc.ConvertTextureFile.Subscriber(pi);
        _penumbraResourcePaths = Penumbra.Api.Ipc.GetGameObjectResourcePaths.Subscriber(pi);

        _penumbraGameObjectResourcePathResolved = Penumbra.Api.Ipc.GameObjectResourcePathResolved.Subscriber(pi, ResourceLoaded);

        CheckAPI();
        CheckModDirectory();

        Mediator.Subscribe<PenumbraRedrawCharacterMessage>(this, (msg) => _penumbraRedrawObject.Invoke(msg.Character, RedrawType.AfterGPose));
    }

    public bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        bool penumbraAvailable = false;
        try
        {
            penumbraAvailable = (_pi.InstalledPlugins
                .FirstOrDefault(p => string.Equals(p.InternalName, "Penumbra", StringComparison.OrdinalIgnoreCase))
                ?.Version ?? new Version(0, 0, 0, 0)) >= new Version(0, 8, 1, 6);
            penumbraAvailable &= _penumbraEnabled.Invoke();
            _shownPenumbraUnavailable = _shownPenumbraUnavailable && !penumbraAvailable;
            APIAvailable = penumbraAvailable;
        }
        catch
        {
            APIAvailable = penumbraAvailable;
        }
        finally
        {
            if (!penumbraAvailable && !_shownPenumbraUnavailable)
            {
                _shownPenumbraUnavailable = true;
                _mareMediator.Publish(new NotificationMessage("Penumbra inactive",
                    "Your Penumbra installation is not active or out of date. Update Penumbra and/or the Enable Mods setting in Penumbra to continue to use Mare. If you just updated Penumbra, ignore this message.",
                    NotificationType.Error));
            }
        }
    }

    public void CheckModDirectory()
    {
        if (!APIAvailable)
        {
            ModDirectory = string.Empty;
        }
        else
        {
            ModDirectory = _penumbraResolveModDir!.Invoke().ToLowerInvariant();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _redrawManager.Cancel();

        _penumbraModSettingChanged.Dispose();
        _penumbraGameObjectResourcePathResolved.Dispose();
        _penumbraDispose.Dispose();
        _penumbraInit.Dispose();
        _penumbraObjectIsRedrawn.Dispose();
    }

    public async Task AssignTemporaryCollectionAsync(ILogger logger, string collName, int idx)
    {
        if (!APIAvailable) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var retAssign = _penumbraAssignTemporaryCollection.Invoke(collName, idx, c: true);
            logger.LogTrace("Assigning Temp Collection {collName} to index {idx}, Success: {ret}", collName, idx, retAssign);
            return collName;
        }).ConfigureAwait(false);
    }

    public async Task ConvertTextureFiles(ILogger logger, Dictionary<string, string[]> textures, IProgress<(string, int)> progress, CancellationToken token)
    {
        if (!APIAvailable) return;

        _mareMediator.Publish(new HaltScanMessage(nameof(ConvertTextureFiles)));
        int currentTexture = 0;
        foreach (var texture in textures)
        {
            if (token.IsCancellationRequested) break;

            progress.Report((texture.Key, ++currentTexture));

            logger.LogInformation("Converting Texture {path} to {type}", texture.Key, TextureType.Bc7Tex);
            var convertTask = _penumbraConvertTextureFile.Invoke(texture.Key, texture.Key, TextureType.Bc7Tex, d: true);
            await convertTask.ConfigureAwait(false);
            if (convertTask.IsCompletedSuccessfully && texture.Value.Any())
            {
                foreach (var duplicatedTexture in texture.Value)
                {
                    logger.LogInformation("Migrating duplicate {dup}", duplicatedTexture);
                    try
                    {
                        File.Copy(texture.Key, duplicatedTexture, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to copy duplicate {dup}", duplicatedTexture);
                    }
                }
            }
        }
        _mareMediator.Publish(new ResumeScanMessage(nameof(ConvertTextureFiles)));

        await _dalamudUtil.RunOnFrameworkThread(async () =>
        {
            var gameObject = await _dalamudUtil.CreateGameObjectAsync(await _dalamudUtil.GetPlayerPointerAsync().ConfigureAwait(false)).ConfigureAwait(false);
            _penumbraRedrawObject.Invoke(gameObject!, RedrawType.Redraw);
        }).ConfigureAwait(false);
    }

    public async Task<string> CreateTemporaryCollectionAsync(ILogger logger, string uid)
    {
        if (!APIAvailable) return string.Empty;

        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var collName = "Mare_" + uid;
            var retCreate = _penumbraCreateNamedTemporaryCollection.Invoke(collName);
            logger.LogTrace("Creating Temp Collection {collName}, Success: {ret}", collName, retCreate);
            return collName;
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string[]>?[]?> GetCharacterData(ILogger logger, GameObjectHandler handler)
    {
        if (!APIAvailable) return null;

        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("Calling On IPC: Penumbra.GetGameObjectResourcePaths");
            var idx = handler.GetGameObject()?.ObjectIndex;
            if (idx == null) return null;
            return _penumbraResourcePaths.Invoke(idx.Value);
        }).ConfigureAwait(false);
    }

    public string GetMetaManipulations()
    {
        if (!APIAvailable) return string.Empty;
        return _penumbraGetMetaManipulations.Invoke();
    }

    public async Task RedrawAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, CancellationToken token)
    {
        if (!APIAvailable || _dalamudUtil.IsZoning) return;
        try
        {
            await _redrawManager.RedrawSemaphore.WaitAsync(token).ConfigureAwait(false);
            await _redrawManager.PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                logger.LogDebug("[{appid}] Calling on IPC: PenumbraRedraw", applicationId);
                _penumbraRedrawObject!.Invoke(chara, RedrawType.Redraw);
            }).ConfigureAwait(false);
        }
        finally
        {
            _redrawManager.RedrawSemaphore.Release();
        }
    }

    public async Task RemoveTemporaryCollectionAsync(ILogger logger, Guid applicationId, string collName)
    {
        if (!APIAvailable) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("[{applicationId}] Removing temp collection for {collName}", applicationId, collName);
            var ret2 = _penumbraRemoveTemporaryCollection.Invoke(collName);
            logger.LogTrace("[{applicationId}] RemoveTemporaryCollection: {ret2}", applicationId, ret2);
        }).ConfigureAwait(false);
    }

    public async Task<(string[] forward, string[][] reverse)> ResolvePathsAsync(string[] forward, string[] reverse)
    {
        return await _penumbraResolvePaths.Invoke(forward, reverse).ConfigureAwait(false);
    }

    public async Task SetManipulationDataAsync(ILogger logger, Guid applicationId, string collName, string manipulationData)
    {
        if (!APIAvailable) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("[{applicationId}] Manip: {data}", applicationId, manipulationData);
            var retAdd = _penumbraAddTemporaryMod.Invoke("MareChara_Meta", collName, [], manipulationData, 0);
            logger.LogTrace("[{applicationId}] Setting temp meta mod for {collName}, Success: {ret}", applicationId, collName, retAdd);
        }).ConfigureAwait(false);
    }

    public async Task SetTemporaryModsAsync(ILogger logger, Guid applicationId, string collName, Dictionary<string, string> modPaths)
    {
        if (!APIAvailable) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            foreach (var mod in modPaths)
            {
                logger.LogTrace("[{applicationId}] Change: {from} => {to}", applicationId, mod.Key, mod.Value);
            }
            var retRemove = _penumbraRemoveTemporaryMod.Invoke("MareChara_Files", collName, 0);
            logger.LogTrace("[{applicationId}] Removing temp files mod for {collName}, Success: {ret}", applicationId, collName, retRemove);
            var retAdd = _penumbraAddTemporaryMod.Invoke("MareChara_Files", collName, modPaths, string.Empty, 0);
            logger.LogTrace("[{applicationId}] Setting temp files mod for {collName}, Success: {ret}", applicationId, collName, retAdd);
        }).ConfigureAwait(false);
    }

    private void RedrawEvent(IntPtr objectAddress, int objectTableIndex)
    {
        bool wasRequested = false;
        if (_penumbraRedrawRequests.TryGetValue(objectAddress, out var redrawRequest) && redrawRequest)
        {
            _penumbraRedrawRequests[objectAddress] = false;
        }
        else
        {
            _mareMediator.Publish(new PenumbraRedrawMessage(objectAddress, objectTableIndex, wasRequested));
        }
    }

    private void ResourceLoaded(IntPtr ptr, string arg1, string arg2)
    {
        if (ptr != IntPtr.Zero && string.Compare(arg1, arg2, ignoreCase: true, System.Globalization.CultureInfo.InvariantCulture) != 0)
        {
            _mareMediator.Publish(new PenumbraResourceLoadMessage(ptr, arg1, arg2));
        }
    }

    private void PenumbraDispose()
    {
        _redrawManager.Cancel();
        _mareMediator.Publish(new PenumbraDisposedMessage());
    }

    private void PenumbraInit()
    {
        APIAvailable = true;
        ModDirectory = _penumbraResolveModDir.Invoke();
        _mareMediator.Publish(new PenumbraInitializedMessage());
        _penumbraRedraw!.Invoke("self", RedrawType.Redraw);
    }
}
