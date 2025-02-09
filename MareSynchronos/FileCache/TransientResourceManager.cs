using MareSynchronos.API.Data.Enum;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Data;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.FileCache;

public sealed class TransientResourceManager : DisposableMediatorSubscriberBase
{
    private readonly object _cacheAdditionLock = new();
    private readonly HashSet<string> _cachedHandledPaths = new(StringComparer.Ordinal);
    private readonly TransientConfigService _configurationService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly string[] _fileTypesToHandle = ["tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk"];
    private readonly string[] _fileTypesToHandleRecording = ["tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk", "tex", "mdl", "mtrl"];
    private readonly HashSet<GameObjectHandler> _playerRelatedPointers = [];
    private ConcurrentDictionary<IntPtr, ObjectKind> _cachedFrameAddresses = [];
    private ConcurrentDictionary<ObjectKind, HashSet<string>>? _semiTransientResources = null;
    private uint _lastClassJobId = uint.MaxValue;
    public bool IsTransientRecording { get; private set; } = false;

    public TransientResourceManager(ILogger<TransientResourceManager> logger, TransientConfigService configurationService,
            DalamudUtilService dalamudUtil, MareMediator mediator) : base(logger, mediator)
    {
        _configurationService = configurationService;
        _dalamudUtil = dalamudUtil;

        Mediator.Subscribe<PenumbraResourceLoadMessage>(this, Manager_PenumbraResourceLoadEvent);
        Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, (_) => Manager_PenumbraModSettingChanged());
        Mediator.Subscribe<PriorityFrameworkUpdateMessage>(this, (_) => DalamudUtil_FrameworkUpdate());
        Mediator.Subscribe<GameObjectHandlerCreatedMessage>(this, (msg) =>
        {
            if (!msg.OwnedObject) return;
            _playerRelatedPointers.Add(msg.GameObjectHandler);
        });
        Mediator.Subscribe<GameObjectHandlerDestroyedMessage>(this, (msg) =>
        {
            if (!msg.OwnedObject) return;
            _playerRelatedPointers.Remove(msg.GameObjectHandler);
        });
    }

    private TransientConfig.TransientPlayerConfig PlayerConfig
    {
        get
        {
            if (!_configurationService.Current.TransientConfigs.TryGetValue(PlayerPersistentDataKey, out var transientConfig))
            {
                _configurationService.Current.TransientConfigs[PlayerPersistentDataKey] = transientConfig = new();
            }

            return transientConfig;
        }
    }

    private string PlayerPersistentDataKey => _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult() + "_" + _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();
    private ConcurrentDictionary<ObjectKind, HashSet<string>> SemiTransientResources
    {
        get
        {
            if (_semiTransientResources == null)
            {
                _semiTransientResources = new();
                PlayerConfig.JobSpecificCache.TryGetValue(_dalamudUtil.ClassJobId, out var jobSpecificData);
                _semiTransientResources[ObjectKind.Player] = PlayerConfig.GlobalPersistentCache.Concat(jobSpecificData ?? []).ToHashSet(StringComparer.Ordinal);
                PlayerConfig.JobSpecificPetCache.TryGetValue(_dalamudUtil.ClassJobId, out var petSpecificData);
                _semiTransientResources[ObjectKind.Pet] = [.. petSpecificData ?? []];
            }

            return _semiTransientResources;
        }
    }
    private ConcurrentDictionary<ObjectKind, HashSet<string>> TransientResources { get; } = new();

    public void CleanUpSemiTransientResources(ObjectKind objectKind, List<FileReplacement>? fileReplacement = null)
    {
        if (SemiTransientResources.TryGetValue(objectKind, out HashSet<string>? value))
        {
            if (fileReplacement == null)
            {
                value.Clear();
                return;
            }

            foreach (var replacement in fileReplacement.Where(p => !p.HasFileReplacement).SelectMany(p => p.GamePaths).ToList())
            {
                PlayerConfig.RemovePath(replacement);
            }

            // force reload semi transient resources
            _semiTransientResources = null;
            _configurationService.Save();
        }
    }

    public HashSet<string> GetSemiTransientResources(ObjectKind objectKind)
    {
        SemiTransientResources.TryGetValue(objectKind, out var result);

        return result ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public void PersistTransientResources(ObjectKind objectKind)
    {
        if (!SemiTransientResources.TryGetValue(objectKind, out HashSet<string>? semiTransientResources))
        {
            SemiTransientResources[objectKind] = semiTransientResources = new(StringComparer.Ordinal);
        }

        if (!TransientResources.TryGetValue(objectKind, out var resources))
        {
            return;
        }

        var transientResources = resources.ToList();
        Logger.LogDebug("Persisting {count} transient resources", transientResources.Count);
        List<string> newlyAddedGamePaths = resources.Except(semiTransientResources, StringComparer.Ordinal).ToList();
        foreach (var gamePath in transientResources)
        {
            semiTransientResources.Add(gamePath);
        }

        if (objectKind == ObjectKind.Player && newlyAddedGamePaths.Any())
        {
            foreach (var item in newlyAddedGamePaths.Where(f => !string.IsNullOrEmpty(f)))
            {
                PlayerConfig.AddOrElevate(_dalamudUtil.ClassJobId, item);
            }

            _configurationService.Save();
        }
        else if (objectKind == ObjectKind.Pet && newlyAddedGamePaths.Any())
        {
            foreach (var item in newlyAddedGamePaths.Where(f => !string.IsNullOrEmpty(f)))
            {
                if (!PlayerConfig.JobSpecificPetCache.TryGetValue(_dalamudUtil.ClassJobId, out var petPerma))
                {
                    PlayerConfig.JobSpecificPetCache[_dalamudUtil.ClassJobId] = petPerma = [];
                }

                petPerma.Add(item);
            }

            _configurationService.Save();
        }

        TransientResources[objectKind].Clear();
    }

    public void RemoveTransientResource(ObjectKind objectKind, string path)
    {
        if (SemiTransientResources.TryGetValue(objectKind, out var resources))
        {
            resources.RemoveWhere(f => string.Equals(path, f, StringComparison.Ordinal));
            if (objectKind == ObjectKind.Player)
            {
                PlayerConfig.RemovePath(path);
                _configurationService.Save();
            }
        }
    }

    internal bool AddTransientResource(ObjectKind objectKind, string item)
    {
        if (SemiTransientResources.TryGetValue(objectKind, out var semiTransient) && semiTransient != null && semiTransient.Contains(item))
            return false;

        if (!TransientResources.TryGetValue(objectKind, out HashSet<string>? value))
        {
            value = new HashSet<string>(StringComparer.Ordinal);
            TransientResources[objectKind] = value;
        }

        value.Add(item.ToLowerInvariant());
        return true;
    }

    internal void ClearTransientPaths(ObjectKind objectKind, List<string> list)
    {
        if (TransientResources.TryGetValue(objectKind, out var set))
        {
            foreach (var file in set.Where(p => list.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                Logger.LogTrace("Removing From Transient: {file}", file);
            }

            int removed = set.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
            Logger.LogInformation("Removed {removed} previously existing transient paths", removed);
        }

        bool reloadSemiTransient = false;
        if (objectKind == ObjectKind.Player && SemiTransientResources.TryGetValue(objectKind, out var semiset))
        {
            foreach (var file in semiset.Where(p => list.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                Logger.LogTrace("Removing From SemiTransient: {file}", file);
                PlayerConfig.RemovePath(file);
            }

            int removed = semiset.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
            Logger.LogInformation("Removed {removed} previously existing semi transient paths", removed);
            if (removed > 0)
            {
                reloadSemiTransient = true;
                _configurationService.Save();
            }
        }

        if (reloadSemiTransient)
            _semiTransientResources = null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        TransientResources.Clear();
        SemiTransientResources.Clear();
    }

    private void DalamudUtil_FrameworkUpdate()
    {
        _cachedFrameAddresses = _cachedFrameAddresses = new(_playerRelatedPointers.Where(k => k.Address != nint.Zero).ToDictionary(c => c.CurrentAddress(), c => c.ObjectKind));
        lock (_cacheAdditionLock)
        {
            _cachedHandledPaths.Clear();
        }

        if (_lastClassJobId != _dalamudUtil.ClassJobId)
        {
            _lastClassJobId = _dalamudUtil.ClassJobId;
            if (SemiTransientResources.TryGetValue(ObjectKind.Pet, out HashSet<string>? value))
            {
                value?.Clear();
            }

            // reload config for current new classjob
            PlayerConfig.JobSpecificCache.TryGetValue(_dalamudUtil.ClassJobId, out var jobSpecificData);
            SemiTransientResources[ObjectKind.Player] = PlayerConfig.GlobalPersistentCache.Concat(jobSpecificData ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            PlayerConfig.JobSpecificPetCache.TryGetValue(_dalamudUtil.ClassJobId, out var petSpecificData);
            SemiTransientResources[ObjectKind.Pet] = [.. petSpecificData ?? []];
        }

        foreach (var kind in Enum.GetValues(typeof(ObjectKind)))
        {
            if (!_cachedFrameAddresses.Any(k => k.Value == (ObjectKind)kind) && TransientResources.Remove((ObjectKind)kind, out _))
            {
                Logger.LogDebug("Object not present anymore: {kind}", kind.ToString());
            }
        }
    }

    private void Manager_PenumbraModSettingChanged()
    {
        _ = Task.Run(() =>
        {
            Logger.LogDebug("Penumbra Mod Settings changed, verifying SemiTransientResources");
            foreach (var item in _playerRelatedPointers)
            {
                Mediator.Publish(new TransientResourceChangedMessage(item.Address));
            }
        });
    }

    public void RebuildSemiTransientResources()
    {
        _semiTransientResources = null;
    }

    private void Manager_PenumbraResourceLoadEvent(PenumbraResourceLoadMessage msg)
    {
        var gamePath = msg.GamePath.ToLowerInvariant();
        var gameObjectAddress = msg.GameObject;
        var filePath = msg.FilePath;

        // ignore files already processed this frame
        if (_cachedHandledPaths.Contains(gamePath)) return;

        lock (_cacheAdditionLock)
        {
            _cachedHandledPaths.Add(gamePath);
        }

        // replace individual mtrl stuff
        if (filePath.StartsWith("|", StringComparison.OrdinalIgnoreCase))
        {
            filePath = filePath.Split("|")[2];
        }
        // replace filepath
        filePath = filePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);

        // ignore files that are the same
        var replacedGamePath = gamePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filePath, replacedGamePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // ignore files to not handle
        var handledTypes = IsTransientRecording ? _fileTypesToHandleRecording : _fileTypesToHandle;
        if (!handledTypes.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
        {
            lock (_cacheAdditionLock)
            {
                _cachedHandledPaths.Add(gamePath);
            }
            return;
        }

        // ignore files not belonging to anything player related
        if (!_cachedFrameAddresses.TryGetValue(gameObjectAddress, out var objectKind))
        {
            lock (_cacheAdditionLock)
            {
                _cachedHandledPaths.Add(gamePath);
            }
            return;
        }

        // ^ all of the code above is just to sanitize the data

        if (!TransientResources.TryGetValue(objectKind, out HashSet<string>? value))
        {
            value = new(StringComparer.OrdinalIgnoreCase);
            TransientResources[objectKind] = value;
        }

        var owner = _playerRelatedPointers.FirstOrDefault(f => f.Address == gameObjectAddress);
        bool alreadyTransient = false;

        if (value.Contains(replacedGamePath)
            || SemiTransientResources.SelectMany(k => k.Value).Any(f => string.Equals(f, gamePath, StringComparison.OrdinalIgnoreCase)))
        {
            if (!IsTransientRecording)
                Logger.LogTrace("Not adding {replacedPath} : {filePath}, Reason: Transient: {contains}, SemiTransient: {contains2}", replacedGamePath, filePath,
                    value.Contains(replacedGamePath), SemiTransientResources.SelectMany(k => k.Value).Any(f => string.Equals(f, gamePath, StringComparison.OrdinalIgnoreCase)));
            alreadyTransient = true;
        }
        else
        {
            if (!IsTransientRecording)
            {
                bool isAdded = value.Add(replacedGamePath);
                if (isAdded)
                {
                    Logger.LogDebug("Adding {replacedGamePath} for {gameObject} ({filePath})", replacedGamePath, owner?.ToString() ?? gameObjectAddress.ToString("X"), filePath);
                    SendTransients(gameObjectAddress);
                }
            }
        }

        if (owner != null && IsTransientRecording)
        {
            _recordedTransients.Add(new TransientRecord(owner, replacedGamePath, filePath, alreadyTransient) { AddTransient = !alreadyTransient });
        }
    }

    private void SendTransients(nint gameObject)
    {
        _ = Task.Run(async () =>
        {
            _sendTransientCts?.Cancel();
            _sendTransientCts?.Dispose();
            _sendTransientCts = new();
            var token = _sendTransientCts.Token;
            await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            Mediator.Publish(new TransientResourceChangedMessage(gameObject));
        });
    }

    public void StartRecording(CancellationToken token)
    {
        if (IsTransientRecording) return;
        _recordedTransients.Clear();
        IsTransientRecording = true;
        RecordTimeRemaining.Value = TimeSpan.FromSeconds(150);
        _ = Task.Run(async () =>
        {
            try
            {
                while (RecordTimeRemaining.Value > TimeSpan.Zero && !token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                    RecordTimeRemaining.Value = RecordTimeRemaining.Value.Subtract(TimeSpan.FromSeconds(1));
                }
            }
            finally
            {
                IsTransientRecording = false;
            }
        });
    }

    public async Task WaitForRecording(CancellationToken token)
    {
        while (IsTransientRecording)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        }
    }

    internal void SaveRecording()
    {
        HashSet<nint> addedTransients = [];
        foreach (var item in _recordedTransients)
        {
            if (!item.AddTransient || item.AlreadyTransient) continue;
            if (!TransientResources.TryGetValue(item.Owner.ObjectKind, out var transient))
            {
                TransientResources[item.Owner.ObjectKind] = transient = [];
            }

            Logger.LogTrace("Adding recorded: {gamePath} => {filePath}", item.GamePath, item.FilePath);

            transient.Add(item.GamePath);
            addedTransients.Add(item.Owner.Address);
        }

        _recordedTransients.Clear();

        foreach (var item in addedTransients)
        {
            Mediator.Publish(new TransientResourceChangedMessage(item));
        }
    }

    private readonly HashSet<TransientRecord> _recordedTransients = [];
    public IReadOnlySet<TransientRecord> RecordedTransients => _recordedTransients;

    public ValueProgress<TimeSpan> RecordTimeRemaining { get; } = new();
    private CancellationTokenSource _sendTransientCts = new();

    public record TransientRecord(GameObjectHandler Owner, string GamePath, string FilePath, bool AlreadyTransient)
    {
        public bool AddTransient { get; set; }
    }
}