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
        private ICallGateSubscriber<string, string, string>? penumbraResolvePath;
        private ICallGateSubscriber<string>? penumbraResolveModDir;
        private ICallGateSubscriber<string>? glamourerGetCharacterCustomization;
        private ICallGateSubscriber<string, string, object>? glamourerApplyCharacterCustomization;
        private ICallGateSubscriber<int> penumbraApiVersion;
        private ICallGateSubscriber<int> glamourerApiVersion;
        private ICallGateSubscriber<string, string> penumbraObjectIsRedrawn;
        private ICallGateSubscriber<string, int, object>? penumbraRedraw;
        private ICallGateSubscriber<string, string, string[]>? penumbraReverseResolvePath;

        public bool Initialized { get; private set; } = false;

        public event EventHandler? PenumbraRedrawEvent;

        public IpcManager(DalamudPluginInterface pi)
        {
            pluginInterface = pi;

            penumbraInit = pluginInterface.GetIpcSubscriber<object>("Penumbra.Initialized");
            penumbraResolvePath = pluginInterface.GetIpcSubscriber<string, string, string>("Penumbra.ResolveCharacterPath");
            penumbraResolveModDir = pluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
            penumbraRedraw = pluginInterface.GetIpcSubscriber<string, int, object>("Penumbra.RedrawObjectByName");
            glamourerGetCharacterCustomization = pluginInterface.GetIpcSubscriber<string>("Glamourer.GetCharacterCustomization");
            glamourerApplyCharacterCustomization = pluginInterface.GetIpcSubscriber<string, string, object>("Glamourer.ApplyCharacterCustomization");
            penumbraReverseResolvePath = pluginInterface.GetIpcSubscriber<string, string, string[]>("Penumbra.ReverseResolvePath");
            penumbraApiVersion = pluginInterface.GetIpcSubscriber<int>("Penumbra.ApiVersion");
            glamourerApiVersion = pluginInterface.GetIpcSubscriber<int>("Glamourer.ApiVersion");
            penumbraObjectIsRedrawn = pluginInterface.GetIpcSubscriber<string, string>("Penumbra.ObjectIsRedrawn");
            penumbraObjectIsRedrawn.Subscribe(RedrawEvent);
            penumbraInit.Subscribe(RedrawSelf);

            Initialized = true;
        }

        public bool CheckPenumbraAPI()
        {
            try
            {
                return penumbraApiVersion.InvokeFunc() >= 4;
            }
            catch
            {
                return false;
            }
        }

        public bool CheckGlamourerAPI()
        {
            try
            {
                return glamourerApiVersion.InvokeFunc() >= 0;
            }
            catch
            {
                return false;
            }
        }

        private void RedrawEvent(string actorName)
        {
            PenumbraRedrawEvent?.Invoke(actorName, EventArgs.Empty);
        }

        private void RedrawSelf()
        {
            penumbraRedraw!.InvokeAction("self", 0);
        }

        private void Uninitialize()
        {
            penumbraInit.Unsubscribe(RedrawSelf);
            penumbraObjectIsRedrawn.Unsubscribe(RedrawEvent);
            penumbraResolvePath = null;
            penumbraResolveModDir = null;
            glamourerGetCharacterCustomization = null;
            glamourerApplyCharacterCustomization = null;
            penumbraReverseResolvePath = null;
            Initialized = false;
            PluginLog.Debug("IPC Manager disposed");
        }

        public string[] PenumbraReverseResolvePath(string path, string characterName)
        {
            if (!CheckPenumbraAPI()) return new[] { path };
            return penumbraReverseResolvePath!.InvokeFunc(path, characterName);
        }

        public string? PenumbraResolvePath(string path, string characterName)
        {
            if (!CheckPenumbraAPI()) return null;
            return penumbraResolvePath!.InvokeFunc(path, characterName);
        }

        public string? PenumbraModDirectory()
        {
            if (!CheckPenumbraAPI()) return null;
            return penumbraResolveModDir!.InvokeFunc();
        }

        public string? GlamourerGetCharacterCustomization()
        {
            if (!CheckGlamourerAPI()) return null;
            return glamourerGetCharacterCustomization!.InvokeFunc();
        }

        public void GlamourerApplyCharacterCustomization(string customization, string characterName)
        {
            if (!CheckGlamourerAPI()) return;
            glamourerApplyCharacterCustomization!.InvokeAction(customization, characterName);
        }

        public void PenumbraRedraw(string actorName)
        {
            if (!CheckPenumbraAPI()) return;
            penumbraRedraw!.InvokeAction(actorName, 0);
        }

        public void Dispose()
        {
            Uninitialize();
        }
    }
}
