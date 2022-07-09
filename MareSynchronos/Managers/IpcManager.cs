using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.GeneratedSheets;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Managers
{
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
        private readonly ICallGateSubscriber<string, string> _penumbraGetMetaManipulations;
        private readonly ICallGateSubscriber<object> _penumbraInit;
        private readonly ICallGateSubscriber<object> _penumbraDispose;
        private readonly ICallGateSubscriber<IntPtr, int, object?> _penumbraObjectIsRedrawn;
        private readonly ICallGateSubscriber<string, int, object>? _penumbraRedraw;
        private readonly ICallGateSubscriber<string, int> _penumbraRemoveTemporaryCollection;
        private readonly ICallGateSubscriber<string>? _penumbraResolveModDir;
        private readonly ICallGateSubscriber<string, string>? _penumbraResolvePlayer;
        private readonly ICallGateSubscriber<string, string[]>? _reverseResolvePlayer;
        private readonly ICallGateSubscriber<string, string, Dictionary<string, string>, string, int, int>
            _penumbraSetTemporaryMod;
        public IpcManager(DalamudPluginInterface pi)
        {
            Logger.Verbose("Creating " + nameof(IpcManager));

            _penumbraInit = pi.GetIpcSubscriber<object>("Penumbra.Initialized");
            _penumbraDispose = pi.GetIpcSubscriber<object>("Penumbra.Disposed");
            _penumbraResolvePlayer = pi.GetIpcSubscriber<string, string>("Penumbra.ResolvePlayerPath");
            _penumbraResolveModDir = pi.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
            _penumbraRedraw = pi.GetIpcSubscriber<string, int, object>("Penumbra.RedrawObjectByName");
            _reverseResolvePlayer = pi.GetIpcSubscriber<string, string[]>("Penumbra.ReverseResolvePlayerPath");
            _penumbraApiVersion = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersions");
            _penumbraObjectIsRedrawn = pi.GetIpcSubscriber<IntPtr, int, object?>("Penumbra.GameObjectRedrawn");
            _penumbraGetMetaManipulations =
                pi.GetIpcSubscriber<string, string>("Penumbra.GetMetaManipulations");

            _glamourerApiVersion = pi.GetIpcSubscriber<int>("Glamourer.ApiVersion");
            _glamourerGetAllCustomization = pi.GetIpcSubscriber<GameObject?, string>("Glamourer.GetAllCustomizationFromCharacter");
            _glamourerApplyAll = pi.GetIpcSubscriber<string, GameObject?, object>("Glamourer.ApplyAllToCharacter");
            _glamourerApplyOnlyCustomization = pi.GetIpcSubscriber<string, GameObject?, object>("Glamourer.ApplyOnlyCustomizationToCharacter");
            _glamourerApplyOnlyEquipment = pi.GetIpcSubscriber<string, GameObject?, object>("Glamourer.ApplyOnlyEquipmentToCharacter");
            _glamourerRevertCustomization = pi.GetIpcSubscriber<GameObject?, object>("Glamourer.RevertCharacter");

            _penumbraObjectIsRedrawn.Subscribe(RedrawEvent);
            _penumbraInit.Subscribe(PenumbraInit);
            _penumbraDispose.Subscribe(PenumbraDispose);

            _penumbraSetTemporaryMod =
                pi
                    .GetIpcSubscriber<string, string, Dictionary<string, string>, string, int,
                        int>("Penumbra.AddTemporaryMod");

            _penumbraCreateTemporaryCollection =
                pi.GetIpcSubscriber<string, string, bool, (int, string)>("Penumbra.CreateTemporaryCollection");
            _penumbraRemoveTemporaryCollection =
                pi.GetIpcSubscriber<string, int>("Penumbra.RemoveTemporaryCollection");

            if (Initialized)
            {
                PenumbraInitialized?.Invoke();
            }
        }

        public event VoidDelegate? PenumbraInitialized;
        public event VoidDelegate? PenumbraDisposed;
        public event EventHandler? PenumbraRedrawEvent;

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
                return _penumbraApiVersion.InvokeFunc() is { Item1: 4, Item2: >=10 };
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Logger.Verbose("Disposing " + nameof(IpcManager));

            _penumbraDispose.Unsubscribe(PenumbraDispose);
            _penumbraInit.Unsubscribe(PenumbraInit);
            _penumbraObjectIsRedrawn.Unsubscribe(RedrawEvent);
        }

        public void GlamourerApplyAll(string customization, GameObject character)
        {
            if (!CheckGlamourerApi()) return;
            Logger.Verbose("Glamourer apply all to " + character);
            _glamourerApplyAll!.InvokeAction(customization, character);
        }

        public void GlamourerApplyOnlyEquipment(string customization, GameObject character)
        {
            if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization)) return;
            Logger.Verbose("Glamourer apply only equipment to " + character);
            _glamourerApplyOnlyEquipment!.InvokeAction(customization, character);
        }

        public void GlamourerApplyOnlyCustomization(string customization, GameObject character)
        {
            if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization)) return;
            Logger.Verbose("Glamourer apply only customization to " + character);
            _glamourerApplyOnlyCustomization!.InvokeAction(customization, character);
        }

        public string GlamourerGetCharacterCustomization(GameObject character)
        {
            if (!CheckGlamourerApi()) return string.Empty;
            return _glamourerGetAllCustomization!.InvokeFunc(character);
        }

        public void GlamourerRevertCharacterCustomization(GameObject character)
        {
            if (!CheckGlamourerApi()) return;
            _glamourerRevertCustomization!.InvokeAction(character);
        }

        public string PenumbraCreateTemporaryCollection(string characterName)
        {
            if (!CheckPenumbraApi()) return string.Empty;
            Logger.Verbose("Creating temp collection for " + characterName);
            var ret = _penumbraCreateTemporaryCollection.InvokeFunc("MareSynchronos", characterName, true);
            return ret.Item2;
        }

        public string PenumbraGetMetaManipulations(string characterName)
        {
            if (!CheckPenumbraApi()) return string.Empty;
            return _penumbraGetMetaManipulations.InvokeFunc(characterName);
        }

        public string? PenumbraModDirectory()
        {
            if (!CheckPenumbraApi()) return null;
            return _penumbraResolveModDir!.InvokeFunc();
        }

        public void PenumbraRedraw(string actorName)
        {
            if (!CheckPenumbraApi()) return;
            _penumbraRedraw!.InvokeAction(actorName, 0);
        }

        public void PenumbraRemoveTemporaryCollection(string characterName)
        {
            if (!CheckPenumbraApi()) return;
            Logger.Verbose("Removing temp collection for " + characterName);
            _penumbraRemoveTemporaryCollection.InvokeFunc(characterName);
        }

        public string? PenumbraResolvePath(string path, string characterName)
        {
            if (!CheckPenumbraApi()) return null;
            var resolvedPath = _penumbraResolvePlayer!.InvokeFunc(path);
            Logger.Verbose("Resolved " + path + "=>" + string.Join(", ", resolvedPath));
            return resolvedPath;
        }

        public string[] PenumbraReverseResolvePlayer(string path)
        {
            if (!CheckPenumbraApi()) return new[] { path };
            var resolvedPaths = _reverseResolvePlayer!.InvokeFunc(path);
            if (resolvedPaths.Length == 0)
            {
                resolvedPaths = new[] { path };
            }
            Logger.Verbose("Reverse Resolved " + path + "=>" + string.Join(", ", resolvedPaths));
            return resolvedPaths;
        }

        public void PenumbraSetTemporaryMods(string collectionName, Dictionary<string, string> modPaths, string manipulationData)
        {
            if (!CheckPenumbraApi()) return;

            Logger.Verbose("Assigning temp mods for " + collectionName);
            foreach (var mod in modPaths)
            {
                Logger.Verbose(mod.Key + " => " + mod.Value);
            }
            var ret = _penumbraSetTemporaryMod.InvokeFunc("MareSynchronos", collectionName, modPaths, manipulationData, 0);
        }

        private void RedrawEvent(IntPtr objectAddress, int objectTableIndex)
        {
            PenumbraRedrawEvent?.Invoke(objectTableIndex, EventArgs.Empty);
        }

        private void PenumbraInit()
        {
            PenumbraInitialized?.Invoke();
            _penumbraRedraw!.InvokeAction("self", 0);
        }

        private void PenumbraDispose()
        {
            PenumbraDisposed?.Invoke();
        }
    }
}
