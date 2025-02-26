using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.PlayerData.Pairs;

public sealed class PairManager : DisposableMediatorSubscriberBase
{
    private readonly ConcurrentDictionary<UserData, Pair> _allClientPairs = new(UserDataComparer.Instance);
    private readonly ConcurrentDictionary<GroupData, GroupFullInfoDto> _allGroups = new(GroupDataComparer.Instance);
    private readonly MareConfigService _configurationService;
    private readonly IContextMenu _dalamudContextMenu;
    private readonly PairFactory _pairFactory;
    private Lazy<List<Pair>> _directPairsInternal;
    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> _groupPairsInternal;
    private Lazy<Dictionary<Pair, List<GroupFullInfoDto>>> _pairsWithGroupsInternal;

    public PairManager(ILogger<PairManager> logger, PairFactory pairFactory,
                MareConfigService configurationService, MareMediator mediator,
                IContextMenu dalamudContextMenu) : base(logger, mediator)
    {
        _pairFactory = pairFactory;
        _configurationService = configurationService;
        _dalamudContextMenu = dalamudContextMenu;
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearPairs());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => ReapplyPairData());
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
        _pairsWithGroupsInternal = PairsWithGroupsLazy();

        _dalamudContextMenu.OnMenuOpened += DalamudContextMenuOnOnOpenGameObjectContextMenu;
    }

    public List<Pair> DirectPairs => _directPairsInternal.Value;

    public Dictionary<GroupFullInfoDto, List<Pair>> GroupPairs => _groupPairsInternal.Value;
    public Dictionary<GroupData, GroupFullInfoDto> Groups => _allGroups.ToDictionary(k => k.Key, k => k.Value);
    public Pair? LastAddedUser { get; internal set; }
    public Dictionary<Pair, List<GroupFullInfoDto>> PairsWithGroups => _pairsWithGroupsInternal.Value;

    public void AddGroup(GroupFullInfoDto dto)
    {
        _allGroups[dto.Group] = dto;
        RecreateLazy();
    }

    public void AddGroupPair(GroupPairFullInfoDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
            _allClientPairs[dto.User] = _pairFactory.Create(new UserFullPairDto(dto.User, API.Data.Enum.IndividualPairStatus.None,
                [dto.Group.GID], dto.SelfToOtherPermissions, dto.OtherToSelfPermissions));
        else _allClientPairs[dto.User].UserPair.Groups.Add(dto.GID);
        RecreateLazy();
    }

    public Pair? GetPairByUID(string uid)
    {
        var existingPair = _allClientPairs.FirstOrDefault(f => f.Key.UID == uid);
        if (!Equals(existingPair, default(KeyValuePair<UserData, Pair>)))
        {
            return existingPair.Value;
        }

        return null;
    }

    public void AddUserPair(UserFullPairDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
        {
            _allClientPairs[dto.User] = _pairFactory.Create(dto);
        }
        else
        {
            _allClientPairs[dto.User].UserPair.IndividualPairStatus = dto.IndividualPairStatus;
            _allClientPairs[dto.User].ApplyLastReceivedData();
        }

        RecreateLazy();
    }

    public void AddUserPair(UserPairDto dto, bool addToLastAddedUser = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
        {
            _allClientPairs[dto.User] = _pairFactory.Create(dto);
        }
        else
        {
            addToLastAddedUser = false;
        }

        _allClientPairs[dto.User].UserPair.IndividualPairStatus = dto.IndividualPairStatus;
        _allClientPairs[dto.User].UserPair.OwnPermissions = dto.OwnPermissions;
        _allClientPairs[dto.User].UserPair.OtherPermissions = dto.OtherPermissions;
        if (addToLastAddedUser)
            LastAddedUser = _allClientPairs[dto.User];
        _allClientPairs[dto.User].ApplyLastReceivedData();
        RecreateLazy();
    }

    public void ClearPairs()
    {
        Logger.LogDebug("Clearing all Pairs");
        DisposePairs();
        _allClientPairs.Clear();
        _allGroups.Clear();
        RecreateLazy();
    }

    public List<Pair> GetOnlineUserPairs() => _allClientPairs.Where(p => !string.IsNullOrEmpty(p.Value.GetPlayerNameHash())).Select(p => p.Value).ToList();

    public int GetVisibleUserCount() => _allClientPairs.Count(p => p.Value.IsVisible);

    public List<UserData> GetVisibleUsers() => [.. _allClientPairs.Where(p => p.Value.IsVisible).Select(p => p.Key)];

    public void MarkPairOffline(UserData user)
    {
        if (_allClientPairs.TryGetValue(user, out var pair))
        {
            Mediator.Publish(new ClearProfileDataMessage(pair.UserData));
            pair.MarkOffline();
        }

        RecreateLazy();
    }

    public void MarkPairOnline(OnlineUserIdentDto dto, bool sendNotif = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) throw new InvalidOperationException("No user found for " + dto);

        Mediator.Publish(new ClearProfileDataMessage(dto.User));

        var pair = _allClientPairs[dto.User];
        if (pair.HasCachedPlayer)
        {
            RecreateLazy();
            return;
        }

        if (sendNotif && _configurationService.Current.ShowOnlineNotifications
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs && pair.IsDirectlyPaired && !pair.IsOneSidedPair
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs)
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs && !string.IsNullOrEmpty(pair.GetNote())
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs))
        {
            string? note = pair.GetNote();
            var msg = !string.IsNullOrEmpty(note)
                ? $"{note} ({pair.UserData.AliasOrUID}) is now online"
                : $"{pair.UserData.AliasOrUID} is now online";
            Mediator.Publish(new NotificationMessage("User online", msg, NotificationType.Info, TimeSpan.FromSeconds(5)));
        }

        pair.CreateCachedPlayer(dto);

        RecreateLazy();
    }

    public void ReceiveCharaData(OnlineUserCharaDataDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair)) throw new InvalidOperationException("No user found for " + dto.User);

        Mediator.Publish(new EventMessage(new Event(pair.UserData, nameof(PairManager), EventSeverity.Informational, "Received Character Data")));
        _allClientPairs[dto.User].ApplyData(dto);
    }

    public void RemoveGroup(GroupData data)
    {
        _allGroups.TryRemove(data, out _);

        foreach (var item in _allClientPairs.ToList())
        {
            item.Value.UserPair.Groups.Remove(data.GID);

            if (!item.Value.HasAnyConnection())
            {
                item.Value.MarkOffline();
                _allClientPairs.TryRemove(item.Key, out _);
            }
        }

        RecreateLazy();
    }

    public void RemoveGroupPair(GroupPairDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair.Groups.Remove(dto.Group.GID);

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }
        }

        RecreateLazy();
    }

    public void RemoveUserPair(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair.IndividualPairStatus = API.Data.Enum.IndividualPairStatus.None;

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }
        }

        RecreateLazy();
    }

    public void SetGroupInfo(GroupInfoDto dto)
    {
        _allGroups[dto.Group].Group = dto.Group;
        _allGroups[dto.Group].Owner = dto.Owner;
        _allGroups[dto.Group].GroupPermissions = dto.GroupPermissions;

        RecreateLazy();
    }

    public void UpdatePairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null) throw new InvalidOperationException("No direct pair for " + dto);

        if (pair.UserPair.OtherPermissions.IsPaused() != dto.Permissions.IsPaused())
        {
            Mediator.Publish(new ClearProfileDataMessage(dto.User));
        }

        pair.UserPair.OtherPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OtherPermissions.IsPaused(),
            pair.UserPair.OtherPermissions.IsDisableAnimations(),
            pair.UserPair.OtherPermissions.IsDisableSounds(),
            pair.UserPair.OtherPermissions.IsDisableVFX());

        if (!pair.IsPaused)
            pair.ApplyLastReceivedData();

        RecreateLazy();
    }

    public void UpdateSelfPairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair.OwnPermissions.IsPaused() != dto.Permissions.IsPaused())
        {
            Mediator.Publish(new ClearProfileDataMessage(dto.User));
        }

        pair.UserPair.OwnPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OwnPermissions.IsPaused(),
            pair.UserPair.OwnPermissions.IsDisableAnimations(),
            pair.UserPair.OwnPermissions.IsDisableSounds(),
            pair.UserPair.OwnPermissions.IsDisableVFX());

        if (!pair.IsPaused)
            pair.ApplyLastReceivedData();

        RecreateLazy();
    }

    internal void ReceiveUploadStatus(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var existingPair) && existingPair.IsVisible)
        {
            existingPair.SetIsUploading();
        }
    }

    internal void SetGroupPairStatusInfo(GroupPairUserInfoDto dto)
    {
        _allGroups[dto.Group].GroupPairUserInfos[dto.UID] = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void SetGroupPermissions(GroupPermissionDto dto)
    {
        _allGroups[dto.Group].GroupPermissions = dto.Permissions;
        RecreateLazy();
    }

    internal void SetGroupStatusInfo(GroupPairUserInfoDto dto)
    {
        _allGroups[dto.Group].GroupUserInfo = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void UpdateGroupPairPermissions(GroupPairUserPermissionDto dto)
    {
        _allGroups[dto.Group].GroupUserPermissions = dto.GroupPairPermissions;
        RecreateLazy();
    }

    internal void UpdateIndividualPairStatus(UserIndividualPairStatusDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair.IndividualPairStatus = dto.IndividualPairStatus;
            RecreateLazy();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _dalamudContextMenu.OnMenuOpened -= DalamudContextMenuOnOnOpenGameObjectContextMenu;

        DisposePairs();
    }

    private void DalamudContextMenuOnOnOpenGameObjectContextMenu(Dalamud.Game.Gui.ContextMenu.IMenuOpenedArgs args)
    {
        if (args.MenuType == Dalamud.Game.Gui.ContextMenu.ContextMenuType.Inventory) return;
        if (!_configurationService.Current.EnableRightClickMenus) return;

        foreach (var pair in _allClientPairs.Where((p => p.Value.IsVisible)))
        {
            pair.Value.AddContextMenu(args);
        }
    }

    private Lazy<List<Pair>> DirectPairsLazy() => new(() => _allClientPairs.Select(k => k.Value)
        .Where(k => k.IndividualPairStatus != API.Data.Enum.IndividualPairStatus.None).ToList());

    private void DisposePairs()
    {
        Logger.LogDebug("Disposing all Pairs");
        Parallel.ForEach(_allClientPairs, item =>
        {
            item.Value.MarkOffline(wait: false);
        });

        RecreateLazy();
    }

    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> GroupPairsLazy()
    {
        return new Lazy<Dictionary<GroupFullInfoDto, List<Pair>>>(() =>
        {
            Dictionary<GroupFullInfoDto, List<Pair>> outDict = [];
            foreach (var group in _allGroups)
            {
                outDict[group.Value] = _allClientPairs.Select(p => p.Value).Where(p => p.UserPair.Groups.Exists(g => GroupDataComparer.Instance.Equals(group.Key, new(g)))).ToList();
            }
            return outDict;
        });
    }

    private Lazy<Dictionary<Pair, List<GroupFullInfoDto>>> PairsWithGroupsLazy()
    {
        return new Lazy<Dictionary<Pair, List<GroupFullInfoDto>>>(() =>
        {
            Dictionary<Pair, List<GroupFullInfoDto>> outDict = [];

            foreach (var pair in _allClientPairs.Select(k => k.Value))
            {
                outDict[pair] = _allGroups.Where(k => pair.UserPair.Groups.Contains(k.Key.GID, StringComparer.Ordinal)).Select(k => k.Value).ToList();
            }

            return outDict;
        });
    }

    private void ReapplyPairData()
    {
        foreach (var pair in _allClientPairs.Select(k => k.Value))
        {
            pair.ApplyLastReceivedData(forced: true);
        }
    }

    private void RecreateLazy()
    {
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
        _pairsWithGroupsInternal = PairsWithGroupsLazy();
        Mediator.Publish(new RefreshUiMessage());
    }
}