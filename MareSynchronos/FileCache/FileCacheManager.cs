using MareSynchronos.Managers;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace MareSynchronos.FileCache;

public class FileCacheManager : IDisposable
{
    private const string _penumbraPrefix = "{penumbra}";
    private const string _cachePrefix = "{cache}";
    private readonly ILogger<FileCacheManager> _logger;
    private readonly IpcManager _ipcManager;
    private readonly MareConfigService _configService;
    private readonly string _csvPath;
    private string CsvBakPath => _csvPath + ".bak";
    private readonly ConcurrentDictionary<string, FileCacheEntity> _fileCaches = new(StringComparer.Ordinal);
    public const string CsvSplit = "|";
    private readonly object _fileWriteLock = new();

    public FileCacheManager(ILogger<FileCacheManager> logger, IpcManager ipcManager, MareConfigService configService)
    {
        _logger = logger;
        _ipcManager = ipcManager;
        _configService = configService;
        _csvPath = Path.Combine(configService.ConfigurationDirectory, "FileCache.csv");

        lock (_fileWriteLock)
        {
            if (File.Exists(CsvBakPath))
            {
                File.Move(CsvBakPath, _csvPath, overwrite: true);
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
                    _logger.LogWarning($"Failed to initialize entry {entry}, ignoring");
                }
            }
        }
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

    public List<FileCacheEntity> GetAllFileCaches() => _fileCaches.Values.ToList();

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

    public FileCacheEntity? GetFileCacheByPath(string path)
    {
        var cleanedPath = path.Replace("/", "\\", StringComparison.OrdinalIgnoreCase).ToLowerInvariant().Replace(_ipcManager.PenumbraModDirectory!.ToLowerInvariant(), "", StringComparison.OrdinalIgnoreCase);
        var entry = _fileCaches.Values.FirstOrDefault(f => f.ResolvedFilepath.EndsWith(cleanedPath, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            _logger.LogDebug("Found no entries for " + cleanedPath);
            return CreateFileEntry(path);
        }

        var validatedCacheEntry = GetValidatedFileCache(entry);

        return validatedCacheEntry;
    }

    public FileCacheEntity? CreateCacheEntry(string path)
    {
        _logger.LogTrace("Creating cache entry for " + path);
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_configService.Current.CacheFolder.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_configService.Current.CacheFolder.ToLowerInvariant(), _cachePrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath, fi.Name.ToUpper(CultureInfo.InvariantCulture));
    }

    public FileCacheEntity? CreateFileEntry(string path)
    {
        _logger.LogTrace("Creating file entry for " + path);
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_ipcManager.PenumbraModDirectory!.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_ipcManager.PenumbraModDirectory!.ToLowerInvariant(), _penumbraPrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
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
        _logger.LogDebug("Creating file cache for " + fileInfo.FullName + " success: " + (result != null));
        return result;
    }

    private FileCacheEntity? GetValidatedFileCache(FileCacheEntity fileCache)
    {
        var resulingFileCache = ReplacePathPrefixes(fileCache);
        resulingFileCache = Validate(resulingFileCache);
        return resulingFileCache;
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

    public void RemoveHash(FileCacheEntity entity)
    {
        _logger.LogTrace("Removing " + entity.ResolvedFilepath);
        _fileCaches.Remove(entity.PrefixedFilePath, out _);
    }

    public void UpdateHash(FileCacheEntity fileCache)
    {
        _logger.LogTrace("Updating hash for " + fileCache.ResolvedFilepath);
        fileCache.Hash = Crypto.GetFileHash(fileCache.ResolvedFilepath);
        fileCache.LastModifiedDateTicks = new FileInfo(fileCache.ResolvedFilepath).LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        _fileCaches.Remove(fileCache.PrefixedFilePath, out _);
        _fileCaches[fileCache.PrefixedFilePath] = fileCache;
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

    public string ResolveFileReplacement(string gamePath)
    {
        return _ipcManager.PenumbraResolvePath(gamePath);
    }

    public void Dispose()
    {
        _logger.LogTrace($"Disposing {GetType()}");
        WriteOutFullCsv();
    }
}
