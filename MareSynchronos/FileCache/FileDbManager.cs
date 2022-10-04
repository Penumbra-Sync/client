using MareSynchronos.Managers;
using MareSynchronos.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MareSynchronos.FileCache;


public enum FileState
{
    Valid,
    RequireUpdate,
    RequireDeletion
}

public class FileCacheManager : IDisposable
{
    private const string PenumbraPrefix = "{penumbra}";
    private const string CachePrefix = "{cache}";
    private readonly IpcManager _ipcManager;
    private readonly Configuration _configuration;
    private readonly string CsvPath;
    private string CsvBakPath => CsvPath + ".bak";
    private readonly ConcurrentDictionary<string, FileCacheEntity> FileCaches = new(StringComparer.Ordinal);
    public const string CsvSplit = "|";
    private object _fileWriteLock = new();

    public FileCacheManager(IpcManager ipcManager, Configuration configuration, string configDirectoryName)
    {
        _ipcManager = ipcManager;
        _configuration = configuration;
        CsvPath = Path.Combine(configDirectoryName, "FileCache.csv");

        if (File.Exists(CsvBakPath))
        {
            File.Move(CsvBakPath, CsvPath, true);
        }

        if (File.Exists(CsvPath))
        {
            var entries = File.ReadAllLines(CsvPath);
            foreach (var entry in entries)
            {
                var splittedEntry = entry.Split(CsvSplit, StringSplitOptions.None);
                try
                {
                    var hash = splittedEntry[0];
                    var path = splittedEntry[1];
                    var time = splittedEntry[2];
                    FileCaches[path] = new FileCacheEntity(hash, path, time);
                }
                catch (Exception)
                {
                    Logger.Warn($"Failed to initialize entry {entry}, ignoring");
                }
            }
        }
    }

    public void WriteOutFullCsv()
    {
        StringBuilder sb = new();
        foreach (var entry in FileCaches.OrderBy(f => f.Value.PrefixedFilePath))
        {
            sb.AppendLine(entry.Value.CsvEntry);
        }
        if (File.Exists(CsvPath))
        {
            File.Copy(CsvPath, CsvBakPath, true);
        }
        lock (_fileWriteLock)
        {
            try
            {
                File.WriteAllText(CsvPath, sb.ToString());
                File.Delete(CsvBakPath);
            }
            catch
            {
                File.WriteAllText(CsvBakPath, sb.ToString());
            }
        }
    }

    public List<FileCacheEntity> GetAllFileCaches() => FileCaches.Values.ToList();

    public FileCacheEntity? GetFileCacheByHash(string hash)
    {
        if (FileCaches.Any(f => string.Equals(f.Value.Hash, hash, StringComparison.Ordinal)))
        {
            return GetValidatedFileCache(FileCaches.FirstOrDefault(f => string.Equals(f.Value.Hash, hash, StringComparison.Ordinal)).Value);
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
        var cleanedPath = path.Replace("/", "\\", StringComparison.Ordinal).ToLowerInvariant().Replace(_ipcManager.PenumbraModDirectory()!.ToLowerInvariant(), "", StringComparison.Ordinal);
        var entry = FileCaches.FirstOrDefault(f => f.Value.ResolvedFilepath.EndsWith(cleanedPath, StringComparison.Ordinal)).Value;

        if (entry == null)
        {
            Logger.Debug("Found no entries for " + cleanedPath);
            return CreateFileEntry(path);
        }

        var validatedCacheEntry = GetValidatedFileCache(entry);

        return validatedCacheEntry;
    }

    public FileCacheEntity? CreateCacheEntry(string path)
    {
        Logger.Verbose("Creating cache entry for " + path);
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_configuration.CacheFolder.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_configuration.CacheFolder.ToLowerInvariant(), CachePrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath, fi.Name.ToUpper(CultureInfo.InvariantCulture));
    }

    public FileCacheEntity? CreateFileEntry(string path)
    {
        Logger.Verbose("Creating file entry for " + path);
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_ipcManager.PenumbraModDirectory()!.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_ipcManager.PenumbraModDirectory()!.ToLowerInvariant(), PenumbraPrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    private FileCacheEntity? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath, string? hash = null)
    {
        if (hash == null)
        {
            hash = Crypto.GetFileHash(fileInfo.FullName);
        }
        var entity = new FileCacheEntity(hash, prefixedPath, fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        entity = ReplacePathPrefixes(entity);
        FileCaches[prefixedPath] = entity;
        lock (_fileWriteLock)
        {
            File.AppendAllLines(CsvPath, new[] { entity.CsvEntry });
        }
        var result = GetFileCacheByPath(fileInfo.FullName);
        Logger.Debug("Creating file cache for " + fileInfo.FullName + " success: " + (result != null));
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
            FileCaches.Remove(fileCache.PrefixedFilePath, out _);
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
        FileCaches.Remove(entity.Hash, out _);
    }

    public void UpdateHash(FileCacheEntity fileCache)
    {
        Logger.Debug("Updating hash for " + fileCache.ResolvedFilepath);
        fileCache.Hash = Crypto.GetFileHash(fileCache.ResolvedFilepath);
        fileCache.LastModifiedDateTicks = new FileInfo(fileCache.ResolvedFilepath).LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        FileCaches.Remove(fileCache.PrefixedFilePath, out _);
        FileCaches[fileCache.PrefixedFilePath] = fileCache;
    }

    private FileCacheEntity ReplacePathPrefixes(FileCacheEntity fileCache)
    {
        if (fileCache.PrefixedFilePath.StartsWith(PenumbraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(PenumbraPrefix, _ipcManager.PenumbraModDirectory(), StringComparison.Ordinal));
        }
        else if (fileCache.PrefixedFilePath.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(CachePrefix, _configuration.CacheFolder, StringComparison.Ordinal));
        }

        return fileCache;
    }

    public void Dispose()
    {
        WriteOutFullCsv();
    }
}
