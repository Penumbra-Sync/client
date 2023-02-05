using MareSynchronos.API.Data.Enum;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Mediator;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using System.Collections.Concurrent;

namespace MareSynchronos.Managers;


public class TransientResourceManager : MediatorSubscriberBase, IDisposable
{
    private readonly TransientConfigService _configurationService;
    private readonly DalamudUtil _dalamudUtil;

    public IntPtr[] PlayerRelatedPointers = Array.Empty<IntPtr>();
    private readonly string[] _fileTypesToHandle = new[] { "tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk" };
    private string PlayerPersistentDataKey => _dalamudUtil.PlayerName + "_" + _dalamudUtil.WorldId;

    private ConcurrentDictionary<IntPtr, HashSet<string>> TransientResources { get; } = new();
    private ConcurrentDictionary<ObjectKind, HashSet<string>> SemiTransientResources { get; } = new();
    public TransientResourceManager(TransientConfigService configurationService, DalamudUtil dalamudUtil, MareMediator mediator) : base(mediator)
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
                    Logger.Debug("Loaded persistent transient resource " + gamePath);
                    SemiTransientResources[ObjectKind.Player].Add(gamePath);
                    restored++;
                }
                catch (Exception ex)
                {
                    Logger.Warn("Error during loading persistent transient resource " + gamePath, ex);
                }

            }
            Logger.Debug($"Restored {restored}/{gamePaths.Count()} semi persistent resources");
        }

        Mediator.Subscribe<PenumbraResourceLoadMessage>(this, (msg) => Manager_PenumbraResourceLoadEvent((PenumbraResourceLoadMessage)msg));
        Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, (_) => Manager_PenumbraModSettingChanged());
        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => DalamudUtil_FrameworkUpdate());
        Mediator.Subscribe<ClassJobChangedMessage>(this, (_) => DalamudUtil_ClassJobChanged());
        Mediator.Subscribe<PlayerRelatedObjectPointerUpdateMessage>(this, (msg) => PlayerRelatedPointers = ((PlayerRelatedObjectPointerUpdateMessage)msg).RelatedObjects);
    }

    private void Manager_PenumbraModSettingChanged()
    {
        Task.Run(() =>
        {
            Logger.Debug("Penumbra Mod Settings changed, verifying SemiTransientResources");
            foreach (var item in SemiTransientResources)
            {
                Mediator.Publish(new TransientResourceChangedMessage(_dalamudUtil.PlayerPointer));
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

    public List<string> GetTransientResources(IntPtr gameObject)
    {
        if (TransientResources.TryGetValue(gameObject, out var result))
        {
            return result.ToList();
        }

        return new List<string>();
    }

    public HashSet<string> GetSemiTransientResources(ObjectKind objectKind)
    {
        if (SemiTransientResources.TryGetValue(objectKind, out var result))
        {
            return result ?? new HashSet<string>(StringComparer.Ordinal);
        }

        return new HashSet<string>(StringComparer.Ordinal);
    }

    private void Manager_PenumbraResourceLoadEvent(PenumbraResourceLoadMessage msg)
    {
        var gamePath = msg.GamePath.ToLowerInvariant();
        var gameObject = msg.GameObject;
        var filePath = msg.FilePath;
        if (!_fileTypesToHandle.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        if (!PlayerRelatedPointers.Contains(gameObject))
        {
            Logger.Debug("Got resource " + gamePath + " for ptr " + gameObject.ToString("X"));
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
            SemiTransientResources.Any(r => r.Value.Any(f => string.Equals(f, gamePath, StringComparison.OrdinalIgnoreCase))))
        {
            Logger.Verbose("Not adding " + replacedGamePath + ":" + filePath);
        }
        else
        {
            TransientResources[gameObject].Add(replacedGamePath);
            Logger.Debug($"Adding {replacedGamePath} for {gameObject} ({filePath})");
            Mediator.Publish(new TransientResourceChangedMessage(gameObject));
        }
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
        Logger.Debug("Persisting " + transientResources.Count + " transient resources");
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

    public override void Dispose()
    {
        base.Dispose();
        TransientResources.Clear();
        SemiTransientResources.Clear();
        if (SemiTransientResources.ContainsKey(ObjectKind.Player))
        {
            _configurationService.Current.PlayerPersistentTransientCache[PlayerPersistentDataKey] = SemiTransientResources[ObjectKind.Player];
            _configurationService.Save();
        }
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
}
