using System;
using System.Linq;
using System.Threading.Tasks;
using MareSynchronos.API;
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

    public void OnGroupChange(Action<GroupDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupChange), act);
    }

    public void OnGroupUserChange(Action<GroupPairDto> act)
    {
        if (_initialized) return;
        _mareHub!.On(nameof(Client_GroupUserChange), act);
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

    public async Task Client_GroupChange(GroupDto dto)
    {
        if (dto.IsDeleted.GetValueOrDefault(false))
        {
            Groups.RemoveAll(g => string.Equals(g.GID, dto.GID, System.StringComparison.Ordinal));
            GroupPairedClients.RemoveAll(g => string.Equals(g.GroupGID, dto.GID, System.StringComparison.Ordinal));
            return;
        }

        var existingGroup = Groups.FirstOrDefault(g => string.Equals(g.GID, dto.GID, System.StringComparison.Ordinal));
        if (existingGroup == null)
        {
            Groups.Add(dto);
            GroupPairedClients.AddRange(await GroupsGetUsersInGroup(dto.GID).ConfigureAwait(false));
            return;
        }

        existingGroup.OwnedBy = dto.OwnedBy ?? existingGroup.OwnedBy;
        existingGroup.InvitesEnabled = dto.InvitesEnabled ?? existingGroup.InvitesEnabled;
        existingGroup.IsPaused = dto.IsPaused ?? existingGroup.IsPaused;
        existingGroup.IsModerator = dto.IsModerator ?? existingGroup.IsModerator;
    }

    public Task Client_GroupUserChange(GroupPairDto dto)
    {
        if (dto.IsRemoved.GetValueOrDefault(false))
        {
            GroupPairedClients.RemoveAll(g => string.Equals(g.GroupGID, dto.GroupGID, System.StringComparison.Ordinal) && string.Equals(g.UserUID, dto.UserUID, System.StringComparison.Ordinal));
            return Task.CompletedTask;
        }

        var existingUser = GroupPairedClients.FirstOrDefault(f => string.Equals(f.GroupGID, dto.GroupGID, System.StringComparison.Ordinal) && string.Equals(f.UserUID, dto.UserUID, System.StringComparison.Ordinal));
        if (existingUser == null)
        {
            GroupPairedClients.Add(dto);
            return Task.CompletedTask;
        }

        existingUser.IsPaused = dto.IsPaused ?? existingUser.IsPaused;
        existingUser.UserAlias = dto.UserAlias ?? existingUser.UserAlias;
        existingUser.IsPinned = dto.IsPinned ?? existingUser.IsPinned;
        existingUser.IsModerator = dto.IsModerator ?? existingUser.IsModerator;

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
                _dalamudUtil.PrintInfoChat(message);
                break;
        }

        return Task.CompletedTask;
    }
}
