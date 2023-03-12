using Dalamud.Interface.Internal.Notifications;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Services.Mediator;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    private void ExecuteSafely(Action act)
    {
        try
        {
            act();
        }
        catch (Exception ex)
        {
            Logger.LogCritical(ex, "Error on executing safely");
        }
    }

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

    public Task Client_GroupSendFullInfo(GroupFullInfoDto groupInfo)
    {
        Logger.LogTrace("Client_GroupSendFullInfo: {dto}", groupInfo);
        ExecuteSafely(() => _pairManager.AddGroup(groupInfo));
        return Task.CompletedTask;
    }

    public void OnGroupSendInfo(Action<GroupInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupSendInfo), act);
    }

    public Task Client_GroupSendInfo(GroupInfoDto groupInfo)
    {
        Logger.LogTrace("Client_GroupSendInfo: {dto}", groupInfo);
        ExecuteSafely(() => _pairManager.SetGroupInfo(groupInfo));
        return Task.CompletedTask;
    }

    public void OnGroupDelete(Action<GroupDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupDelete), act);
    }

    public Task Client_GroupDelete(GroupDto groupDto)
    {
        Logger.LogTrace("Client_GroupDelete: {dto}", groupDto);
        ExecuteSafely(() => _pairManager.RemoveGroup(groupDto.Group));
        return Task.CompletedTask;
    }

    public void OnGroupPairJoined(Action<GroupPairFullInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairJoined), act);
    }

    public Task Client_GroupPairJoined(GroupPairFullInfoDto groupPairInfoDto)
    {
        Logger.LogTrace("Client_GroupPairJoined: {dto}", groupPairInfoDto);
        ExecuteSafely(() => _pairManager.AddGroupPair(groupPairInfoDto));
        return Task.CompletedTask;
    }

    public void OnGroupPairLeft(Action<GroupPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairLeft), act);
    }

    public Task Client_GroupPairLeft(GroupPairDto groupPairDto)
    {
        Logger.LogTrace("Client_GroupPairLeft: {dto}", groupPairDto);
        ExecuteSafely(() => _pairManager.RemoveGroupPair(groupPairDto));
        return Task.CompletedTask;
    }

    public void OnGroupChangePermissions(Action<GroupPermissionDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupChangePermissions), act);
    }

    public Task Client_GroupChangePermissions(GroupPermissionDto groupPermission)
    {
        Logger.LogTrace("Client_GroupChangePermissions: {perm}", groupPermission);
        ExecuteSafely(() => _pairManager.SetGroupPermissions(groupPermission));
        return Task.CompletedTask;
    }

    public void OnGroupPairChangePermissions(Action<GroupPairUserPermissionDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairChangePermissions), act);
    }

    public Task Client_GroupPairChangePermissions(GroupPairUserPermissionDto permissionDto)
    {
        Logger.LogTrace("Client_GroupPairChangePermissions: {perm}", permissionDto);
        ExecuteSafely(() =>
        {
            if (string.Equals(permissionDto.UID, UID, StringComparison.Ordinal)) _pairManager.SetGroupUserPermissions(permissionDto);
            else _pairManager.SetGroupPairUserPermissions(permissionDto);
        });
        return Task.CompletedTask;
    }

    public void OnGroupPairChangeUserInfo(Action<GroupPairUserInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupPairChangeUserInfo), act);
    }

    public Task Client_GroupPairChangeUserInfo(GroupPairUserInfoDto userInfo)
    {
        Logger.LogTrace("Client_GroupPairChangeUserInfo: {dto}", userInfo);
        ExecuteSafely(() =>
        {
            if (string.Equals(userInfo.UID, UID, StringComparison.Ordinal)) _pairManager.SetGroupStatusInfo(userInfo);
            else _pairManager.SetGroupPairStatusInfo(userInfo);
        });
        return Task.CompletedTask;
    }

    public Task Client_UserReceiveCharacterData(OnlineUserCharaDataDto dataDto)
    {
        Logger.LogTrace("Client_UserReceiveCharacterData: {user}", dataDto.User);
        ExecuteSafely(() => _pairManager.ReceiveCharaData(dataDto));
        return Task.CompletedTask;
    }

    public void OnUserAddClientPair(Action<UserPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserAddClientPair), act);
    }

    public Task Client_UserAddClientPair(UserPairDto dto)
    {
        Logger.LogDebug("Client_UserAddClientPair: {dto}", dto);
        ExecuteSafely(() => _pairManager.AddUserPair(dto));
        return Task.CompletedTask;
    }

    public void OnUserRemoveClientPair(Action<UserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserRemoveClientPair), act);
    }

    public Task Client_UserRemoveClientPair(UserDto dto)
    {
        Logger.LogDebug("Client_UserRemoveClientPair: {dto}", dto);
        ExecuteSafely(() => _pairManager.RemoveUserPair(dto));
        return Task.CompletedTask;
    }

    public void OnUserSendOffline(Action<UserDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserSendOffline), act);
    }

    public Task Client_UserSendOffline(UserDto dto)
    {
        Logger.LogDebug("Client_UserSendOffline: {dto}", dto);
        ExecuteSafely(() => _pairManager.MarkPairOffline(dto.User));
        return Task.CompletedTask;
    }

    public void OnUserSendOnline(Action<OnlineUserIdentDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserSendOnline), act);
    }

    public Task Client_UserSendOnline(OnlineUserIdentDto dto)
    {
        Logger.LogDebug("Client_UserSendOnline: {dto}", dto);
        ExecuteSafely(() => _pairManager.MarkPairOnline(dto));
        return Task.CompletedTask;
    }

    public void OnUserUpdateOtherPairPermissions(Action<UserPermissionsDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateOtherPairPermissions), act);
    }

    public Task Client_UserUpdateOtherPairPermissions(UserPermissionsDto dto)
    {
        Logger.LogDebug("Client_UserUpdateOtherPairPermissions: {dto}", dto);
        ExecuteSafely(() => _pairManager.UpdatePairPermissions(dto));
        return Task.CompletedTask;
    }

    public void OnUserUpdateSelfPairPermissions(Action<UserPermissionsDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateSelfPairPermissions), act);
    }

    public Task Client_UserUpdateSelfPairPermissions(UserPermissionsDto dto)
    {
        Logger.LogDebug("Client_UserUpdateSelfPairPermissions: {dto}", dto);
        ExecuteSafely(() => _pairManager.UpdateSelfPairPermissions(dto));
        return Task.CompletedTask;
    }

    public Task Client_UpdateSystemInfo(SystemInfoDto systemInfo)
    {
        SystemInfoDto = systemInfo;
        return Task.CompletedTask;
    }

    public Task Client_ReceiveServerMessage(MessageSeverity messageSeverity, string message)
    {
        switch (messageSeverity)
        {
            case MessageSeverity.Error:
                Mediator.Publish(new NotificationMessage("Warning from " + _serverManager.CurrentServer!.ServerName, message, NotificationType.Error, 7500));
                break;
            case MessageSeverity.Warning:
                Mediator.Publish(new NotificationMessage("Warning from " + _serverManager.CurrentServer!.ServerName, message, NotificationType.Warning, 7500));
                break;
            case MessageSeverity.Information:
                if (_doNotNotifyOnNextInfo)
                {
                    _doNotNotifyOnNextInfo = false;
                    break;
                }
                Mediator.Publish(new NotificationMessage("Info from " + _serverManager.CurrentServer!.ServerName, message, NotificationType.Info, 5000));
                break;
        }

        return Task.CompletedTask;
    }

    public Task Client_DownloadReady(Guid requestId)
    {
        Logger.LogDebug("Server sent {requestId} ready", requestId);
        Mediator.Publish(new DownloadReadyMessage(requestId));
        return Task.CompletedTask;
    }
}
