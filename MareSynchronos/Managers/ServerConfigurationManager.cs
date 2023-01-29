using MareSynchronos.MareConfiguration;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Managers;

public class ServerConfigurationManager
{
    private readonly Dictionary<JwtCache, string> _tokenDictionary = new();
    private readonly ConfigurationService _configService;
    private readonly DalamudUtil _dalamudUtil;

    public string CurrentApiUrl => string.IsNullOrEmpty(_configService.Current.CurrentServer) ? ApiController.MainServiceUri : _configService.Current.CurrentServer;
    public ServerStorage? CurrentServer => (_configService.Current.ServerStorage.ContainsKey(CurrentApiUrl) ? _configService.Current.ServerStorage[CurrentApiUrl] : null);

    public ServerConfigurationManager(ConfigurationService configService, DalamudUtil dalamudUtil)
    {
        _configService = configService;
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
            _configService.Save();
            return CurrentServer!;
        }
    }

    public int GetCurrentServerIndex()
    {
        return Array.IndexOf(_configService.Current.ServerStorage.Keys.ToArray(), CurrentApiUrl);
    }

    public void Save()
    {
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
        _configService.Save();
    }

    internal void AddEmptyCharacterToServer(int serverSelectionIndex)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Add(new Authentication());
        _configService.Save();
    }

    internal void RemoveCharacterFromServer(int serverSelectionIndex, Authentication item)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Remove(item);
    }

    internal void AddServer(ServerStorage serverStorage)
    {
        _configService.Current.ServerStorage[serverStorage.ServerUri] = serverStorage;
        _configService.Save();
    }

    internal void DeleteServer(ServerStorage selectedServer)
    {
        _configService.Current.ServerStorage.Remove(selectedServer.ServerUri);
    }
}
