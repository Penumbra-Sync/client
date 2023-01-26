using MareSynchronos.API.Dto.Admin;
using MareSynchronos.API.Dto.Files;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task AdminUpdateOrAddForbiddenFile(ForbiddenFileDto forbiddenFile)
    {
        await _mareHub!.SendAsync(nameof(AdminUpdateOrAddForbiddenFile), forbiddenFile).ConfigureAwait(false);
    }

    public async Task AdminDeleteForbiddenFile(ForbiddenFileDto forbiddenFile)
    {
        await _mareHub!.SendAsync(nameof(AdminDeleteForbiddenFile), forbiddenFile).ConfigureAwait(false);
    }

    public async Task AdminUpdateOrAddBannedUser(BannedUserDto bannedUser)
    {
        await _mareHub!.SendAsync(nameof(AdminUpdateOrAddBannedUser), bannedUser).ConfigureAwait(false);
    }

    public async Task AdminDeleteBannedUser(BannedUserDto bannedUser)
    {
        await _mareHub!.SendAsync(nameof(AdminDeleteBannedUser), bannedUser).ConfigureAwait(false);
    }

    public async Task RefreshOnlineUsers()
    {
        AdminOnlineUsers = await AdminGetOnlineUsers().ConfigureAwait(false);
    }

    public async Task<List<OnlineUserDto>> AdminGetOnlineUsers()
    {
        return await _mareHub!.InvokeAsync<List<OnlineUserDto>>(nameof(AdminGetOnlineUsers)).ConfigureAwait(false);
    }

    public List<OnlineUserDto> AdminOnlineUsers { get; set; } = new List<OnlineUserDto>();

    public async Task AdminChangeModeratorStatus(string onlineUserUID, bool isModerator)
    {
        await _mareHub!.SendAsync(nameof(AdminChangeModeratorStatus), onlineUserUID, isModerator).ConfigureAwait(false);
    }

    public async Task<List<ForbiddenFileDto>> AdminGetForbiddenFiles()
    {
        return await _mareHub!.InvokeAsync<List<ForbiddenFileDto>>(nameof(AdminGetForbiddenFiles)).ConfigureAwait(false);
    }

    public async Task<List<BannedUserDto>> AdminGetBannedUsers()
    {
        return await _mareHub!.InvokeAsync<List<BannedUserDto>>(nameof(AdminGetBannedUsers)).ConfigureAwait(false);
    }
}
