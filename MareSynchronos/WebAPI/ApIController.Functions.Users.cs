using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronos.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task UserDelete()
    {
        _pluginConfiguration.ClientSecret.Remove(ApiUri);
        _pluginConfiguration.Save();
        await FilesDeleteAll().ConfigureAwait(false);
        await _mareHub!.SendAsync(nameof(UserDelete)).ConfigureAwait(false);
        await CreateConnections().ConfigureAwait(false);
    }

    public async Task UserPushData(CharacterCacheDto characterCache, List<string> visibleCharacterIds)
    {
        try
        {
            await _mareHub!.InvokeAsync(nameof(UserPushData), characterCache, visibleCharacterIds).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn("Failed to Push character data: " + ex.Message);
        }
    }

    public async Task<List<ClientPairDto>> UserGetPairedClients()
    {
        return await _mareHub!.InvokeAsync<List<ClientPairDto>>(nameof(UserGetPairedClients)).ConfigureAwait(false);
    }

    public async Task<List<string>> UserGetOnlineCharacters()
    {
        return await _mareHub!.InvokeAsync<List<string>>(nameof(UserGetOnlineCharacters)).ConfigureAwait(false);
    }

    public async Task UserAddPair(string uid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(UserAddPair), uid.Trim()).ConfigureAwait(false);
    }

    public async Task UserChangePairPauseStatus(string uid, bool paused)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(UserChangePairPauseStatus), uid, paused).ConfigureAwait(false);
    }

    public async Task UserRemovePair(string uid)
    {
        if (!IsConnected || string.Equals(SecretKey, "-", System.StringComparison.Ordinal)) return;
        await _mareHub!.SendAsync(nameof(UserRemovePair), uid).ConfigureAwait(false);
    }
}


