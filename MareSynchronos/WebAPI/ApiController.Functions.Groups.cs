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
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return new GroupCreatedDto();
        return await _mareHub!.InvokeAsync<GroupCreatedDto>(Api.InvokeGroupCreate).ConfigureAwait(false);
    }

    public async Task<bool> ChangeGroupPassword(string gid, string newpassword)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return false;
        return await _mareHub!.InvokeAsync<bool>(Api.InvokeGroupChangePassword, gid, newpassword).ConfigureAwait(false);
    }

    public async Task<List<GroupDto>> GetGroups()
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return new List<GroupDto>();
        return await _mareHub!.InvokeAsync<List<GroupDto>>(Api.InvokeGroupGetGroups).ConfigureAwait(false);
    }

    public async Task<List<GroupPairDto>> GetUsersInGroup(string gid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return new List<GroupPairDto>();
        return await _mareHub!.InvokeAsync<List<GroupPairDto>>(Api.InvokeGroupGetUsersInGroup, gid).ConfigureAwait(false);
    }

    public async Task<bool> SendGroupJoin(string gid, string password)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return false;
        return await _mareHub!.InvokeAsync<bool>(Api.InvokeGroupJoin, gid, password).ConfigureAwait(false);
    }

    public async Task SendGroupChangeInviteState(string gid, bool opened)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendGroupChangeInviteState, gid, opened).ConfigureAwait(false);
    }

    public async Task SendDeleteGroup(string gid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendGroupDelete, gid).ConfigureAwait(false);
    }

    public async Task SendChangeUserPinned(string gid, string uid, bool isPinned)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendGroupChangePinned, gid, uid, isPinned).ConfigureAwait(false);
    }

    public async Task SendClearGroup(string gid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendGroupClear, gid).ConfigureAwait(false);
    }

    public async Task SendLeaveGroup(string gid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendGroupLeave, gid).ConfigureAwait(false);
    }

    public async Task SendPauseGroup(string gid, bool isPaused)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendGroupPause, gid, isPaused).ConfigureAwait(false);
    }

    public async Task SendRemoveUserFromGroup(string gid, string uid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendGroupRemoveUser, gid, uid).ConfigureAwait(false);
    }

    public async Task ChangeOwnerOfGroup(string gid, string uid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendGroupChangeOwner, gid, uid).ConfigureAwait(false);
    }
}