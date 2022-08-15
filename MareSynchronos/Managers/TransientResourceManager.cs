using MareSynchronos.Models;
using MareSynchronos.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.Managers
{
    public delegate void TransientResourceLoadedEvent(IntPtr drawObject);

    public class TransientResourceManager : IDisposable
    {
        private readonly IpcManager manager;
        private readonly DalamudUtil dalamudUtil;
        public event TransientResourceLoadedEvent? TransientResourceLoaded;

        private Dictionary<IntPtr, HashSet<string>> TransientResources { get; } = new();
        public TransientResourceManager(IpcManager manager, DalamudUtil dalamudUtil)
        {
            manager.PenumbraResourceLoadEvent += Manager_PenumbraResourceLoadEvent;
            this.manager = manager;
            this.dalamudUtil = dalamudUtil;
            dalamudUtil.FrameworkUpdate += DalamudUtil_FrameworkUpdate;
        }

        private void DalamudUtil_FrameworkUpdate()
        {
            foreach (var item in TransientResources.ToList())
            {
                if (!dalamudUtil.IsDrawObjectPresent(item.Key))
                {
                    Logger.Debug("Object not present anymore: " + item.Key);
                    TransientResources.Remove(item.Key);
                }
            }
        }

        public List<string> GetTransientResources(IntPtr drawObject)
        {
            if (TransientResources.TryGetValue(drawObject, out var result))
            {
                return result.ToList();
            }

            return new List<string>();
        }

        private void Manager_PenumbraResourceLoadEvent(IntPtr drawObject, string gamePath, string filePath)
        {
            if (!TransientResources.ContainsKey(drawObject))
            {
                TransientResources[drawObject] = new();
            }

            if (filePath.StartsWith("|"))
            {
                filePath = filePath.Split("|")[2];
            }

            var newPath = filePath.ToLowerInvariant().Replace("\\", "/");

            if (filePath != gamePath && !TransientResources[drawObject].Contains(newPath))
            {
                TransientResources[drawObject].Add(newPath);
                Logger.Debug($"Adding {filePath.ToLowerInvariant().Replace("\\", "/")} for {drawObject}");
                TransientResourceLoaded?.Invoke(drawObject);
            }
        }

        public void RemoveTransientResource(IntPtr drawObject, FileReplacement fileReplacement)
        {
            if (TransientResources.ContainsKey(drawObject))
            {
                TransientResources[drawObject].RemoveWhere(f => fileReplacement.ResolvedPath == f);
            }
        }

        public void Dispose()
        {
            dalamudUtil.FrameworkUpdate -= DalamudUtil_FrameworkUpdate;
            manager.PenumbraResourceLoadEvent -= Manager_PenumbraResourceLoadEvent;
            TransientResources.Clear();
        }
    }
}
