using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    private void UserForcedReconnectCallback()
    {
        _ = CreateConnections();
    }

    private void UpdateLocalClientPairsCallback(ClientPairDto dto)
    {
        var entry = PairedClients.SingleOrDefault(e => e.OtherUID == dto.OtherUID);
        if (dto.IsRemoved)
        {
            PairedClients.RemoveAll(p => p.OtherUID == dto.OtherUID);
            return;
        }
        if (entry == null)
        {
            PairedClients.Add(dto);
            return;
        }

        entry.IsPaused = dto.IsPaused;
        entry.IsPausedFromOthers = dto.IsPausedFromOthers;
        entry.IsSynced = dto.IsSynced;
    }

    private Task ReceiveCharacterDataCallback(CharacterCacheDto character, string characterHash)
    {
        Logger.Verbose("Received DTO for " + characterHash);
        CharacterReceived?.Invoke(null, new CharacterReceivedEventArgs(characterHash, character));
        return Task.CompletedTask;
    }

    private void UpdateOrAddBannedUserCallback(BannedUserDto obj)
    {
        var user = AdminBannedUsers.SingleOrDefault(b => b.CharacterHash == obj.CharacterHash);
        if (user == null)
        {
            AdminBannedUsers.Add(obj);
        }
        else
        {
            user.Reason = obj.Reason;
        }
    }

    private void DeleteBannedUserCallback(BannedUserDto obj)
    {
        AdminBannedUsers.RemoveAll(a => a.CharacterHash == obj.CharacterHash);
    }

    private void UpdateOrAddForbiddenFileCallback(ForbiddenFileDto obj)
    {
        var user = AdminForbiddenFiles.SingleOrDefault(b => b.Hash == obj.Hash);
        if (user == null)
        {
            AdminForbiddenFiles.Add(obj);
        }
        else
        {
            user.ForbiddenBy = obj.ForbiddenBy;
        }
    }

    private void DeleteForbiddenFileCallback(ForbiddenFileDto obj)
    {
        AdminForbiddenFiles.RemoveAll(f => f.Hash == obj.Hash);
    }

    private void GroupPairChangedCallback(GroupPairDto dto)
    {
        if (dto.IsRemoved.GetValueOrDefault(false))
        {
            GroupPairedClients.RemoveAll(g => g.GroupGID == dto.GroupGID && g.UserUID == dto.UserUID);
            return;
        }

        var existingUser = GroupPairedClients.FirstOrDefault(f => f.GroupGID == dto.GroupGID && f.UserUID == dto.UserUID);
        if (existingUser == null)
        {
            GroupPairedClients.Add(dto);
            return;
        }

        existingUser.IsPaused = dto.IsPaused ?? existingUser.IsPaused;
        existingUser.UserAlias = dto.UserAlias ?? existingUser.UserAlias;
    }

    private async Task GroupChangedCallback(GroupDto dto)
    {
        if (dto.IsDeleted.GetValueOrDefault(false))
        {
            Groups.RemoveAll(g => g.GID == dto.GID);
            GroupPairedClients.RemoveAll(g => g.GroupGID == dto.GID);
            return;
        }

        var existingGroup = Groups.FirstOrDefault(g => g.GID == dto.GID);
        if (existingGroup == null)
        {
            Groups.Add(dto);
            GroupPairedClients.AddRange(await _mareHub!.InvokeAsync<List<GroupPairDto>>(Api.InvokeGroupGetUsersInGroup, dto.GID));
            return;
        }

        existingGroup.OwnedBy = dto.OwnedBy ?? existingGroup.OwnedBy;
        existingGroup.InvitesEnabled = dto.InvitesEnabled ?? existingGroup.InvitesEnabled;
        existingGroup.IsPaused = dto.IsPaused ?? existingGroup.IsPaused;
    }
}
