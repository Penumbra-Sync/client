using MareSynchronos.API.Dto.User;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task UserDelete()
    {
        CheckConnection();
        await FilesDeleteAll().ConfigureAwait(false);
        await _mareHub!.SendAsync(nameof(UserDelete)).ConfigureAwait(false);
        await CreateConnections().ConfigureAwait(false);
    }

    public async Task UserPushData(UserCharaDataMessageDto dto)
    {
        try
        {
            await _mareHub!.InvokeAsync(nameof(UserPushData), dto).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to Push character data");
        }
    }

    public async Task<List<UserPairDto>> UserGetPairedClients()
    {
        return await _mareHub!.InvokeAsync<List<UserPairDto>>(nameof(UserGetPairedClients)).ConfigureAwait(false);
    }

    public async Task<List<OnlineUserIdentDto>> UserGetOnlinePairs()
    {
        return await _mareHub!.InvokeAsync<List<OnlineUserIdentDto>>(nameof(UserGetOnlinePairs)).ConfigureAwait(false);
    }

    public async Task UserSetPairPermissions(UserPermissionsDto dto)
    {
        await _mareHub!.SendAsync(nameof(UserSetPairPermissions), dto).ConfigureAwait(false);
    }

    public async Task UserAddPair(UserDto dto)
    {
        if (!IsConnected) return;
        await _mareHub!.SendAsync(nameof(UserAddPair), dto).ConfigureAwait(false);
    }

    public async Task UserRemovePair(UserDto dto)
    {
        if (!IsConnected) return;
        await _mareHub!.SendAsync(nameof(UserRemovePair), dto).ConfigureAwait(false);
    }
}