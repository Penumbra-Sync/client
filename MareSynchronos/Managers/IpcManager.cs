using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Action = System.Action;
using System.Collections.Concurrent;

namespace MareSynchronos.Managers
{
    public delegate void PenumbraRedrawEvent(IntPtr address, int objTblIdx);
    public delegate void HeelsOffsetChange(float change);
    public delegate void PenumbraResourceLoadEvent(IntPtr drawObject, string gamePath, string filePath);
    public class IpcManager : IDisposable
    {
        private readonly ICallGateSubscriber<int> _glamourerApiVersion;
        private readonly ICallGateSubscriber<string, GameObject?, object>? _glamourerApplyAll;
        private readonly ICallGateSubscriber<GameObject?, string>? _glamourerGetAllCustomization;
        private readonly ICallGateSubscriber<GameObject?, object> _glamourerRevertCustomization;
        private readonly ICallGateSubscriber<string, GameObject?, object>? _glamourerApplyOnlyEquipment;
        private readonly ICallGateSubscriber<string, GameObject?, object>? _glamourerApplyOnlyCustomization;
        private readonly ICallGateSubscriber<(int, int)> _penumbraApiVersion;
        private readonly ICallGateSubscriber<string, string, bool, (int, string)> _penumbraCreateTemporaryCollection;
        private readonly ICallGateSubscriber<string> _penumbraGetMetaManipulations;
        private readonly ICallGateSubscriber<object> _penumbraInit;
        private readonly ICallGateSubscriber<object> _penumbraDispose;
        private readonly ICallGateSubscriber<IntPtr, int, object?> _penumbraObjectIsRedrawn;
        private readonly ICallGateSubscriber<string, int, object>? _penumbraRedraw;
        private readonly ICallGateSubscriber<GameObject, int, object>? _penumbraRedrawObject;
        private readonly ICallGateSubscriber<string, int> _penumbraRemoveTemporaryCollection;
        private readonly ICallGateSubscriber<string>? _penumbraResolveModDir;
        private readonly ICallGateSubscriber<string, string>? _penumbraResolvePlayer;
        private readonly ICallGateSubscriber<string, string[]>? _reverseResolvePlayer;
        private readonly ICallGateSubscriber<string, string, Dictionary<string, string>, string, int, int>
            _penumbraSetTemporaryMod;
        private readonly ICallGateSubscriber<IntPtr, string, string, object?> _penumbraGameObjectResourcePathResolved;

        private readonly ICallGateSubscriber<string> _heelsGetApiVersion;
        private readonly ICallGateSubscriber<float> _heelsGetOffset;
        private readonly ICallGateSubscriber<float, object?> _heelsOffsetUpdate;
        private readonly ICallGateSubscriber<GameObject, float, object?> _heelsRegisterPlayer;
        private readonly ICallGateSubscriber<GameObject, object?> _heelsUnregisterPlayer;

        private readonly DalamudUtil _dalamudUtil;
        private readonly ConcurrentQueue<Action> actionQueue = new();

