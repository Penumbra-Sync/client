using MareSynchronos.API;
using MareSynchronos.Factories;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MareSynchronos.Managers;

public delegate void TransientResourceLoadedEvent(IntPtr drawObject);

public class TransientResourceManager : IDisposable
{
    private readonly IpcManager manager;
    private readonly DalamudUtil dalamudUtil;
    private readonly FileReplacementFactory fileReplacementFactory;
    private readonly string configurationDirectory;

    public event TransientResourceLoadedEvent? TransientResourceLoaded;
    public IntPtr[] PlayerRelatedPointers = Array.Empty<IntPtr>();
    private readonly string[] FileTypesToHandle = new[] { "tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp" };
    private string PersistentDataCache => Path.Combine(configurationDirectory, "PersistentTransientData.lst");

    private ConcurrentDictionary<IntPtr, HashSet<string>> TransientResources { get; } = new();
    private ConcurrentDictionary<ObjectKind, HashSet<FileReplacement>> SemiTransientResources { get; } = new();
    public TransientResourceManager(IpcManager manager, DalamudUtil dalamudUtil, FileReplacementFactory fileReplacementFactory, string configurationDirectory)
    {
        manager.PenumbraResourceLoadEvent += Manager_PenumbraResourceLoadEvent;
        this.manager = manager;
        this.dalamudUtil = dalamudUtil;
        this.fileReplacementFactory = fileReplacementFactory;
        this.configurationDirectory = configurationDirectory;
        dalamudUtil.FrameworkUpdate += DalamudUtil_FrameworkUpdate;
        dalamudUtil.ClassJobChanged += DalamudUtil_ClassJobChanged;
        if (File.Exists(PersistentDataCache))
        {
            var persistentEntities = File.ReadAllLines(PersistentDataCache);
            SemiTransientResources.TryAdd(ObjectKind.Player, new HashSet<FileReplacement>());
            int restored = 0;
            foreach (var line in persistentEntities)
            {
                var fileReplacement = fileReplacementFactory.Create();
                fileReplacement.ResolvePath(line);
                if (fileReplacement.HasFileReplacement)
                {
                    Logger.Debug("Loaded persistent transient resource " + line);
                    SemiTransientResources[ObjectKind.Player].Add(fileReplacement);
                    restored++;
                }
            }

            Logger.Debug($"Restored {restored}/{persistentEntities.Count()} semi persistent resources");
        }
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
                Logger.Debug("Object not present anymore: " + item.Key.ToString("X"));
                TransientResources.TryRemove(item.Key, out _);
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
        if (!FileTypesToHandle.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        if (!PlayerRelatedPointers.Contains(gameObject))
        {
            return;
        }

        if (!TransientResources.ContainsKey(gameObject))
        {
            TransientResources[gameObject] = new(StringComparer.OrdinalIgnoreCase);
        }

        if (filePath.StartsWith("|", StringComparison.OrdinalIgnoreCase))
        {
            filePath = filePath.Split("|")[2];
        }

        filePath = filePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);

        var replacedGamePath = gamePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);

        if (TransientResources[gameObject].Contains(replacedGamePath) ||
            SemiTransientResources.Any(r => r.Value.Any(f => string.Equals(f.GamePaths.First(), replacedGamePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(f.ResolvedPath, filePath, StringComparison.OrdinalIgnoreCase))))
        {
            Logger.Verbose("Not adding " + replacedGamePath + ":" + filePath);
            Logger.Verbose("SemiTransientAny: " + SemiTransientResources.Any(r => r.Value.Any(f => string.Equals(f.GamePaths.First(), replacedGamePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ResolvedPath, filePath, StringComparison.OrdinalIgnoreCase))).ToString() + ", TransientAny: " + TransientResources[gameObject].Contains(replacedGamePath));
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
            TransientResources[gameObject].RemoveWhere(f => fileReplacement.GamePaths.Any(g => string.Equals(g, f, StringComparison.OrdinalIgnoreCase)));
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

        SemiTransientResources[objectKind].RemoveWhere(p => !p.Verify());

        var transientResources = resources.ToList();
        Logger.Debug("Persisting " + transientResources.Count + " transient resources");
        foreach (var gamePath in transientResources)
        {
            var existingResource = SemiTransientResources[objectKind].Any(f => string.Equals(f.GamePaths.First(), gamePath, StringComparison.OrdinalIgnoreCase));
            if (existingResource)
            {
                Logger.Debug("Semi Transient resource replaced: " + gamePath);
                SemiTransientResources[objectKind].RemoveWhere(f => string.Equals(f.GamePaths.First(), gamePath, StringComparison.OrdinalIgnoreCase));
            }

            try
            {
                var fileReplacement = createFileReplacement(gamePath.ToLowerInvariant(), true);
                if (!fileReplacement.HasFileReplacement)
                    fileReplacement = createFileReplacement(gamePath.ToLowerInvariant(), false);
                if (fileReplacement.HasFileReplacement)
                {
                    Logger.Debug("Persisting " + gamePath.ToLowerInvariant());
                    if (SemiTransientResources[objectKind].Add(fileReplacement))
                    {
                        Logger.Debug("Added " + fileReplacement);
                    }
                    else
                    {
                        Logger.Debug("Not added " + fileReplacement);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Issue during transient file persistence");
                Logger.Warn(ex.Message);
                Logger.Warn(ex.StackTrace.ToString());
            }
        }

        if (objectKind == ObjectKind.Player && SemiTransientResources.TryGetValue(ObjectKind.Player, out var fileReplacements))
        {
            File.WriteAllLines(PersistentDataCache, fileReplacements.SelectMany(p => p.GamePaths).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        TransientResources[gameObject].Clear();
    }

    public void Dispose()
    {
        dalamudUtil.FrameworkUpdate -= DalamudUtil_FrameworkUpdate;
        manager.PenumbraResourceLoadEvent -= Manager_PenumbraResourceLoadEvent;
        dalamudUtil.ClassJobChanged -= DalamudUtil_ClassJobChanged;
        TransientResources.Clear();
        SemiTransientResources.Clear();
        if (SemiTransientResources.ContainsKey(ObjectKind.Player))
        {
            File.WriteAllLines(PersistentDataCache, SemiTransientResources[ObjectKind.Player].SelectMany(p => p.GamePaths).Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }

    internal void AddSemiTransientResource(ObjectKind objectKind, FileReplacement item)
    {
        if (!SemiTransientResources.ContainsKey(objectKind))
        {
            SemiTransientResources[objectKind] = new HashSet<FileReplacement>();
        }

        if (!SemiTransientResources[objectKind].Any(f => string.Equals(f.ResolvedPath, item.ResolvedPath, StringComparison.OrdinalIgnoreCase)))
        {
            SemiTransientResources[objectKind].Add(item);
        }
    }
}
