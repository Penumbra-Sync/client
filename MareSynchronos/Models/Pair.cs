using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Managers;
using MareSynchronos.Utils;

namespace MareSynchronos.Models;

public class Pair
{
    private readonly Configuration _configuration;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private OptionalPluginWarning? _pluginWarnings;

    public Pair(Configuration configuration, ServerConfigurationManager serverConfigurationManager)
    {
        _configuration = configuration;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public UserPairDto? UserPair { get; set; }
    public CachedPlayer? CachedPlayer { get; set; }
    public API.Data.CharacterData? LastReceivedCharacterData { get; set; }
    public Dictionary<GroupFullInfoDto, GroupPairFullInfoDto> GroupPair { get; set; } = new(GroupDtoComparer.Instance);
    public string PlayerNameHash => CachedPlayer?.PlayerNameHash ?? string.Empty;
    public string? PlayerName => CachedPlayer?.PlayerName ?? string.Empty;
    public UserData UserData => UserPair?.User ?? GroupPair.First().Value.User;
    public bool IsOnline => CachedPlayer != null;
    public bool IsVisible => CachedPlayer != null && CachedPlayer.IsVisible;

    public string? GetNote()
    {
        if (_serverConfigurationManager.CurrentServer.UidServerComments.TryGetValue(UserData.UID, out string? note))
        {
            return note;
        }

        return null;
    }

    public void SetNote(string note)
    {
        _serverConfigurationManager.CurrentServer.UidServerComments[UserData.UID] = note;
        _serverConfigurationManager.Save();
    }

    public bool HasAnyConnection()
    {
        return UserPair != null || GroupPair.Any();
    }

    public void InitializePair(nint address, string name)
    {
        if (!PlayerName.IsNullOrEmpty()) return;

        if (CachedPlayer == null) throw new InvalidOperationException("CachedPlayer not initialized");
        _pluginWarnings ??= new()
        {
            ShownCustomizePlusWarning = _configuration.DisableOptionalPluginWarnings,
            ShownHeelsWarning = _configuration.DisableOptionalPluginWarnings
        };

        CachedPlayer.Initialize(address, name, RemoveNotSyncedFiles(LastReceivedCharacterData), _pluginWarnings);
        LastReceivedCharacterData = null;
    }

    public void ApplyData(OnlineUserCharaDataDto data)
    {
        if (CachedPlayer == null) throw new InvalidOperationException("CachedPlayer not initialized");
        _pluginWarnings ??= new()
        {
            ShownCustomizePlusWarning = _configuration.DisableOptionalPluginWarnings,
            ShownHeelsWarning = _configuration.DisableOptionalPluginWarnings
        };

        CachedPlayer.ApplyCharacterData(RemoveNotSyncedFiles(data.CharaData)!, _pluginWarnings);
    }

    private API.Data.CharacterData? RemoveNotSyncedFiles(API.Data.CharacterData? data)
    {
        if (data == null || UserPair != null) return data;

        bool disableAnimations = GroupPair.All(u =>
        {
            return u.Value.GroupUserPermissions.IsDisableAnimations() || u.Key.GroupPermissions.IsDisableAnimations() || u.Key.GroupUserPermissions.IsDisableAnimations();
        });
        bool disableSounds = GroupPair.All(pair =>
        {
            return pair.Value.GroupUserPermissions.IsDisableSounds() || pair.Key.GroupPermissions.IsDisableSounds() || pair.Key.GroupUserPermissions.IsDisableSounds();
        });

        if (disableAnimations || disableSounds)
        {
            Logger.Verbose($"Data cleaned up: Animations disabled: {disableAnimations}, Sounds disabled: {disableSounds}");
            foreach (var kvp in data.FileReplacements)
            {
                if (disableSounds)
                    data.FileReplacements[kvp.Key] = data.FileReplacements[kvp.Key]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if (disableAnimations)
                    data.FileReplacements[kvp.Key] = data.FileReplacements[kvp.Key]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
            }
        }

        return data;
    }
}
