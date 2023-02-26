using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task PushCharacterData(CharacterData data, List<UserData> visibleCharacters)
    {
        if (!IsConnected) return;

        try
        {
            await _fileTransferManager.UploadFiles(data).ConfigureAwait(false);

            await PushCharacterDataInternal(data, visibleCharacters.ToList()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Upload operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during upload of files");
        }
    }

    private async Task PushCharacterDataInternal(CharacterData character, List<UserData> visibleCharacters)
    {
        _logger.LogInformation("Pushing character data for " + character.DataHash.Value + " to " + string.Join(", ", visibleCharacters.Select(c => c.AliasOrUID)));
        StringBuilder sb = new();
        foreach (var kvp in character.FileReplacements.ToList())
        {
            sb.AppendLine($"FileReplacements for {kvp.Key}: {kvp.Value.Count}");
            character.FileReplacements[kvp.Key].RemoveAll(i => ForbiddenTransfers.Any(f => string.Equals(f.Hash, i.Hash, StringComparison.OrdinalIgnoreCase)));
        }
        foreach (var item in character.GlamourerData)
        {
            sb.AppendLine($"GlamourerData for {item.Key}: {!string.IsNullOrEmpty(item.Value)}");
        }
        _logger.LogDebug("Chara data contained: " + Environment.NewLine + sb.ToString());
        await UserPushData(new(visibleCharacters, character)).ConfigureAwait(false);
    }

    public async Task UserDelete()
    {
        CheckConnection();
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