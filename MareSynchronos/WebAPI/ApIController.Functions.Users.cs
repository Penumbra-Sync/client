using System.Collections.Generic;
using System.Threading.Tasks;
using MareSynchronos.API;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task DeleteAccount()
    {
        _pluginConfiguration.ClientSecret.Remove(ApiUri);
        _pluginConfiguration.Save();
        await _mareHub!.SendAsync(Api.SendFileDeleteAllFiles).ConfigureAwait(false);
        await _mareHub!.SendAsync(Api.SendUserDeleteAccount).ConfigureAwait(false);
        await CreateConnections().ConfigureAwait(false);
    }

    public async Task<List<string>> GetOnlineCharacters()
    {
        return await _mareHub!.InvokeAsync<List<string>>(Api.InvokeUserGetOnlineCharacters).ConfigureAwait(false);
    }

    public async Task SendPairedClientAddition(string uid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendUserPairedClientAddition, uid).ConfigureAwait(false);
    }

    public async Task SendPairedClientPauseChange(string uid, bool paused)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendUserPairedClientPauseChange, uid, paused).ConfigureAwait(false);
    }

    public async Task SendPairedClientRemoval(string uid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(Api.SendUserPairedClientRemoval, uid).ConfigureAwait(false);
    }
}


