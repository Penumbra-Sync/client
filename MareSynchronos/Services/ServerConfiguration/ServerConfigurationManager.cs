using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MareSynchronos.Services.ServerConfiguration;

public class ServerConfigurationManager
{
    private readonly ServerConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ILogger<ServerConfigurationManager> _logger;
    private readonly NotesConfigService _notesConfig;
    private readonly ServerTagConfigService _serverTagConfig;
    private readonly Dictionary<JwtCache, string> _tokenDictionary = new();

    public ServerConfigurationManager(ILogger<ServerConfigurationManager> logger, ServerConfigService configService,
        ServerTagConfigService serverTagConfig, NotesConfigService notesConfig, DalamudUtilService dalamudUtil)
    {
        _logger = logger;
        _configService = configService;
        _serverTagConfig = serverTagConfig;
        _notesConfig = notesConfig;
        _dalamudUtil = dalamudUtil;
        EnsureMainExists();
    }

    public string CurrentApiUrl => CurrentServer.ServerUri;
    public ServerStorage CurrentServer => _configService.Current.ServerStorage[CurrentServerIndex];

    public int CurrentServerIndex
    {
        set
        {
            _configService.Current.CurrentServer = value;
            _configService.Save();
        }
        get
        {
            if (_configService.Current.CurrentServer < 0)
            {
                _configService.Current.CurrentServer = 0;
                _configService.Save();
            }

            return _configService.Current.CurrentServer;
        }
    }

    public string? GetSecretKey(int serverIdx = -1)
    {
        ServerStorage? currentServer;
        currentServer = serverIdx == -1 ? CurrentServer : GetServerByIndex(serverIdx);
        if (currentServer == null)
        {
            currentServer = new();
            Save();
        }

        var charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
        var worldId = _dalamudUtil.GetWorldIdAsync().GetAwaiter().GetResult();
        if (!currentServer.Authentications.Any() && currentServer.SecretKeys.Any())
        {
            currentServer.Authentications.Add(new Authentication()
            {
                CharacterName = charaName,
                WorldId = worldId,
                SecretKeyIdx = currentServer.SecretKeys.Last().Key,
            });

            Save();
        }

        var auth = currentServer.Authentications.Find(f => string.Equals(f.CharacterName, charaName, StringComparison.Ordinal) && f.WorldId == worldId);
        if (auth == null) return null;

        if (currentServer.SecretKeys.TryGetValue(auth.SecretKeyIdx, out var secretKey))
        {
            return secretKey.Key;
        }

        return null;
    }

    public string[] GetServerApiUrls()
    {
        return _configService.Current.ServerStorage.Select(v => v.ServerUri).ToArray();
    }

    public ServerStorage GetServerByIndex(int idx)
    {
        try
        {
            return _configService.Current.ServerStorage.ElementAt(idx);
        }
        catch
        {
            _configService.Current.CurrentServer = 0;
            EnsureMainExists();
            return CurrentServer!;
        }
    }

    public string[] GetServerNames()
    {
        return _configService.Current.ServerStorage.Select(v => v.ServerName).ToArray();
    }

    public string? GetToken()
    {
        var charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
        var worldId = _dalamudUtil.GetWorldIdAsync().GetAwaiter().GetResult();
        var secretKey = GetSecretKey();
        if (secretKey == null) return null;
        if (_tokenDictionary.TryGetValue(new JwtCache(CurrentApiUrl, charaName, worldId, secretKey), out var token))
        {
            return token;
        }

        return null;
    }

    public bool HasValidConfig()
    {
        return CurrentServer != null;
    }

