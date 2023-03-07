using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
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

public class IpcManager : MediatorSubscriberBase
{
    private readonly ICallGateSubscriber<int> _glamourerApiVersion;
    private readonly ICallGateSubscriber<string, GameObject?, object>? _glamourerApplyAll;
    private readonly ICallGateSubscriber<GameObject?, string>? _glamourerGetAllCustomization;
    private readonly ICallGateSubscriber<GameObject?, object> _glamourerRevertCustomization;
    private readonly ICallGateSubscriber<string, GameObject?, object>? _glamourerApplyOnlyEquipment;
    private readonly ICallGateSubscriber<string, GameObject?, object>? _glamourerApplyOnlyCustomization;

    private readonly FuncSubscriber<(int, int)> _penumbraApiVersion;
    private readonly FuncSubscriber<string, PenumbraApiEc> _penumbraCreateNamedTemporaryCollection;
    private readonly FuncSubscriber<string> _penumbraGetMetaManipulations;
    private readonly EventSubscriber _penumbraInit;
    private readonly EventSubscriber _penumbraDispose;
    private readonly EventSubscriber<nint, int> _penumbraObjectIsRedrawn;
    private readonly ActionSubscriber<string, RedrawType> _penumbraRedraw;
    private readonly ActionSubscriber<GameObject, RedrawType> _penumbraRedrawObject;
    private readonly FuncSubscriber<string, PenumbraApiEc> _penumbraRemoveTemporaryCollection;
    private readonly FuncSubscriber<string, string, int, PenumbraApiEc> _penumbraRemoveTemporaryMod;
    private readonly FuncSubscriber<string, int, bool, PenumbraApiEc> _penumbraAssignTemporaryCollection;
    private readonly FuncSubscriber<string> _penumbraResolveModDir;
    private readonly FuncSubscriber<string, string> _penumbraResolvePlayer;
    private readonly FuncSubscriber<string, string[]> _reverseResolvePlayer;
    private readonly FuncSubscriber<string, string, Dictionary<string, string>, string, int, PenumbraApiEc> _penumbraAddTemporaryMod;
    private readonly FuncSubscriber<string[], string[], (string[], string[][])> _penumbraResolvePaths;
    private readonly FuncSubscriber<bool> _penumbraEnabled;
    private readonly EventSubscriber<ModSettingChange, string, string, bool> _penumbraModSettingChanged;
    private readonly EventSubscriber<nint, string, string> _penumbraGameObjectResourcePathResolved;

    private readonly ICallGateSubscriber<string> _heelsGetApiVersion;
    private readonly ICallGateSubscriber<float> _heelsGetOffset;
    private readonly ICallGateSubscriber<float, object?> _heelsOffsetUpdate;
    private readonly ICallGateSubscriber<GameObject, float, object?> _heelsRegisterPlayer;
    private readonly ICallGateSubscriber<GameObject, object?> _heelsUnregisterPlayer;

    private readonly ICallGateSubscriber<string> _customizePlusApiVersion;
    private readonly ICallGateSubscriber<string, string> _customizePlusGetBodyScale;
    private readonly ICallGateSubscriber<string, Character?, object> _customizePlusSetBodyScaleToCharacter;
    private readonly ICallGateSubscriber<Character?, object> _customizePlusRevert;
    private readonly ICallGateSubscriber<string?, object> _customizePlusOnScaleUpdate;

    private readonly ICallGateSubscriber<string> _palettePlusApiVersion;
    private readonly ICallGateSubscriber<Character, string> _palettePlusBuildCharaPalette;
    private readonly ICallGateSubscriber<Character, string, object> _palettePlusSetCharaPalette;
    private readonly ICallGateSubscriber<Character, object> _palettePlusRemoveCharaPalette;
    private readonly ICallGateSubscriber<Character, string, object> _palettePlusPaletteChanged;
    private readonly DalamudUtil _dalamudUtil;
    private bool _inGposeQueueMode = false;
    private ConcurrentQueue<Action> ActionQueue => _inGposeQueueMode ? _gposeActionQueue : _normalQueue;
    private readonly ConcurrentQueue<Action> _normalQueue = new();
    private readonly ConcurrentQueue<Action> _gposeActionQueue = new();

