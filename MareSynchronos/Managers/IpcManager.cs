using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace MareSynchronos.Managers
{
    public class IpcManager : IDisposable
    {
        private readonly DalamudPluginInterface _pluginInterface;
        private readonly ICallGateSubscriber<object> _penumbraInit;
        private readonly ICallGateSubscriber<string, string, string>? _penumbraResolvePath;
        private readonly ICallGateSubscriber<string>? _penumbraResolveModDir;
        private readonly ICallGateSubscriber<string>? _glamourerGetCharacterCustomization;
        private readonly ICallGateSubscriber<string, string, object>? _glamourerApplyCharacterCustomization;
        private readonly ICallGateSubscriber<int> _penumbraApiVersion;
        private readonly ICallGateSubscriber<int> _glamourerApiVersion;
        private readonly ICallGateSubscriber<IntPtr, int, object?> _penumbraObjectIsRedrawn;
        private readonly ICallGateSubscriber<string, int, object>? _penumbraRedraw;
        private readonly ICallGateSubscriber<string, string, string[]>? _penumbraReverseResolvePath;
        private readonly ICallGateSubscriber<string, object> _glamourerRevertCustomization;

        private readonly ICallGateSubscriber<string, string, IReadOnlyDictionary<string, string>, List<string>, int, int>
            _penumbraSetTemporaryMod;
        private readonly ICallGateSubscriber<string, string, bool, (int, string)> _penumbraCreateTemporaryCollection;
        private readonly ICallGateSubscriber<string, int> _penumbraRemoveTemporaryCollection;

        public bool Initialized { get; private set; } = false;

        public event EventHandler? PenumbraRedrawEvent;

        public IpcManager(DalamudPluginInterface pi)
        {
            _pluginInterface = pi;

            _penumbraInit = _pluginInterface.GetIpcSubscriber<object>("Penumbra.Initialized");
            _penumbraResolvePath = _pluginInterface.GetIpcSubscriber<string, string, string>("Penumbra.ResolveCharacterPath");
            _penumbraResolveModDir = _pluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
            _penumbraRedraw = _pluginInterface.GetIpcSubscriber<string, int, object>("Penumbra.RedrawObjectByName");
            _glamourerGetCharacterCustomization = _pluginInterface.GetIpcSubscriber<string>("Glamourer.GetCharacterCustomization");
            _glamourerApplyCharacterCustomization = _pluginInterface.GetIpcSubscriber<string, string, object>("Glamourer.ApplyCharacterCustomization");
            _penumbraReverseResolvePath = _pluginInterface.GetIpcSubscriber<string, string, string[]>("Penumbra.ReverseResolvePath");
            _penumbraApiVersion = _pluginInterface.GetIpcSubscriber<int>("Penumbra.ApiVersion");
            _glamourerApiVersion = _pluginInterface.GetIpcSubscriber<int>("Glamourer.ApiVersion");
            _glamourerRevertCustomization = _pluginInterface.GetIpcSubscriber<string, object>("Glamourer.RevertCharacterCustomization");
            _penumbraObjectIsRedrawn = _pluginInterface.GetIpcSubscriber<IntPtr, int, object?>("Penumbra.GameObjectRedrawn");

            _penumbraObjectIsRedrawn.Subscribe(RedrawEvent);
            _penumbraInit.Subscribe(RedrawSelf);

            _penumbraSetTemporaryMod =
                _pluginInterface
                    .GetIpcSubscriber<string, string, IReadOnlyDictionary<string, string>, List<string>, int,
                        int>("Penumbra.AddTemporaryMod");

            _penumbraCreateTemporaryCollection =
                _pluginInterface.GetIpcSubscriber<string, string, bool, (int, string)>("Penumbra.CreateTemporaryCollection");
            _penumbraRemoveTemporaryCollection =
                _pluginInterface.GetIpcSubscriber<string, int>("Penumbra.RemoveTemporaryCollection");

            Initialized = true;
        }

        public bool CheckPenumbraApi()
        {
            try
            {
                return _penumbraApiVersion.InvokeFunc() >= 4;
            }
            catch
            {
                return false;
            }
        }

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

        private void RedrawEvent(IntPtr objectAddress, int objectTableIndex)
        {
            PenumbraRedrawEvent?.Invoke(objectTableIndex, EventArgs.Empty);
        }

        private void RedrawSelf()
        {
            _penumbraRedraw!.InvokeAction("self", 0);
        }

        private void Uninitialize()
        {
            _penumbraInit.Unsubscribe(RedrawSelf);
            _penumbraObjectIsRedrawn.Unsubscribe(RedrawEvent);
            Initialized = false;
            PluginLog.Debug("IPC Manager disposed");
        }

        public string[] PenumbraReverseResolvePath(string path, string characterName)
        {
            if (!CheckPenumbraApi()) return new[] { path };
            return _penumbraReverseResolvePath!.InvokeFunc(path, characterName);
        }

        public string? PenumbraResolvePath(string path, string characterName)
        {
            if (!CheckPenumbraApi()) return null;
            return _penumbraResolvePath!.InvokeFunc(path, characterName);
        }

        public string? PenumbraModDirectory()
        {
            if (!CheckPenumbraApi()) return null;
            return _penumbraResolveModDir!.InvokeFunc();
        }

        public string? GlamourerGetCharacterCustomization()
        {
            if (!CheckGlamourerApi()) return null;
            return _glamourerGetCharacterCustomization!.InvokeFunc();
        }

        public void GlamourerApplyCharacterCustomization(string customization, string characterName)
        {
            if (!CheckGlamourerApi()) return;
            _glamourerApplyCharacterCustomization!.InvokeAction(customization, characterName);
        }

        public void GlamourerRevertCharacterCustomization(string characterName)
        {
            if (!CheckGlamourerApi()) return;
            _glamourerRevertCustomization!.InvokeAction(characterName);
        }

        public void PenumbraRedraw(string actorName)
        {
            if (!CheckPenumbraApi()) return;
            _penumbraRedraw!.InvokeAction(actorName, 0);
        }

        public void PenumbraCreateTemporaryCollection(string characterName)
        {
            if (!CheckPenumbraApi()) return;
            PluginLog.Debug("Creating temp collection for " + characterName);
            //penumbraCreateTemporaryCollection.InvokeFunc("MareSynchronos", characterName, true);
        }

        public void PenumbraRemoveTemporaryCollection(string characterName)
        {
            if (!CheckPenumbraApi()) return;
            PluginLog.Debug("Removing temp collection for " + characterName);
            //penumbraRemoveTemporaryCollection.InvokeFunc(characterName);
        }

        public void PenumbraSetTemporaryMods(string characterName, IReadOnlyDictionary<string, string> modPaths)
        {
            if (!CheckPenumbraApi()) return;
            PluginLog.Debug("Assigning temp mods for " + characterName);
            //penumbraSetTemporaryMod.InvokeFunc("MareSynchronos", characterName, modPaths, new List<string>(), 0);
        }

        public void Dispose()
        {
            Uninitialize();
        }
    }
}
