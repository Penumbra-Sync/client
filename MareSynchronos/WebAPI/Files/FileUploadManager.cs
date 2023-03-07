using LZ4;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MareSynchronos.WebAPI.Files;

public class FileUploadManager : MediatorSubscriberBase
{
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly FileCacheManager _fileDbManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _uploadCancellationTokenSource = new();

    public FileUploadManager(ILogger<FileUploadManager> logger, MareMediator mediator,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileDbManager,
        ServerConfigurationManager serverManager) : base(logger, mediator)
    {
        _orchestrator = orchestrator;
        _fileDbManager = fileDbManager;
        _serverManager = serverManager;

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            Reset();
        });
    }

    public List<FileTransfer> CurrentUploads { get; } = new();
    public bool IsUploading => CurrentUploads.Count > 0;

    public bool CancelUpload()
    {
        if (CurrentUploads.Any())
        {
            _logger.LogDebug("Cancelling current upload");
            _uploadCancellationTokenSource?.Cancel();
            _uploadCancellationTokenSource?.Dispose();
            _uploadCancellationTokenSource = null;
            CurrentUploads.Clear();
            return true;
        }

        return false;
    }

    public async Task DeleteAllFiles()
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");

        await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.ServerFilesDeleteAllFullPath(_orchestrator.FilesCdnUri!)).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        Reset();
        base.Dispose(disposing);
    }

    public async Task<CharacterData> UploadFiles(CharacterData data)
    {
        CancelUpload();

        _uploadCancellationTokenSource = new CancellationTokenSource();
        var uploadToken = _uploadCancellationTokenSource.Token;
        _logger.LogDebug("Sending Character data {hash} to service {url}", data.DataHash.Value, _serverManager.CurrentApiUrl);

        HashSet<string> unverifiedUploads = VerifyFiles(data);
        if (unverifiedUploads.Any())
        {
            await UploadMissingFiles(unverifiedUploads, uploadToken).ConfigureAwait(false);
            _logger.LogInformation("Upload complete for {hash}", data.DataHash.Value);
        }

        foreach (var kvp in data.FileReplacements)
        {
            data.FileReplacements[kvp.Key].RemoveAll(i => _orchestrator.ForbiddenTransfers.Any(f => string.Equals(f.Hash, i.Hash, StringComparison.OrdinalIgnoreCase)));
        }

        return data;
    }

    private async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
    {
        var fileCache = _fileDbManager.GetFileCacheByHash(fileHash)!.ResolvedFilepath;
        return (fileHash, LZ4Codec.WrapHC(await File.ReadAllBytesAsync(fileCache, uploadToken).ConfigureAwait(false), 0,
            (int)new FileInfo(fileCache).Length));
    }

    private void Reset()
    {
        _uploadCancellationTokenSource?.Cancel();
        _uploadCancellationTokenSource?.Dispose();
        _uploadCancellationTokenSource = null;
        CurrentUploads.Clear();
        _verifiedUploadedHashes.Clear();
    }

    private async Task<List<UploadFileDto>> FilesSend(List<string> hashes, CancellationToken ct)
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        var response = await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.ServerFilesFilesSendFullPath(_orchestrator.FilesCdnUri!), hashes, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<UploadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? new List<UploadFileDto>();
    }

    private async Task UploadFile(byte[] compressedFile, string fileHash, CancellationToken uploadToken)
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");

        _logger.LogInformation("Uploading {file}, {size}", fileHash, UiShared.ByteToString(compressedFile.Length));

        if (uploadToken.IsCancellationRequested) return;

        using var ms = new MemoryStream(compressedFile);

        Progress<UploadProgress> prog = new((prog) =>
        {
            CurrentUploads.Single(f => string.Equals(f.Hash, fileHash, StringComparison.Ordinal)).Transferred = prog.Uploaded;
        });
        var streamContent = new ProgressableStreamContent(ms, prog);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await _orchestrator.SendRequestStreamAsync(HttpMethod.Post, MareFiles.ServerFilesUploadFullPath(_orchestrator.FilesCdnUri!, fileHash), streamContent, uploadToken).ConfigureAwait(false);
        _logger.LogDebug("Upload Status: {status}", response.StatusCode);
    }

    private async Task UploadMissingFiles(HashSet<string> unverifiedUploadHashes, CancellationToken uploadToken)
    {
        unverifiedUploadHashes = unverifiedUploadHashes.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);

        _logger.LogDebug("Verifying {count} files", unverifiedUploadHashes.Count);
        var filesToUpload = await FilesSend(unverifiedUploadHashes.ToList(), uploadToken).ConfigureAwait(false);

        foreach (var file in filesToUpload.Where(f => !f.IsForbidden))
        {
            try
            {
                CurrentUploads.Add(new UploadFileTransfer(file)
                {
                    Total = new FileInfo(_fileDbManager.GetFileCacheByHash(file.Hash)!.ResolvedFilepath).Length,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tried to request file {hash} but file was not present", file.Hash);
            }
        }

        foreach (var file in filesToUpload.Where(c => c.IsForbidden))
        {
            if (_orchestrator.ForbiddenTransfers.All(f => !string.Equals(f.Hash, file.Hash, StringComparison.Ordinal)))
            {
                _orchestrator.ForbiddenTransfers.Add(new UploadFileTransfer(file)
                {
                    LocalFile = _fileDbManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath ?? string.Empty,
                });
            }

            _verifiedUploadedHashes[file.Hash] = DateTime.UtcNow;
        }

        var totalSize = CurrentUploads.Sum(c => c.Total);
        _logger.LogDebug("Compressing and uploading files");
        Task uploadTask = Task.CompletedTask;
        foreach (var file in CurrentUploads.Where(f => f.CanBeTransferred && !f.IsTransferred).ToList())
        {
            _logger.LogDebug("Compressing {file}", file);
            var data = await GetCompressedFileData(file.Hash, uploadToken).ConfigureAwait(false);
            CurrentUploads.Single(e => string.Equals(e.Hash, data.Item1, StringComparison.Ordinal)).Total = data.Item2.Length;
            await uploadTask.ConfigureAwait(false);
            uploadTask = UploadFile(data.Item2, file.Hash, uploadToken);
            _verifiedUploadedHashes[file.Hash] = DateTime.UtcNow;
            uploadToken.ThrowIfCancellationRequested();
        }

        if (CurrentUploads.Any())
        {
            await uploadTask.ConfigureAwait(false);

            var compressedSize = CurrentUploads.Sum(c => c.Total);
            _logger.LogDebug("Upload complete, compressed {size} to {compressed}", UiShared.ByteToString(totalSize), UiShared.ByteToString(compressedSize));
        }

        foreach (var file in unverifiedUploadHashes.Where(c => !CurrentUploads.Any(u => string.Equals(u.Hash, c, StringComparison.Ordinal))))
        {
            _verifiedUploadedHashes[file] = DateTime.UtcNow;
        }

        CurrentUploads.Clear();
    }

    private HashSet<string> VerifyFiles(CharacterData data)
    {
        HashSet<string> unverifiedUploadHashes = new(StringComparer.Ordinal);
        foreach (var item in data.FileReplacements.SelectMany(c => c.Value.Where(f => string.IsNullOrEmpty(f.FileSwapPath)).Select(v => v.Hash).Distinct(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList())
        {
            if (!_verifiedUploadedHashes.TryGetValue(item, out var verifiedTime))
            {
                verifiedTime = DateTime.MinValue;
            }

            if (verifiedTime < DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)))
            {
                _logger.LogTrace("Verifying {item}, last verified: {date}", item, verifiedTime);
                unverifiedUploadHashes.Add(item);
            }
        }

        return unverifiedUploadHashes;
    }
}