    public void Save()
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        _logger.LogDebug(caller + " Calling config save");
        _configService.Save();
    }

    public void SaveToken(string token)
    {
        var charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
        var worldId = _dalamudUtil.GetWorldIdAsync().GetAwaiter().GetResult();
        var secretKey = GetSecretKey();
        if (string.IsNullOrEmpty(secretKey)) throw new InvalidOperationException("No secret key set");
        _tokenDictionary[new JwtCache(CurrentApiUrl, charaName, worldId, secretKey)] = token;
    }

    public void SelectServer(int idx)
    {
        _configService.Current.CurrentServer = idx;
        CurrentServer!.FullPause = false;
        Save();
    }

    internal void AddCurrentCharacterToServer(int serverSelectionIndex = -1, bool addLastSecretKey = false)
    {
        if (serverSelectionIndex == -1) serverSelectionIndex = CurrentServerIndex;
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Add(new Authentication()
        {
            CharacterName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult(),
            WorldId = _dalamudUtil.GetWorldIdAsync().GetAwaiter().GetResult(),
            SecretKeyIdx = addLastSecretKey ? server.SecretKeys.Last().Key : -1,
        });
        Save();
    }

    internal void AddEmptyCharacterToServer(int serverSelectionIndex)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Add(new Authentication());
        Save();
    }

    internal void AddOpenPairTag(string tag)
    {
        CurrentServerTagStorage().OpenPairTags.Add(tag);
        _serverTagConfig.Save();
    }

    internal void AddServer(ServerStorage serverStorage)
    {
        _configService.Current.ServerStorage.Add(serverStorage);
        Save();
    }

    internal void AddTag(string tag)
    {
        CurrentServerTagStorage().ServerAvailablePairTags.Add(tag);
        _serverTagConfig.Save();
    }

    internal void AddTagForUid(string uid, string tagName)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            tags.Add(tagName);
        }
        else
        {
            CurrentServerTagStorage().UidServerPairedUserTags[uid] = new() { tagName };
        }

        _serverTagConfig.Save();
    }

    internal bool ContainsOpenPairTag(string tag)
    {
        return CurrentServerTagStorage().OpenPairTags.Contains(tag);
    }

    internal bool ContainsTag(string uid, string tag)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            return tags.Contains(tag, StringComparer.Ordinal);
        }

        return false;
    }

    internal void DeleteServer(ServerStorage selectedServer)
    {
        if (Array.IndexOf(_configService.Current.ServerStorage.ToArray(), selectedServer) <
            _configService.Current.CurrentServer)
        {
            _configService.Current.CurrentServer--;
        }

        _configService.Current.ServerStorage.Remove(selectedServer);
        Save();
    }

    internal string? GetNoteForGid(string gID)
    {
        if (CurrentNotesStorage().GidServerComments.TryGetValue(gID, out var note))
        {
            if (string.IsNullOrEmpty(note)) return null;
            return note;
        }

        return null;
    }

    internal string? GetNoteForUid(string uid)
    {
        if (CurrentNotesStorage().UidServerComments.TryGetValue(uid, out var note))
        {
            if (string.IsNullOrEmpty(note)) return null;
            return note;
        }
        return null;
    }

    internal HashSet<string> GetServerAvailablePairTags()
    {
        return CurrentServerTagStorage().ServerAvailablePairTags;
    }

    internal Dictionary<string, List<string>> GetUidServerPairedUserTags()
    {
        return CurrentServerTagStorage().UidServerPairedUserTags;
    }

    internal HashSet<string> GetUidsForTag(string tag)
    {
        return CurrentServerTagStorage().UidServerPairedUserTags.Where(p => p.Value.Contains(tag, StringComparer.Ordinal)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
    }

    internal bool HasTags(string uid)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            return tags.Any();
        }

        return false;
    }

    internal void RemoveCharacterFromServer(int serverSelectionIndex, Authentication item)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Remove(item);
        Save();
    }

    internal void RemoveOpenPairTag(string tag)
    {
        CurrentServerTagStorage().OpenPairTags.Remove(tag);
        _serverTagConfig.Save();
    }

    internal void RemoveTag(string tag)
    {
        CurrentServerTagStorage().ServerAvailablePairTags.Remove(tag);
        foreach (var uid in GetUidsForTag(tag))
        {
            RemoveTagForUid(uid, tag, save: false);
        }
        _serverTagConfig.Save();
    }

    internal void RemoveTagForUid(string uid, string tagName, bool save = true)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            tags.Remove(tagName);
            if (save)
                _serverTagConfig.Save();
        }
    }

    internal void SaveNotes()
    {
        _notesConfig.Save();
    }

    internal void SetNoteForGid(string gid, string note, bool save = true)
    {
        CurrentNotesStorage().GidServerComments[gid] = note;
        if (save)
            _notesConfig.Save();
    }

    internal void SetNoteForUid(string uid, string note, bool save = true)
    {
        CurrentNotesStorage().UidServerComments[uid] = note;
        if (save)
            _notesConfig.Save();
    }

    private ServerNotesStorage CurrentNotesStorage()
    {
        TryCreateCurrentNotesStorage();
        return _notesConfig.Current.ServerNotes[CurrentApiUrl];
    }

    private ServerTagStorage CurrentServerTagStorage()
    {
        TryCreateCurrentServerTagStorage();
        return _serverTagConfig.Current.ServerTagStorage[CurrentApiUrl];
    }

    private void EnsureMainExists()
    {
        if (_configService.Current.ServerStorage.Count == 0 || !string.Equals(_configService.Current.ServerStorage[0].ServerUri, ApiController.MainServiceUri, StringComparison.OrdinalIgnoreCase))
        {
            _configService.Current.ServerStorage.Insert(0, new ServerStorage() { ServerUri = ApiController.MainServiceUri, ServerName = ApiController.MainServer });
        }
        Save();
    }

    private void TryCreateCurrentNotesStorage()
    {
        if (!_notesConfig.Current.ServerNotes.ContainsKey(CurrentApiUrl))
        {
            _notesConfig.Current.ServerNotes[CurrentApiUrl] = new();
        }
    }

    private void TryCreateCurrentServerTagStorage()
    {
        if (!_serverTagConfig.Current.ServerTagStorage.ContainsKey(CurrentApiUrl))
        {
            _serverTagConfig.Current.ServerTagStorage[CurrentApiUrl] = new();
        }
    }
}