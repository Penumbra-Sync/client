using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using System.Collections.Concurrent;

namespace MareSynchronos.Managers;

public class PairManager : IDisposable
{
    private readonly ConcurrentDictionary<UserData, Pair> _allClientPairs = new(UserDataComparer.Instance);
    private readonly ConcurrentDictionary<GroupData, GroupFullInfoDto> _allGroups = new(GroupDataComparer.Instance);
    private readonly CachedPlayerFactory _cachedPlayerFactory;
    private readonly DalamudUtil _dalamudUtil;
    private readonly PairFactory _pairFactory;

    public PairManager(CachedPlayerFactory cachedPlayerFactory, DalamudUtil dalamudUtil, PairFactory pairFactory)
    {
        _cachedPlayerFactory = cachedPlayerFactory;
        _dalamudUtil = dalamudUtil;
        _pairFactory = pairFactory;
        _dalamudUtil.ZoneSwitchStart += DalamudUtilOnZoneSwitched;
        _dalamudUtil.DelayedFrameworkUpdate += DalamudUtilOnDelayedFrameworkUpdate;
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
    }

    private void RecreateLazy()
    {
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
    }

    public Lazy<List<Pair>> _directPairsInternal { get; private set; }
    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> _groupPairsInternal;
    public Dictionary<GroupFullInfoDto, List<Pair>> GroupPairs => _groupPairsInternal.Value;
    public List<Pair> DirectPairs => _directPairsInternal.Value;

    private Lazy<List<Pair>> DirectPairsLazy() => new Lazy<List<Pair>>(() => _allClientPairs.Select(k => k.Value).Where(k => k.UserPair != null).ToList());
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

    public List<Pair> OnlineUserPairs => _allClientPairs.Where(p => !string.IsNullOrEmpty(p.Value.PlayerNameHash)).Select(p => p.Value).ToList();
    public List<UserData> VisibleUsers => _allClientPairs.Where(p => p.Value.CachedPlayer != null && p.Value.CachedPlayer.IsVisible).Select(p => p.Key).ToList();

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
            foreach (var grpPair in item.Value.GroupPair.Select(k => k.Key).ToList())
            {
                if (GroupDataComparer.Instance.Equals(grpPair.Group, data))
                {
                    item.Value.GroupPair.Remove(grpPair);
                }
            }

            if (!item.Value.HasAnyConnection())
            {
                _allClientPairs.TryRemove(item.Key, out _);
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

    public void AddUserPair(UserPairDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) _allClientPairs[dto.User] = _pairFactory.Create();

        _allClientPairs[dto.User].UserPair = dto;
        RecreateLazy();
    }

    public void ClearPairs()
    {
        Logger.Debug("Clearing all Pairs");
        DisposePairs();
        _allClientPairs.Clear();
        RecreateLazy();
    }

    public void Dispose()
    {
        _dalamudUtil.DelayedFrameworkUpdate -= DalamudUtilOnDelayedFrameworkUpdate;
        _dalamudUtil.ZoneSwitchStart -= DalamudUtilOnZoneSwitched;
        DisposePairs();
    }

    public void DisposePairs()
    {
        Logger.Debug("Disposing all Pairs");
        foreach (var item in _allClientPairs)
        {
            item.Value.CachedPlayer?.Dispose();
        }
    }

    public Pair? FindPair(PlayerCharacter? pChar)
    {
        if (pChar == null) return null;
        var hash = pChar.GetHash256();
        return OnlineUserPairs.FirstOrDefault(p => string.Equals(p.PlayerNameHash, hash, StringComparison.Ordinal));
    }

    public void MarkPairOffline(UserData user)
    {
        _allClientPairs[user].CachedPlayer?.Dispose();
        _allClientPairs[user].CachedPlayer = null;
    }

    public void MarkPairOnline(OnlineUserIdentDto dto, ApiController controller)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) throw new InvalidOperationException("No user found for " + dto);

        if (_allClientPairs[dto.User].CachedPlayer != null) return;

        _allClientPairs[dto.User].CachedPlayer?.Dispose();
        _allClientPairs[dto.User].CachedPlayer = _cachedPlayerFactory.Create(dto, controller);
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
                _allClientPairs.TryRemove(dto.User, out _);
                RecreateLazy();
            }
        }
    }

    public void RemoveUserPair(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair = null;
            if (!pair.HasAnyConnection())
            {
                _allClientPairs.TryRemove(dto.User, out _);
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
    }

    public void UpdateSelfPairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null) throw new InvalidOperationException("No direct pair for " + dto);

        pair.UserPair.OwnPermissions = dto.Permissions;
    }

    private void DalamudUtilOnDelayedFrameworkUpdate()
    {
        foreach (var player in _allClientPairs.Select(p => p.Value).Where(p => p.CachedPlayer != null && p.CachedPlayer.IsVisible).ToList())
        {
            if (!player.CachedPlayer!.CheckExistence())
            {
                player.CachedPlayer.Dispose();
            }
        }
    }

    private void DalamudUtilOnZoneSwitched()
    {
        DisposePairs();
    }

    public void SetGroupInfo(GroupInfoDto dto)
    {
        _allGroups[dto.Group].Group = dto.Group;
        _allGroups[dto.Group].Owner = dto.Owner;
        _allGroups[dto.Group].GroupPermissions = dto.GroupPermissions;
    }

    internal void SetGroupPermissions(GroupPermissionDto dto)
    {
        _allGroups[dto.Group].GroupPermissions = dto.Permissions;
    }

    internal void SetGroupPairUserPermissions(GroupPairUserPermissionDto dto)
    {
        _allGroups[dto.Group].GroupUserPermissions = dto.GroupPairPermissions;
    }

    internal void SetGroupUserPermissions(GroupPairUserPermissionDto dto)
    {
        var group = _allGroups[dto.Group];
        _allClientPairs[dto.User].GroupPair[group].GroupUserPermissions = dto.GroupPairPermissions;
    }

    internal void SetGroupPairStatusInfo(GroupPairUserInfoDto dto)
    {
        var group = _allGroups[dto.Group];
        _allClientPairs[dto.User].GroupPair[group].GroupPairStatusInfo = dto.GroupUserInfo;
    }
}