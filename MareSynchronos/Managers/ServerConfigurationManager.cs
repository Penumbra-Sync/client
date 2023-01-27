using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Managers;

public class ServerConfigurationManager
{
    private Dictionary<JwtCache, string> _tokenDictionary = new();
    private readonly Configuration _configuration;
    private readonly DalamudUtil _dalamudUtil;

    public string CurrentApiUrl => _configuration.CurrentServer;
    public ServerStorage CurrentServer => _configuration.ServerStorage[CurrentApiUrl];

    public ServerConfigurationManager(Configuration configuration, DalamudUtil dalamudUtil)
    {
        _configuration = configuration;
        _dalamudUtil = dalamudUtil;
    }

    public string[] GetServerApiUrls()
    {
        return _configuration.ServerStorage.Keys.ToArray();
    }

    public string[] GetServerNames()
    {
        return _configuration.ServerStorage.Values.Select(v => v.ServerName).ToArray();
    }

    public ServerStorage GetServerByIndex(int idx)
    {
        return _configuration.ServerStorage.ElementAt(idx).Value;
    }

    public int GetCurrentServerIndex()
    {
        return Array.IndexOf(_configuration.ServerStorage.Keys.ToArray(), CurrentApiUrl);
    }

    public void Save()
    {
        _configuration.Save();
    }

    public void SelectServer(int idx)
    {
        _configuration.CurrentServer = GetServerByIndex(idx).ServerUri;
        Save();
    }

    public string? GetSecretKey()
    {
        var charaName = _dalamudUtil.PlayerName;
        var worldId = _dalamudUtil.WorldId;
        if (!CurrentServer.Authentications.Any())
        {
            CurrentServer.Authentications.Add(new Authentication()
            {
                CharacterName = charaName,
                WorldId = worldId,
                SecretKeyIdx = CurrentServer.SecretKeys.Last().Key
            });

            Save();
        }

        var auth = CurrentServer.Authentications.FirstOrDefault(f => string.Equals(f.CharacterName, charaName, StringComparison.Ordinal) && f.WorldId == worldId);
        if (auth == null) return null;

        if (CurrentServer.SecretKeys.TryGetValue(auth.SecretKeyIdx, out var secretKey))
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

    internal void AddCurrentCharacterToServer(int serverSelectionIndex)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Add(new Authentication()
        {
            CharacterName = _dalamudUtil.PlayerName,
            WorldId = _dalamudUtil.WorldId
        });
        _configuration.Save();
    }

    internal void AddEmptyCharacterToServer(int serverSelectionIndex)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Add(new Authentication());
        _configuration.Save();
    }

    internal void RemoveCharacterFromServer(int serverSelectionIndex, Authentication item)
    {
        var server = GetServerByIndex(serverSelectionIndex);
        server.Authentications.Remove(item);
    }
}
