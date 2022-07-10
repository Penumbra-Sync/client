using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MareSynchronos.API;
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
            await _fileHub!.SendAsync(FilesHubAPI.SendDeleteAllFiles);
            await _userHub!.SendAsync(UserHubAPI.SendDeleteAccount);
            await CreateConnections();
        }

        public async Task Register(bool isIntroUi = false)
        {
            if (!ServerAlive) return;
            Logger.Debug("Registering at service " + ApiUri);
            var response = await _userHub!.InvokeAsync<string>(UserHubAPI.InvokeRegister);
            _pluginConfiguration.ClientSecret[ApiUri] = response;
            _pluginConfiguration.Save();
            if (!isIntroUi)
            {
                RegisterFinalized?.Invoke();
            }

            await CreateConnections();
        }

        public async Task<List<string>> GetOnlineCharacters()
        {
            return await _userHub!.InvokeAsync<List<string>>(UserHubAPI.InvokeGetOnlineCharacters);
        }

        public async Task SendPairedClientAddition(string uid)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync(UserHubAPI.SendPairedClientAddition, uid);
        }

        public async Task SendPairedClientPauseChange(string uid, bool paused)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync(UserHubAPI.SendPairedClientPauseChange, uid, paused);
        }

        public async Task SendPairedClientRemoval(string uid)
        {
            if (!IsConnected || SecretKey == "-") return;
            await _userHub!.SendAsync(UserHubAPI.SendPairedClientRemoval, uid);
        }
    }

}
