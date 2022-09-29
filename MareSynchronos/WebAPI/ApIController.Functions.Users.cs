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
        await _mareHub!.SendAsync(Api.SendFileDeleteAllFiles);
        await _mareHub!.SendAsync(Api.SendUserDeleteAccount);
        await CreateConnections();
    }

    public async Task<List<string>> GetOnlineCharacters()
    {
        return await _mareHub!.InvokeAsync<List<string>>(Api.InvokeUserGetOnlineCharacters);
    }

    public async Task SendPairedClientAddition(string uid)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendUserPairedClientAddition, uid);
    }

    public async Task SendPairedClientPauseChange(string uid, bool paused)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendUserPairedClientPauseChange, uid, paused);
    }

    public async Task SendPairedClientRemoval(string uid)
    {
        if (!IsConnected || SecretKey == "-") return;
        await _mareHub!.SendAsync(Api.SendUserPairedClientRemoval, uid);
    }
}


