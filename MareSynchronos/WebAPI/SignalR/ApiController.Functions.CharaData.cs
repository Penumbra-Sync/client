using MareSynchronos.API.Dto.CharaData;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.WebAPI;
public partial class ApiController
{
    public async Task<CharaDataFullDto?> CharaDataCreate()
    {
        if (!IsConnected) return null;

        try
        {
            Logger.LogDebug("Creating new Character Data");
            return await _mareHub!.InvokeAsync<CharaDataFullDto?>(nameof(CharaDataCreate)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to create new character data");
            return null;
        }
    }

    public async Task<CharaDataFullDto?> CharaDataUpdate(CharaDataUpdateDto updateDto)
    {
        if (!IsConnected) return null;

        try
        {
            Logger.LogDebug("Updating chara data for {id}", updateDto.Id);
            return await _mareHub!.InvokeAsync<CharaDataFullDto?>(nameof(CharaDataUpdate), updateDto).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to update chara data for {id}", updateDto.Id);
            return null;
        }
    }

    public async Task<bool> CharaDataDelete(string id)
    {
        if (!IsConnected) return false;

        try
        {
            Logger.LogDebug("Deleting chara data for {id}", id);
            return await _mareHub!.InvokeAsync<bool>(nameof(CharaDataDelete), id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to delete chara data for {id}", id);
            return false;
        }
    }

    public async Task<CharaDataMetaInfoDto?> CharaDataGetMetainfo(string id)
    {
        if (!IsConnected) return null;

        try
        {
            Logger.LogDebug("Getting metainfo for chara data {id}", id);
            return await _mareHub!.InvokeAsync<CharaDataMetaInfoDto?>(nameof(CharaDataGetMetainfo), id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get meta info for chara data {id}", id);
            return null;
        }
    }

    public async Task<List<CharaDataFullDto>> CharaDataGetOwn()
    {
        if (!IsConnected) return [];

        try
        {
            Logger.LogDebug("Getting all own chara data");
            return await _mareHub!.InvokeAsync<List<CharaDataFullDto>>(nameof(CharaDataGetOwn)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get own chara data");
            return [];
        }
    }

    public async Task<List<CharaDataMetaInfoDto>> CharaDataGetShared()
    {
        if (!IsConnected) return [];

        try
        {
            Logger.LogDebug("Getting all own chara data");
            return await _mareHub!.InvokeAsync<List<CharaDataMetaInfoDto>>(nameof(CharaDataGetShared)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get shared chara data");
            return [];
        }
    }

    public async Task<CharaDataDownloadDto?> CharaDataDownload(string id)
    {
        if (!IsConnected) return null;

        try
        {
            Logger.LogDebug("Getting download chara data for {id}", id);
            return await _mareHub!.InvokeAsync<CharaDataDownloadDto>(nameof(CharaDataDownload), id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get download chara data for {id}", id);
            return null;
        }
    }
}
