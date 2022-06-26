using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using MareSynchronos.API;

namespace MareSynchronos.Models;

public class CachedPlayer
{
    private bool _isVisible = false;

    public CachedPlayer(string nameHash)
    {
        PlayerNameHash = nameHash;
    }

    public Dictionary<int, CharacterCacheDto> CharacterCache { get; set; } = new();
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            WasVisible = _isVisible;
            _isVisible = value;
        }
    }

    public string OriginalGlamourerData { get; set; }
    public string LastGlamourerData { get; set; }
    public int? JobId => (int?)PlayerCharacter?.ClassJob.Id;
    public PlayerCharacter? PlayerCharacter { get; set; }
    public string? PlayerName { get; set; }
    public string PlayerNameHash { get; }
    public bool WasVisible { get; private set; }
    public int RequestedRedraws
    {
        get => _requestedRedraws;
        set => _requestedRedraws = value < 0 ? 0 : value;
    }

    private int _requestedRedraws;
    public void Reset()
    {
        PlayerName = string.Empty;
        PlayerCharacter = null;
    }

    public override string ToString()
    {
        return PlayerNameHash + " : " + PlayerName + " : HasChar " + (PlayerCharacter != null);
    }
}