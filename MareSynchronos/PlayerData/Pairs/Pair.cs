using Dalamud.ContextMenu;
using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Pairs;

public class Pair
{
    private readonly Func<OnlineUserIdentDto, CachedPlayer> _cachedPlayerFactory;
    private readonly MareConfigService _configService;
    private readonly ILogger<Pair> _logger;
    private readonly MareMediator _mediator;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private OnlineUserIdentDto? _onlineUserIdentDto = null;
    private OptionalPluginWarning? _pluginWarnings;

    public Pair(ILogger<Pair> logger, Func<OnlineUserIdentDto, CachedPlayer> cachedPlayerFactory,
        MareMediator mediator, MareConfigService configService, ServerConfigurationManager serverConfigurationManager)
    {
        _logger = logger;
        _cachedPlayerFactory = cachedPlayerFactory;
        _mediator = mediator;
        _configService = configService;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public bool CachedPlayerExists => CachedPlayer?.CheckExistence() ?? false;
    public Dictionary<GroupFullInfoDto, GroupPairFullInfoDto> GroupPair { get; set; } = new(GroupDtoComparer.Instance);
    public bool HasCachedPlayer => CachedPlayer != null && !string.IsNullOrEmpty(CachedPlayer.PlayerName) && _onlineUserIdentDto != null;
    public bool IsOnline => CachedPlayer != null;

    public bool IsPaused => UserPair != null && UserPair.OtherPermissions.IsPaired() ? UserPair.OtherPermissions.IsPaused() || UserPair.OwnPermissions.IsPaused()
            : GroupPair.All(p => p.Key.GroupUserPermissions.IsPaused() || p.Value.GroupUserPermissions.IsPaused());

    public bool IsVisible => CachedPlayer?.PlayerName != null;
    public CharacterData? LastReceivedCharacterData { get; set; }
    public string? PlayerName => CachedPlayer?.PlayerName ?? string.Empty;

    public UserData UserData => UserPair?.User ?? GroupPair.First().Value.User;

    public UserPairDto? UserPair { get; set; }

    private CachedPlayer? CachedPlayer { get; set; }

    public void AddContextMenu(GameObjectContextMenuOpenArgs args)
    {
        if (CachedPlayer == null || args.ObjectId != CachedPlayer.PlayerCharacterId) return;

        if (!IsPaused)
        {
            args.AddCustomItem(new GameObjectContextMenuItem("[Mare] Open Profile", (a) =>
            {
                _mediator.Publish(new ProfileOpenStandaloneMessage(this));
            }));
        }
        args.AddCustomItem(new GameObjectContextMenuItem("[Mare] Reapply last data", (a) =>
        {
            ApplyLastReceivedData(true);
        }, false));
        if (UserPair != null && UserPair.OtherPermissions.IsPaired() && UserPair.OwnPermissions.IsPaired())
        {
            args.AddCustomItem(new GameObjectContextMenuItem("[Mare] Cycle pause state", (a) =>
            {
                _mediator.Publish(new CyclePauseMessage(UserData));
            }, false));
        }
    }

    public void ApplyData(OnlineUserCharaDataDto data)
    {
        if (CachedPlayer == null) throw new InvalidOperationException("CachedPlayer not initialized");

        if (string.Equals(LastReceivedCharacterData?.DataHash.Value, data.CharaData.DataHash.Value, StringComparison.Ordinal)) return;

        LastReceivedCharacterData = data.CharaData;

        ApplyLastReceivedData();
    }

    public void ApplyLastReceivedData(bool forced = false)
    {
        if (CachedPlayer == null) return;
        if (LastReceivedCharacterData == null) return;

        _pluginWarnings ??= new()
        {
            ShownCustomizePlusWarning = _configService.Current.DisableOptionalPluginWarnings,
            ShownHeelsWarning = _configService.Current.DisableOptionalPluginWarnings,
            ShownPalettePlusWarning = _configService.Current.DisableOptionalPluginWarnings,
            ShownHonorificWarning = _configService.Current.DisableOptionalPluginWarnings,
        };

        CachedPlayer.ApplyCharacterData(RemoveNotSyncedFiles(LastReceivedCharacterData.DeepClone())!, _pluginWarnings, forced);
    }

    public string? GetNote()
    {
        return _serverConfigurationManager.GetNoteForUid(UserData.UID);
    }

    public string GetPlayerNameHash()
    {
        try
        {
            return CachedPlayer?.PlayerNameHash ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error accessing PlayerNameHash, recreating CachedPlayer");
            RecreateCachedPlayer();
        }

        return string.Empty;
    }

    public bool HasAnyConnection()
    {
        return UserPair != null || GroupPair.Any();
    }

    public bool InitializePair(string name)
    {
        if (!PlayerName.IsNullOrEmpty()) return false;

        if (CachedPlayer == null) throw new InvalidOperationException("CachedPlayer not initialized");
        _pluginWarnings ??= new()
        {
            ShownCustomizePlusWarning = _configService.Current.DisableOptionalPluginWarnings,
            ShownHeelsWarning = _configService.Current.DisableOptionalPluginWarnings,
            ShownPalettePlusWarning = _configService.Current.DisableOptionalPluginWarnings,
            ShownHonorificWarning = _configService.Current.DisableOptionalPluginWarnings,
        };

        CachedPlayer.Initialize(name);

        ApplyLastReceivedData();

        return true;
    }

    public void MarkOffline()
    {
        _onlineUserIdentDto = null;
        LastReceivedCharacterData = null;
        CachedPlayer?.Dispose();
        CachedPlayer = null;
    }

    public void RecreateCachedPlayer(OnlineUserIdentDto? dto = null)
    {
        if (dto == null && _onlineUserIdentDto == null)
        {
            CachedPlayer?.Dispose();
            CachedPlayer = null;
            return;
        }
        if (dto != null)
        {
            _onlineUserIdentDto = dto;
        }

        CachedPlayer?.Dispose();
        CachedPlayer = _cachedPlayerFactory(_onlineUserIdentDto!);
    }

    public void SetNote(string note)
    {
        _serverConfigurationManager.SetNoteForUid(UserData.UID, note);
    }

    internal void SetIsUploading()
    {
        CachedPlayer?.SetUploading();
    }

    private CharacterData? RemoveNotSyncedFiles(CharacterData? data)
    {
        _logger.LogTrace("Removing not synced files");
        if (data == null)
        {
            _logger.LogTrace("Nothing to remove");
            return data;
        }

        bool disableIndividualAnimations = UserPair != null && (UserPair.OtherPermissions.IsDisableAnimations() || UserPair.OwnPermissions.IsDisableAnimations());
        bool disableGroupAnimations = GroupPair.All(pair => pair.Value.GroupUserPermissions.IsDisableAnimations() || pair.Key.GroupPermissions.IsDisableAnimations() || pair.Key.GroupUserPermissions.IsDisableAnimations());

        bool disableAnimations = (UserPair != null && disableIndividualAnimations) || (UserPair == null && disableGroupAnimations);

        bool disableIndividualSounds = UserPair != null && (UserPair.OtherPermissions.IsDisableSounds() || UserPair.OwnPermissions.IsDisableSounds());
        bool disableGroupSounds = GroupPair.All(pair => pair.Value.GroupUserPermissions.IsDisableSounds() || pair.Key.GroupPermissions.IsDisableSounds() || pair.Key.GroupUserPermissions.IsDisableSounds());

        bool disableSounds = (UserPair != null && disableIndividualSounds) || (UserPair == null && disableGroupSounds);

        _logger.LogTrace("Individual Sounds: {disableIndividualSounds}, Individual Anims: {disableIndividualAnims}; " +
            "Group Sounds: {disableGroupSounds}, Group Anims: {disableGroupAnims} => Disable Sounds: {disableSounds}, Disable Anims: {disableAnims}",
            disableIndividualSounds, disableIndividualAnimations, disableGroupSounds, disableGroupAnimations, disableSounds, disableAnimations);

        if (disableAnimations || disableSounds)
        {
            _logger.LogTrace("Data cleaned up: Animations disabled: {disableAnimations}, Sounds disabled: {disableSounds}", disableAnimations, disableSounds);
            foreach (var objectKind in data.FileReplacements.Select(k => k.Key))
            {
                if (disableSounds)
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if (disableAnimations)
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
            }
        }

        return data;
    }
}