    private readonly ConcurrentDictionary<IntPtr, bool> _penumbraRedrawRequests = new();
    private CancellationTokenSource _disposalCts = new();

    private bool _penumbraAvailable = false;
    private bool _glamourerAvailable = false;
    private bool _customizePlusAvailable = false;
    private bool _heelsAvailable = false;
    private bool _palettePlusAvailable = false;

    public IpcManager(ILogger<IpcManager> logger, DalamudPluginInterface pi, DalamudUtil dalamudUtil, MareMediator mediator) : base(logger, mediator)
    {
        _dalamudUtil = dalamudUtil;

        _logger.LogTrace("Creating " + nameof(IpcManager));

        _penumbraInit = Penumbra.Api.Ipc.Initialized.Subscriber(pi, () => PenumbraInit());
        _penumbraDispose = Penumbra.Api.Ipc.Disposed.Subscriber(pi, () => PenumbraDispose());
        _penumbraResolvePlayer = Penumbra.Api.Ipc.ResolvePlayerPath.Subscriber(pi);
        _penumbraResolveModDir = Penumbra.Api.Ipc.GetModDirectory.Subscriber(pi);
        _penumbraRedraw = Penumbra.Api.Ipc.RedrawObjectByName.Subscriber(pi);
        _penumbraRedrawObject = Penumbra.Api.Ipc.RedrawObject.Subscriber(pi);
        _reverseResolvePlayer = Penumbra.Api.Ipc.ReverseResolvePlayerPath.Subscriber(pi);
        _penumbraApiVersion = Penumbra.Api.Ipc.ApiVersions.Subscriber(pi);
        _penumbraObjectIsRedrawn = Penumbra.Api.Ipc.GameObjectRedrawn.Subscriber(pi, (ptr, idx) => RedrawEvent(ptr, idx));
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

        _penumbraGameObjectResourcePathResolved = Penumbra.Api.Ipc.GameObjectResourcePathResolved.Subscriber(pi, (ptr, arg1, arg2) => ResourceLoaded(ptr, arg1, arg2));

        _glamourerApiVersion = pi.GetIpcSubscriber<int>("Glamourer.ApiVersion");
        _glamourerGetAllCustomization = pi.GetIpcSubscriber<GameObject?, string>("Glamourer.GetAllCustomizationFromCharacter");
        _glamourerApplyAll = pi.GetIpcSubscriber<string, GameObject?, object>("Glamourer.ApplyAllToCharacter");
        _glamourerApplyOnlyCustomization = pi.GetIpcSubscriber<string, GameObject?, object>("Glamourer.ApplyOnlyCustomizationToCharacter");
        _glamourerApplyOnlyEquipment = pi.GetIpcSubscriber<string, GameObject?, object>("Glamourer.ApplyOnlyEquipmentToCharacter");
        _glamourerRevertCustomization = pi.GetIpcSubscriber<GameObject?, object>("Glamourer.RevertCharacter");

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

        if (Initialized)
        {
            Mediator.Publish(new PenumbraInitializedMessage());
        }

        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => HandleActionQueue());
        Mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, (_) => HandleGposeActionQueue());
        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (_) => ClearActionQueue());
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => PeriodicApiStateCheck());
    }

    private void PeriodicApiStateCheck()
    {
        _glamourerAvailable = CheckGlamourerApiInternal();
        _penumbraAvailable = CheckPenumbraApiInternal();
        _heelsAvailable = CheckHeelsApiInternal();
        _customizePlusAvailable = CheckCustomizePlusApiInternal();
        _palettePlusAvailable = CheckPalettePlusApiInternal();
        PenumbraModDirectory = GetPenumbraModDirectory();
    }

    private void HandleGposeActionQueue()
    {
        if (_gposeActionQueue.TryDequeue(out var action))
        {
            if (action == null) return;
            _logger.LogDebug("Execution action in gpose queue: {method}", action.Method);
            action();
        }
    }

    public void ToggleGposeQueueMode(bool on)
    {
        _inGposeQueueMode = on;
    }

    private void ClearActionQueue()
    {
        ActionQueue.Clear();
        _gposeActionQueue.Clear();
    }

    private void ResourceLoaded(IntPtr ptr, string arg1, string arg2)
    {
        Task.Run(() =>
        {
            if (ptr != IntPtr.Zero && string.Compare(arg1, arg2, ignoreCase: true, System.Globalization.CultureInfo.InvariantCulture) != 0)
            {
                Mediator.Publish(new PenumbraResourceLoadMessage(ptr, arg1, arg2));
            }
        });
    }

    private void HandleActionQueue()
    {
        if (ActionQueue.TryDequeue(out var action))
        {
            if (action == null) return;
            _logger.LogDebug("Execution action in queue: {method}", action.Method);
            action();
        }
    }

    public bool Initialized => CheckPenumbraApiInternal() && CheckGlamourerApiInternal();

    public bool CheckGlamourerApi() => _glamourerAvailable;

    private bool _shownGlamourerUnavailable = false;

    public bool CheckGlamourerApiInternal()
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

    public bool CheckPenumbraApi() => _penumbraAvailable;

    private bool _shownPenumbraUnavailable = false;
    public bool CheckPenumbraApiInternal()
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

    public bool CheckHeelsApi() => _heelsAvailable;

    public bool CheckHeelsApiInternal()
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

    public bool CheckCustomizePlusApi() => _customizePlusAvailable;

    public bool CheckCustomizePlusApiInternal()
    {
        try
        {
            return string.Equals(_customizePlusApiVersion.InvokeFunc(), "1.0", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public bool CheckPalettePlusApi() => _palettePlusAvailable;

    public bool CheckPalettePlusApiInternal()
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _disposalCts.Cancel();

        int totalSleepTime = 0;
        while (!ActionQueue.IsEmpty && totalSleepTime < 2000)
        {
            _logger.LogTrace("Waiting for actionqueue to clear...");
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
            _logger.LogTrace("Action queue clear or not, disposing");
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
    }

    public float GetHeelsOffset()
    {
        if (!CheckHeelsApi()) return 0.0f;
        return _heelsGetOffset.InvokeFunc();
    }

    public async Task HeelsSetOffsetForPlayer(IntPtr character, float offset)
    {
        if (!CheckHeelsApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj != null)
            {
                _logger.LogTrace("Applying Heels data to {chara}", character.ToString("X"));
                _heelsRegisterPlayer.InvokeAction(gameObj, offset);
            }
        }).ConfigureAwait(false);
    }

    public async Task HeelsRestoreOffsetForPlayer(IntPtr character)
    {
        if (!CheckHeelsApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj != null)
            {
                _logger.LogTrace("Restoring Heels data to {chara}", character.ToString("X"));
                _heelsUnregisterPlayer.InvokeAction(gameObj);
            }
        }).ConfigureAwait(false);
    }

    public string GetCustomizePlusScale()
    {
        if (!CheckCustomizePlusApi()) return string.Empty;
        var scale = _customizePlusGetBodyScale.InvokeFunc(_dalamudUtil.PlayerName);
        if (string.IsNullOrEmpty(scale)) return string.Empty;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(scale));
    }

    public async Task CustomizePlusSetBodyScale(IntPtr character, string scale)
    {
        if (!CheckCustomizePlusApi() || string.IsNullOrEmpty(scale)) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                string decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(scale));
                _logger.LogTrace("CustomizePlus applying for {chara}", c.Address.ToString("X"));
                _customizePlusSetBodyScaleToCharacter!.InvokeAction(decodedScale, c);
            }
        }).ConfigureAwait(false);
    }

    public async Task CustomizePlusRevert(IntPtr character)
    {
        if (!CheckCustomizePlusApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                _logger.LogTrace("CustomizePlus reverting for {chara}", c.Address.ToString("X"));
                _customizePlusRevert!.InvokeAction(c);
            }
        }).ConfigureAwait(false);
    }

    private async Task PenumbraRedrawAsync(ILogger logger, GameObjectHandler obj, Guid applicationId, Action action, bool fireAndForget, CancellationToken token)
    {
        Mediator.Publish(new PenumbraStartRedrawMessage(obj.Address));

        _penumbraRedrawRequests[obj.Address] = !fireAndForget;

        ActionQueue.Enqueue(action);

        if (!fireAndForget)
        {
            var disposeToken = _disposalCts.Token;
            var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(disposeToken, token).Token;

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

            if (!combinedToken.IsCancellationRequested)
                await _dalamudUtil.WaitWhileCharacterIsDrawing(logger, obj, applicationId, 30000, combinedToken).ConfigureAwait(false);

            _penumbraRedrawRequests[obj.Address] = false;
        }
        Mediator.Publish(new PenumbraEndRedrawMessage(obj.Address));

    }

    public async Task GlamourerApplyAll(ILogger logger, GameObjectHandler handler, string? customization, Guid applicationId, CancellationToken token, bool fireAndForget = false)
    {
        if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning) return;
        var gameObj = _dalamudUtil.CreateGameObject(handler.Address);
        if (gameObj is Character c)
        {
            await PenumbraRedrawAsync(logger, handler, applicationId, () => _glamourerApplyAll!.InvokeAction(customization, c), fireAndForget, token).ConfigureAwait(false);
        }
    }

    public async Task GlamourerApplyOnlyEquipment(ILogger logger, GameObjectHandler handler, string customization, Guid applicationId, CancellationToken token, bool fireAndForget = false)
    {
        if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning) return;
        var gameObj = _dalamudUtil.CreateGameObject(handler.Address);
        if (gameObj is Character c)
        {
            await PenumbraRedrawAsync(logger, handler, applicationId, () => _glamourerApplyOnlyEquipment!.InvokeAction(customization, c), fireAndForget, token).ConfigureAwait(false);
        }
    }

    public async Task GlamourerApplyOnlyCustomization(ILogger logger, GameObjectHandler handler, string customization, Guid applicationId, CancellationToken token, bool fireAndForget = false)
    {
        if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning) return;
        var gameObj = _dalamudUtil.CreateGameObject(handler.Address);
        if (gameObj is Character c)
        {
            await PenumbraRedrawAsync(logger, handler, applicationId, () => _glamourerApplyOnlyCustomization!.InvokeAction(customization, c), fireAndForget, token).ConfigureAwait(false);
        }
    }

    public string GlamourerGetCharacterCustomization(IntPtr character)
    {
        if (!CheckGlamourerApi()) return string.Empty;
        try
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
        }
        catch
        {
            return string.Empty;
        }
    }

    public void GlamourerRevertCharacterCustomization(GameObject character)
    {
        if (!CheckGlamourerApi() || _dalamudUtil.IsZoning) return;
        ActionQueue.Enqueue(() => _glamourerRevertCustomization!.InvokeAction(character));
    }

    public string PenumbraGetMetaManipulations()
    {
        if (!CheckPenumbraApi()) return string.Empty;
        return _penumbraGetMetaManipulations.Invoke();
    }

    public string? PenumbraModDirectory { get; private set; }

    public string? GetPenumbraModDirectory()
    {
        if (!CheckPenumbraApi()) return null;
        return _penumbraResolveModDir!.Invoke().ToLowerInvariant();
    }

    public async Task PenumbraRedraw(ILogger logger, GameObjectHandler handler, Guid applicationId, CancellationToken token, bool fireAndForget = false)
    {
        if (!CheckPenumbraApi() || _dalamudUtil.IsZoning) return;
        var gameObj = _dalamudUtil.CreateGameObject(handler.Address);
        if (gameObj is Character c)
        {
            await PenumbraRedrawAsync(logger, handler, applicationId, () => _penumbraRedrawObject!.Invoke(c, RedrawType.Redraw), fireAndForget, token).ConfigureAwait(false);
        }
    }

    public void PenumbraRemoveTemporaryCollection(ILogger logger, Guid applicationId, string characterName)
    {
        if (!CheckPenumbraApi()) return;
        var collName = "Mare_" + characterName;
        logger.LogTrace("[{applicationId}] Removing temp collection for {collName}", applicationId, collName);
        var ret = _penumbraRemoveTemporaryMod.Invoke("MareChara", collName, 0);
        logger.LogTrace("[{applicationId}] RemoveTemporaryMod: {ret}", applicationId, ret);
        var ret2 = _penumbraRemoveTemporaryCollection.Invoke(collName);
        logger.LogTrace("[{applicationId}] RemoveTemporaryCollection: {ret2}", applicationId, ret2);
    }

    public string PenumbraResolvePath(string path)
    {
        if (!CheckPenumbraApi()) return path;
        var resolvedPath = _penumbraResolvePlayer!.Invoke(path);
        return resolvedPath ?? path;
    }

    public string[] PenumbraReverseResolvePlayer(string path)
    {
        if (!CheckPenumbraApi()) return new[] { path };
        var resolvedPaths = _reverseResolvePlayer.Invoke(path);
        if (resolvedPaths.Length == 0)
        {
            resolvedPaths = new[] { path };
        }
        return resolvedPaths;
    }

    public void PenumbraSetTemporaryMods(ILogger logger, Guid applicationId, string characterName, Dictionary<string, string> modPaths, string manipulationData)
    {
        if (!CheckPenumbraApi()) return;

        var idx = _dalamudUtil.GetIndexFromObjectTableByName(characterName);
        if (idx == null)
        {
            return;
        }
        var collName = "Mare_" + characterName;
        var ret = _penumbraCreateNamedTemporaryCollection.Invoke(collName);
        logger.LogTrace("[{applicationId}] Creating Temp Collection {collName}, Success: {ret}", applicationId, collName, ret);
        var retAssign = _penumbraAssignTemporaryCollection.Invoke(collName, idx.Value, c: true);
        logger.LogTrace("[{applicationId}] Assigning Temp Collection {collName} to index {idx}", applicationId, collName, idx.Value);
        foreach (var mod in modPaths)
        {
            logger.LogTrace("[{applicationId}] Change: {from} => {to}", applicationId, mod.Key, mod.Value);
        }

        var ret2 = _penumbraAddTemporaryMod.Invoke("MareChara", collName, modPaths, manipulationData, 0);
        logger.LogTrace("[{applicationId}] Setting temp mods for {collName}, Success: {ret2}", applicationId, collName, ret2);
    }

    public (string[] forward, string[][] reverse) PenumbraResolvePaths(string[] forward, string[] reverse)
    {
        return _penumbraResolvePaths.Invoke(forward, reverse);
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

    private void PenumbraInit()
    {
        Mediator.Publish(new PenumbraInitializedMessage());
        _penumbraRedraw!.Invoke("self", RedrawType.Redraw);
    }

    private void HeelsOffsetChange(float offset)
    {
        Mediator.Publish(new HeelsOffsetMessage());
    }

    private void OnCustomizePlusScaleChange(string? scale)
    {
        Mediator.Publish(new CustomizePlusMessage());
    }

    private void OnPalettePlusPaletteChange(Character character, string palette)
    {
        Mediator.Publish(new PalettePlusMessage());
    }

    public async Task PalettePlusSetPalette(IntPtr character, string palette)
    {
        if (!CheckPalettePlusApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                string decodedPalette = Encoding.UTF8.GetString(Convert.FromBase64String(palette));

                if (string.IsNullOrEmpty(decodedPalette))
                {
                    _logger.LogTrace("PalettePlus removing for {addr}", c.Address.ToString("X"));
                    _palettePlusRemoveCharaPalette!.InvokeAction(c);
                }
                else
                {
                    _logger.LogTrace("PalettePlus applying for {addr}", c.Address.ToString("X"));
                    _palettePlusSetCharaPalette!.InvokeAction(c, decodedPalette);
                }
            }
        }).ConfigureAwait(false);
    }

    public string PalettePlusBuildPalette()
    {
        if (!CheckPalettePlusApi()) return string.Empty;
        var palette = _palettePlusBuildCharaPalette.InvokeFunc(_dalamudUtil.PlayerCharacter);
        if (string.IsNullOrEmpty(palette)) return string.Empty;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(palette));
    }

    public async Task PalettePlusRemovePalette(IntPtr character)
    {
        if (!CheckPalettePlusApi()) return;
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                _logger.LogTrace("PalettePlus removing for {addr}", c.Address.ToString("X"));
                _palettePlusRemoveCharaPalette!.InvokeAction(c);
            }
        }).ConfigureAwait(false);
    }

    private void PenumbraDispose()
    {
        _disposalCts.Cancel();
        _disposalCts.Dispose();
        Mediator.Publish(new PenumbraDisposedMessage());
        ActionQueue.Clear();
        _disposalCts = new();
    }

    internal bool RequestedRedraw(nint address)
    {
        if (_penumbraRedrawRequests.TryGetValue(address, out var requested))
        {
            return requested;
        }

        return false;
    }
}
