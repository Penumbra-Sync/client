using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MareSynchronos.Utils;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI
{
    public partial class ApiController
    {
        public async Task DeleteAccount()
        {
            _pluginConfiguration.ClientSecret.Remove(ApiUri);
            _pluginConfiguration.Save();
            await _fileHub!.SendAsync("DeleteAllFiles");
            await _userHub!.SendAsync("DeleteAccount");
            await CreateConnections();
        }

        public async Task Register()
        {
            if (!ServerAlive) return;
            Logger.Debug("Registering at service " + ApiUri);
            var response = await _userHub!.InvokeAsync<string>("Register");
            _pluginConfiguration.ClientSecret[ApiUri] = response;
            _pluginConfiguration.Save();
            RegisterFinalized?.Invoke();
            await CreateConnections();
        }

        public async Task<List<string>> GetOnlineCharacters()
        {
            return await _userHub!.InvokeAsync<List<string>>("GetOnlineCharacters");
        }

        public async Task SendPairedClientAddition(string uid)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendPairedClientAddition", uid);
        }

        public async Task SendPairedClientPauseChange(string uid, bool paused)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendPairedClientPauseChange", uid, paused);
        }

        public async Task SendPairedClientRemoval(string uid)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync("SendPairedClientRemoval", uid);
        }
    }

}
