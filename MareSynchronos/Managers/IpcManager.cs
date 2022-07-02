using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using MareSynchronos.Utils;

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
        private readonly ICallGateSubscriber<int> _penumbraApiVersion;
        private readonly ICallGateSubscriber<string, string, bool, (int, string)> _penumbraCreateTemporaryCollection;
        private readonly ICallGateSubscriber<string, string> _penumbraGetMetaManipulations;
        private readonly ICallGateSubscriber<object> _penumbraInit;
        private readonly ICallGateSubscriber<object> _penumbraDispose;
        private readonly ICallGateSubscriber<IntPtr, int, object?> _penumbraObjectIsRedrawn;
        private readonly ICallGateSubscriber<string, int, object>? _penumbraRedraw;
        private readonly ICallGateSubscriber<string, int> _penumbraRemoveTemporaryCollection;
        private readonly ICallGateSubscriber<string>? _penumbraResolveModDir;
        private readonly ICallGateSubscriber<string, string, string>? _penumbraResolvePath;
        private readonly ICallGateSubscriber<string, string, string[]>? _penumbraReverseResolvePath;
        private readonly ICallGateSubscriber<string, string, Dictionary<string, string>, string, int, int>
            _penumbraSetTemporaryMod;
        public IpcManager(DalamudPluginInterface pi)
        {
            Logger.Debug("Creating " + nameof(IpcManager));

            _penumbraInit = pi.GetIpcSubscriber<object>("Penumbra.Initialized");
            _penumbraDispose = pi.GetIpcSubscriber<object>("Penumbra.Disposed");
            _penumbraResolvePath = pi.GetIpcSubscriber<string, string, string>("Penumbra.ResolveCharacterPath");
            _penumbraResolveModDir = pi.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
            _penumbraRedraw = pi.GetIpcSubscriber<string, int, object>("Penumbra.RedrawObjectByName");
            _penumbraReverseResolvePath = pi.GetIpcSubscriber<string, string, string[]>("Penumbra.ReverseResolvePath");
            _penumbraApiVersion = pi.GetIpcSubscriber<int>("Penumbra.ApiVersion");
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
                PenumbraInitialized?.Invoke(null, EventArgs.Empty);
            }
        }

        public event EventHandler? PenumbraInitialized;
        public event EventHandler? PenumbraDisposed;
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
                return _penumbraApiVersion.InvokeFunc() >= 5;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Logger.Debug("Disposing " + nameof(IpcManager));

            _penumbraDispose.Unsubscribe(PenumbraDispose);
            _penumbraInit.Unsubscribe(PenumbraInit);
            _penumbraObjectIsRedrawn.Unsubscribe(RedrawEvent);
            Logger.Debug("IPC Manager disposed");
        }

        public void GlamourerApplyAll(string customization, GameObject character)
        {
            if (!CheckGlamourerApi()) return;
            Logger.Debug("Glamourer apply all to " + character);
            _glamourerApplyAll!.InvokeAction(customization, character);
        }

        public void GlamourerApplyOnlyEquipment(string customization, GameObject character)
        {
            if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization)) return;
            Logger.Debug("Glamourer apply only equipment to " + character);
            _glamourerApplyOnlyEquipment!.InvokeAction(customization, character);
        }

        public void GlamourerApplyOnlyCustomization(string customization, GameObject character)
        {
            if (!CheckGlamourerApi() || string.IsNullOrEmpty(customization)) return;
            Logger.Debug("Glamourer apply only customization to " + character);
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
            Logger.Debug("Creating temp collection for " + characterName);
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
            Logger.Debug("Removing temp collection for " + characterName);
            _penumbraRemoveTemporaryCollection.InvokeFunc(characterName);
        }

        public string? PenumbraResolvePath(string path, string characterName)
        {
            if (!CheckPenumbraApi()) return null;
            var resolvedPath = _penumbraResolvePath!.InvokeFunc(path, characterName);
            PluginLog.Verbose("Resolving " + path + Environment.NewLine + "=>" + string.Join(", ", resolvedPath));
            return resolvedPath;
        }

        public string[] PenumbraReverseResolvePath(string path, string characterName)
        {
            if (!CheckPenumbraApi()) return new[] { path };
            var resolvedPaths = _penumbraReverseResolvePath!.InvokeFunc(path, characterName);
            PluginLog.Verbose("ReverseResolving " + path + Environment.NewLine + "=>" + string.Join(", ", resolvedPaths));
            return resolvedPaths;
        }

        public void PenumbraSetTemporaryMods(string collectionName, Dictionary<string, string> modPaths, string manipulationData)
        {
            if (!CheckPenumbraApi()) return;

            Logger.Debug("Assigning temp mods for " + collectionName);
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
            PenumbraInitialized?.Invoke(null, EventArgs.Empty);
            _penumbraRedraw!.InvokeAction("self", 0);
        }

        private void PenumbraDispose()
        {
            PenumbraDisposed?.Invoke(null, EventArgs.Empty);
        }
    }
}
