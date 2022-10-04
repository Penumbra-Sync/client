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
        var entry = PairedClients.SingleOrDefault(e => string.Equals(e.OtherUID, dto.OtherUID, System.StringComparison.Ordinal));
        if (dto.IsRemoved)
        {
            PairedClients.RemoveAll(p => string.Equals(p.OtherUID, dto.OtherUID, System.StringComparison.Ordinal));
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
        var user = AdminBannedUsers.SingleOrDefault(b => string.Equals(b.CharacterHash, obj.CharacterHash, System.StringComparison.Ordinal));
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
        AdminBannedUsers.RemoveAll(a => string.Equals(a.CharacterHash, obj.CharacterHash, System.StringComparison.Ordinal));
    }

    private void UpdateOrAddForbiddenFileCallback(ForbiddenFileDto obj)
    {
        var user = AdminForbiddenFiles.SingleOrDefault(b => string.Equals(b.Hash, obj.Hash, System.StringComparison.Ordinal));
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
        AdminForbiddenFiles.RemoveAll(f => string.Equals(f.Hash, obj.Hash, System.StringComparison.Ordinal));
    }

    private void GroupPairChangedCallback(GroupPairDto dto)
    {
        if (dto.IsRemoved.GetValueOrDefault(false))
        {
            GroupPairedClients.RemoveAll(g => string.Equals(g.GroupGID, dto.GroupGID, System.StringComparison.Ordinal) && string.Equals(g.UserUID, dto.UserUID, System.StringComparison.Ordinal));
            return;
        }

        var existingUser = GroupPairedClients.FirstOrDefault(f => string.Equals(f.GroupGID, dto.GroupGID, System.StringComparison.Ordinal) && string.Equals(f.UserUID, dto.UserUID, System.StringComparison.Ordinal));
        if (existingUser == null)
        {
            GroupPairedClients.Add(dto);
            return;
        }

        existingUser.IsPaused = dto.IsPaused ?? existingUser.IsPaused;
        existingUser.UserAlias = dto.UserAlias ?? existingUser.UserAlias;
        existingUser.IsPinned = dto.IsPinned ?? existingUser.IsPinned;
    }

    private async Task GroupChangedCallback(GroupDto dto)
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
            GroupPairedClients.AddRange(await _mareHub!.InvokeAsync<List<GroupPairDto>>(Api.InvokeGroupGetUsersInGroup, dto.GID).ConfigureAwait(false));
            return;
        }

        existingGroup.OwnedBy = dto.OwnedBy ?? existingGroup.OwnedBy;
        existingGroup.InvitesEnabled = dto.InvitesEnabled ?? existingGroup.InvitesEnabled;
        existingGroup.IsPaused = dto.IsPaused ?? existingGroup.IsPaused;
    }
}
