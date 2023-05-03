using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Action = System.Action;
using System.Collections.Concurrent;
using System.Text;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Dalamud.Interface.Internal.Notifications;
using Microsoft.Extensions.Logging;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services;

namespace MareSynchronos.Interop;

public sealed class IpcManager : DisposableMediatorSubscriberBase
{
    private readonly ICallGateSubscriber<string> _customizePlusApiVersion;
    private readonly ICallGateSubscriber<string, string> _customizePlusGetBodyScale;
    private readonly ICallGateSubscriber<string?, object> _customizePlusOnScaleUpdate;
    private readonly ICallGateSubscriber<Character?, object> _customizePlusRevert;
    private readonly ICallGateSubscriber<string, Character?, object> _customizePlusSetBodyScaleToCharacter;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ICallGateSubscriber<int> _glamourerApiVersion;
    private readonly ICallGateSubscriber<string, GameObject?, object>? _glamourerApplyAll;
    private readonly ICallGateSubscriber<string, GameObject?, object>? _glamourerApplyOnlyCustomization;
    private readonly ICallGateSubscriber<string, GameObject?, object>? _glamourerApplyOnlyEquipment;
    private readonly ICallGateSubscriber<GameObject?, string>? _glamourerGetAllCustomization;
    private readonly ConcurrentQueue<Action> _gposeActionQueue = new();
    private readonly ICallGateSubscriber<string> _heelsGetApiVersion;
    private readonly ICallGateSubscriber<float> _heelsGetOffset;
    private readonly ICallGateSubscriber<float, object?> _heelsOffsetUpdate;
    private readonly ICallGateSubscriber<GameObject, float, object?> _heelsRegisterPlayer;
    private readonly ICallGateSubscriber<GameObject, object?> _heelsUnregisterPlayer;
    private readonly ICallGateSubscriber<(uint major, uint minor)> _honorificApiVersion;
    private readonly ICallGateSubscriber<Character, object> _honorificClearCharacterTitle;
    private readonly ICallGateSubscriber<(string Title, bool IsPrefix)> _honorificGetLocalCharacterTitle;
    private readonly ICallGateSubscriber<string, bool, object> _honorificLocalCharacterTitleChanged;
    private readonly ICallGateSubscriber<Character, string, bool, object> _honorificSetCharacterTitle;
    private readonly ConcurrentQueue<Action> _normalQueue = new();
    private readonly ICallGateSubscriber<string> _palettePlusApiVersion;
    private readonly ICallGateSubscriber<Character, string> _palettePlusBuildCharaPalette;
    private readonly ICallGateSubscriber<Character, string, object> _palettePlusPaletteChanged;
    private readonly ICallGateSubscriber<Character, object> _palettePlusRemoveCharaPalette;
    private readonly ICallGateSubscriber<Character, string, object> _palettePlusSetCharaPalette;
    private readonly FuncSubscriber<string, string, Dictionary<string, string>, string, int, PenumbraApiEc> _penumbraAddTemporaryMod;
    private readonly FuncSubscriber<(int, int)> _penumbraApiVersion;
    private readonly FuncSubscriber<string, int, bool, PenumbraApiEc> _penumbraAssignTemporaryCollection;
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
    private readonly FuncSubscriber<string[], string[], (string[], string[][])> _penumbraResolvePaths;
    private readonly SemaphoreSlim _redrawSemaphore = new(2);
    private bool _customizePlusAvailable = false;
    private CancellationTokenSource _disposalCts = new();
    private bool _glamourerAvailable = false;
    private bool _heelsAvailable = false;
    private bool _honorificAvailable = false;
    private bool _inGposeQueueMode = false;
    private bool _palettePlusAvailable = false;
    private bool _penumbraAvailable = false;
    private bool _shownGlamourerUnavailable = false;
    private bool _shownPenumbraUnavailable = false;

