using MareSynchronos.API;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MareSynchronos.WebAPI;
public partial class ApiController
{
    public async Task<GroupCreatedDto> CreateGroup()
    {
        return await _mareHub!.InvokeAsync<GroupCreatedDto>(Api.InvokeGroupCreate);
    }

    public async Task<bool> ChangeGroupPassword(string gid, string newpassword)
    {
        return await _mareHub!.InvokeAsync<bool>(Api.InvokeGroupChangePassword, gid, newpassword);
    }

    public async Task<List<GroupDto>> GetGroups()
    {
        return await _mareHub!.InvokeAsync<List<GroupDto>>(Api.InvokeGroupGetGroups);
    }

    public async Task<List<GroupPairDto>> GetUsersInGroup(string gid)
    {
        return await _mareHub!.InvokeAsync<List<GroupPairDto>>(Api.InvokeGroupGetUsersInGroup, gid);
    }

    public async Task SendGroupJoin(string gid, string password)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendGroupJoin, gid, password);
    }

    public async Task SendGroupChangeInviteState(string gid, bool opened)
    {
        await _mareHub!.SendAsync(Api.SendGroupChangeInviteState, gid, opened);
    }

    public async Task SendDeleteGroup(string gid)
    {
        await _mareHub!.SendAsync(Api.SendGroupDelete, gid);
    }

    public async Task SendLeaveGroup(string gid)
    {
        await _mareHub!.SendAsync(Api.SendGroupLeave, gid);
    }

    public async Task SendPauseGroup(string gid, bool isPaused)
    {
        await _mareHub!.SendAsync(Api.SendGroupPause, gid, isPaused);
    }

    public async Task SendRemoveUserFromGroup(string gid, string uid)
    {
        await _mareHub!.SendAsync(Api.SendGroupRemoveUser, gid, uid);
    }

    public async Task ChangeOwnerOfGroup(string gid, string uid)
    {
        await _mareHub!.SendAsync(Api.SendGroupChangeOwner, gid, uid);
    }
}