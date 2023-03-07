using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Linq;

namespace MareSynchronos.PlayerData.Pairs;

public class PairManager : MediatorSubscriberBase
{
    private readonly ConcurrentDictionary<UserData, Pair> _allClientPairs = new(UserDataComparer.Instance);
    private readonly ConcurrentDictionary<GroupData, GroupFullInfoDto> _allGroups = new(GroupDataComparer.Instance);
    private readonly PairFactory _pairFactory;
    private readonly MareConfigService _configurationService;

    public PairManager(ILogger<PairManager> logger, PairFactory pairFactory,
        MareConfigService configurationService, MareMediator mediator) : base(logger, mediator)
    {
        _pairFactory = pairFactory;
        _configurationService = configurationService;
        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) => DalamudUtilOnZoneSwitched());
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => DalamudUtilOnDelayedFrameworkUpdate());
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearPairs());
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
    }

    private void RecreateLazy()
    {
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
    }

    private Lazy<List<Pair>> _directPairsInternal;
    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> _groupPairsInternal;
    public Dictionary<GroupFullInfoDto, List<Pair>> GroupPairs => _groupPairsInternal.Value;
    public List<Pair> DirectPairs => _directPairsInternal.Value;

    private Lazy<List<Pair>> DirectPairsLazy() => new(() => _allClientPairs.Select(k => k.Value).Where(k => k.UserPair != null).ToList());
    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> GroupPairsLazy()
    {
        return new Lazy<Dictionary<GroupFullInfoDto, List<Pair>>>(() =>
        {
            Dictionary<GroupFullInfoDto, List<Pair>> outDict = new();
            foreach (var group in _allGroups)
            {
                outDict[group.Value] = _allClientPairs.Select(p => p.Value).Where(p => p.GroupPair.Any(g => GroupDataComparer.Instance.Equals(group.Key, g.Key.Group))).ToList();
            }
            return outDict;
        });
    }

    public List<Pair> GetOnlineUserPairs() => _allClientPairs.Where(p => !string.IsNullOrEmpty(p.Value.PlayerNameHash)).Select(p => p.Value).ToList();
    public List<UserData> GetVisibleUsers() => _allClientPairs.Where(p => p.Value.HasCachedPlayer).Select(p => p.Key).ToList();

    public Pair? LastAddedUser { get; internal set; }

    public void AddGroup(GroupFullInfoDto dto)
    {
        _allGroups[dto.Group] = dto;
        RecreateLazy();
    }

    public void RemoveGroup(GroupData data)
    {
        _allGroups.TryRemove(data, out _);
        foreach (var item in _allClientPairs.ToList())
        {
            foreach (var grpPair in item.Value.GroupPair.Select(k => k.Key).Where(grpPair => GroupDataComparer.Instance.Equals(grpPair.Group, data)).ToList())
            {
                _allClientPairs[item.Key].GroupPair.Remove(grpPair);
            }

            if (!_allClientPairs[item.Key].HasAnyConnection() && _allClientPairs.TryRemove(item.Key, out var pair))
            {
                pair.MarkOffline();
            }
        }
        RecreateLazy();
    }

    public void AddGroupPair(GroupPairFullInfoDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) _allClientPairs[dto.User] = _pairFactory.Create();

        var group = _allGroups[dto.Group];
        _allClientPairs[dto.User].GroupPair[group] = dto;
        RecreateLazy();
    }

    public void AddUserPair(UserPairDto dto, bool addToLastAddedUser = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
        {
            _allClientPairs[dto.User] = _pairFactory.Create();
        }
        else
        {
            addToLastAddedUser = false;
        }

        _allClientPairs[dto.User].UserPair = dto;
        if (addToLastAddedUser)
            LastAddedUser = _allClientPairs[dto.User];
        _allClientPairs[dto.User].ApplyLastReceivedData();
        RecreateLazy();
    }

    public void ClearPairs()
    {
        _logger.LogDebug("Clearing all Pairs");
        DisposePairs();
        _allClientPairs.Clear();
        _allGroups.Clear();
        RecreateLazy();
    }

    public override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        DisposePairs();
    }

    private void DisposePairs(bool recreate = false)
    {
        _logger.LogDebug("Disposing all Pairs");
        foreach (var item in _allClientPairs)
        {
            if (recreate)
                item.Value.RecreateCachedPlayer();
            else
                item.Value.MarkOffline();
        }
        RecreateLazy();
    }

    public Pair? FindPair(PlayerCharacter? pChar)
    {
        if (pChar == null) return null;
        var hash = pChar.GetHash256();
        return GetOnlineUserPairs().Find(p => string.Equals(p.PlayerNameHash, hash, StringComparison.Ordinal));
    }

    public void MarkPairOffline(UserData user)
    {
        if (_allClientPairs.TryGetValue(user, out var pair))
        {
            pair.MarkOffline();
            RecreateLazy();
        }
    }

    public void MarkPairOnline(OnlineUserIdentDto dto, bool sendNotif = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) throw new InvalidOperationException("No user found for " + dto);
        var pair = _allClientPairs[dto.User];
        if (pair.HasCachedPlayer) return;

        if (sendNotif && _configurationService.Current.ShowOnlineNotifications
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs && pair.UserPair != null
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs)
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs && !string.IsNullOrEmpty(pair.GetNote())
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs))
        {
            string? note = pair.GetNote();
            var msg = !string.IsNullOrEmpty(note)
                ? $"{note} ({pair.UserData.AliasOrUID}) is now online"
                : $"{pair.UserData.AliasOrUID} is now online";
            Mediator.Publish(new NotificationMessage("User online", msg, NotificationType.Info, 5000));
        }

        pair.RecreateCachedPlayer(dto);
        RecreateLazy();
    }

    public void ReceiveCharaData(OnlineUserCharaDataDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) throw new InvalidOperationException("No user found for " + dto.User);

        var pair = _allClientPairs[dto.User];
        if (!pair.PlayerName.IsNullOrEmpty())
        {
            pair.ApplyData(dto);
        }
        else
        {
            _allClientPairs[dto.User].LastReceivedCharacterData = dto.CharaData;
        }
    }

    public void RemoveGroupPair(GroupPairDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            var group = _allGroups[dto.Group];
            pair.GroupPair.Remove(group);

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }

            RecreateLazy();
        }
    }

    public void RemoveUserPair(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair = null;
            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }
            else
            {
                pair.ApplyLastReceivedData();
            }

            RecreateLazy();
        }
    }

    public void UpdatePairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null) throw new InvalidOperationException("No direct pair for " + dto);

        pair.UserPair.OtherPermissions = dto.Permissions;

        _logger.LogTrace("Paired: {synced}, Paused: {paused}, Anims: {anims}, Sounds: {sounds}",
            pair.UserPair.OwnPermissions.IsPaired(), pair.UserPair.OwnPermissions.IsPaused(), pair.UserPair.OwnPermissions.IsDisableAnimations(), pair.UserPair.OwnPermissions.IsDisableSounds());
        pair.ApplyLastReceivedData();
    }

    public void UpdateSelfPairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null) throw new InvalidOperationException("No direct pair for " + dto);

        pair.UserPair.OwnPermissions = dto.Permissions;

        _logger.LogTrace("Paired: {synced}, Paused: {paused}, Anims: {anims}, Sounds: {sounds}",
            pair.UserPair.OwnPermissions.IsPaired(), pair.UserPair.OwnPermissions.IsPaused(), pair.UserPair.OwnPermissions.IsDisableAnimations(), pair.UserPair.OwnPermissions.IsDisableSounds());

        pair.ApplyLastReceivedData();
    }

    private void DalamudUtilOnDelayedFrameworkUpdate()
    {
        foreach (Pair pair in _allClientPairs.Select(p => p.Value).Where(p => p.HasCachedPlayer).ToList())
        {
            if (!pair.CachedPlayerExists)
            {
                pair.RecreateCachedPlayer();
            }
        }
    }

    private void DalamudUtilOnZoneSwitched()
    {
        DisposePairs(recreate: true);
    }

    public void SetGroupInfo(GroupInfoDto dto)
    {
        _allGroups[dto.Group].Group = dto.Group;
        _allGroups[dto.Group].Owner = dto.Owner;
        _allGroups[dto.Group].GroupPermissions = dto.GroupPermissions;
        RecreateLazy();
    }

    internal void SetGroupPermissions(GroupPermissionDto dto)
    {
        var prevPermissions = _allGroups[dto.Group].GroupPermissions;
        _allGroups[dto.Group].GroupPermissions = dto.Permissions;
        if (prevPermissions.IsDisableAnimations() != dto.Permissions.IsDisableAnimations()
            || prevPermissions.IsDisableSounds() != dto.Permissions.IsDisableSounds())
        {
            RecreateLazy();
            var group = _allGroups[dto.Group];
            GroupPairs[group].ForEach(p => p.ApplyLastReceivedData());
        }
        RecreateLazy();
    }

    internal void SetGroupPairUserPermissions(GroupPairUserPermissionDto dto)
    {
        var group = _allGroups[dto.Group];
        var prevPermissions = _allClientPairs[dto.User].GroupPair[group].GroupUserPermissions;
        _allClientPairs[dto.User].GroupPair[group].GroupUserPermissions = dto.GroupPairPermissions;
        if (prevPermissions.IsDisableAnimations() != dto.GroupPairPermissions.IsDisableAnimations()
            || prevPermissions.IsDisableSounds() != dto.GroupPairPermissions.IsDisableSounds())
        {
            _allClientPairs[dto.User].ApplyLastReceivedData();
        }
        RecreateLazy();
    }

    internal void SetGroupUserPermissions(GroupPairUserPermissionDto dto)
    {
        var prevPermissions = _allGroups[dto.Group].GroupUserPermissions;
        _allGroups[dto.Group].GroupUserPermissions = dto.GroupPairPermissions;
        if (prevPermissions.IsDisableAnimations() != dto.GroupPairPermissions.IsDisableAnimations()
            || prevPermissions.IsDisableSounds() != dto.GroupPairPermissions.IsDisableSounds())
        {
            RecreateLazy();
            var group = _allGroups[dto.Group];
            GroupPairs[group].ForEach(p => p.ApplyLastReceivedData());
        }
        RecreateLazy();
    }

    internal void SetGroupStatusInfo(GroupPairUserInfoDto dto)
    {
        _allGroups[dto.Group].GroupUserInfo = dto.GroupUserInfo;
    }

    internal void SetGroupPairStatusInfo(GroupPairUserInfoDto dto)
    {
        var group = _allGroups[dto.Group];
        _allClientPairs[dto.User].GroupPair[group].GroupPairStatusInfo = dto.GroupUserInfo;
        RecreateLazy();
    }
}