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
    private readonly ConcurrentDictionary<string, FileCache> FileCaches = new();
    public const string CsvSplit = "|";
    private object _fileWriteLock = new object();

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
                var hash = splittedEntry[0];
                var path = splittedEntry[1];
                var time = splittedEntry[2];
                FileCaches[path] = new FileCache(hash, path, time);
            }
        }
    }

    public void WriteOutFullCsv()
    {
        StringBuilder sb = new StringBuilder();
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

    public List<FileCache> GetAllFileCaches() => FileCaches.Values.ToList();

    public FileCache? GetFileCacheByHash(string hash)
    {
        if (FileCaches.Any(f => f.Value.Hash == hash))
        {
            return GetValidatedFileCache(FileCaches.FirstOrDefault(f => f.Value.Hash == hash).Value);
        }

        return null;
    }

    public (FileState, FileCache) ValidateFileCacheEntity(FileCache fileCache)
    {
        fileCache = ReplacePathPrefixes(fileCache);
        FileInfo fi = new(fileCache.ResolvedFilepath);
        if (!fi.Exists)
        {
            return (FileState.RequireDeletion, fileCache);
        }
        if (fi.LastWriteTimeUtc.Ticks.ToString() != fileCache.LastModifiedDateTicks)
        {
            return (FileState.RequireUpdate, fileCache);
        }

        return (FileState.Valid, fileCache);
    }

    public FileCache? GetFileCacheByPath(string path)
    {
        var cleanedPath = path.Replace("/", "\\").ToLowerInvariant().Replace(_ipcManager.PenumbraModDirectory()!.ToLowerInvariant(), "");
        var entry = FileCaches.FirstOrDefault(f => f.Value.ResolvedFilepath.EndsWith(cleanedPath)).Value;

        if (entry == null)
        {
            Logger.Debug("Found no entries for " + cleanedPath);
            return CreateFileEntry(path);
        }

        var validatedCacheEntry = GetValidatedFileCache(entry);

        return validatedCacheEntry;
    }

    public FileCache? CreateCacheEntry(string path)
    {
        Logger.Debug("Creating cache entry for " + path);
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_configuration.CacheFolder.ToLowerInvariant())) return null;
        string prefixedPath = fullName.Replace(_configuration.CacheFolder.ToLowerInvariant(), CachePrefix + "\\").Replace("\\\\", "\\");
        return CreateFileCacheEntity(fi, prefixedPath, fi.Name.ToUpper());
    }

    public FileCache? CreateFileEntry(string path)
    {
        Logger.Debug("Creating file entry for " + path);
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_ipcManager.PenumbraModDirectory()!.ToLowerInvariant())) return null;
        string prefixedPath = fullName.Replace(_ipcManager.PenumbraModDirectory()!.ToLowerInvariant(), PenumbraPrefix + "\\").Replace("\\\\", "\\");
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    private FileCache? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath, string? hash = null)
    {
        if (hash == null)
        {
            hash = Crypto.GetFileHash(fileInfo.FullName);
        }
        var entity = new FileCache(hash, prefixedPath, fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
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

    private FileCache? GetValidatedFileCache(FileCache fileCache)
    {
        var resulingFileCache = ReplacePathPrefixes(fileCache);
        resulingFileCache = Validate(resulingFileCache);
        return resulingFileCache;
    }

    private FileCache? Validate(FileCache fileCache)
    {
        var file = new FileInfo(fileCache.ResolvedFilepath);
        if (!file.Exists)
        {
            FileCaches.Remove(fileCache.PrefixedFilePath, out _);
            return null;
        }

        if (file.LastWriteTimeUtc.Ticks.ToString() != fileCache.LastModifiedDateTicks)
        {
            UpdateHash(fileCache);
        }

        return fileCache;
    }

    public void RemoveHash(FileCache entity)
    {
        FileCaches.Remove(entity.Hash, out _);
    }

    public void UpdateHash(FileCache fileCache)
    {
        Logger.Debug("Updating hash for " + fileCache.ResolvedFilepath);
        fileCache.Hash = Crypto.GetFileHash(fileCache.ResolvedFilepath);
        fileCache.LastModifiedDateTicks = new FileInfo(fileCache.ResolvedFilepath).LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        FileCaches.Remove(fileCache.PrefixedFilePath, out _);
        FileCaches[fileCache.PrefixedFilePath] = fileCache;
    }

    private FileCache ReplacePathPrefixes(FileCache fileCache)
    {
        if (fileCache.PrefixedFilePath.StartsWith(PenumbraPrefix))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(PenumbraPrefix, _ipcManager.PenumbraModDirectory()));
        }
        else if (fileCache.PrefixedFilePath.StartsWith(CachePrefix))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(CachePrefix, _configuration.CacheFolder));
        }

        return fileCache;
    }

    public void Dispose()
    {
        WriteOutFullCsv();
    }
}
