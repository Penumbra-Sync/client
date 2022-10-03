using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MareSynchronos.API;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task AddOrUpdateForbiddenFileEntry(ForbiddenFileDto forbiddenFile)
    {
        await _mareHub!.SendAsync(Api.SendAdminUpdateOrAddForbiddenFile, forbiddenFile).ConfigureAwait(false);
    }

    public async Task DeleteForbiddenFileEntry(ForbiddenFileDto forbiddenFile)
    {
        await _mareHub!.SendAsync(Api.SendAdminDeleteForbiddenFile, forbiddenFile).ConfigureAwait(false);
    }

    public async Task AddOrUpdateBannedUserEntry(BannedUserDto bannedUser)
    {
        await _mareHub!.SendAsync(Api.SendAdminUpdateOrAddBannedUser, bannedUser).ConfigureAwait(false);
    }

    public async Task DeleteBannedUserEntry(BannedUserDto bannedUser)
    {
        await _mareHub!.SendAsync(Api.SendAdminDeleteBannedUser, bannedUser).ConfigureAwait(false);
    }

    public async Task RefreshOnlineUsers()
    {
        AdminOnlineUsers = await _mareHub!.InvokeAsync<List<OnlineUserDto>>(Api.InvokeAdminGetOnlineUsers).ConfigureAwait(false);
    }

    public List<OnlineUserDto> AdminOnlineUsers { get; set; } = new List<OnlineUserDto>();

    public void PromoteToModerator(string onlineUserUID)
    {
        _mareHub!.SendAsync(Api.SendAdminChangeModeratorStatus, onlineUserUID, true);
    }

    public void DemoteFromModerator(string onlineUserUID)
    {
        _mareHub!.SendAsync(Api.SendAdminChangeModeratorStatus, onlineUserUID, false);
    }
}
