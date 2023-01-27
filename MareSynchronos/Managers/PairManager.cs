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
using System.ComponentModel.Design;

namespace MareSynchronos.Managers;

public class PairManager : IDisposable
{
    private readonly ConcurrentDictionary<UserData, Pair> _allClientPairs = new(new UserDataComparer());
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
    }

    public List<Pair> OnlineUserPairs => _allClientPairs.Where(p => !string.IsNullOrEmpty(p.Value.PlayerNameHash)).Select(p => p.Value).ToList();
    public List<UserData> VisibleUsers => _allClientPairs.Where(p => p.Value.CachedPlayer != null && p.Value.CachedPlayer.IsVisible).Select(p => p.Key).ToList();

    public void AddAssociatedGroup(UserData user, GroupFullInfoDto group)
    {
        if (!_allClientPairs.ContainsKey(user)) _allClientPairs[user] = _pairFactory.Create();

        _allClientPairs[user].AssociatedGroups[group] = group;
    }

    public void AddGroupPair(GroupPairFullInfoDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) _allClientPairs[dto.User] = _pairFactory.Create();

        _allClientPairs[dto.User].GroupPair[dto] = dto;
    }

    public void AddUserPair(UserPairDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) _allClientPairs[dto.User] = _pairFactory.Create();

        _allClientPairs[dto.User].UserPair = dto;
    }

    public void ClearPairs()
    {
        Logger.Debug("Clearing all Pairs");
        DisposePairs();
        _allClientPairs.Clear();
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
            item.Value.CachedPlayer = null;
        }
    }

    public Pair? FindPair(PlayerCharacter? pChar)
    {
        if (pChar == null) return null;
        var hash = pChar.GetHash256();
        return OnlineUserPairs.FirstOrDefault(p => p.PlayerNameHash == hash);
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

    public void RemoveUserPair(UserDto dto)
    {
        _allClientPairs.TryRemove(dto.User, out var pair);
        pair?.CachedPlayer?.Dispose();
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
}