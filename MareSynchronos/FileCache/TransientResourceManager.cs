using MareSynchronos.API.Data.Enum;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Data;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.FileCache;

public sealed class TransientResourceManager : DisposableMediatorSubscriberBase
{
    private readonly HashSet<string> _cachedHandledPaths = new(StringComparer.Ordinal);
    private readonly TransientConfigService _configurationService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly string[] _fileTypesToHandle = new[] { "tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk" };
    private readonly HashSet<GameObjectHandler> _playerRelatedPointers = new();
    private HashSet<IntPtr> _cachedFrameAddresses = new();

    public TransientResourceManager(ILogger<TransientResourceManager> logger, TransientConfigService configurationService,
        DalamudUtilService dalamudUtil, MareMediator mediator) : base(logger, mediator)
    {
        _configurationService = configurationService;
        _dalamudUtil = dalamudUtil;

        SemiTransientResources.TryAdd(ObjectKind.Player, new HashSet<string>(StringComparer.Ordinal));
        if (_configurationService.Current.PlayerPersistentTransientCache.TryGetValue(PlayerPersistentDataKey, out var gamePaths))
        {
            int restored = 0;
            foreach (var gamePath in gamePaths)
            {
                if (string.IsNullOrEmpty(gamePath)) continue;

                try
                {
                    Logger.LogDebug("Loaded persistent transient resource {path}", gamePath);
                    SemiTransientResources[ObjectKind.Player].Add(gamePath);
                    restored++;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error during loading persistent transient resource {path}", gamePath);
                }
            }
            Logger.LogDebug("Restored {restored}/{total} semi persistent resources", restored, gamePaths.Count);
        }

        Mediator.Subscribe<PenumbraResourceLoadMessage>(this, Manager_PenumbraResourceLoadEvent);
        Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, (_) => Manager_PenumbraModSettingChanged());
        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => DalamudUtil_FrameworkUpdate());
        Mediator.Subscribe<ClassJobChangedMessage>(this, (_) => DalamudUtil_ClassJobChanged());
        Mediator.Subscribe<AddWatchedGameObjectHandler>(this, (msg) =>
        {
            _playerRelatedPointers.Add(msg.Handler);
        });
        Mediator.Subscribe<RemoveWatchedGameObjectHandler>(this, (msg) =>
        {
            _playerRelatedPointers.Remove(msg.Handler);
        });
    }

    private string PlayerPersistentDataKey => _dalamudUtil.PlayerName + "_" + _dalamudUtil.WorldId;
    private ConcurrentDictionary<ObjectKind, HashSet<string>> SemiTransientResources { get; } = new();
    private ConcurrentDictionary<IntPtr, HashSet<string>> TransientResources { get; } = new();

    public void CleanUpSemiTransientResources(ObjectKind objectKind, List<FileReplacement>? fileReplacement = null)
    {
        if (SemiTransientResources.ContainsKey(objectKind))
        {
            if (fileReplacement == null)
            {
                SemiTransientResources[objectKind].Clear();
                return;
            }

            foreach (var replacement in fileReplacement.Where(p => !p.HasFileReplacement).SelectMany(p => p.GamePaths).ToList())
            {
                SemiTransientResources[objectKind].RemoveWhere(p => string.Equals(p, replacement, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public HashSet<string> GetSemiTransientResources(ObjectKind objectKind)
    {
        if (SemiTransientResources.TryGetValue(objectKind, out var result))
        {
            return result ?? new HashSet<string>(StringComparer.Ordinal);
        }

        return new HashSet<string>(StringComparer.Ordinal);
    }

    public List<string> GetTransientResources(IntPtr gameObject)
    {
        if (TransientResources.TryGetValue(gameObject, out var result))
        {
            return result.ToList();
        }

        return new List<string>();
    }

    public void PersistTransientResources(IntPtr gameObject, ObjectKind objectKind)
    {
        if (!SemiTransientResources.ContainsKey(objectKind))
        {
            SemiTransientResources[objectKind] = new HashSet<string>(StringComparer.Ordinal);
        }

        if (!TransientResources.TryGetValue(gameObject, out var resources))
        {
            return;
        }

        var transientResources = resources.ToList();
        Logger.LogDebug("Persisting {count} transient resources", transientResources.Count);
        foreach (var gamePath in transientResources)
        {
            SemiTransientResources[objectKind].Add(gamePath);
        }

        if (objectKind == ObjectKind.Player && SemiTransientResources.TryGetValue(ObjectKind.Player, out var fileReplacements))
        {
            _configurationService.Current.PlayerPersistentTransientCache[PlayerPersistentDataKey] = fileReplacements.Where(f => !string.IsNullOrEmpty(f)).ToHashSet(StringComparer.Ordinal);
            _configurationService.Save();
        }
        TransientResources[gameObject].Clear();
    }

    internal void AddSemiTransientResource(ObjectKind objectKind, string item)
    {
        if (!SemiTransientResources.ContainsKey(objectKind))
        {
            SemiTransientResources[objectKind] = new HashSet<string>(StringComparer.Ordinal);
        }

        SemiTransientResources[objectKind].Add(item.ToLowerInvariant());
    }

    internal void ClearTransientPaths(IntPtr ptr, List<string> list)
    {
        if (TransientResources.TryGetValue(ptr, out var set))
        {
            set.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        TransientResources.Clear();
        SemiTransientResources.Clear();
        if (SemiTransientResources.ContainsKey(ObjectKind.Player))
        {
            _configurationService.Current.PlayerPersistentTransientCache[PlayerPersistentDataKey] = SemiTransientResources[ObjectKind.Player];
            _configurationService.Save();
        }
    }

    private void DalamudUtil_ClassJobChanged()
    {
        if (SemiTransientResources.TryGetValue(ObjectKind.Pet, out HashSet<string>? value))
        {
            value?.Clear();
        }
    }

    private void DalamudUtil_FrameworkUpdate()
    {
        _cachedFrameAddresses = _playerRelatedPointers.Select(c => c.CurrentAddress().GetAwaiter().GetResult()).ToHashSet();
        _cachedHandledPaths.Clear();
        foreach (var item in TransientResources.Where(item => !_dalamudUtil.IsGameObjectPresent(item.Key)).Select(i => i.Key).ToList())
        {
            Logger.LogDebug("Object not present anymore: {addr}", item.ToString("X"));
            TransientResources.TryRemove(item, out _);
        }
    }

    private void Manager_PenumbraModSettingChanged()
    {
        Task.Run(() =>
        {
            Logger.LogDebug("Penumbra Mod Settings changed, verifying SemiTransientResources");
            foreach (var item in SemiTransientResources)
            {
                Mediator.Publish(new TransientResourceChangedMessage(_dalamudUtil.PlayerPointer));
            }
        });
    }

    private void Manager_PenumbraResourceLoadEvent(PenumbraResourceLoadMessage msg)
    {
        var gamePath = msg.GamePath.ToLowerInvariant();
        var gameObject = msg.GameObject;
        var filePath = msg.FilePath;

        // ignore files already processed this frame
        if (_cachedHandledPaths.Contains(gamePath)) return;

        _cachedHandledPaths.Add(gamePath);

        // replace individual mtrl stuff
        if (filePath.StartsWith("|", StringComparison.OrdinalIgnoreCase))
        {
            filePath = filePath.Split("|")[2];
        }
        // replace filepath
        filePath = filePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);

        // ignore files that are the same
        var replacedGamePath = gamePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filePath, replacedGamePath, StringComparison.OrdinalIgnoreCase)) return;

        // ignore files to not handle
        if (!_fileTypesToHandle.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
        {
            _cachedHandledPaths.Add(gamePath);
            return;
        }

        // ignore files not belonging to anything player related
        if (!_cachedFrameAddresses.Contains(gameObject))
        {
            _cachedHandledPaths.Add(gamePath);
            return;
        }

        if (!TransientResources.ContainsKey(gameObject))
        {
            TransientResources[gameObject] = new(StringComparer.OrdinalIgnoreCase);
        }

        if (TransientResources[gameObject].Contains(replacedGamePath) ||
            SemiTransientResources.SelectMany(k => k.Value).Any(f => string.Equals(f, gamePath, StringComparison.OrdinalIgnoreCase)))
        {
            Logger.LogTrace("Not adding {replacedPath} : {filePath}", replacedGamePath, filePath);
        }
        else
        {
            TransientResources[gameObject].Add(replacedGamePath);
            Logger.LogDebug("Adding {replacedGamePath} for {gameObject} ({filePath})", replacedGamePath, gameObject.ToString("X"), filePath);
            Mediator.Publish(new TransientResourceChangedMessage(gameObject));
        }
    }
}