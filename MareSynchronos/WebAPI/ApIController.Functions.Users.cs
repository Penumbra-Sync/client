using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task UserDelete()
    {
        CheckConnection();
        _pluginConfiguration.ClientSecret.Remove(ApiUri);
        _pluginConfiguration.Save();
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
            Logger.Warn("Failed to Push character data: " + ex.Message);
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
        Logger.Verbose("Sending UserSetPairPermissions: " + dto);
        await _mareHub!.SendAsync(nameof(UserSetPairPermissions), dto).ConfigureAwait(false);
    }

    public async Task UserAddPair(UserDto dto)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(UserAddPair), dto).ConfigureAwait(false);
    }

    public async Task UserRemovePair(UserDto dto)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(UserRemovePair), dto).ConfigureAwait(false);
    }
}