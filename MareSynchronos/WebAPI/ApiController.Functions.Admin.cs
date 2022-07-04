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
            await _adminHub!.SendAsync(AdminHubAPI.SendUpdateOrAddForbiddenFile, forbiddenFile);
        }

        public async Task DeleteForbiddenFileEntry(ForbiddenFileDto forbiddenFile)
        {
            await _adminHub!.SendAsync(AdminHubAPI.SendDeleteForbiddenFile, forbiddenFile);
        }

        public async Task AddOrUpdateBannedUserEntry(BannedUserDto bannedUser)
        {
            await _adminHub!.SendAsync(AdminHubAPI.SendUpdateOrAddBannedUser, bannedUser);
        }

        public async Task DeleteBannedUserEntry(BannedUserDto bannedUser)
        {
            await _adminHub!.SendAsync(AdminHubAPI.SendDeleteBannedUser, bannedUser);
        }

        public async Task RefreshOnlineUsers()
        {
            AdminOnlineUsers = await _adminHub!.InvokeAsync<List<OnlineUserDto>>(AdminHubAPI.InvokeGetOnlineUsers);
        }

        public List<OnlineUserDto> AdminOnlineUsers { get; set; } = new List<OnlineUserDto>();

        public void PromoteToModerator(string onlineUserUID)
        {
            _adminHub!.SendAsync(AdminHubAPI.SendChangeModeratorStatus, onlineUserUID, true);
        }

        public void DemoteFromModerator(string onlineUserUID)
        {
            _adminHub!.SendAsync(AdminHubAPI.SendChangeModeratorStatus, onlineUserUID, false);
        }
    }
}
