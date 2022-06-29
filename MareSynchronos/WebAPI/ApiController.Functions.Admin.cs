using System.Collections.Generic;
using System.Threading.Tasks;
using MareSynchronos.API;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI
{
    public partial class ApiController
    {
        public async Task AddOrUpdateForbiddenFileEntry(ForbiddenFileDto forbiddenFile)
        {
            await _adminHub!.SendAsync("UpdateOrAddForbiddenFile", forbiddenFile);
        }

        public async Task DeleteForbiddenFileEntry(ForbiddenFileDto forbiddenFile)
        {
            await _adminHub!.SendAsync("DeleteForbiddenFile", forbiddenFile);
        }

        public async Task AddOrUpdateBannedUserEntry(BannedUserDto bannedUser)
        {
            await _adminHub!.SendAsync("UpdateOrAddBannedUser", bannedUser);
        }

        public async Task DeleteBannedUserEntry(BannedUserDto bannedUser)
        {
            await _adminHub!.SendAsync("DeleteBannedUser", bannedUser);
        }

        public async Task RefreshOnlineUsers()
        {
            AdminOnlineUsers = await _adminHub!.InvokeAsync<List<OnlineUserDto>>("GetOnlineUsers");
        }

        public List<OnlineUserDto> AdminOnlineUsers { get; set; } = new List<OnlineUserDto>();

        public void PromoteToModerator(string onlineUserUID)
        {
            _adminHub!.SendAsync("ChangeModeratorStatus", onlineUserUID, true);
        }

        public void DemoteFromModerator(string onlineUserUID)
        {
            _adminHub!.SendAsync("ChangeModeratorStatus", onlineUserUID, false);
        }
    }
}
