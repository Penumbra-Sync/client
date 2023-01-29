using MareSynchronos.API.Dto.Group;
using MareSynchronos.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;
public partial class ApiController
{
    private void CheckConnection()
    {
        if (ServerState is not (ServerState.Connected or ServerState.Connecting or ServerState.Reconnecting)) throw new System.Exception("Not connected");
    }

    public async Task<List<BannedGroupUserDto>> GroupGetBannedUsers(GroupDto group)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<BannedGroupUserDto>>(nameof(GroupGetBannedUsers), group).ConfigureAwait(false);
    }

    public async Task GroupClear(GroupDto group)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupClear), group).ConfigureAwait(false);
    }

    public async Task GroupChangeOwnership(GroupPairDto groupPair)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupChangeOwnership), groupPair).ConfigureAwait(false);
    }

    public async Task<bool> GroupChangePassword(GroupPasswordDto groupPassword)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(GroupChangePassword), groupPassword).ConfigureAwait(false);
    }

    public async Task<GroupPasswordDto> GroupCreate()
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<GroupPasswordDto>(nameof(GroupCreate)).ConfigureAwait(false);
    }

    public async Task<List<GroupFullInfoDto>> GroupsGetAll()
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<GroupFullInfoDto>>(nameof(GroupsGetAll)).ConfigureAwait(false);
    }

    public async Task<List<GroupPairFullInfoDto>> GroupsGetUsersInGroup(GroupDto group)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<GroupPairFullInfoDto>>(nameof(GroupsGetUsersInGroup), group).ConfigureAwait(false);
    }

    public async Task GroupBanUser(GroupPairDto dto, string reason)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupBanUser), dto, reason).ConfigureAwait(false);
    }

    public async Task GroupChangeGroupPermissionState(GroupPermissionDto dto)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupChangeGroupPermissionState), dto).ConfigureAwait(false);
    }

    public async Task GroupChangeIndividualPermissionState(GroupPairUserPermissionDto dto)
    {
        CheckConnection();
        Logger.Debug("Sending " + dto);
        await _mareHub!.SendAsync(nameof(GroupChangeIndividualPermissionState), dto).ConfigureAwait(false);
    }

    public async Task GroupDelete(GroupDto group)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupDelete), group).ConfigureAwait(false);
    }

    public async Task<bool> GroupJoin(GroupPasswordDto passwordedGroup)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(GroupJoin), passwordedGroup).ConfigureAwait(false);
    }

    public async Task GroupLeave(GroupDto group)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupLeave), group).ConfigureAwait(false);
    }

    public async Task GroupRemoveUser(GroupPairDto groupPair)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupRemoveUser), groupPair).ConfigureAwait(false);
    }

    public async Task GroupUnbanUser(GroupPairDto groupPair)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupUnbanUser), groupPair).ConfigureAwait(false);
    }

    public async Task GroupSetUserInfo(GroupPairUserInfoDto userInfo)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupSetUserInfo), userInfo).ConfigureAwait(false);
    }

    public async Task<List<string>> GroupCreateTempInvite(GroupDto group, int amount)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<string>>(nameof(GroupCreateTempInvite), group, amount).ConfigureAwait(false);
    }
}