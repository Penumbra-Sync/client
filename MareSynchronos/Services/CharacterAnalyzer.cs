using Lumina.Data.Files;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.FileCache;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class CharacterAnalyzer : MediatorSubscriberBase, IDisposable
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly XivDataAnalyzer _xivDataAnalyzer;
    private CancellationTokenSource? _analysisCts;
    private CancellationTokenSource _baseAnalysisCts = new();
    private string _lastDataHash = string.Empty;

    public CharacterAnalyzer(ILogger<CharacterAnalyzer> logger, MareMediator mediator, FileCacheManager fileCacheManager, XivDataAnalyzer modelAnalyzer)
        : base(logger, mediator)
    {
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            _baseAnalysisCts = _baseAnalysisCts.CancelRecreate();
            var token = _baseAnalysisCts.Token;
            _ = BaseAnalysis(msg.CharacterData, token);
        });
        _fileCacheManager = fileCacheManager;
        _xivDataAnalyzer = modelAnalyzer;
    }

    public int CurrentFile { get; internal set; }
    public bool IsAnalysisRunning => _analysisCts != null;
    public int TotalFiles { get; internal set; }
    internal Dictionary<ObjectKind, Dictionary<string, FileDataEntry>> LastAnalysis { get; } = [];

    public void CancelAnalyze()
    {
        _analysisCts?.CancelDispose();
        _analysisCts = null;
    }

    public async Task ComputeAnalysis(bool print = true, bool recalculate = false)
    {
        Logger.LogDebug("=== Calculating Character Analysis ===");

        _analysisCts = _analysisCts?.CancelRecreate() ?? new();

        var cancelToken = _analysisCts.Token;

        var allFiles = LastAnalysis.SelectMany(v => v.Value.Select(d => d.Value)).ToList();
        if (allFiles.Exists(c => !c.IsComputed || recalculate))
        {
            var remaining = allFiles.Where(c => !c.IsComputed || recalculate).ToList();
            TotalFiles = remaining.Count;
            CurrentFile = 1;
            Logger.LogDebug("=== Computing {amount} remaining files ===", remaining.Count);

            Mediator.Publish(new HaltScanMessage(nameof(CharacterAnalyzer)));
            try
            {
                foreach (var file in remaining)
                {
                    Logger.LogDebug("Computing file {file}", file.FilePaths[0]);
                    await file.ComputeSizes(_fileCacheManager, cancelToken).ConfigureAwait(false);
                    CurrentFile++;
                }

                _fileCacheManager.WriteOutFullCsv();

            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to analyze files");
            }
            finally
            {
                Mediator.Publish(new ResumeScanMessage(nameof(CharacterAnalyzer)));
            }
        }

        Mediator.Publish(new CharacterDataAnalyzedMessage());

        _analysisCts.CancelDispose();
        _analysisCts = null;

        if (print) PrintAnalysis();
    }

    public void Dispose()
    {
        _analysisCts.CancelDispose();
    }

    private async Task BaseAnalysis(CharacterData charaData, CancellationToken token)
    {
        if (string.Equals(charaData.DataHash.Value, _lastDataHash, StringComparison.Ordinal)) return;

        LastAnalysis.Clear();

        foreach (var obj in charaData.FileReplacements)
        {
            Dictionary<string, FileDataEntry> data = new(StringComparer.OrdinalIgnoreCase);
            foreach (var fileEntry in obj.Value)
            {
                token.ThrowIfCancellationRequested();

                var fileCacheEntries = _fileCacheManager.GetAllFileCachesByHash(fileEntry.Hash, ignoreCacheEntries: true, validate: false).ToList();
                if (fileCacheEntries.Count == 0) continue;

                var filePath = fileCacheEntries[0].ResolvedFilepath;
                FileInfo fi = new(filePath);
                string ext = "unk?";
                try
                {
                    ext = fi.Extension[1..];
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not identify extension for {path}", filePath);
                }

                var tris = await _xivDataAnalyzer.GetTrianglesByHash(fileEntry.Hash).ConfigureAwait(false);

                foreach (var entry in fileCacheEntries)
                {
                    data[fileEntry.Hash] = new FileDataEntry(fileEntry.Hash, ext,
                        [.. fileEntry.GamePaths],
                        fileCacheEntries.Select(c => c.ResolvedFilepath).Distinct().ToList(),
                        entry.Size > 0 ? entry.Size.Value : 0,
                        entry.CompressedSize > 0 ? entry.CompressedSize.Value : 0,
                        tris);
                }
            }

            LastAnalysis[obj.Key] = data;
        }

        Mediator.Publish(new CharacterDataAnalyzedMessage());

        _lastDataHash = charaData.DataHash.Value;
    }

    private void PrintAnalysis()
    {
        if (LastAnalysis.Count == 0) return;
        foreach (var kvp in LastAnalysis)
        {
            int fileCounter = 1;
            int totalFiles = kvp.Value.Count;
            Logger.LogInformation("=== Analysis for {obj} ===", kvp.Key);

            foreach (var entry in kvp.Value.OrderBy(b => b.Value.GamePaths.OrderBy(p => p, StringComparer.Ordinal).First(), StringComparer.Ordinal))
            {
                Logger.LogInformation("File {x}/{y}: {hash}", fileCounter++, totalFiles, entry.Key);
                foreach (var path in entry.Value.GamePaths)
                {
                    Logger.LogInformation("  Game Path: {path}", path);
                }
                if (entry.Value.FilePaths.Count > 1) Logger.LogInformation("  Multiple fitting files detected for {key}", entry.Key);
                foreach (var filePath in entry.Value.FilePaths)
                {
                    Logger.LogInformation("  File Path: {path}", filePath);
                }
                Logger.LogInformation("  Size: {size}, Compressed: {compressed}", UiSharedService.ByteToString(entry.Value.OriginalSize),
                    UiSharedService.ByteToString(entry.Value.CompressedSize));
            }
        }
        foreach (var kvp in LastAnalysis)
        {
            Logger.LogInformation("=== Detailed summary by file type for {obj} ===", kvp.Key);
            foreach (var entry in kvp.Value.Select(v => v.Value).GroupBy(v => v.FileType, StringComparer.Ordinal))
            {
                Logger.LogInformation("{ext} files: {count}, size extracted: {size}, size compressed: {sizeComp}", entry.Key, entry.Count(),
                    UiSharedService.ByteToString(entry.Sum(v => v.OriginalSize)), UiSharedService.ByteToString(entry.Sum(v => v.CompressedSize)));
            }
            Logger.LogInformation("=== Total summary for {obj} ===", kvp.Key);
            Logger.LogInformation("Total files: {count}, size extracted: {size}, size compressed: {sizeComp}", kvp.Value.Count,
            UiSharedService.ByteToString(kvp.Value.Sum(v => v.Value.OriginalSize)), UiSharedService.ByteToString(kvp.Value.Sum(v => v.Value.CompressedSize)));
        }

        Logger.LogInformation("=== Total summary for all currently present objects ===");
        Logger.LogInformation("Total files: {count}, size extracted: {size}, size compressed: {sizeComp}",
            LastAnalysis.Values.Sum(v => v.Values.Count),
            UiSharedService.ByteToString(LastAnalysis.Values.Sum(c => c.Values.Sum(v => v.OriginalSize))),
            UiSharedService.ByteToString(LastAnalysis.Values.Sum(c => c.Values.Sum(v => v.CompressedSize))));
        Logger.LogInformation("IMPORTANT NOTES:\n\r- For Mare up- and downloads only the compressed size is relevant.\n\r- An unusually high total files count beyond 200 and up will also increase your download time to others significantly.");
    }

    internal sealed record FileDataEntry(string Hash, string FileType, List<string> GamePaths, List<string> FilePaths, long OriginalSize, long CompressedSize, long Triangles)
    {
        public bool IsComputed => OriginalSize > 0 && CompressedSize > 0;
        public async Task ComputeSizes(FileCacheManager fileCacheManager, CancellationToken token)
        {
            var compressedsize = await fileCacheManager.GetCompressedFileData(Hash, token).ConfigureAwait(false);
            var normalSize = new FileInfo(FilePaths[0]).Length;
            var entries = fileCacheManager.GetAllFileCachesByHash(Hash, ignoreCacheEntries: true, validate: false);
            foreach (var entry in entries)
            {
                entry.Size = normalSize;
                entry.CompressedSize = compressedsize.Item2.LongLength;
            }
            OriginalSize = normalSize;
            CompressedSize = compressedsize.Item2.LongLength;
        }
        public long OriginalSize { get; private set; } = OriginalSize;
        public long CompressedSize { get; private set; } = CompressedSize;
        public long Triangles { get; private set; } = Triangles;

        public Lazy<string> Format = new(() =>
        {
            switch (FileType)
            {
                case "tex":
                    {
                        try
                        {
                            using var stream = new FileStream(FilePaths[0], FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var reader = new BinaryReader(stream);
                            reader.BaseStream.Position = 4;
                            var format = (TexFile.TextureFormat)reader.ReadInt32();
                            return format.ToString();
                        }
                        catch
                        {
                            return "Unknown";
                        }
                    }
                default:
                    return string.Empty;
            }
        });
    }
}