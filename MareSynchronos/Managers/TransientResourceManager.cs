using MareSynchronos.API;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MareSynchronos.Managers
{
    public delegate void TransientResourceLoadedEvent(IntPtr drawObject);

    public class TransientResourceManager : IDisposable
    {
        private readonly IpcManager manager;
        private readonly DalamudUtil dalamudUtil;

        public event TransientResourceLoadedEvent? TransientResourceLoaded;

        private Dictionary<IntPtr, HashSet<string>> TransientResources { get; } = new();
        private Dictionary<ObjectKind, HashSet<FileReplacement>> SemiTransientResources { get; } = new();
        public TransientResourceManager(IpcManager manager, DalamudUtil dalamudUtil)
        {
            manager.PenumbraResourceLoadEvent += Manager_PenumbraResourceLoadEvent;
            this.manager = manager;
            this.dalamudUtil = dalamudUtil;
            dalamudUtil.FrameworkUpdate += DalamudUtil_FrameworkUpdate;
            dalamudUtil.ClassJobChanged += DalamudUtil_ClassJobChanged;
        }

        private void DalamudUtil_ClassJobChanged()
        {
            if (SemiTransientResources.ContainsKey(ObjectKind.Pet))
            {
                SemiTransientResources[ObjectKind.Pet].Clear();
            }
        }

        private void DalamudUtil_FrameworkUpdate()
        {
            foreach (var item in TransientResources.ToList())
            {
                if (!dalamudUtil.IsGameObjectPresent(item.Key))
                {
                    Logger.Debug("Object not present anymore: " + item.Key);
                    TransientResources.Remove(item.Key);
                }
            }
        }

        public void CleanSemiTransientResources(ObjectKind objectKind)
        {
            if (SemiTransientResources.ContainsKey(objectKind))
            {
                SemiTransientResources[objectKind].Clear();
            }
        }

        public List<string> GetTransientResources(IntPtr gameObject)
        {
            if (TransientResources.TryGetValue(gameObject, out var result))
            {
                return result.ToList();
            }

            return new List<string>();
        }

        public List<FileReplacement> GetSemiTransientResources(ObjectKind objectKind)
        {
            if (SemiTransientResources.TryGetValue(objectKind, out var result))
            {
                return result.ToList();
            }

            return new List<FileReplacement>();
        }

        private void Manager_PenumbraResourceLoadEvent(IntPtr gameObject, string gamePath, string filePath)
        {
            if (!TransientResources.ContainsKey(gameObject))
            {
                TransientResources[gameObject] = new();
            }

            if (filePath.StartsWith("|"))
            {
                filePath = filePath.Split("|")[2];
            }

            filePath = filePath.ToLowerInvariant();

            var replacedGamePath = gamePath.ToLowerInvariant().Replace("\\", "/");

            if (TransientResources[gameObject].Contains(replacedGamePath) ||
                SemiTransientResources.Any(r => r.Value.Any(f => f.GamePaths.First().ToLowerInvariant() == replacedGamePath
                    && f.ResolvedPath.ToLowerInvariant() == filePath)))
            {
                Logger.Debug("Not adding " + replacedGamePath + ":" + filePath);
                Logger.Verbose("SemiTransientAny: " + SemiTransientResources.Any(r => r.Value.Any(f => f.GamePaths.First().ToLowerInvariant() == replacedGamePath
                    && f.ResolvedPath.ToLowerInvariant() == filePath)).ToString() + ", TransientAny: " + TransientResources[gameObject].Contains(replacedGamePath));
            }
            else
            {
                TransientResources[gameObject].Add(replacedGamePath);
                Logger.Debug($"Adding {replacedGamePath} for {gameObject} ({filePath})");
                TransientResourceLoaded?.Invoke(gameObject);
            }
        }

        public void RemoveTransientResource(IntPtr gameObject, FileReplacement fileReplacement)
        {
            if (TransientResources.ContainsKey(gameObject))
            {
                TransientResources[gameObject].RemoveWhere(f => fileReplacement.ResolvedPath.ToLowerInvariant() == f.ToLowerInvariant());
            }
        }

        public void PersistTransientResources(IntPtr gameObject, ObjectKind objectKind, Func<string, bool, FileReplacement> createFileReplacement)
        {
            if (!SemiTransientResources.ContainsKey(objectKind))
            {
                SemiTransientResources[objectKind] = new HashSet<FileReplacement>();
            }

            if (!TransientResources.TryGetValue(gameObject, out var resources))
            {
                return;
            }

            var transientResources = resources.ToList();
            Logger.Debug("Persisting " + transientResources.Count + " transient resources");
            foreach (var item in transientResources)
            {
                var existingResource = SemiTransientResources[objectKind].Any(f => f.GamePaths.First().ToLowerInvariant() == item.ToLowerInvariant());
                if (existingResource)
                {
                    Logger.Debug("Semi Transient resource replaced: " + item);
                    SemiTransientResources[objectKind].RemoveWhere(f => f.GamePaths.First().ToLowerInvariant() == item.ToLowerInvariant());
                }

                if (!SemiTransientResources[objectKind].Any(f => f.GamePaths.First().ToLowerInvariant() == item.ToLowerInvariant()))
                {
                    Logger.Debug("Persisting " + item.ToLowerInvariant());
                    var fileReplacement = createFileReplacement(item.ToLowerInvariant(), true);
                    if (!fileReplacement.HasFileReplacement)
                        fileReplacement = createFileReplacement(item.ToLowerInvariant(), false);
                    SemiTransientResources[objectKind].Add(fileReplacement);
                }
            }

            TransientResources[gameObject].Clear();
        }

        public void Dispose()
        {
            dalamudUtil.FrameworkUpdate -= DalamudUtil_FrameworkUpdate;
            manager.PenumbraResourceLoadEvent -= Manager_PenumbraResourceLoadEvent;
            dalamudUtil.ClassJobChanged -= DalamudUtil_ClassJobChanged;
            TransientResources.Clear();
        }

        internal void AddSemiTransientResource(ObjectKind objectKind, FileReplacement item)
        {
            if (!SemiTransientResources.ContainsKey(objectKind))
            {
                SemiTransientResources[objectKind] = new HashSet<FileReplacement>();
            }

            if (!SemiTransientResources[objectKind].Any(f => f.ResolvedPath.ToLowerInvariant() == item.ResolvedPath.ToLowerInvariant()))
            {
                SemiTransientResources[objectKind].Add(item);
            }
        }
    }
}