        public IpcManager(DalamudPluginInterface pi, DalamudUtil dalamudUtil)
        {
            Logger.Verbose("Creating " + nameof(IpcManager));

            _penumbraInit = pi.GetIpcSubscriber<object>("Penumbra.Initialized");
            _penumbraDispose = pi.GetIpcSubscriber<object>("Penumbra.Disposed");
            _penumbraResolvePlayer = pi.GetIpcSubscriber<string, string>("Penumbra.ResolvePlayerPath");
            _penumbraResolveModDir = pi.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
            _penumbraRedraw = pi.GetIpcSubscriber<string, int, object>("Penumbra.RedrawObjectByName");
            _penumbraRedrawObject = pi.GetIpcSubscriber<GameObject, int, object>("Penumbra.RedrawObject");
            _reverseResolvePlayer = pi.GetIpcSubscriber<string, string[]>("Penumbra.ReverseResolvePlayerPath");
            _penumbraApiVersion = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersions");
            _penumbraObjectIsRedrawn = pi.GetIpcSubscriber<IntPtr, int, object?>("Penumbra.GameObjectRedrawn");
            _penumbraGetMetaManipulations =
                pi.GetIpcSubscriber<string>("Penumbra.GetPlayerMetaManipulations");
            _penumbraSetTemporaryMod = pi.GetIpcSubscriber<string, string, Dictionary<string, string>, string, int,
            int>("Penumbra.AddTemporaryMod");
            _penumbraCreateTemporaryCollection =
                pi.GetIpcSubscriber<string, string, bool, (int, string)>("Penumbra.CreateTemporaryCollection");
            _penumbraRemoveTemporaryCollection =
                pi.GetIpcSubscriber<string, int>("Penumbra.RemoveTemporaryCollection");
            _penumbraGameObjectResourcePathResolved = pi.GetIpcSubscriber<IntPtr, string, string, object?>("Penumbra.GameObjectResourcePathResolved");

            _penumbraGameObjectResourcePathResolved.Subscribe(ResourceLoaded);
            _penumbraObjectIsRedrawn.Subscribe(RedrawEvent);
            _penumbraInit.Subscribe(PenumbraInit);
            _penumbraDispose.Subscribe(PenumbraDispose);

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

            if (Initialized)
            {
                PenumbraInitialized?.Invoke();
            }

            _dalamudUtil = dalamudUtil;
            _dalamudUtil.FrameworkUpdate += HandleActionQueue;
            _dalamudUtil.ZoneSwitchEnd += ClearActionQueue;
        }

        private void ClearActionQueue()
        {
            actionQueue.Clear();
        }

        private void ResourceLoaded(IntPtr ptr, string arg1, string arg2)
        {
            if (ptr != IntPtr.Zero && string.Compare(arg1, arg2, true, System.Globalization.CultureInfo.InvariantCulture) != 0)
            {
                PenumbraResourceLoadEvent?.Invoke(ptr, arg1, arg2);
                //Logger.Debug($"Resolved {ptr:X}: {arg1} => {arg2}");
            }
        }

        private void HandleActionQueue()
        {
            if (actionQueue.TryDequeue(out var action))
            {
                if (action == null) return;
                Logger.Debug("Execution action in queue: " + action.Method);
                action();
            }
        }

        public event VoidDelegate? PenumbraInitialized;
        public event VoidDelegate? PenumbraDisposed;
        public event PenumbraRedrawEvent? PenumbraRedrawEvent;
        public event HeelsOffsetChange? HeelsOffsetChangeEvent;
        public event PenumbraResourceLoadEvent? PenumbraResourceLoadEvent;

        public bool Initialized => CheckPenumbraApi();
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
                return _penumbraApiVersion.InvokeFunc() is { Item1: 4, Item2: >= 13 };
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
                return _heelsGetApiVersion.InvokeFunc() == "1.0.1";
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Logger.Verbose("Disposing " + nameof(IpcManager));

            int totalSleepTime = 0;
            while (actionQueue.Count > 0 && totalSleepTime < 2000)
            {
                Logger.Verbose("Waiting for actionqueue to clear...");
                System.Threading.Thread.Sleep(16);
                totalSleepTime += 16;
            }

            Logger.Verbose("Action queue clear or not, disposing");
            _dalamudUtil.FrameworkUpdate -= HandleActionQueue;
            _dalamudUtil.ZoneSwitchEnd -= ClearActionQueue;
            actionQueue.Clear();

            _penumbraDispose.Unsubscribe(PenumbraDispose);
            _penumbraInit.Unsubscribe(PenumbraInit);
            _penumbraObjectIsRedrawn.Unsubscribe(RedrawEvent);
            _penumbraGameObjectResourcePathResolved.Unsubscribe(ResourceLoaded);
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
            actionQueue.Enqueue(() =>
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
            actionQueue.Enqueue(() =>
            {
                var gameObj = _dalamudUtil.CreateGameObject(character);
                if (gameObj != null)
                {
                    Logger.Verbose("Restoring Heels data to " + character.ToString("X"));
                    _heelsUnregisterPlayer.InvokeAction(gameObj);
                }
            });
        }

