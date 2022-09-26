using MareSynchronos.FileCacheDB;
using MareSynchronos.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MareSynchronos.Managers;

public class FileDbManager
{
    private const string PenumbraPrefix = "{penumbra}";
    private const string CachePrefix = "{cache}";
    private readonly IpcManager _ipcManager;
    private readonly Configuration _configuration;
    private static object _lock = new();

    public FileDbManager(IpcManager ipcManager, Configuration configuration)
    {
        _ipcManager = ipcManager;
        _configuration = configuration;
    }

    public FileCache? GetFileCacheByHash(string hash)
    {
        List<FileCacheEntity> matchingEntries = new List<FileCacheEntity>();
        using (var db = new FileCacheContext())
        {
            matchingEntries = db.FileCaches.Where(f => f.Hash.ToLower() == hash.ToLower()).ToList();
        }

        if (!matchingEntries.Any()) return null;

        if (matchingEntries.Any(f => f.Filepath.Contains(PenumbraPrefix) && matchingEntries.Any(f => f.Filepath.Contains(CachePrefix))))
        {
            var cachedEntries = matchingEntries.Where(f => f.Filepath.Contains(CachePrefix)).ToList();
            DeleteFromDatabase(cachedEntries.Select(f => new FileCache(f)));
            foreach (var entry in cachedEntries)
            {
                matchingEntries.Remove(entry);
            }
        }

        return GetValidatedFileCache(matchingEntries.First());
    }

    public FileCache? ValidateFileCacheEntity(FileCacheEntity fileCacheEntity)
    {
        return GetValidatedFileCache(fileCacheEntity);
    }

    public FileCache? GetFileCacheByPath(string path)
    {
        FileCacheEntity? matchingEntries = null;
        var cleanedPath = path.Replace("/", "\\").ToLowerInvariant().Replace(_ipcManager.PenumbraModDirectory()!.ToLowerInvariant(), "");
        using (var db = new FileCacheContext())
        {
            matchingEntries = db.FileCaches.FirstOrDefault(f => f.Filepath.EndsWith(cleanedPath));
        }

        if (matchingEntries == null)
        {
            return CreateFileEntry(path);
        }

        var validatedCacheEntry = GetValidatedFileCache(matchingEntries);

        return validatedCacheEntry;
    }

