using LZ4;
using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace MareSynchronos.FileCache;

public sealed class FileCacheManager : IDisposable
{
    public const string CsvSplit = "|";
    public const string CachePrefix = "{cache}";
    public const string PenumbraPrefix = "{penumbra}";
    private readonly MareConfigService _configService;
    private readonly string _csvPath;
    private readonly ConcurrentDictionary<string, List<FileCacheEntity>> _fileCaches = new(StringComparer.Ordinal);
    private readonly object _fileWriteLock = new();
    private readonly IpcManager _ipcManager;
    private readonly ILogger<FileCacheManager> _logger;

    public FileCacheManager(ILogger<FileCacheManager> logger, IpcManager ipcManager, MareConfigService configService)
    {
        _logger = logger;
        _ipcManager = ipcManager;
        _configService = configService;
        _csvPath = Path.Combine(configService.ConfigurationDirectory, "FileCache.csv");

        lock (_fileWriteLock)
        {
            try
            {
                if (File.Exists(CsvBakPath))
                {
                    File.Move(CsvBakPath, _csvPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to move BAK to ORG, deleting BAK");
                try
                {
                    if (File.Exists(CsvBakPath))
                        File.Delete(CsvBakPath);
                }
                catch (Exception ex1)
                {
                    _logger.LogWarning(ex1, "Could not delete bak file");
                }
            }
        }

        if (File.Exists(_csvPath))
        {
            var entries = File.ReadAllLines(_csvPath);
            foreach (var entry in entries)
            {
                var splittedEntry = entry.Split(CsvSplit, StringSplitOptions.None);
                try
                {
                    var hash = splittedEntry[0];
                    if (hash.Length != 40) throw new InvalidOperationException("Expected Hash length of 40, received " + hash.Length);
                    var path = splittedEntry[1];
                    var time = splittedEntry[2];
                    long size = -1;
                    long compressed = -1;
                    if (splittedEntry.Length > 3)
                    {
                        if (long.TryParse(splittedEntry[3], CultureInfo.InvariantCulture, out long result))
                        {
                            size = result;
                        }
                        if (long.TryParse(splittedEntry[4], CultureInfo.InvariantCulture, out long resultCompressed))
                        {
                            compressed = resultCompressed;
                        }
                    }
                    AddHashedFile(ReplacePathPrefixes(new FileCacheEntity(hash, path, time, size, compressed)));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize entry {entry}, ignoring", entry);
                }
            }
        }
    }

    private string CsvBakPath => _csvPath + ".bak";

    public FileCacheEntity? CreateCacheEntry(string path)
    {
        _logger.LogTrace("Creating cache entry for {path}", path);
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_configService.Current.CacheFolder.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_configService.Current.CacheFolder.ToLowerInvariant(), CachePrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    public FileCacheEntity? CreateFileEntry(string path)
    {
        _logger.LogTrace("Creating file entry for {path}", path);
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_ipcManager.PenumbraModDirectory!.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_ipcManager.PenumbraModDirectory!.ToLowerInvariant(), PenumbraPrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    public void Dispose()
    {
        _logger.LogTrace("Disposing {type}", GetType());
        WriteOutFullCsv();
        GC.SuppressFinalize(this);
    }

    public List<FileCacheEntity> GetAllFileCaches() => _fileCaches.Values.SelectMany(v => v).ToList();

    public string GetCacheFilePath(string hash, string extension)
    {
        return Path.Combine(_configService.Current.CacheFolder, hash + "." + extension);
    }

    public FileCacheEntity? GetFileCacheByHash(string hash)
    {
        if (_fileCaches.TryGetValue(hash, out var hashes))
        {
            var item = hashes.FirstOrDefault();
            if (item != null) return GetValidatedFileCache(item);
        }
        return null;
    }

    public List<FileCacheEntity> GetAllFileCachesByHash(string hash)
    {
        List<FileCacheEntity> output = new();
        if (_fileCaches.TryGetValue(hash, out var fileCacheEntities))
        {
            foreach (var filecache in fileCacheEntities)
            {
                var validated = GetValidatedFileCache(filecache);
                if (validated != null) output.Add(validated);
            }
        }

        return output;
    }

    public FileCacheEntity? GetFileCacheByPath(string path)
    {
        var cleanedPath = path.Replace("/", "\\", StringComparison.OrdinalIgnoreCase).ToLowerInvariant().Replace(_ipcManager.PenumbraModDirectory!.ToLowerInvariant(), "", StringComparison.OrdinalIgnoreCase);
        var entry = _fileCaches.SelectMany(v => v.Value).FirstOrDefault(f => f.ResolvedFilepath.EndsWith(cleanedPath, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            _logger.LogDebug("Found no entries for {path}", cleanedPath);
            return CreateFileEntry(path);
        }

        var validatedCacheEntry = GetValidatedFileCache(entry);

        return validatedCacheEntry;
    }

    public async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
    {
        var fileCache = GetFileCacheByHash(fileHash)!.ResolvedFilepath;
        return (fileHash, LZ4Codec.WrapHC(await File.ReadAllBytesAsync(fileCache, uploadToken).ConfigureAwait(false), 0,
            (int)new FileInfo(fileCache).Length));
    }

    public void RemoveHashedFile(FileCacheEntity? fileCache)
    {
        if (fileCache == null) return;
        if (_fileCaches.TryGetValue(fileCache.Hash, out var caches))
        {
            caches?.RemoveAll(c => string.Equals(c.PrefixedFilePath, fileCache.PrefixedFilePath, StringComparison.Ordinal));
            if (_fileCaches.Count == 0)
            {
                _fileCaches.Remove(fileCache.Hash, out _);
            }
        }
    }

    public void UpdateHashedFile(FileCacheEntity fileCache, bool computeProperties = true)
    {
        _logger.LogTrace("Updating hash for {path}", fileCache.ResolvedFilepath);
        if (computeProperties)
        {
            var fi = new FileInfo(fileCache.ResolvedFilepath);
            fileCache.Size = fi.Length;
            fileCache.CompressedSize = null;
            fileCache.Hash = Crypto.GetFileHash(fileCache.ResolvedFilepath);
            fileCache.LastModifiedDateTicks = fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        RemoveHashedFile(fileCache);
        AddHashedFile(fileCache);
    }

    public (FileState, FileCacheEntity) ValidateFileCacheEntity(FileCacheEntity fileCache)
    {
        fileCache = ReplacePathPrefixes(fileCache);
        FileInfo fi = new(fileCache.ResolvedFilepath);
        if (!fi.Exists)
        {
            return (FileState.RequireDeletion, fileCache);
        }
        if (!string.Equals(fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            return (FileState.RequireUpdate, fileCache);
        }

        return (FileState.Valid, fileCache);
    }

    public void WriteOutFullCsv()
    {
        StringBuilder sb = new();
        foreach (var entry in _fileCaches.SelectMany(k => k.Value).OrderBy(f => f.PrefixedFilePath, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(entry.CsvEntry);
        }
        lock (_fileWriteLock)
        {
            if (File.Exists(_csvPath))
            {
                File.Copy(_csvPath, CsvBakPath, overwrite: true);
            }

            try
            {
                File.WriteAllText(_csvPath, sb.ToString());
                File.Delete(CsvBakPath);
            }
            catch
            {
                File.WriteAllText(CsvBakPath, sb.ToString());
            }
        }
    }

    internal FileCacheEntity MigrateFileHashToExtension(FileCacheEntity fileCache, string ext)
    {
        try
        {
            RemoveHashedFile(fileCache);
            FileInfo oldCache = new(fileCache.ResolvedFilepath);
            var extensionPath = fileCache.ResolvedFilepath.ToUpper() + "." + ext;
            File.Move(fileCache.ResolvedFilepath, extensionPath, true);
            var newHashedEntity = new FileCacheEntity(fileCache.Hash, fileCache.PrefixedFilePath + "." + ext, DateTime.UtcNow.Ticks.ToString());
            newHashedEntity.SetResolvedFilePath(extensionPath);
            FileInfo newCache = new FileInfo(extensionPath);
            newCache.LastAccessTime = oldCache.LastAccessTime;
            newCache.LastWriteTime = oldCache.LastWriteTime;
            AddHashedFile(newHashedEntity);
            _logger.LogDebug("Migrated from {oldPath} to {newPath}", fileCache.ResolvedFilepath, newHashedEntity.ResolvedFilepath);
            return newHashedEntity;
        }
        catch (Exception ex)
        {
            AddHashedFile(fileCache);
            _logger.LogWarning(ex, "Failed to migrate entity {entity}", fileCache.PrefixedFilePath);
            return fileCache;
        }
    }

    private void AddHashedFile(FileCacheEntity fileCache)
    {
        if (!_fileCaches.TryGetValue(fileCache.Hash, out var entries))
        {
            _fileCaches[fileCache.Hash] = entries = new();
        }

        entries.Add(fileCache);
    }

    private FileCacheEntity? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath, string? hash = null)
    {
        hash ??= Crypto.GetFileHash(fileInfo.FullName);
        var entity = new FileCacheEntity(hash, prefixedPath, fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileInfo.Length);
        entity = ReplacePathPrefixes(entity);
        AddHashedFile(entity);
        lock (_fileWriteLock)
        {
            File.AppendAllLines(_csvPath, new[] { entity.CsvEntry });
        }
        var result = GetFileCacheByPath(fileInfo.FullName);
        _logger.LogDebug("Creating file cache for {name} success: {success}", fileInfo.FullName, (result != null));
        return result;
    }

    private FileCacheEntity? GetValidatedFileCache(FileCacheEntity fileCache)
    {
        var resulingFileCache = ReplacePathPrefixes(fileCache);
        resulingFileCache = Validate(resulingFileCache);
        return resulingFileCache;
    }

    private FileCacheEntity ReplacePathPrefixes(FileCacheEntity fileCache)
    {
        if (fileCache.PrefixedFilePath.StartsWith(PenumbraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(PenumbraPrefix, _ipcManager.PenumbraModDirectory, StringComparison.Ordinal));
        }
        else if (fileCache.PrefixedFilePath.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(CachePrefix, _configService.Current.CacheFolder, StringComparison.Ordinal));
        }

        return fileCache;
    }

    private FileCacheEntity? Validate(FileCacheEntity fileCache)
    {
        var file = new FileInfo(fileCache.ResolvedFilepath);
        if (!file.Exists)
        {
            RemoveHashedFile(fileCache);
            return null;
        }

        if (!string.Equals(file.LastWriteTimeUtc.Ticks.ToString(), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            UpdateHashedFile(fileCache);
        }

        return fileCache;
    }
}