using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;

namespace MareSynchronos.Managers
{
    public class IpcManager : IDisposable
    {
        private readonly DalamudPluginInterface pluginInterface;
        private ICallGateSubscriber<object> penumbraInit;
        private readonly ICallGateSubscriber<object> penumbraDispose;
        private ICallGateSubscriber<string, string, string>? penumbraResolvePath;
        private ICallGateSubscriber<string>? penumbraResolveModDir;
        private ICallGateSubscriber<string>? glamourerGetCharacterCustomization;
        private ICallGateSubscriber<string, string, object>? glamourerApplyCharacterCustomization;
        private ICallGateSubscriber<string, int, object>? penumbraRedraw;

        public bool Initialized { get; private set; } = false;

        public event EventHandler? IpcManagerInitialized;

        public IpcManager(DalamudPluginInterface pi)
        {
            pluginInterface = pi;
            penumbraInit = pluginInterface.GetIpcSubscriber<object>("Penumbra.Initialized");
            penumbraInit.Subscribe(Initialize);
            penumbraDispose = pluginInterface.GetIpcSubscriber<object>("Penumbra.Disposed");
            penumbraDispose.Subscribe(Uninitialize);
        }

        private bool CheckPenumbraAPI()
        {
            try
            {
                var penumbraApiVersion = pluginInterface.GetIpcSubscriber<int>("Penumbra.ApiVersion").InvokeFunc();
                return penumbraApiVersion >= 4;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckGlamourerAPI()
        {
            try
            {
                var glamourerApiVersion = pluginInterface.GetIpcSubscriber<int>("Glamourer.ApiVersion").InvokeFunc();
                return glamourerApiVersion >= 0;
            }
            catch
            {
                return false;
            }
        }

        public void Initialize()
        {
            if (Initialized) return;
            if (!CheckPenumbraAPI()) throw new Exception("Penumbra API is outdated or not available");
            if (!CheckGlamourerAPI()) throw new Exception("Glamourer API is oudated or not available");
            penumbraResolvePath = pluginInterface.GetIpcSubscriber<string, string, string>("Penumbra.ResolveCharacterPath");
            penumbraResolveModDir = pluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
            penumbraRedraw = pluginInterface.GetIpcSubscriber<string, int, object>("Penumbra.RedrawObjectByName");
            glamourerGetCharacterCustomization = pluginInterface.GetIpcSubscriber<string>("Glamourer.GetCharacterCustomization");
            glamourerApplyCharacterCustomization = pluginInterface.GetIpcSubscriber<string, string, object>("Glamourer.ApplyCharacterCustomization");
            Initialized = true;
            IpcManagerInitialized?.Invoke(this, new EventArgs());
            PluginLog.Debug("[IPC Manager] initialized");
        }

        private void Uninitialize()
        {
            penumbraResolvePath = null;
            penumbraResolveModDir = null;
            glamourerGetCharacterCustomization = null;
            glamourerApplyCharacterCustomization = null;
            Initialized = false;
            PluginLog.Debug("IPC Manager disposed");
        }

        public string? PenumbraResolvePath(string path, string characterName)
        {
            if (!Initialized) return null;
            return penumbraResolvePath!.InvokeFunc(path, characterName);
        }

        public string? PenumbraModDirectory()
        {
            if (!Initialized) return null;
            return penumbraResolveModDir!.InvokeFunc();
        }

        public string? GlamourerGetCharacterCustomization()
        {
            if (!Initialized) return null;
            return glamourerGetCharacterCustomization!.InvokeFunc();
        }

        public void GlamourerApplyCharacterCustomization(string customization, string characterName)
        {
            if (!Initialized) return;
            glamourerApplyCharacterCustomization!.InvokeAction(customization, characterName);
        }

        public void PenumbraRedraw(string actorName)
        {
            if (!Initialized) return;
            penumbraRedraw!.InvokeAction(actorName, 0);
        }

        public void Dispose()
        {
            Uninitialize();
            IpcManagerInitialized = null;
        }
    }
}
