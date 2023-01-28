using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto;
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
        Logger.Verbose("Client_GroupSendFullInfo: " + dto);
        _pairManager.AddGroup(dto);
        return Task.CompletedTask;
    }

    public void OnGroupSendInfo(Action<GroupInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupSendInfo), act);
    }

    public Task Client_GroupSendInfo(GroupInfoDto dto)
    {
        Logger.Verbose("Client_GroupSendInfo: " + dto);
        _pairManager.SetGroupInfo(dto);
        return Task.CompletedTask;
    }

    public void OnGroupDelete(Action<GroupDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupDelete), act);
    }

    public Task Client_GroupDelete(GroupDto dto)
    {
        Logger.Verbose("Client_GroupDelete: " + dto);
        _pairManager.RemoveGroup(dto.Group);
        return Task.CompletedTask;
    }

    public void OnGroupPairJoined(Action<GroupPairFullInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairJoined), act);
    }

    public Task Client_GroupPairJoined(GroupPairFullInfoDto dto)
    {
        Logger.Verbose("Client_GroupPairJoined: " + dto);
        _pairManager.AddGroupPair(dto);
        return Task.CompletedTask;
    }

    public void OnGroupPairLeft(Action<GroupPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairLeft), act);
    }

    public Task Client_GroupPairLeft(GroupPairDto dto)
    {
        Logger.Verbose("Client_GroupPairLeft: " + dto);
        _pairManager.RemoveGroupPair(dto);
        return Task.CompletedTask;
    }

    public void OnGroupChangePermissions(Action<GroupPermissionDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupChangePermissions), act);
    }

    public Task Client_GroupChangePermissions(GroupPermissionDto dto)
    {
        Logger.Verbose("Client_GroupChangePermissions: " + dto);
        _pairManager.SetGroupPermissions(dto);
        return Task.CompletedTask;
    }

    public void OnGroupPairChangePermissions(Action<GroupPairUserPermissionDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairChangePermissions), act);
    }

    public Task Client_GroupPairChangePermissions(GroupPairUserPermissionDto dto)
    {
        Logger.Verbose("Client_GroupPairChangePermissions: " + dto);
        if (string.Equals(dto.UID, UID, StringComparison.Ordinal)) _pairManager.SetGroupUserPermissions(dto);
        else _pairManager.SetGroupPairUserPermissions(dto);
        return Task.CompletedTask;
    }

    public void OnGroupPairChangeUserInfo(Action<GroupPairUserInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairChangeUserInfo), act);
    }

    public Task Client_GroupPairChangeUserInfo(GroupPairUserInfoDto dto)
    {
        Logger.Verbose("Client_GroupPairChangeUserInfo: " + dto);
        _pairManager.SetGroupPairStatusInfo(dto);
        return Task.CompletedTask;
    }

    public Task Client_UserReceiveCharacterData(OnlineUserCharaDataDto dto)
    {
        Logger.Verbose("Client_UserReceiveCharacterData: " + dto.User);
        _pairManager.ReceiveCharaData(dto);
        return Task.CompletedTask;
    }

    public void OnUserAddClientPair(Action<UserPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserAddClientPair), act);
    }

    public Task Client_UserAddClientPair(UserPairDto dto)
    {
        Logger.Debug($"Client_UserAddClientPair: " + dto);
        _pairManager.AddUserPair(dto);
        return Task.CompletedTask;
    }

    public void OnUserRemoveClientPair(Action<UserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserRemoveClientPair), act);
    }

    public Task Client_UserRemoveClientPair(UserDto dto)
    {
        Logger.Debug($"Client_UserRemoveClientPair: " + dto);
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
        Logger.Debug($"Client_UserSendOffline: {dto}");
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
        Logger.Debug($"Client_UserSendOnline: {dto}");
        _pairManager.MarkPairOnline(dto, this);
        return Task.CompletedTask;
    }

    public void OnUserUpdateOtherPairPermissions(Action<UserPermissionsDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateOtherPairPermissions), act);
    }

    public Task Client_UserUpdateOtherPairPermissions(UserPermissionsDto dto)
    {
        Logger.Debug($"Client_UserUpdateOtherPairPermissions: {dto}");
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
        Logger.Debug($"Client_UserUpdateSelfPairPermissions: {dto}");
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
                if (_configService.Current.HideInfoMessages)
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
