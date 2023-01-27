using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto;
using MareSynchronos.API.Dto.Admin;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public UserPairDto? LastAddedUser { get; set; }

    public void OnUpdateSystemInfo(Action<SystemInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UpdateSystemInfo), act);
    }

    public void OnUserReceiveCharacterData(Action<OnlineUserCharaDataDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserReceiveCharacterData), act);
    }

    public void OnAdminForcedReconnect(Action act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_AdminForcedReconnect), act);
    }

    public void OnAdminDeleteBannedUser(Action<BannedUserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_AdminDeleteBannedUser), act);
    }

    public void OnAdminDeleteForbiddenFile(Action<ForbiddenFileDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_AdminDeleteForbiddenFile), act);
    }

    public void OnAdminUpdateOrAddBannedUser(Action<BannedUserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_AdminUpdateOrAddBannedUser), act);
    }

    public void OnAdminUpdateOrAddForbiddenFile(Action<ForbiddenFileDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_AdminUpdateOrAddForbiddenFile), act);
    }

    public void OnReceiveServerMessage(Action<MessageSeverity, string> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_ReceiveServerMessage), act);
    }

    public void OnDownloadReady(Action<Guid> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_DownloadReady), act);
    }

    public void OnGroupSendFullInfo(Action<GroupFullInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupSendFullInfo), act);
    }

    public Task Client_GroupSendFullInfo(GroupFullInfoDto dto)
    {
        Groups[dto] = dto;
        return Task.CompletedTask;
    }

    public void OnGroupSendInfo(Action<GroupInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupSendInfo), act);
    }

    public Task Client_GroupSendInfo(GroupInfoDto dto)
    {
        Groups[dto].Group = dto.Group;
        Groups[dto].Owner = dto.Owner;
        Groups[dto].GroupPermissions = dto.GroupPermissions;
        return Task.CompletedTask;
    }

    public void OnGroupDelete(Action<GroupDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupDelete), act);
    }

    public Task Client_GroupDelete(GroupDto dto)
    {
        Groups.TryRemove(dto, out _);
        return Task.CompletedTask;
    }

    public void OnGroupPairJoined(Action<GroupPairFullInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairJoined), act);
    }

    public Task Client_GroupPairJoined(GroupPairFullInfoDto dto)
    {
        GroupPairedClients[dto] = dto;
        return Task.CompletedTask;
    }

    public void OnGroupPairLeft(Action<GroupPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairLeft), act);
    }

    public Task Client_GroupPairLeft(GroupPairDto dto)
    {
        GroupPairedClients.TryRemove(dto, out _);
        return Task.CompletedTask;
    }

    public void OnGroupChangePermissions(Action<GroupPermissionDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupChangePermissions), act);
    }

    public Task Client_GroupChangePermissions(GroupPermissionDto dto)
    {
        Groups[dto].GroupPermissions = dto.Permissions;
        return Task.CompletedTask;
    }

    public void OnGroupPairChangePermissions(Action<GroupPairUserPermissionDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairChangePermissions), act);
    }

    public Task Client_GroupPairChangePermissions(GroupPairUserPermissionDto dto)
    {
        Logger.Debug("GroupPairChangePermissions: " + dto);
        if (dto.UID == UID) Groups[dto].GroupUserPermissions = dto.GroupPairPermissions;
        else GroupPairedClients[dto].GroupUserPermissions = dto.GroupPairPermissions;
        return Task.CompletedTask;
    }

    public void OnGroupPairChangeUserInfo(Action<GroupPairUserInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairChangeUserInfo), act);
    }

    public Task Client_GroupPairChangeUserInfo(GroupPairUserInfoDto dto)
    {
        GroupPairedClients[dto].GroupPairStatusInfo = dto.GroupUserInfo;
        return Task.CompletedTask;
    }

    public Task Client_UserReceiveCharacterData(OnlineUserCharaDataDto dto)
    {
        Logger.Verbose("Data: " + dto.User);
        _pairManager.ReceiveCharaData(dto);
        return Task.CompletedTask;
    }

    public void OnUserAddClientPair(Action<UserPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserAddClientPair), act);
    }

    public Task Client_UserAddClientPair(UserPairDto userPairDto)
    {
        Logger.Debug($"Added: {userPairDto}");
        PairedClients[userPairDto] = userPairDto;
        _pairManager.AddUserPair(userPairDto);
        return Task.CompletedTask;
    }

    public void OnUserRemoveClientPair(Action<UserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserRemoveClientPair), act);
    }

    public Task Client_UserRemoveClientPair(UserDto dto)
    {
        Logger.Debug($"Removing {dto}");
        PairedClients.TryRemove(dto, out _);
        _pairManager.RemoveUserPair(dto);
        return Task.CompletedTask;
    }

    public void OnUserSendOffline(Action<UserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserSendOffline), act);
    }

    public Task Client_UserSendOffline(UserDto dto)
    {
        Logger.Debug($"Offline: {dto}");
        _pairManager.MarkPairOffline(dto.User);
        return Task.CompletedTask;
    }

    public void OnUserSendOnline(Action<OnlineUserIdentDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserSendOnline), act);
    }

    public Task Client_UserSendOnline(OnlineUserIdentDto dto)
    {
        Logger.Debug($"Online: {dto}");
        try
        {
            _pairManager.MarkPairOnline(dto, this);
        }
        catch (Exception ex)
        {
            Logger.Error("Error UserSendOnline", ex);
        }
        return Task.CompletedTask;
    }

    public void OnUserUpdateOtherPairPermissions(Action<UserPermissionsDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateOtherPairPermissions), act);
    }

    public Task Client_UserUpdateOtherPairPermissions(UserPermissionsDto dto)
    {
        PairedClients[dto].OtherPermissions = dto.Permissions;
        _pairManager.UpdatePairPermissions(dto);
        return Task.CompletedTask;
    }

    public void OnUserUpdateSelfPairPermissions(Action<UserPermissionsDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateSelfPairPermissions), act);
    }

    public Task Client_UserUpdateSelfPairPermissions(UserPermissionsDto dto)
    {
        PairedClients[dto].OwnPermissions = dto.Permissions;
        _pairManager.UpdateSelfPairPermissions(dto);
        return Task.CompletedTask;
    }

    public Task Client_UpdateSystemInfo(SystemInfoDto systemInfo)
    {
        SystemInfoDto = systemInfo;
        return Task.CompletedTask;
    }

    public Task Client_AdminForcedReconnect()
    {
        _ = CreateConnections();
        return Task.CompletedTask;
    }

    public Task Client_AdminDeleteBannedUser(BannedUserDto dto)
    {
        AdminBannedUsers.RemoveAll(a => string.Equals(a.CharacterHash, dto.CharacterHash, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public Task Client_AdminDeleteForbiddenFile(ForbiddenFileDto dto)
    {
        AdminForbiddenFiles.RemoveAll(f => string.Equals(f.Hash, dto.Hash, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public Task Client_AdminUpdateOrAddBannedUser(BannedUserDto dto)
    {
        var user = AdminBannedUsers.SingleOrDefault(b => string.Equals(b.CharacterHash, dto.CharacterHash, StringComparison.Ordinal));
        if (user == null)
        {
            AdminBannedUsers.Add(dto);
        }
        else
        {
            user.Reason = dto.Reason;
        }

        return Task.CompletedTask;
    }

    public Task Client_AdminUpdateOrAddForbiddenFile(ForbiddenFileDto dto)
    {
        var user = AdminForbiddenFiles.SingleOrDefault(b => string.Equals(b.Hash, dto.Hash, StringComparison.Ordinal));
        if (user == null)
        {
            AdminForbiddenFiles.Add(dto);
        }
        else
        {
            user.ForbiddenBy = dto.ForbiddenBy;
        }

        return Task.CompletedTask;
    }

    public Task Client_ReceiveServerMessage(MessageSeverity severity, string message)
    {
        switch (severity)
        {
            case MessageSeverity.Error:
                Logger.Error(message);
                _dalamudUtil.PrintErrorChat(message);
                break;
            case MessageSeverity.Warning:
                Logger.Warn(message);
                _dalamudUtil.PrintWarnChat(message);
                break;
            case MessageSeverity.Information:
                Logger.Info(message);
                if (_pluginConfiguration.HideInfoMessages)
                {
                    _dalamudUtil.PrintInfoChat(message);
                }
                break;
        }

        return Task.CompletedTask;
    }

    public Task Client_DownloadReady(Guid requestId)
    {
        Logger.Debug($"Server sent {requestId} ready");
        _downloadReady[requestId] = true;
        return Task.CompletedTask;
    }
}
