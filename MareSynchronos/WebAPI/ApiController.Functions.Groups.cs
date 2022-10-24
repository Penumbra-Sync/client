using MareSynchronos.API;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MareSynchronos.WebAPI;
public partial class ApiController
{
    public async Task<GroupCreatedDto> GroupCreate()
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return new GroupCreatedDto();
        return await _mareHub!.InvokeAsync<GroupCreatedDto>(nameof(GroupCreate)).ConfigureAwait(false);
    }

    public async Task<bool> GroupChangePassword(string gid, string newpassword)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return false;
        return await _mareHub!.InvokeAsync<bool>(nameof(GroupChangePassword), gid, newpassword).ConfigureAwait(false);
    }

    public async Task<List<GroupDto>> GroupsGetAll()
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return new List<GroupDto>();
        return await _mareHub!.InvokeAsync<List<GroupDto>>(nameof(GroupsGetAll)).ConfigureAwait(false);
    }

    public async Task<List<GroupPairDto>> GroupsGetUsersInGroup(string gid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return new List<GroupPairDto>();
        return await _mareHub!.InvokeAsync<List<GroupPairDto>>(nameof(GroupsGetUsersInGroup), gid).ConfigureAwait(false);
    }

    public async Task<bool> GroupJoin(string gid, string password)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return false;
        return await _mareHub!.InvokeAsync<bool>(nameof(GroupJoin), gid.Trim(), password).ConfigureAwait(false);
    }

    public async Task GroupChangeInviteState(string gid, bool opened)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupChangeInviteState), gid, opened).ConfigureAwait(false);
    }

    public async Task GroupDelete(string gid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupDelete), gid).ConfigureAwait(false);
    }

    public async Task GroupChangePinned(string gid, string uid, bool isPinned)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupChangePinned), gid, uid, isPinned).ConfigureAwait(false);
    }

    public async Task GroupClear(string gid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupClear), gid).ConfigureAwait(false);
    }

    public async Task GroupLeave(string gid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupLeave), gid).ConfigureAwait(false);
    }

    public async Task GroupChangePauseState(string gid, bool isPaused)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupChangePauseState), gid, isPaused).ConfigureAwait(false);
    }

    public async Task GroupRemoveUser(string gid, string uid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupRemoveUser), gid, uid).ConfigureAwait(false);
    }

    public async Task GroupChangeOwnership(string gid, string uid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupChangeOwnership), gid, uid).ConfigureAwait(false);
    }

    public async Task GroupBanUser(string gid, string uid, string reason)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupBanUser), gid, uid, reason).ConfigureAwait(false);
    }

    public async Task GroupUnbanUser(string gid, string uid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupUnbanUser), gid, uid).ConfigureAwait(false);
    }

    public async Task<List<BannedGroupUserDto>> GroupGetBannedUsers(string gid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return new();
        return await _mareHub!.InvokeAsync<List<BannedGroupUserDto>>(nameof(GroupGetBannedUsers), gid).ConfigureAwait(false);
    }

    public async Task GroupSetModerator(string gid, string uid, bool isModerator)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(GroupSetModerator), gid, uid, isModerator).ConfigureAwait(false);
    }

    public async Task<List<string>> GroupCreateTempInvite(string gid, int amount)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return new();
        return await _mareHub!.InvokeAsync<List<string>>(nameof(GroupCreateTempInvite), gid, amount).ConfigureAwait(false);
    }
}