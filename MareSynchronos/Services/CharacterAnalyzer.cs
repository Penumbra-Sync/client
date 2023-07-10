using MareSynchronos.API.Data;
using MareSynchronos.FileCache;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class CharacterAnalyzer : MediatorSubscriberBase, IDisposable
{
    private readonly FileCacheManager _fileCacheManager;
    private CharacterData? _lastCreatedData;
    private CancellationTokenSource? _analysisCts;

    public CharacterAnalyzer(ILogger<CharacterAnalyzer> logger, MareMediator mediator, FileCacheManager fileCacheManager) : base(logger, mediator)
    {
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            _lastCreatedData = msg.CharacterData.DeepClone();
        });
        _fileCacheManager = fileCacheManager;
    }

    public bool IsAnalysisRunning => _analysisCts != null;

    public void CancelAnalyze()
    {
        _analysisCts?.CancelDispose();
        _analysisCts = null;
    }

    public async Task Analyze()
    {
        _analysisCts = _analysisCts?.CancelRecreate() ?? new();

        var cancelToken = _analysisCts.Token;

        if (_lastCreatedData == null) return;

        Logger.LogInformation("=== Calculating Character Analysis, this may take a while ===");
        foreach (var obj in _lastCreatedData.FileReplacements)
        {
            Logger.LogInformation("=== File Calculation for {obj} ===", obj.Key);
            Dictionary<string, List<DataEntry>> data = new(StringComparer.OrdinalIgnoreCase);
            var totalFiles = obj.Value.Count(c => !string.IsNullOrEmpty(c.Hash));
            var currentFile = 1;
            foreach (var hash in obj.Value.Select(c => c.Hash))
            {
                var fileCacheEntry = _fileCacheManager.GetFileCacheByHash(hash);
                if (fileCacheEntry == null) continue;

                Logger.LogInformation("Computing File {x}/{y}: {hash}", currentFile, totalFiles, hash);

                Logger.LogInformation("  File Path: {path}", fileCacheEntry.ResolvedFilepath);

                var filePath = fileCacheEntry.ResolvedFilepath;
                FileInfo fi = new(filePath);
                var ext = fi.Extension;
                if (!data.ContainsKey(ext)) data[ext] = new List<DataEntry>();

                (_, byte[] fileLength) = await _fileCacheManager.GetCompressedFileData(hash, cancelToken).ConfigureAwait(false);

                Logger.LogInformation("  Original Size: {size}, Compressed Size: {compr}",
                    UiSharedService.ByteToString(fi.Length), UiSharedService.ByteToString(fileLength.LongLength));

                data[ext].Add(new DataEntry(fi.FullName, fi.Length, fileLength.LongLength));

                currentFile++;

                cancelToken.ThrowIfCancellationRequested();
            }

            Logger.LogInformation("=== Summary by file type for {obj} ===", obj.Key);
            foreach (var entry in data)
            {
                Logger.LogInformation("{ext} files: {count}, size extracted: {size}, size compressed: {sizeComp}", entry.Key, entry.Value.Count,
                    UiSharedService.ByteToString(entry.Value.Sum(v => v.OriginalSize)), UiSharedService.ByteToString(entry.Value.Sum(v => v.CompressedSize)));
            }
            Logger.LogInformation("=== Total summary for {obj} ===", obj.Key);
            Logger.LogInformation("Total files: {count}, size extracted: {size}, size compressed: {sizeComp}", data.Values.Sum(c => c.Count),
                UiSharedService.ByteToString(data.Values.Sum(v => v.Sum(c => c.OriginalSize))), UiSharedService.ByteToString(data.Values.Sum(v => v.Sum(c => c.CompressedSize))));

            Logger.LogInformation("IMPORTANT NOTES:\n\r- For Mare up- and downloads only the compressed size is relevant.\n\r- An unusually high total files count beyond 200 and up will also increase your download time to others significantly.");
        }

        _analysisCts.CancelDispose();
        _analysisCts = null;
    }

    public void Dispose()
    {
        _analysisCts.CancelDispose();
    }

    private sealed record DataEntry(string filePath, long OriginalSize, long CompressedSize);
}
