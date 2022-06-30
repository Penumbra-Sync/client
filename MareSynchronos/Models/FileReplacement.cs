using Dalamud.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using MareSynchronos.FileCacheDB;
using System.IO;
using MareSynchronos.API;
using MareSynchronos.Utils;

namespace MareSynchronos.Models
{
    [JsonObject(MemberSerialization.OptIn)]
    public class FileReplacement
    {
        private readonly string _penumbraDirectory;

        private Task? _computationTask = null;

        public FileReplacement(string penumbraDirectory)
        {
            this._penumbraDirectory = penumbraDirectory;
        }

        public bool Computed => (_computationTask == null || (_computationTask?.IsCompleted ?? true));

        [JsonProperty]
        public List<string> GamePaths { get; set; } = new();

        public bool HasFileReplacement => GamePaths.Count >= 1 && GamePaths.Any(p => p != ResolvedPath);

        [JsonProperty]
        public string Hash { get; set; } = string.Empty;
        
        [JsonProperty]
        public string ResolvedPath { get; set; } = string.Empty;
        
        public void SetResolvedPath(string path)
        {
            ResolvedPath = path.ToLower().Replace('/', '\\').Replace(_penumbraDirectory, "").Replace('\\', '/');
            if (!HasFileReplacement) return;

            _computationTask = Task.Run(() =>
            {
                FileCache? fileCache;
                using (FileCacheContext db = new())
                {
                    fileCache = db.FileCaches.SingleOrDefault(f => f.Filepath == path.ToLower());
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
                        var newTempCache = db.FileCaches.Single(f => f.Filepath == path.ToLower());
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
            var fileAddedDuringCompute = db.FileCaches.SingleOrDefault(f => f.Filepath == fi.FullName.ToLower());
            if (fileAddedDuringCompute != null) return fileAddedDuringCompute.Hash;

            try
            {
                db.Add(new FileCache()
                {
                    Hash = hash,
                    Filepath = fi.FullName.ToLower(),
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
