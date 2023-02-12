using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using System.Diagnostics;

namespace MareSynchronos.Managers;

public class ServerConfigurationManager
{
    private readonly Dictionary<JwtCache, string> _tokenDictionary = new();
    private readonly ServerConfigService _configService;
    private readonly ServerTagConfigService _serverTagConfig;
    private readonly NotesConfigService _notesConfig;
    private readonly DalamudUtil _dalamudUtil;

    public string CurrentApiUrl => string.IsNullOrEmpty(_configService.Current.CurrentServer) ? ApiController.MainServiceUri : _configService.Current.CurrentServer;
    public ServerStorage? CurrentServer => (_configService.Current.ServerStorage.ContainsKey(CurrentApiUrl) ? _configService.Current.ServerStorage[CurrentApiUrl] : null);
    private ServerTagStorage CurrentServerTagStorage()
    {
        TryCreateCurrentServerTagStorage();
        return _serverTagConfig.Current.ServerTagStorage[CurrentApiUrl];
    }

    private ServerNotesStorage CurrentNotesStorage()
    {
        TryCreateCurrentNotesStorage();
        return _notesConfig.Current.ServerNotes[CurrentApiUrl];
    }

    public ServerConfigurationManager(ServerConfigService configService, ServerTagConfigService serverTagConfig, NotesConfigService notesConfig, DalamudUtil dalamudUtil)
    {
        _configService = configService;
        _serverTagConfig = serverTagConfig;
        _notesConfig = notesConfig;
        _dalamudUtil = dalamudUtil;
    }

    public bool HasValidConfig()
    {
        return CurrentServer != null;
    }

    public string[] GetServerApiUrls()
    {
        return _configService.Current.ServerStorage.Keys.ToArray();
    }

    public string[] GetServerNames()
    {
        return _configService.Current.ServerStorage.Values.Select(v => v.ServerName).ToArray();
    }

    public ServerStorage GetServerByIndex(int idx)
    {
        try
        {
            return _configService.Current.ServerStorage.ElementAt(idx).Value;
        }
        catch
        {
            _configService.Current.CurrentServer = ApiController.MainServiceUri;
            if (!_configService.Current.ServerStorage.ContainsKey(ApiController.MainServer))
            {
                _configService.Current.ServerStorage.Add(_configService.Current.CurrentServer, new ServerStorage() { ServerUri = ApiController.MainServiceUri, ServerName = ApiController.MainServer });
            }
            Save();
            return CurrentServer!;
        }
    }

    public int GetCurrentServerIndex()
    {
        return Array.IndexOf(_configService.Current.ServerStorage.Keys.ToArray(), CurrentApiUrl);
    }

    public void Save()
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        Logger.Debug(caller + " Calling config save");
        _configService.Save();
    }

    public void SelectServer(int idx)
    {
        _configService.Current.CurrentServer = GetServerByIndex(idx).ServerUri;
        CurrentServer!.FullPause = false;
        Save();
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

        var charaName = _dalamudUtil.PlayerName;
        var worldId = _dalamudUtil.WorldId;
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

    public string? GetToken()
    {
        var charaName = _dalamudUtil.PlayerName;
        var worldId = _dalamudUtil.WorldId;
        var secretKey = GetSecretKey();
        if (secretKey == null) return null;
        if (_tokenDictionary.TryGetValue(new JwtCache(CurrentApiUrl, charaName, worldId, secretKey), out var token))
        {
            return token;
        }

        return null;
    }

    public void SaveToken(string token)
    {
        var charaName = _dalamudUtil.PlayerName;
        var worldId = _dalamudUtil.WorldId;
        var secretKey = GetSecretKey();
        if (string.IsNullOrEmpty(secretKey)) throw new InvalidOperationException("No secret key set");
        _tokenDictionary[new JwtCache(CurrentApiUrl, charaName, worldId, secretKey)] = token;
    }

    internal void AddCurrentCharacterToServer(int serverSelectionIndex = -1, bool addLastSecretKey = false)
    {
        if (serverSelectionIndex == -1) serverSelectionIndex = GetCurrentServerIndex();
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Add(new Authentication()
        {
            CharacterName = _dalamudUtil.PlayerName,
            WorldId = _dalamudUtil.WorldId,
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

    internal void RemoveCharacterFromServer(int serverSelectionIndex, Authentication item)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Remove(item);
        Save();
    }

    internal void AddServer(ServerStorage serverStorage)
    {
        _configService.Current.ServerStorage[serverStorage.ServerUri] = serverStorage;
        Save();
    }

    internal void DeleteServer(ServerStorage selectedServer)
    {
        _configService.Current.ServerStorage.Remove(selectedServer.ServerUri);
        Save();
    }

    internal void AddOpenPairTag(string tag)
    {
        CurrentServerTagStorage().OpenPairTags.Add(tag);
        _serverTagConfig.Save();
    }

    internal void RemoveOpenPairTag(string tag)
    {
        CurrentServerTagStorage().OpenPairTags.Remove(tag);
        _serverTagConfig.Save();
    }

    internal bool ContainsOpenPairTag(string tag)
    {
        return CurrentServerTagStorage().OpenPairTags.Contains(tag);
    }

    internal Dictionary<string, List<string>> GetUidServerPairedUserTags()
    {
        return CurrentServerTagStorage().UidServerPairedUserTags;
    }

    internal HashSet<string> GetServerAvailablePairTags()
    {
        return CurrentServerTagStorage().ServerAvailablePairTags;
    }

    internal void AddTag(string tag)
    {
        CurrentServerTagStorage().ServerAvailablePairTags.Add(tag);
        _serverTagConfig.Save();
    }

    internal void RemoveTag(string tag)
    {
        CurrentServerTagStorage().ServerAvailablePairTags.Remove(tag);
        foreach (var uid in GetUidsForTag(tag))
        {
            RemoveTagForUid(uid, tag, false);
        }
        _serverTagConfig.Save();
    }

    private void TryCreateCurrentServerTagStorage()
    {
        if (!_serverTagConfig.Current.ServerTagStorage.ContainsKey(CurrentApiUrl))
        {
            _serverTagConfig.Current.ServerTagStorage[CurrentApiUrl] = new();
        }
    }

    private void TryCreateCurrentNotesStorage()
    {
        if (!_notesConfig.Current.ServerNotes.ContainsKey(CurrentApiUrl))
        {
            _notesConfig.Current.ServerNotes[CurrentApiUrl] = new();
        }
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

    internal void RemoveTagForUid(string uid, string tagName, bool save = true)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            tags.Remove(tagName);
            if (save)
                _serverTagConfig.Save();
        }
    }

    internal bool ContainsTag(string uid, string tag)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            return tags.Contains(tag, StringComparer.Ordinal);
        }

        return false;
    }

    internal bool HasTags(string uid)
    {
        if (CurrentServerTagStorage().UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            return tags.Any();
        }

        return false;
    }

    internal HashSet<string> GetUidsForTag(string tag)
    {
        return CurrentServerTagStorage().UidServerPairedUserTags.Where(p => p.Value.Contains(tag, StringComparer.Ordinal)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
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

    internal void SetNoteForUid(string uid, string note, bool save = true)
    {
        CurrentNotesStorage().UidServerComments[uid] = note;
        if (save)
            _notesConfig.Save();
    }

    internal void SaveNotes()
    {
        _notesConfig.Save();
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

    internal void SetNoteForGid(string gid, string note, bool save = true)
    {
        CurrentNotesStorage().GidServerComments[gid] = note;
        if (save)
            _notesConfig.Save();
    }
}
