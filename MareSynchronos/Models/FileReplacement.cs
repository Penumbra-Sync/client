using Dalamud.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MareSynchronos.FileCacheDB;
using System.IO;
using MareSynchronos.API;
using MareSynchronos.Utils;
using System.Text.RegularExpressions;

namespace MareSynchronos.Models
{
    public class FileReplacement
    {
        private readonly string _penumbraDirectory;

        public FileReplacement(string penumbraDirectory)
        {
            _penumbraDirectory = penumbraDirectory;
        }

        public bool Computed => IsFileSwap || !HasFileReplacement || !string.IsNullOrEmpty(Hash);

        public List<string> GamePaths { get; set; } = new();

        public bool HasFileReplacement => GamePaths.Count >= 1 && GamePaths.Any(p => p != ResolvedPath);

        public bool IsFileSwap => !Regex.IsMatch(ResolvedPath, @"^[a-zA-Z]:(/|\\)", RegexOptions.ECMAScript) && GamePaths.First() != ResolvedPath;

        public string Hash { get; set; } = string.Empty;

        public string ResolvedPath { get; set; } = string.Empty;

        public void SetResolvedPath(string path)
        {
            ResolvedPath = path.ToLowerInvariant().Replace('/', '\\').Replace(_penumbraDirectory, "").Replace('\\', '/');
            if (!HasFileReplacement || IsFileSwap) return;

            _ = Task.Run(() =>
            {
                FileCache? fileCache;
                using (FileCacheContext db = new())
                {
                    fileCache = db.FileCaches.FirstOrDefault(f => f.Filepath == path.ToLowerInvariant());
                }

                if (fileCache != null)
                {
                    FileInfo fi = new(fileCache.Filepath);
                    if (fi.LastWriteTimeUtc.Ticks == long.Parse(fileCache.LastModifiedDate))
                    {
                        Hash = fileCache.Hash;
                    }
                    else
                    {
                        Hash = ComputeHash(fi);
                        using var db = new FileCacheContext();
                        var newTempCache = db.FileCaches.Single(f => f.Filepath == path.ToLowerInvariant());
                        newTempCache.Hash = Hash;
                        db.Update(newTempCache);
                        db.SaveChanges();
                    }
                }
                else
                {
                    Hash = ComputeHash(new FileInfo(path));
                }
            });
        }

        public FileReplacementDto ToFileReplacementDto()
        {
            return new FileReplacementDto
            {
                GamePaths = GamePaths.ToArray(),
                Hash = Hash,
                FileSwapPath = IsFileSwap ? ResolvedPath : string.Empty
            };
        }
        public override string ToString()
        {
            StringBuilder builder = new();
            builder.AppendLine($"Modded: {HasFileReplacement} - {string.Join(",", GamePaths)} => {ResolvedPath}");
            return builder.ToString();
        }

        private string ComputeHash(FileInfo fi)
        {
            // compute hash if hash is not present
            string hash = Crypto.GetFileHash(fi.FullName);

            using FileCacheContext db = new();
            var fileAddedDuringCompute = db.FileCaches.FirstOrDefault(f => f.Filepath == fi.FullName.ToLowerInvariant());
            if (fileAddedDuringCompute != null) return fileAddedDuringCompute.Hash;

            try
            {
                Logger.Debug("Adding new file to DB: " + fi.FullName + ", " + hash);
                db.Add(new FileCache()
                {
                    Hash = hash,
                    Filepath = fi.FullName.ToLowerInvariant(),
                    LastModifiedDate = fi.LastWriteTimeUtc.Ticks.ToString()
                });
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Error adding files to database. Most likely not an issue though.");
            }

            return hash;
        }
    }
}
