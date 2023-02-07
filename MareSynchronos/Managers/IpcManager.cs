﻿using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Game.ClientState.Objects.Types;
using MareSynchronos.Utils;
using Action = System.Action;
using System.Collections.Concurrent;
using System.Text;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using MareSynchronos.Mediator;

namespace MareSynchronos.Managers;

public class IpcManager : MediatorSubscriberBase, IDisposable
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

    public IpcManager(DalamudPluginInterface pi, DalamudUtil dalamudUtil, MareMediator mediator) : base(mediator)
    {
        Logger.Verbose("Creating " + nameof(IpcManager));

        _penumbraInit = Penumbra.Api.Ipc.Initialized.Subscriber(pi, () => PenumbraInit());
        _penumbraDispose = Penumbra.Api.Ipc.Disposed.Subscriber(pi, () => PenumbraDispose());
        _penumbraResolvePlayer = Penumbra.Api.Ipc.ResolvePlayerPath.Subscriber(pi);
        _penumbraResolveModDir = Penumbra.Api.Ipc.GetModDirectory.Subscriber(pi);
        _penumbraRedraw = Penumbra.Api.Ipc.RedrawObjectByName.Subscriber(pi);
        _penumbraRedrawObject = Penumbra.Api.Ipc.RedrawObject.Subscriber(pi);
        _reverseResolvePlayer = Penumbra.Api.Ipc.ReverseResolvePlayerPath.Subscriber(pi);
        _penumbraApiVersion = Penumbra.Api.Ipc.ApiVersions.Subscriber(pi);
        _penumbraObjectIsRedrawn = Penumbra.Api.Ipc.GameObjectRedrawn.Subscriber(pi, (ptr, idx) => RedrawEvent((IntPtr)ptr, idx));
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

        _penumbraGameObjectResourcePathResolved = Penumbra.Api.Ipc.GameObjectResourcePathResolved.Subscriber(pi, (ptr, arg1, arg2) => ResourceLoaded((IntPtr)ptr, arg1, arg2));

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

        _dalamudUtil = dalamudUtil;
        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => HandleActionQueue());
        Mediator.Subscribe<GposeFrameworkUpdateMessage>(this, (_) => HandleGposeActionQueue());
        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (_) => ClearActionQueue());
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => CheckPenumbraModPath());
    }

    private void CheckPenumbraModPath()
    {
        PenumbraModDirectory = GetPenumbraModDirectory();
    }

    private void HandleGposeActionQueue()
    {
        if (_gposeActionQueue.TryDequeue(out var action))
        {
            if (action == null) return;
            Logger.Debug("Execution action in gpose queue: " + action.Method);
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
            Logger.Debug("Execution action in queue: " + action.Method);
            action();
        }
    }

    public bool Initialized => CheckPenumbraApi() && CheckGlamourerApi();
    public bool CheckGlamourerApi()
    {
        try
        {
            return _glamourerApiVersion.InvokeFunc() >= 0;
        }
        catch
        {
            return false;
        }
    }

    public bool CheckPenumbraApi()
    {
        try
        {
            return _penumbraApiVersion.Invoke() is { Item1: 4, Item2: >= 19 } && _penumbraEnabled.Invoke();
        }
        catch
        {
            return false;
        }
    }

    public bool CheckHeelsApi()
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

    public bool CheckCustomizePlusApi()
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

    public bool CheckPalettePlusApi()
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

    public override void Dispose()
    {
        base.Dispose();

        int totalSleepTime = 0;
        while (!ActionQueue.IsEmpty && totalSleepTime < 2000)
        {
            Logger.Verbose("Waiting for actionqueue to clear...");
            HandleActionQueue();
            Thread.Sleep(16);
            totalSleepTime += 16;
        }

        if (totalSleepTime >= 2000)
        {
            Logger.Verbose("Action queue clear or not, disposing");
        }

        ActionQueue.Clear();

        _penumbraModSettingChanged.Dispose();
        _penumbraGameObjectResourcePathResolved.Dispose();
        _penumbraDispose.Dispose();
        _penumbraInit.Dispose();
        _penumbraObjectIsRedrawn.Dispose();
        _heelsOffsetUpdate.Unsubscribe(HeelsOffsetChange);
    }

    public float GetHeelsOffset()
    {
        if (!CheckHeelsApi()) return 0.0f;
        return _heelsGetOffset.InvokeFunc();
    }

    public void HeelsSetOffsetForPlayer(float offset, IntPtr character)
    {
        if (!CheckHeelsApi()) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj != null)
            {
                Logger.Verbose("Applying Heels data to " + character.ToString("X"));
                _heelsRegisterPlayer.InvokeAction(gameObj, offset);
            }
        });
    }

    public void HeelsRestoreOffsetForPlayer(IntPtr character)
    {
        if (!CheckHeelsApi()) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj != null)
            {
                Logger.Verbose("Restoring Heels data to " + character.ToString("X"));
                _heelsUnregisterPlayer.InvokeAction(gameObj);
            }
        });
    }

    public string GetCustomizePlusScale()
    {
        if (!CheckCustomizePlusApi()) return string.Empty;
        var scale = _customizePlusGetBodyScale.InvokeFunc(_dalamudUtil.PlayerName);
        if (string.IsNullOrEmpty(scale)) return string.Empty;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(scale));
    }

    public void CustomizePlusSetBodyScale(IntPtr character, string scale)
    {
        if (!CheckCustomizePlusApi() || string.IsNullOrEmpty(scale)) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                string decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(scale));
                Logger.Verbose("CustomizePlus applying for " + c.Address.ToString("X"));
                _customizePlusSetBodyScaleToCharacter!.InvokeAction(decodedScale, c);
            }
        });
    }

    public void CustomizePlusRevert(IntPtr character)
    {
        if (!CheckCustomizePlusApi()) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                Logger.Verbose("CustomizePlus reverting for " + c.Address.ToString("X"));
                _customizePlusRevert!.InvokeAction(c);
            }
        });
    }

    public void GlamourerApplyAll(string? customization, IntPtr obj)
    {
        if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization)) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(obj);
            if (gameObj is Character c)
            {
                Logger.Verbose("Glamourer applying for " + c.Address.ToString("X"));
                _glamourerApplyAll!.InvokeAction(customization, c);
            }
        });
    }

    public void GlamourerApplyOnlyEquipment(string customization, IntPtr character)
    {
        if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization)) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                Logger.Verbose("Glamourer apply only equipment to " + c.Address.ToString("X"));
                _glamourerApplyOnlyEquipment!.InvokeAction(customization, c);
            }
        });
    }

    public void GlamourerApplyOnlyCustomization(string customization, IntPtr character)
    {
        if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization)) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                Logger.Verbose("Glamourer apply only customization to " + c.Address.ToString("X"));
                _glamourerApplyOnlyCustomization!.InvokeAction(customization, c);
            }
        });
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
        if (!CheckGlamourerApi()) return;
        ActionQueue.Enqueue(() => _glamourerRevertCustomization!.InvokeAction(character));
    }

    public string PenumbraGetMetaManipulations()
    {
        if (!CheckPenumbraApi()) return string.Empty;
        return _penumbraGetMetaManipulations.Invoke();
    }

    public string? PenumbraModDirectory;

    public string? GetPenumbraModDirectory()
    {
        if (!CheckPenumbraApi()) return null;
        return _penumbraResolveModDir!.Invoke().ToLowerInvariant();
    }

    public void PenumbraRedraw(IntPtr obj)
    {
        if (!CheckPenumbraApi()) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(obj);
            if (gameObj != null)
            {
                Logger.Verbose("Redrawing " + gameObj);
                _penumbraRedrawObject!.Invoke(gameObj, RedrawType.Redraw);
            }
        });
    }

    public void PenumbraRedraw(string actorName)
    {
        if (!CheckPenumbraApi()) return;
        ActionQueue.Enqueue(() => _penumbraRedraw!.Invoke(actorName, RedrawType.Redraw));
    }

    public void PenumbraRemoveTemporaryCollection(string characterName)
    {
        if (!CheckPenumbraApi()) return;
        ActionQueue.Enqueue(() =>
        {
            var collName = "Mare_" + characterName;
            Logger.Verbose("Removing temp collection for " + collName);
            var ret = _penumbraRemoveTemporaryMod.Invoke("MareChara", collName, 0);
            Logger.Verbose("RemoveTemporaryMod: " + ret);   
            var ret2 = _penumbraRemoveTemporaryCollection.Invoke(collName);
            Logger.Verbose("RemoveTemporaryCollection: " + ret2);
        });
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

    public void PenumbraSetTemporaryMods(string characterName, Dictionary<string, string> modPaths, string manipulationData)
    {
        if (!CheckPenumbraApi()) return;

        ActionQueue.Enqueue(() =>
        {
            var idx = _dalamudUtil.GetIndexFromObjectTableByName(characterName);
            if (idx == null)
            {
                return;
            }
            var collName = "Mare_" + characterName;
            var ret = _penumbraCreateNamedTemporaryCollection.Invoke(collName);
            Logger.Verbose("Creating Temp Collection " + collName + ", Success: " + ret);
            var retAssign = _penumbraAssignTemporaryCollection.Invoke(collName, idx.Value, c: true);
            Logger.Verbose("Assigning Temp Collection " + collName + " to index " + idx.Value);
            foreach (var mod in modPaths)
            {
                Logger.Verbose(mod.Key + " => " + mod.Value);
            }

            var ret2 = _penumbraAddTemporaryMod.Invoke("MareChara", collName, modPaths, manipulationData, 0);
            Logger.Verbose("Setting temp mods for " + collName + ", Success: " + ret2);
        });
    }

    public (string[] forward, string[][] reverse) PenumbraResolvePaths(string[] forward, string[] reverse)
    {
        return _penumbraResolvePaths.Invoke(forward, reverse);
    }

    private void RedrawEvent(IntPtr objectAddress, int objectTableIndex)
    {
        Mediator.Publish(new PenumbraRedrawMessage(objectAddress, objectTableIndex));
    }

    private void PenumbraInit()
    {
        Mediator.Publish(new PenumbraInitializedMessage());
        _penumbraRedraw!.Invoke("self", RedrawType.Redraw);
    }

    private void HeelsOffsetChange(float offset)
    {
        Mediator.Publish(new HeelsOffsetMessage(offset));
    }

    private void OnCustomizePlusScaleChange(string? scale)
    {
        if (scale != null) scale = Convert.ToBase64String(Encoding.UTF8.GetBytes(scale));
        Mediator.Publish(new CustomizePlusMessage(scale));
    }

    private void OnPalettePlusPaletteChange(Character character, string palette)
    {
        if (character.Address == 0 || character.Address != _dalamudUtil.PlayerPointer) return;
        if (palette != null) palette = Convert.ToBase64String(Encoding.UTF8.GetBytes(palette));
        Mediator.Publish(new PalettePlusMessage(palette));
    }

    public void PalettePlusSetPalette(IntPtr character, string palette)
    {
        if (!CheckPalettePlusApi()) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                string decodedPalette = Encoding.UTF8.GetString(Convert.FromBase64String(palette));

                if (string.IsNullOrEmpty(decodedPalette))
                {
                    Logger.Verbose("PalettePlus removing for " + c.Address.ToString("X"));
                    _palettePlusRemoveCharaPalette!.InvokeAction(c);
                }
                else
                {
                    Logger.Verbose("PalettePlus applying for " + c.Address.ToString("X"));
                    _palettePlusSetCharaPalette!.InvokeAction(c, decodedPalette);
                }
            }
        });
    }

    public string PalettePlusBuildPalette()
    {
        if (!CheckPalettePlusApi()) return string.Empty;
        var palette = _palettePlusBuildCharaPalette.InvokeFunc(_dalamudUtil.PlayerCharacter);
        if (string.IsNullOrEmpty(palette)) return string.Empty;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(palette));
    }

    public void PalettePlusRemovePalette(IntPtr character)
    {
        if (!CheckPalettePlusApi()) return;
        ActionQueue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is Character c)
            {
                Logger.Verbose("PalettePlus removing for " + c.Address.ToString("X"));
                _palettePlusRemoveCharaPalette!.InvokeAction(c);
            }
        });
    }

    private void PenumbraDispose()
    {
        Mediator.Publish(new PenumbraDisposedMessage());
        ActionQueue.Clear();
    }
}