    public IpcManager(ILogger<IpcManager> logger, DalamudPluginInterface pi, DalamudUtilService dalamudUtil, MareMediator mediator) : base(logger, mediator)
    {
        _dalamudUtil = dalamudUtil;

        _penumbraInit = Penumbra.Api.Ipc.Initialized.Subscriber(pi, PenumbraInit);
        _penumbraDispose = Penumbra.Api.Ipc.Disposed.Subscriber(pi, PenumbraDispose);
        _penumbraResolveModDir = Penumbra.Api.Ipc.GetModDirectory.Subscriber(pi);
        _penumbraRedraw = Penumbra.Api.Ipc.RedrawObjectByName.Subscriber(pi);
        _penumbraRedrawObject = Penumbra.Api.Ipc.RedrawObject.Subscriber(pi);
        _penumbraApiVersion = Penumbra.Api.Ipc.ApiVersions.Subscriber(pi);
        _penumbraObjectIsRedrawn = Penumbra.Api.Ipc.GameObjectRedrawn.Subscriber(pi, RedrawEvent);
        _penumbraGetMetaManipulations = Penumbra.Api.Ipc.GetPlayerMetaManipulations.Subscriber(pi);
        _penumbraAddTemporaryMod = Penumbra.Api.Ipc.AddTemporaryMod.Subscriber(pi);
        _penumbraCreateNamedTemporaryCollection = Penumbra.Api.Ipc.CreateNamedTemporaryCollection.Subscriber(pi);
        _penumbraRemoveTemporaryCollection = Penumbra.Api.Ipc.RemoveTemporaryCollectionByName.Subscriber(pi);
        _penumbraRemoveTemporaryMod = Penumbra.Api.Ipc.RemoveTemporaryMod.Subscriber(pi);
        _penumbraAssignTemporaryCollection = Penumbra.Api.Ipc.AssignTemporaryCollection.Subscriber(pi);
        _penumbraResolvePaths = Penumbra.Api.Ipc.ResolvePlayerPaths.Subscriber(pi);
        _penumbraEnabled = Penumbra.Api.Ipc.GetEnabledState.Subscriber(pi);
        _penumbraModSettingChanged = Penumbra.Api.Ipc.ModSettingChanged.Subscriber(pi, (change, arg1, arg, b) =>
        {
            if (change == ModSettingChange.EnableState)
                Mediator.Publish(new PenumbraModSettingChangedMessage());
        });

        _penumbraGameObjectResourcePathResolved = Penumbra.Api.Ipc.GameObjectResourcePathResolved.Subscriber(pi, ResourceLoaded);

        _glamourerApiVersion = pi.GetIpcSubscriber<int>("Glamourer.ApiVersion");
        _glamourerGetAllCustomization = pi.GetIpcSubscriber<GameObject?, string>("Glamourer.GetAllCustomizationFromCharacter");
        _glamourerApplyAll = pi.GetIpcSubscriber<string, GameObject?, object>("Glamourer.ApplyAllToCharacter");
        _glamourerApplyOnlyCustomization = pi.GetIpcSubscriber<string, GameObject?, object>("Glamourer.ApplyOnlyCustomizationToCharacter");
        _glamourerApplyOnlyEquipment = pi.GetIpcSubscriber<string, GameObject?, object>("Glamourer.ApplyOnlyEquipmentToCharacter");

        _heelsGetApiVersion = pi.GetIpcSubscriber<string>("HeelsPlugin.ApiVersion");
        _heelsGetOffset = pi.GetIpcSubscriber<float>("HeelsPlugin.GetOffset");
        _heelsRegisterPlayer = pi.GetIpcSubscriber<GameObject, float, object?>("HeelsPlugin.RegisterPlayer");
        _heelsUnregisterPlayer = pi.GetIpcSubscriber<GameObject, object?>("HeelsPlugin.UnregisterPlayer");
        _heelsOffsetUpdate = pi.GetIpcSubscriber<float, object?>("HeelsPlugin.OffsetChanged");

        _heelsOffsetUpdate.Subscribe(HeelsOffsetChange);

        _customizePlusApiVersion = pi.GetIpcSubscriber<string>("CustomizePlus.GetApiVersion");
        _customizePlusGetBodyScale = pi.GetIpcSubscriber<string, string>("CustomizePlus.GetBodyScale");
        _customizePlusRevert = pi.GetIpcSubscriber<Character?, object>("CustomizePlus.RevertCharacter");
        _customizePlusSetBodyScaleToCharacter = pi.GetIpcSubscriber<string, Character?, object>("CustomizePlus.SetBodyScaleToCharacter");
        _customizePlusOnScaleUpdate = pi.GetIpcSubscriber<string?, object>("CustomizePlus.OnScaleUpdate");

        _customizePlusOnScaleUpdate.Subscribe(OnCustomizePlusScaleChange);

        _palettePlusApiVersion = pi.GetIpcSubscriber<string>("PalettePlus.ApiVersion");
        _palettePlusBuildCharaPalette = pi.GetIpcSubscriber<Character, string>("PalettePlus.BuildCharaPaletteOrEmpty");
        _palettePlusSetCharaPalette = pi.GetIpcSubscriber<Character, string, object>("PalettePlus.SetCharaPalette");
        _palettePlusRemoveCharaPalette = pi.GetIpcSubscriber<Character, object>("PalettePlus.RemoveCharaPalette");
        _palettePlusPaletteChanged = pi.GetIpcSubscriber<Character, string, object>("PalettePlus.PaletteChanged");

        _palettePlusPaletteChanged.Subscribe(OnPalettePlusPaletteChange);

        _honorificApiVersion = pi.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion");
        _honorificGetLocalCharacterTitle = pi.GetIpcSubscriber<(string, bool)>("Honorific.GetLocalCharacterTitle");
        _honorificClearCharacterTitle = pi.GetIpcSubscriber<Character, object>("Honorific.ClearCharacterTitle");
        _honorificSetCharacterTitle = pi.GetIpcSubscriber<Character, string, bool, object>("Honorific.SetCharacterTitle");
        _honorificLocalCharacterTitleChanged = pi.GetIpcSubscriber<string, bool, object>("Honorific.LocalCharacterTitleChanged");

        _honorificLocalCharacterTitleChanged.Subscribe(OnHonorificLocalCharacterTitleChanged);

        if (Initialized)
        {
            Mediator.Publish(new PenumbraInitializedMessage());
        }

        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => HandleActionQueue());
        Mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, (_) => HandleGposeActionQueue());
        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (_) => ClearActionQueue());
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => PeriodicApiStateCheck());
    }

    public bool Initialized => CheckPenumbraApiInternal() && CheckGlamourerApiInternal();
    public string? PenumbraModDirectory { get; private set; }
    private ConcurrentQueue<Action> ActionQueue => _inGposeQueueMode ? _gposeActionQueue : _normalQueue;

    public bool CheckCustomizePlusApi() => _customizePlusAvailable;

    public bool CheckGlamourerApi() => _glamourerAvailable;

    public bool CheckHeelsApi() => _heelsAvailable;

    public bool CheckHonorificApi() => _honorificAvailable;

    public bool CheckPalettePlusApi() => _palettePlusAvailable;

    public bool CheckPenumbraApi() => _penumbraAvailable;

    public async Task CustomizePlusRevertAsync(IntPtr character)
    {
        if (!CheckCustomizePlusApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                Logger.LogTrace("CustomizePlus reverting for {chara}", c.Address.ToString("X"));
                _customizePlusRevert!.InvokeAction(c);
            }
        }).ConfigureAwait(false);
    }

    public async Task CustomizePlusSetBodyScaleAsync(IntPtr character, string scale)
    {
        if (!CheckCustomizePlusApi() || string.IsNullOrEmpty(scale)) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                string decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(scale));
                Logger.LogTrace("CustomizePlus applying for {chara}", c.Address.ToString("X"));
                _customizePlusSetBodyScaleToCharacter!.InvokeAction(decodedScale, c);
            }
        }).ConfigureAwait(false);
    }

    public async Task<string> GetCustomizePlusScaleAsync()
    {
        if (!CheckCustomizePlusApi()) return string.Empty;
        var scale = await _dalamudUtil.RunOnFrameworkThread(() => _customizePlusGetBodyScale.InvokeFunc(_dalamudUtil.GetPlayerName())).ConfigureAwait(false);
        if (string.IsNullOrEmpty(scale)) return string.Empty;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(scale));
    }

    public async Task<float> GetHeelsOffsetAsync()
    {
        if (!CheckHeelsApi()) return 0.0f;
        return await _dalamudUtil.RunOnFrameworkThread(_heelsGetOffset.InvokeFunc).ConfigureAwait(false);
    }

    public async Task GlamourerApplyAllAsync(ILogger logger, GameObjectHandler handler, string? customization, Guid applicationId, CancellationToken token, bool fireAndForget = false)
    {
        if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning) return;
        try
        {
            await _redrawSemaphore.WaitAsync(token).ConfigureAwait(false);

            await PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                logger.LogDebug("[{appid}] Calling on IPC: GlamourerApplyAll", applicationId);
                _glamourerApplyAll!.InvokeAction(customization, chara);
            }).ConfigureAwait(false);
        }
        finally
        {
            _redrawSemaphore.Release();
        }
    }

    public async Task GlamourerApplyCustomizationAndEquipmentAsync(ILogger logger, GameObjectHandler handler, string customization, string equipment, Guid applicationid, CancellationToken token, bool fireAndForget = false)
    {
        if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning) return;
        try
        {
            await _redrawSemaphore.WaitAsync(token).ConfigureAwait(false);
            await PenumbraRedrawInternalAsync(logger, handler, applicationid, (chara) =>
            {
                logger.LogDebug("[{appid}] Calling on IPC: GlamourerApplyOnlyCustomization", applicationid);
                _glamourerApplyOnlyCustomization!.InvokeAction(customization, chara);
            }).ConfigureAwait(false);
            await PenumbraRedrawInternalAsync(logger, handler, applicationid, (chara) =>
            {
                logger.LogDebug("[{appid}] Calling on IPC: GlamourerApplyOnlyEquipment", applicationid);
                _glamourerApplyOnlyEquipment!.InvokeAction(equipment, chara);
            }).ConfigureAwait(false);
        }
        finally
        {
            _redrawSemaphore.Release();
        }
    }

    public async Task<string> GlamourerGetCharacterCustomizationAsync(IntPtr character)
    {
        if (!CheckGlamourerApi()) return string.Empty;
        try
        {
            return await _dalamudUtil.RunOnFrameworkThread(() =>
            {
                var gameObj = _dalamudUtil.CreateGameObject(character);
                if (gameObj is Character c)
                {
                    var glamourerString = _glamourerGetAllCustomization!.InvokeFunc(c);
                    byte[] bytes = Convert.FromBase64String(glamourerString);
                    // ignore transparency
                    bytes[88] = 128;
                    bytes[89] = 63;
                    return Convert.ToBase64String(bytes);
                }
                return string.Empty;
            }).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task HeelsRestoreOffsetForPlayerAsync(IntPtr character)
    {
        if (!CheckHeelsApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj != null)
            {
                Logger.LogTrace("Restoring Heels data to {chara}", character.ToString("X"));
                _heelsUnregisterPlayer.InvokeAction(gameObj);
            }
        }).ConfigureAwait(false);
    }

    public async Task HeelsSetOffsetForPlayerAsync(IntPtr character, float offset)
    {
        if (!CheckHeelsApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj != null)
            {
                Logger.LogTrace("Applying Heels data to {chara}", character.ToString("X"));
                _heelsRegisterPlayer.InvokeAction(gameObj, offset);
            }
        }).ConfigureAwait(false);
    }

    public async Task HonorificClearTitleAsync(nint character)
    {
        if (!CheckHonorificApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is PlayerCharacter c)
            {
                Logger.LogTrace("Honorific removing for {addr}", c.Address.ToString("X"));
                _honorificClearCharacterTitle!.InvokeAction(c);
            }
        }).ConfigureAwait(false);
    }

    public string HonorificGetTitle()
    {
        if (!CheckHonorificApi()) return string.Empty;
        (string? title, bool isPrefix) = _honorificGetLocalCharacterTitle.InvokeFunc();
        return string.IsNullOrEmpty(title) ? string.Empty : $"{(isPrefix ? 1 : 0)}{title}";
    }

    public async Task HonorificSetTitleAsync(IntPtr character, string honorificData)
    {
        if (!CheckHonorificApi()) return;
        Logger.LogTrace("Applying Honorific data to {chara}", character.ToString("X"));
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is PlayerCharacter pc)
            {
                if (string.IsNullOrEmpty(honorificData))
                {
                    _honorificClearCharacterTitle!.InvokeAction(pc);
                }
                else
                {
                    _honorificSetCharacterTitle!.InvokeAction(pc, honorificData[1..], honorificData[0] == '1');
                }
            }
        }).ConfigureAwait(false);
    }

    public async Task<string> PalettePlusBuildPaletteAsync()
    {
        if (!CheckPalettePlusApi()) return string.Empty;
        var palette = await _dalamudUtil.RunOnFrameworkThread(() => _palettePlusBuildCharaPalette.InvokeFunc(_dalamudUtil.GetPlayerCharacter())).ConfigureAwait(false);
        if (string.IsNullOrEmpty(palette)) return string.Empty;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(palette));
    }

    public async Task PalettePlusRemovePaletteAsync(IntPtr character)
    {
        if (!CheckPalettePlusApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                Logger.LogTrace("PalettePlus removing for {addr}", c.Address.ToString("X"));
                _palettePlusRemoveCharaPalette!.InvokeAction(c);
            }
        }).ConfigureAwait(false);
    }

    public async Task PalettePlusSetPaletteAsync(IntPtr character, string palette)
    {
        if (!CheckPalettePlusApi()) return;
        string decodedPalette = Encoding.UTF8.GetString(Convert.FromBase64String(palette));
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                if (string.IsNullOrEmpty(decodedPalette))
                {
                    Logger.LogTrace("PalettePlus removing for {addr}", c.Address.ToString("X"));
                    _palettePlusRemoveCharaPalette!.InvokeAction(c);
                }
                else
                {
                    Logger.LogTrace("PalettePlus applying for {addr}", c.Address.ToString("X"));
                    _palettePlusSetCharaPalette!.InvokeAction(c, decodedPalette);
                }
            }
        }).ConfigureAwait(false);
    }

    public string PenumbraGetMetaManipulations()
    {
        if (!CheckPenumbraApi()) return string.Empty;
        return _penumbraGetMetaManipulations.Invoke();
    }

    public async Task PenumbraRedrawAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, CancellationToken token)
    {
        if (!CheckPenumbraApi() || _dalamudUtil.IsZoning) return;
        try
        {
            await _redrawSemaphore.WaitAsync(token).ConfigureAwait(false);
            await PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                logger.LogDebug("[{appid}] Calling on IPC: PenumbraRedraw", applicationId);
                _penumbraRedrawObject!.Invoke(chara, RedrawType.Redraw);
            }).ConfigureAwait(false);
        }
        finally
        {
            _redrawSemaphore.Release();
        }
    }

    public async Task PenumbraRemoveTemporaryCollectionAsync(ILogger logger, Guid applicationId, string characterName)
    {
        if (!CheckPenumbraApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var collName = "Mare_" + characterName;
            logger.LogTrace("[{applicationId}] Removing temp collection for {collName}", applicationId, collName);
            var ret = _penumbraRemoveTemporaryMod.Invoke("MareChara", collName, 0);
            logger.LogTrace("[{applicationId}] RemoveTemporaryMod: {ret}", applicationId, ret);
            var ret2 = _penumbraRemoveTemporaryCollection.Invoke(collName);
            logger.LogTrace("[{applicationId}] RemoveTemporaryCollection: {ret2}", applicationId, ret2);
        }).ConfigureAwait(false);
    }

    public async Task<(string[] forward, string[][] reverse)> PenumbraResolvePathsAsync(string[] forward, string[] reverse)
    {
        return await _dalamudUtil.RunOnFrameworkThread(() => _penumbraResolvePaths.Invoke(forward, reverse)).ConfigureAwait(false);
    }

    public async Task PenumbraSetTemporaryModsAsync(ILogger logger, Guid applicationId, string characterName, int? idx, Dictionary<string, string> modPaths, string manipulationData)
    {
        if (!CheckPenumbraApi() || idx == null) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var collName = "Mare_" + characterName;
            var ret = _penumbraCreateNamedTemporaryCollection.Invoke(collName);
            logger.LogTrace("[{applicationId}] Creating Temp Collection {collName}, Success: {ret}", applicationId, collName, ret);
            var retAssign = _penumbraAssignTemporaryCollection.Invoke(collName, idx.Value, c: true);
            logger.LogTrace("[{applicationId}] Assigning Temp Collection {collName} to index {idx}, Success: {ret}", applicationId, collName, idx, retAssign);
            foreach (var mod in modPaths)
            {
                logger.LogTrace("[{applicationId}] Change: {from} => {to}", applicationId, mod.Key, mod.Value);
            }

            logger.LogTrace("[{applicationId}] Manip: {data}", applicationId, manipulationData);
            var ret2 = _penumbraAddTemporaryMod.Invoke("MareChara", collName, modPaths, manipulationData, 0);
            logger.LogTrace("[{applicationId}] Setting temp mods for {collName}, Success: {ret2}", applicationId, collName, ret2);
        }).ConfigureAwait(false);
    }

    public void ToggleGposeQueueMode(bool on)
    {
        _inGposeQueueMode = on;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _disposalCts.Cancel();

        int totalSleepTime = 0;
        while (!ActionQueue.IsEmpty && totalSleepTime < 2000)
        {
            Logger.LogTrace("Waiting for actionqueue to clear...");
            PeriodicApiStateCheck();
            if (CheckPenumbraApi())
            {
                HandleActionQueue();
            }
            Thread.Sleep(16);
            totalSleepTime += 16;
        }

        if (totalSleepTime >= 2000)
        {
            Logger.LogTrace("Action queue clear or not, disposing");
        }

        ActionQueue.Clear();

        _penumbraModSettingChanged.Dispose();
        _penumbraGameObjectResourcePathResolved.Dispose();
        _penumbraDispose.Dispose();
        _penumbraInit.Dispose();
        _penumbraObjectIsRedrawn.Dispose();
        _heelsOffsetUpdate.Unsubscribe(HeelsOffsetChange);
        _palettePlusPaletteChanged.Unsubscribe(OnPalettePlusPaletteChange);
        _customizePlusOnScaleUpdate.Unsubscribe(OnCustomizePlusScaleChange);
        _honorificLocalCharacterTitleChanged.Unsubscribe(OnHonorificLocalCharacterTitleChanged);
    }

    private bool CheckCustomizePlusApiInternal()
    {
        try
        {
            return string.Equals(_customizePlusApiVersion.InvokeFunc(), "2.0", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private bool CheckGlamourerApiInternal()
    {
        bool apiAvailable = false;
        try
        {
            apiAvailable = _glamourerApiVersion.InvokeFunc() >= 0;
            _shownGlamourerUnavailable = _shownGlamourerUnavailable && !apiAvailable;
            return apiAvailable;
        }
        catch
        {
            return apiAvailable;
        }
        finally
        {
            if (!apiAvailable && !_shownGlamourerUnavailable)
            {
                _shownGlamourerUnavailable = true;
                Mediator.Publish(new NotificationMessage("Glamourer inactive", "Your Glamourer installation is not active or out of date. Update Glamourer to continue to use Mare.", NotificationType.Error));
            }
        }
    }

    private bool CheckHeelsApiInternal()
    {
        try
        {
            return string.Equals(_heelsGetApiVersion.InvokeFunc(), "1.0.1", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private bool CheckHonorificApiInternal()
    {
        try
        {
            return _honorificApiVersion.InvokeFunc() is { Item1: 1, Item2: >= 0 };
        }
        catch
        {
            return false;
        }
    }

    private bool CheckPalettePlusApiInternal()
    {
        try
        {
            return string.Equals(_palettePlusApiVersion.InvokeFunc(), "1.1.0", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private bool CheckPenumbraApiInternal()
    {
        bool apiAvailable = false;
        try
        {
            apiAvailable = _penumbraApiVersion.Invoke() is { Item1: 4, Item2: >= 19 } && _penumbraEnabled.Invoke();
            _shownPenumbraUnavailable = _shownPenumbraUnavailable && !apiAvailable;
            return apiAvailable;
        }
        catch
        {
            return apiAvailable;
        }
        finally
        {
            if (!apiAvailable && !_shownPenumbraUnavailable)
            {
                _shownPenumbraUnavailable = true;
                Mediator.Publish(new NotificationMessage("Penumbra inactive", "Your Penumbra installation is not active or out of date. Update Penumbra and/or the Enable Mods setting in Penumbra to continue to use Mare.", NotificationType.Error));
            }
        }
    }

    private void ClearActionQueue()
    {
        ActionQueue.Clear();
        _gposeActionQueue.Clear();
    }

    private string? GetPenumbraModDirectoryInternal()
    {
        if (!CheckPenumbraApi()) return null;
        return _penumbraResolveModDir!.Invoke().ToLowerInvariant();
    }

    private void HandleActionQueue()
    {
        if (ActionQueue.TryDequeue(out var action))
        {
            if (action == null) return;
            Logger.LogDebug("Execution action in queue: {method}", action.Method);
            action();
        }
    }

    private void HandleGposeActionQueue()
    {
        if (_gposeActionQueue.TryDequeue(out var action))
        {
            if (action == null) return;
            Logger.LogDebug("Execution action in gpose queue: {method}", action.Method);
            action();
        }
    }

    private void HeelsOffsetChange(float offset)
    {
        Mediator.Publish(new HeelsOffsetMessage());
    }

    private void OnCustomizePlusScaleChange(string? scale)
    {
        Mediator.Publish(new CustomizePlusMessage());
    }

    private void OnHonorificLocalCharacterTitleChanged(string title, bool isPrefix)
    {
        Mediator.Publish(new HonorificMessage((isPrefix ? 1 : 0) + title));
    }

    private void OnPalettePlusPaletteChange(Character character, string palette)
    {
        Mediator.Publish(new PalettePlusMessage(character));
    }

    private void PenumbraDispose()
    {
        _disposalCts.Cancel();
        _disposalCts.Dispose();
        Mediator.Publish(new PenumbraDisposedMessage());
        ActionQueue.Clear();
        _disposalCts = new();
    }

    private void PenumbraInit()
    {
        Mediator.Publish(new PenumbraInitializedMessage());
        _penumbraRedraw!.Invoke("self", RedrawType.Redraw);
    }

    private async Task PenumbraRedrawInternalAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, Action<Character> action)
    {
        Mediator.Publish(new PenumbraStartRedrawMessage(handler.Address));

        _penumbraRedrawRequests[handler.Address] = true;

        try
        {
            CancellationTokenSource cancelToken = new CancellationTokenSource();
            cancelToken.CancelAfter(TimeSpan.FromSeconds(15));
            await handler.ActOnFrameworkAfterEnsureNoDrawAsync(action, cancelToken.Token).ConfigureAwait(false);

            if (!_disposalCts.Token.IsCancellationRequested)
                await _dalamudUtil.WaitWhileCharacterIsDrawing(logger, handler, applicationId, 30000, _disposalCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _penumbraRedrawRequests[handler.Address] = false;
        }

        Mediator.Publish(new PenumbraEndRedrawMessage(handler.Address));
    }

    private void PeriodicApiStateCheck()
    {
        _glamourerAvailable = CheckGlamourerApiInternal();
        _penumbraAvailable = CheckPenumbraApiInternal();
        _heelsAvailable = CheckHeelsApiInternal();
        _customizePlusAvailable = CheckCustomizePlusApiInternal();
        _palettePlusAvailable = CheckPalettePlusApiInternal();
        _honorificAvailable = CheckHonorificApiInternal();
        PenumbraModDirectory = GetPenumbraModDirectoryInternal();
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
            Mediator.Publish(new PenumbraRedrawMessage(objectAddress, objectTableIndex, wasRequested));
        }
    }

    private void ResourceLoaded(IntPtr ptr, string arg1, string arg2)
    {
        if (ptr != IntPtr.Zero && string.Compare(arg1, arg2, ignoreCase: true, System.Globalization.CultureInfo.InvariantCulture) != 0)
        {
            Mediator.Publish(new PenumbraResourceLoadMessage(ptr, arg1, arg2));
        }
    }
}