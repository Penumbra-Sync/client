using System;
using System.Linq;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;

namespace MareSynchronos.WebAPI
{
    public partial class ApiController
    {
        private void UserForcedReconnectCallback()
        {
            _ = CreateConnections();
        }

        private void UpdateLocalClientPairsCallback(ClientPairDto dto, string characterIdentifier)
        {
            var entry = PairedClients.SingleOrDefault(e => e.OtherUID == dto.OtherUID);
            if (dto.IsRemoved)
            {
                PairedClients.RemoveAll(p => p.OtherUID == dto.OtherUID);
                UnpairedFromOther?.Invoke(characterIdentifier, EventArgs.Empty);
                return;
            }
            if (entry == null)
            {
                PairedClients.Add(dto);
                return;
            }

            if ((entry.IsPausedFromOthers != dto.IsPausedFromOthers || entry.IsSynced != dto.IsSynced || entry.IsPaused != dto.IsPaused)
                && !dto.IsPaused && dto.IsSynced && !dto.IsPausedFromOthers)
            {
                PairedWithOther?.Invoke(characterIdentifier, EventArgs.Empty);
            }

            entry.IsPaused = dto.IsPaused;
            entry.IsPausedFromOthers = dto.IsPausedFromOthers;
            entry.IsSynced = dto.IsSynced;

            if (dto.IsPaused || dto.IsPausedFromOthers || !dto.IsSynced)
            {
                UnpairedFromOther?.Invoke(characterIdentifier, EventArgs.Empty);
            }
        }

        private Task ReceiveCharacterDataCallback(CharacterCacheDto character, string characterHash)
        {
            Logger.Verbose("Received DTO for " + characterHash);
            CharacterReceived?.Invoke(null, new CharacterReceivedEventArgs(characterHash, character));
            return Task.CompletedTask;
        }

        private void UpdateOrAddBannedUserCallback(BannedUserDto obj)
        {
            var user = BannedUsers.SingleOrDefault(b => b.CharacterHash == obj.CharacterHash);
            if (user == null)
            {
                BannedUsers.Add(obj);
            }
            else
            {
                user.Reason = obj.Reason;
            }
        }

        private void DeleteBannedUserCallback(BannedUserDto obj)
        {
            BannedUsers.RemoveAll(a => a.CharacterHash == obj.CharacterHash);
        }

        private void UpdateOrAddForbiddenFileCallback(ForbiddenFileDto obj)
        {
            var user = ForbiddenFiles.SingleOrDefault(b => b.Hash == obj.Hash);
            if (user == null)
            {
                ForbiddenFiles.Add(obj);
            }
            else
            {
                user.ForbiddenBy = obj.ForbiddenBy;
            }
        }

        private void DeleteForbiddenFileCallback(ForbiddenFileDto obj)
        {
            ForbiddenFiles.RemoveAll(f => f.Hash == obj.Hash);
        }
    }
}
