using System.Linq;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;

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

    }

    private void GroupChangedCallback(GroupDto dto)
    {

    }
}
