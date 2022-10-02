using MareSynchronos.API;
using MareSynchronos.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MareSynchronos.WebAPI;
public partial class ApiController
{
    public async Task<GroupCreatedDto> CreateGroup()
    {
        if (!IsConnected || SecretKey == "-") return new GroupCreatedDto();
        return await _mareHub!.InvokeAsync<GroupCreatedDto>(Api.InvokeGroupCreate);
    }

    public async Task<bool> ChangeGroupPassword(string gid, string newpassword)
    {
        if (!IsConnected || SecretKey == "-") return false;
        return await _mareHub!.InvokeAsync<bool>(Api.InvokeGroupChangePassword, gid, newpassword);
    }

    public async Task<List<GroupDto>> GetGroups()
    {
        if (!IsConnected || SecretKey == "-") return new List<GroupDto>();
        return await _mareHub!.InvokeAsync<List<GroupDto>>(Api.InvokeGroupGetGroups);
    }

    public async Task<List<GroupPairDto>> GetUsersInGroup(string gid)
    {
        if (!IsConnected || SecretKey == "-") return new List<GroupPairDto>();
        return await _mareHub!.InvokeAsync<List<GroupPairDto>>(Api.InvokeGroupGetUsersInGroup, gid);
    }

    public async Task<bool> SendGroupJoin(string gid, string password)
    {
        if (!IsConnected || SecretKey == "-") return false;
        return await _mareHub!.InvokeAsync<bool>(Api.InvokeGroupJoin, gid, password);
    }

    public async Task SendGroupChangeInviteState(string gid, bool opened)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendGroupChangeInviteState, gid, opened);
    }

    public async Task SendDeleteGroup(string gid)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendGroupDelete, gid);
    }

    public async Task SendChangeUserPinned(string gid, string uid, bool isPinned)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendGroupChangePinned, gid, uid, isPinned);
    }

    public async Task SendClearGroup(string gid)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendGroupClear, gid);
    }

    public async Task SendLeaveGroup(string gid)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendGroupLeave, gid);
    }

    public async Task SendPauseGroup(string gid, bool isPaused)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendGroupPause, gid, isPaused);
    }

    public async Task SendRemoveUserFromGroup(string gid, string uid)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendGroupRemoveUser, gid, uid);
    }

    public async Task ChangeOwnerOfGroup(string gid, string uid)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendGroupChangeOwner, gid, uid);
    }
}