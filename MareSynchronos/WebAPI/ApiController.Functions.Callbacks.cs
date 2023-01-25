using System;
using System.Linq;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Routes;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public ClientPairDto? LastAddedUser { get; set; }

    public void OnUserUpdateClientPairs(Action<ClientPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserUpdateClientPairs), act);
    }

    public void OnUpdateSystemInfo(Action<SystemInfoDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UpdateSystemInfo), act);
    }

    public void OnUserReceiveCharacterData(Action<CharacterCacheDto, string> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserReceiveCharacterData), act);
    }

    public void OnUserChangePairedPlayer(Action<string, bool> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_UserChangePairedPlayer), act);
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
        GroupPairedClients[dto].GroupUserPermissions = dto.GroupPairPermissions;
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

    public Task Client_UserUpdateClientPairs(ClientPairDto dto)
    {
        var entry = PairedClients.SingleOrDefault(e => string.Equals(e.OtherUID, dto.OtherUID, System.StringComparison.Ordinal));
        if (dto.IsRemoved)
        {
            PairedClients.RemoveAll(p => string.Equals(p.OtherUID, dto.OtherUID, System.StringComparison.Ordinal));
            return Task.CompletedTask;
        }
        if (entry == null)
        {
            LastAddedUser = dto;
            PairedClients.Add(dto);
            return Task.CompletedTask;
        }

        entry.IsPaused = dto.IsPaused;
        entry.IsPausedFromOthers = dto.IsPausedFromOthers;
        entry.IsSynced = dto.IsSynced;

        return Task.CompletedTask;
    }

    public Task Client_UpdateSystemInfo(SystemInfoDto systemInfo)
    {
        SystemInfoDto = systemInfo;
        return Task.CompletedTask;
    }

    public Task Client_UserReceiveCharacterData(CharacterCacheDto clientPairDto, string characterIdent)
    {
        Logger.Verbose("Received DTO for " + characterIdent);
        CharacterReceived?.Invoke(null, new CharacterReceivedEventArgs(characterIdent, clientPairDto));
        return Task.CompletedTask;
    }

    public Task Client_UserChangePairedPlayer(string characterIdent, bool isOnline)
    {
        if (isOnline) PairedClientOnline?.Invoke(characterIdent);
        else PairedClientOffline?.Invoke(characterIdent);
        return Task.CompletedTask;
    }

    public Task Client_AdminForcedReconnect()
    {
        _ = CreateConnections();
        return Task.CompletedTask;
    }

    public Task Client_AdminDeleteBannedUser(BannedUserDto dto)
    {
        AdminBannedUsers.RemoveAll(a => string.Equals(a.CharacterHash, dto.CharacterHash, System.StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public Task Client_AdminDeleteForbiddenFile(ForbiddenFileDto dto)
    {
        AdminForbiddenFiles.RemoveAll(f => string.Equals(f.Hash, dto.Hash, System.StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    public Task Client_AdminUpdateOrAddBannedUser(BannedUserDto dto)
    {
        var user = AdminBannedUsers.SingleOrDefault(b => string.Equals(b.CharacterHash, dto.CharacterHash, System.StringComparison.Ordinal));
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
        var user = AdminForbiddenFiles.SingleOrDefault(b => string.Equals(b.Hash, dto.Hash, System.StringComparison.Ordinal));
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
                if (!_pluginConfiguration.HideInfoMessages)
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
