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
    private OptionalPluginWarning? _pluginWarnings;

    public Pair(Configuration configuration)
    {
        _configuration = configuration;
    }

    public UserPairDto? UserPair { get; set; }
    public CachedPlayer? CachedPlayer { get; set; }
    public API.Data.CharacterData? LastReceivedCharacterData { get; set; }
    public Dictionary<GroupPairDto, GroupPairFullInfoDto> GroupPair { get; set; } = new(new GroupPairDtoComparer());
    public Dictionary<GroupDto, GroupFullInfoDto> AssociatedGroups { get; set; } = new(new GroupDtoComparer());
    public string PlayerNameHash => CachedPlayer?.PlayerNameHash ?? string.Empty;
    public string? PlayerName => CachedPlayer?.PlayerName ?? string.Empty;
    private UserData UserData => UserPair?.User ?? GroupPair.First().Value.User;

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

        var userDataComparer = new UserDataComparer();
        var groupComparer = new GroupDataComparer();
        var userGroupPairs = GroupPair.Select(p => p.Value).Where(p => userDataComparer.Equals(p.User, UserData)).ToList();
        bool disableAnimations = userGroupPairs.All(u =>
        {
            var group = AssociatedGroups.Select(g => g.Value).Single(p => groupComparer.Equals(p.Group, u.Group));
            return u.GroupUserPermissions.IsDisableAnimations() || group.GroupPermissions.IsDisableAnimations() || group.GroupUserPermissions.IsDisableAnimations();
        });
        bool disableSounds = userGroupPairs.All(pair =>
        {
            var group = AssociatedGroups.Select(g => g.Value).Single(p => groupComparer.Equals(p.Group, pair.Group));
            return pair.GroupUserPermissions.IsDisableSounds() || group.GroupPermissions.IsDisableSounds() || group.GroupUserPermissions.IsDisableSounds();
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