        public void GlamourerApplyAll(string? customization, IntPtr obj)
        {
            if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization)) return;
            actionQueue.Enqueue(() =>
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
            actionQueue.Enqueue(() =>
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
            actionQueue.Enqueue(() =>
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
            actionQueue.Enqueue(() => _glamourerRevertCustomization!.InvokeAction(character));
        }

        public string PenumbraGetMetaManipulations()
        {
            if (!CheckPenumbraApi()) return string.Empty;
            return _penumbraGetMetaManipulations.InvokeFunc();
        }

        public string? PenumbraModDirectory()
        {
            if (!CheckPenumbraApi()) return null;
            return _penumbraResolveModDir!.InvokeFunc();
        }

        public void PenumbraRedraw(IntPtr obj)
        {
            if (!CheckPenumbraApi()) return;
            actionQueue.Enqueue(() =>
            {
                var gameObj = _dalamudUtil.CreateGameObject(obj);
                if (gameObj != null)
                {
                    Logger.Verbose("Redrawing " + gameObj);
                    _penumbraRedrawObject!.InvokeAction(gameObj, 0);
                }
            });
        }

        public void PenumbraRedraw(string actorName)
        {
            if (!CheckPenumbraApi()) return;
            actionQueue.Enqueue(() => _penumbraRedraw!.InvokeAction(actorName, 0));
        }

        public void PenumbraRemoveTemporaryCollection(string characterName)
        {
            if (!CheckPenumbraApi()) return;
            actionQueue.Enqueue(() =>
            {
                Logger.Verbose("Removing temp collection for " + characterName);
                _penumbraRemoveTemporaryCollection.InvokeFunc(characterName);
            });
        }

        public string PenumbraResolvePath(string path)
        {
            if (!CheckPenumbraApi()) return path;
            var resolvedPath = _penumbraResolvePlayer!.InvokeFunc(path);
            return resolvedPath ?? path;
        }

        public string[] PenumbraReverseResolvePlayer(string path)
        {
            if (!CheckPenumbraApi()) return new[] { path };
            var resolvedPaths = _reverseResolvePlayer!.InvokeFunc(path);
            if (resolvedPaths.Length == 0)
            {
                resolvedPaths = new[] { path };
            }
            return resolvedPaths;
        }

        public void PenumbraSetTemporaryMods(string characterName, Dictionary<string, string> modPaths, string manipulationData)
        {
            if (!CheckPenumbraApi()) return;

            actionQueue.Enqueue(() =>
            {
                var ret = _penumbraCreateTemporaryCollection.InvokeFunc("MareSynchronos", characterName, true);
                Logger.Verbose("Assigning temp mods for " + ret.Item2);
                foreach (var mod in modPaths)
                {
                    Logger.Verbose(mod.Key + " => " + mod.Value);
                }
                _penumbraSetTemporaryMod.InvokeFunc("MareSynchronos", ret.Item2, modPaths, manipulationData, 0);
            });
        }

        private void RedrawEvent(IntPtr objectAddress, int objectTableIndex)
        {
            PenumbraRedrawEvent?.Invoke(objectAddress, objectTableIndex);
        }

        private void PenumbraInit()
        {
            PenumbraInitialized?.Invoke();
            _penumbraRedraw!.InvokeAction("self", 0);
        }

        private void HeelsOffsetChange(float offset)
        {
            HeelsOffsetChangeEvent?.Invoke(offset);
        }

        private void PenumbraDispose()
        {
            PenumbraDisposed?.Invoke();
            actionQueue.Clear();
        }
    }
}