    public FileCache? CreateCacheEntry(string path)
    {
        Logger.Debug("Creating cache entry for " + path);
        FileInfo fi = new FileInfo(path);
        if (!fi.Exists) return null;
        string prefixedPath = fi.FullName.ToLowerInvariant().Replace(_configuration.CacheFolder.ToLowerInvariant(), CachePrefix + "\\").Replace("\\\\", "\\");
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    public FileCache? CreateFileEntry(string path)
    {
        Logger.Debug("Creating file entry for " + path);
        FileInfo fi = new FileInfo(path);
        if (!fi.Exists) return null;
        string prefixedPath = fi.FullName.ToLowerInvariant().Replace(_ipcManager.PenumbraModDirectory()!.ToLowerInvariant(), PenumbraPrefix + "\\").Replace("\\\\", "\\");
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    private FileCache? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath)
    {
        var hash = Crypto.GetFileHash(fileInfo.FullName);
        lock (_lock)
        {
            var entity = new FileCacheEntity();
            entity.Hash = hash;
            entity.Filepath = prefixedPath;
            entity.LastModifiedDate = fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            try
            {
                using var db = new FileCacheContext();
                db.FileCaches.Add(entity);
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not add " + fileInfo.FullName);
            }
        }
        var result = GetFileCacheByPath(prefixedPath);
        Logger.Debug("Creating file cache for " + fileInfo.FullName + " success: " + (result != null));
        return result;
    }

    private FileCache? GetValidatedFileCache(FileCacheEntity e)
    {
        var fileCache = new FileCache(e);
        var resulingFileCache = MigrateLegacy(fileCache);
        if (resulingFileCache == null) return null;

        resulingFileCache = ReplacePathPrefixes(resulingFileCache);
        resulingFileCache = Validate(resulingFileCache);
        return resulingFileCache;
    }

    private FileCache? Validate(FileCache fileCache)
    {
        var file = new FileInfo(fileCache.Filepath);
        if (!file.Exists)
        {
            DeleteFromDatabase(new[] { fileCache });
            return null;
        }

        if (file.LastWriteTimeUtc.Ticks != fileCache.LastModifiedDateTicks)
        {
            fileCache.SetHash(Crypto.GetFileHash(fileCache.Filepath));
            UpdateCacheHash(fileCache, file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        return fileCache;
    }

    private FileCache? MigrateLegacy(FileCache fileCache)
    {
        if (fileCache.OriginalFilepath.Contains(PenumbraPrefix + "\\") || fileCache.OriginalFilepath.Contains(CachePrefix)) return fileCache;

        var fileInfo = new FileInfo(fileCache.OriginalFilepath);
        var penumbraDir = _ipcManager.PenumbraModDirectory()!;
        if (penumbraDir.Last() != '\\') penumbraDir += "\\";
        // check if it's a cache file
        if (fileInfo.Exists && fileInfo.Name.Length == 40)
        {
            MigrateLegacyFilePath(fileCache, CachePrefix + "\\" + fileInfo.Name.ToLower());
        }
        else if (fileInfo.Exists && fileInfo.FullName.ToLowerInvariant().Contains(penumbraDir))
        {
            // attempt to replace penumbra mod folder path with {penumbra}
            var newPath = PenumbraPrefix + "\\" + fileCache.OriginalFilepath.ToLowerInvariant().Replace(penumbraDir, string.Empty);
            MigrateLegacyFilePath(fileCache, newPath);
        }
        else if (fileInfo.FullName.ToLowerInvariant().Contains(PenumbraPrefix))
        {
            var newPath = PenumbraPrefix + "\\" + fileCache.OriginalFilepath.ToLowerInvariant().Replace(PenumbraPrefix, string.Empty);
            MigrateLegacyFilePath(fileCache, newPath);
        }
        else
        {
            DeleteFromDatabase(new[] { fileCache });
            return null;
        }

        return fileCache;
    }

    private FileCache ReplacePathPrefixes(FileCache fileCache)
    {
        if (fileCache.OriginalFilepath.StartsWith(PenumbraPrefix))
        {
            fileCache.SetResolvedFilePath(fileCache.OriginalFilepath.Replace(PenumbraPrefix, _ipcManager.PenumbraModDirectory()));
        }
        else if (fileCache.OriginalFilepath.StartsWith(CachePrefix))
        {
            fileCache.SetResolvedFilePath(fileCache.OriginalFilepath.Replace(CachePrefix, _configuration.CacheFolder));
        }

        return fileCache;
    }

    private void UpdateCacheHash(FileCache markedForUpdate, string lastModifiedDate)
    {
        lock (_lock)
        {
            Logger.Verbose("Updating Hash for " + markedForUpdate.OriginalFilepath);
            using var db = new FileCacheContext();
            var cache = db.FileCaches.First(f => f.Filepath == markedForUpdate.OriginalFilepath && f.Hash == markedForUpdate.OriginalHash);
            var newcache = new FileCacheEntity()
            {
                Filepath = cache.Filepath,
                Hash = markedForUpdate.Hash,
                LastModifiedDate = lastModifiedDate
            };
            db.Remove(cache);
            db.FileCaches.Add(newcache);
            markedForUpdate.UpdateFileCache(newcache);
            db.SaveChanges();
        }
    }

    private void MigrateLegacyFilePath(FileCache fileCacheToMigrate, string newPath)
    {
        lock (_lock)
        {
            Logger.Verbose("Migrating legacy file path for " + fileCacheToMigrate.OriginalFilepath);
            using var db = new FileCacheContext();
            var cache = db.FileCaches.First(f => f.Filepath == fileCacheToMigrate.OriginalFilepath && f.Hash == fileCacheToMigrate.OriginalHash);
            var newcache = new FileCacheEntity()
            {
                Filepath = newPath,
                Hash = cache.Hash,
                LastModifiedDate = cache.LastModifiedDate
            };
            db.Remove(cache);
            db.SaveChanges();
            var existingCache = db.FileCaches.FirstOrDefault(f => f.Filepath == newPath && f.Hash == cache.Hash);
            if (existingCache != null)
            {
                fileCacheToMigrate.UpdateFileCache(existingCache);
            }
            else
            {
                db.FileCaches.Add(newcache);
                fileCacheToMigrate.UpdateFileCache(newcache);
                db.SaveChanges();
            }
        }
    }

    private void DeleteFromDatabase(IEnumerable<FileCache> markedForDeletion)
    {
        lock (_lock)
        {
            using var db = new FileCacheContext();
            foreach (var item in markedForDeletion)
            {
                Logger.Verbose("Removing " + item.OriginalFilepath);
                var itemToRemove = db.FileCaches.FirstOrDefault(f => f.Hash == item.OriginalHash && f.Filepath == item.OriginalFilepath);
                if (itemToRemove == null) continue;
                db.FileCaches.Remove(itemToRemove);
            }
            db.SaveChanges();
        }
    }
}
