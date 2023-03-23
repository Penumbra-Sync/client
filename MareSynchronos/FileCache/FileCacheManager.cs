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
    private const string _cachePrefix = "{cache}";
    private const string _penumbraPrefix = "{penumbra}";
    private readonly MareConfigService _configService;
    private readonly string _csvPath;
    private readonly ConcurrentDictionary<string, FileCacheEntity> _fileCaches = new(StringComparer.Ordinal);
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
                if (File.Exists(CsvBakPath))
                    File.Delete(CsvBakPath);
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
                    var path = splittedEntry[1];
                    var time = splittedEntry[2];
                    _fileCaches[path] = ReplacePathPrefixes(new FileCacheEntity(hash, path, time));
                }
                catch (Exception)
                {
                    _logger.LogWarning("Failed to initialize entry {entry}, ignoring", entry);
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
        string prefixedPath = fullName.Replace(_configService.Current.CacheFolder.ToLowerInvariant(), _cachePrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    public FileCacheEntity? CreateFileEntry(string path)
    {
        _logger.LogTrace("Creating file entry for {path}", path);
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_ipcManager.PenumbraModDirectory!.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_ipcManager.PenumbraModDirectory!.ToLowerInvariant(), _penumbraPrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    public void Dispose()
    {
        _logger.LogTrace("Disposing {type}", GetType());
        WriteOutFullCsv();
        GC.SuppressFinalize(this);
    }

    public List<FileCacheEntity> GetAllFileCaches() => _fileCaches.Values.ToList();

    public string GetCacheFilePath(string hash, bool isTemporaryFile)
    {
        return Path.Combine(_configService.Current.CacheFolder, hash + (isTemporaryFile ? ".tmp" : string.Empty));
    }

    public FileCacheEntity? GetFileCacheByHash(string hash)
    {
        if (_fileCaches.Any(f => string.Equals(f.Value.Hash, hash, StringComparison.Ordinal)))
        {
            return GetValidatedFileCache(_fileCaches.Where(f => string.Equals(f.Value.Hash, hash, StringComparison.Ordinal))
                .OrderByDescending(f => f.Value.PrefixedFilePath.Length)
                .FirstOrDefault(f => string.Equals(f.Value.Hash, hash, StringComparison.Ordinal)).Value);
        }

        return null;
    }

    public FileCacheEntity? GetFileCacheByPath(string path)
    {
        var cleanedPath = path.Replace("/", "\\", StringComparison.OrdinalIgnoreCase).ToLowerInvariant().Replace(_ipcManager.PenumbraModDirectory!.ToLowerInvariant(), "", StringComparison.OrdinalIgnoreCase);
        var entry = _fileCaches.Values.FirstOrDefault(f => f.ResolvedFilepath.EndsWith(cleanedPath, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            _logger.LogDebug("Found no entries for {path}", cleanedPath);
            return CreateFileEntry(path);
        }

        var validatedCacheEntry = GetValidatedFileCache(entry);

        return validatedCacheEntry;
    }

    public void RemoveHash(FileCacheEntity? entity)
    {
        if (entity != null)
        {
            _logger.LogTrace("Removing {path}", entity.ResolvedFilepath);
            _fileCaches.Remove(entity.PrefixedFilePath, out _);
        }
    }

    public void UpdateHash(FileCacheEntity fileCache)
    {
        _logger.LogTrace("Updating hash for {path}", fileCache.ResolvedFilepath);
        fileCache.Hash = Crypto.GetFileHash(fileCache.ResolvedFilepath);
        fileCache.LastModifiedDateTicks = new FileInfo(fileCache.ResolvedFilepath).LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        _fileCaches.Remove(fileCache.PrefixedFilePath, out _);
        _fileCaches[fileCache.PrefixedFilePath] = fileCache;
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
        foreach (var entry in _fileCaches.OrderBy(f => f.Value.PrefixedFilePath, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(entry.Value.CsvEntry);
        }
        if (File.Exists(_csvPath))
        {
            File.Copy(_csvPath, CsvBakPath, overwrite: true);
        }
        lock (_fileWriteLock)
        {
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

    private FileCacheEntity? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath, string? hash = null)
    {
        hash ??= Crypto.GetFileHash(fileInfo.FullName);
        var entity = new FileCacheEntity(hash, prefixedPath, fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        entity = ReplacePathPrefixes(entity);
        _fileCaches[prefixedPath] = entity;
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
        if (fileCache.PrefixedFilePath.StartsWith(_penumbraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(_penumbraPrefix, _ipcManager.PenumbraModDirectory, StringComparison.Ordinal));
        }
        else if (fileCache.PrefixedFilePath.StartsWith(_cachePrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(_cachePrefix, _configService.Current.CacheFolder, StringComparison.Ordinal));
        }

        return fileCache;
    }

    private FileCacheEntity? Validate(FileCacheEntity fileCache)
    {
        var file = new FileInfo(fileCache.ResolvedFilepath);
        if (!file.Exists)
        {
            _fileCaches.Remove(fileCache.PrefixedFilePath, out _);
            return null;
        }

        if (!string.Equals(file.LastWriteTimeUtc.Ticks.ToString(), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            UpdateHash(fileCache);
        }

        return fileCache;
    }
}