using MareSynchronos.API.Data.Enum;
using MareSynchronos.Delegates;
using MareSynchronos.Factories;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Mediator;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using System.Collections.Concurrent;

namespace MareSynchronos.Managers;


public class TransientResourceManager : IDisposable
{
    private readonly IpcManager _ipcManager;
    private readonly ConfigurationService _configurationService;
    private readonly DalamudUtil _dalamudUtil;
    private readonly MareMediator _mediator;

    public event DrawObjectDelegate? TransientResourceLoaded;
    public IntPtr[] PlayerRelatedPointers = Array.Empty<IntPtr>();
    private readonly string[] _fileTypesToHandle = new[] { "tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk" };
    [Obsolete]
    private string PersistentDataCache => Path.Combine(_configurationService.ConfigurationDirectory, "PersistentTransientData.lst");
    private string PlayerPersistentDataKey => _dalamudUtil.PlayerName + "_" + _dalamudUtil.WorldId;

    private ConcurrentDictionary<IntPtr, HashSet<string>> TransientResources { get; } = new();
    private ConcurrentDictionary<ObjectKind, HashSet<FileReplacement>> SemiTransientResources { get; } = new();
    public TransientResourceManager(IpcManager manager, ConfigurationService configurationService, DalamudUtil dalamudUtil, FileReplacementFactory fileReplacementFactory, MareMediator mediator)
    {
        manager.PenumbraResourceLoadEvent += Manager_PenumbraResourceLoadEvent;
        manager.PenumbraModSettingChanged += Manager_PenumbraModSettingChanged;
        _ipcManager = manager;
        _configurationService = configurationService;
        _dalamudUtil = dalamudUtil;
        _mediator = mediator;
        _mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => DalamudUtil_FrameworkUpdate());
        _mediator.Subscribe<ClassJobChangedMessage>(this, (_) => DalamudUtil_ClassJobChanged());
        // migrate obsolete data to new format
        if (File.Exists(PersistentDataCache))
        {
            var persistentEntities = File.ReadAllLines(PersistentDataCache).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _configurationService.Current.PlayerPersistentTransientCache[PlayerPersistentDataKey] = persistentEntities;
            _configurationService.Save();
            File.Delete(PersistentDataCache);
        }

        SemiTransientResources.TryAdd(ObjectKind.Player, new HashSet<FileReplacement>());
        if (_configurationService.Current.PlayerPersistentTransientCache.TryGetValue(PlayerPersistentDataKey, out var linesInConfig))
        {
            int restored = 0;
            foreach (var file in linesInConfig)
            {
                try
                {
                    var fileReplacement = fileReplacementFactory.Create();
                    fileReplacement.ResolvePath(file);
                    if (fileReplacement.HasFileReplacement)
                    {
                        Logger.Debug("Loaded persistent transient resource " + file);
                        SemiTransientResources[ObjectKind.Player].Add(fileReplacement);
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Error during loading persistent transient resource " + file, ex);
                }

            }
            Logger.Debug($"Restored {restored}/{linesInConfig.Count()} semi persistent resources");
        }
    }

    private void Manager_PenumbraModSettingChanged()
    {
        bool successfulValidation = true;
        Task.Run(() =>
        {
            Logger.Debug("Penumbra Mod Settings changed, verifying SemiTransientResources");
            foreach (var item in SemiTransientResources)
            {
                item.Value.RemoveWhere(p =>
                {
                    var verified = p.Verify();
                    successfulValidation &= verified;
                    return !verified;
                });
                if (!successfulValidation)
                    TransientResourceLoaded?.Invoke(_dalamudUtil.PlayerPointer, -1);
            }
        });
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
            if (!_dalamudUtil.IsGameObjectPresent(item.Key))
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
        if (!_fileTypesToHandle.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
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
        if (string.Equals(filePath, replacedGamePath, StringComparison.OrdinalIgnoreCase)) return;

        if (TransientResources[gameObject].Contains(replacedGamePath) ||
            SemiTransientResources.Any(r => r.Value.Any(f =>
                string.Equals(f.GamePaths.First(), replacedGamePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ResolvedPath, filePath, StringComparison.OrdinalIgnoreCase))
            ))
        {
            Logger.Verbose("Not adding " + replacedGamePath + ":" + filePath);
            Logger.Verbose("SemiTransientAny: " + SemiTransientResources.Any(r => r.Value.Any(f => string.Equals(f.GamePaths.First(), replacedGamePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ResolvedPath, filePath, StringComparison.OrdinalIgnoreCase))).ToString() + ", TransientAny: " + TransientResources[gameObject].Contains(replacedGamePath));
        }
        else
        {
            TransientResources[gameObject].Add(replacedGamePath);
            Logger.Debug($"Adding {replacedGamePath} for {gameObject} ({filePath})");
            TransientResourceLoaded?.Invoke(gameObject, -1);
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
                var fileReplacement = createFileReplacement(gamePath.ToLowerInvariant(), arg2: true);
                if (!fileReplacement.HasFileReplacement)
                    fileReplacement = createFileReplacement(gamePath.ToLowerInvariant(), arg2: false);
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
                Logger.Warn("Issue during transient file persistence", ex);
            }
        }

        if (objectKind == ObjectKind.Player && SemiTransientResources.TryGetValue(ObjectKind.Player, out var fileReplacements))
        {
            _configurationService.Current.PlayerPersistentTransientCache[PlayerPersistentDataKey]
                = fileReplacements.SelectMany(p => p.GamePaths).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _configurationService.Save();
        }
        TransientResources[gameObject].Clear();
    }

    public void Dispose()
    {
        _mediator.Unsubscribe<FrameworkUpdateMessage>(this);
        _mediator.Unsubscribe<ClassJobChangedMessage>(this);
        _ipcManager.PenumbraResourceLoadEvent -= Manager_PenumbraResourceLoadEvent;
        _ipcManager.PenumbraModSettingChanged -= Manager_PenumbraModSettingChanged;
        TransientResources.Clear();
        SemiTransientResources.Clear();
        if (SemiTransientResources.ContainsKey(ObjectKind.Player))
        {
            _configurationService.Current.PlayerPersistentTransientCache[PlayerPersistentDataKey]
                = SemiTransientResources[ObjectKind.Player].SelectMany(p => p.GamePaths).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _configurationService.Save();
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
