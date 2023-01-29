using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Managers;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Utils;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MareSynchronos.Models;

public class Pair
{
    private readonly ConfigurationService _configService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private OptionalPluginWarning? _pluginWarnings;

    public Pair(ConfigurationService configService, ServerConfigurationManager serverConfigurationManager)
    {
        _configService = configService;
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
    public bool IsPaused => UserPair != null ? (UserPair.OtherPermissions.IsPaused() || UserPair.OwnPermissions.IsPaused())
            : GroupPair.All(p => p.Key.GroupUserPermissions.IsPaused() || p.Value.GroupUserPermissions.IsPaused());

    public string? GetNote()
    {
        if (_serverConfigurationManager.CurrentServer.UidServerComments.TryGetValue(UserData.UID, out string? note))
        {
            return string.IsNullOrEmpty(note) ? null : note;
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
            ShownCustomizePlusWarning = _configService.Current.DisableOptionalPluginWarnings,
            ShownHeelsWarning = _configService.Current.DisableOptionalPluginWarnings,
        };

        CachedPlayer.Initialize(address, name);

        ApplyLastReceivedData();
    }

    public void ApplyData(OnlineUserCharaDataDto data)
    {
        if (CachedPlayer == null) throw new InvalidOperationException("CachedPlayer not initialized");

        if (string.Equals(LastReceivedCharacterData?.DataHash.Value, data.CharaData.DataHash.Value, StringComparison.Ordinal)) return;

        LastReceivedCharacterData = data.CharaData;

        ApplyLastReceivedData();
    }

    public void ApplyLastReceivedData()
    {
        if (CachedPlayer == null) return;
        if (LastReceivedCharacterData == null) return;

        _pluginWarnings ??= new()
        {
            ShownCustomizePlusWarning = _configService.Current.DisableOptionalPluginWarnings,
            ShownHeelsWarning = _configService.Current.DisableOptionalPluginWarnings,
        };

        CachedPlayer.ApplyCharacterData(RemoveNotSyncedFiles(LastReceivedCharacterData.DeepClone())!, _pluginWarnings);
    }

    private API.Data.CharacterData? RemoveNotSyncedFiles(API.Data.CharacterData? data)
    {
        Logger.Verbose("Removing not synced files");
        if (data == null || (UserPair != null && UserPair.OtherPermissions.IsPaired()))
        {
            Logger.Verbose("Nothing to remove or user is paired directly");
            return data;
        }

        bool disableAnimations = GroupPair.All(pair =>
        {
            return pair.Value.GroupUserPermissions.IsDisableAnimations() || pair.Key.GroupPermissions.IsDisableAnimations() || pair.Key.GroupUserPermissions.IsDisableAnimations();
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
