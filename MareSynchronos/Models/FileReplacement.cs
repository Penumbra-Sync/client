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
        public FileReplacementDto ToFileReplacementDto()
        {
            return new FileReplacementDto
            {
                GamePaths = GamePaths,
                Hash = Hash,
                ImcData = ImcData
            };
        }

        private readonly string penumbraDirectory;

        [JsonProperty]
        public string[] GamePaths { get; set; } = Array.Empty<string>();
        [JsonProperty]
        public string ResolvedPath { get; set; } = string.Empty;
        [JsonProperty]
        public string Hash { get; set; } = string.Empty;
        public bool IsInUse { get; set; } = false;
        public List<FileReplacement> Associated { get; set; } = new List<FileReplacement>();
        [JsonProperty]
        public string ImcData { get; set; } = string.Empty;
        public bool HasFileReplacement => GamePaths.Length >= 1 && GamePaths[0] != ResolvedPath;

        public bool Computed => (computationTask == null || (computationTask?.IsCompleted ?? true)) && Associated.All(f => f.Computed);
        private Task? computationTask = null;
        public FileReplacement(string penumbraDirectory)
        {
            this.penumbraDirectory = penumbraDirectory;
        }

        public void AddAssociated(FileReplacement fileReplacement)
        {
            fileReplacement.IsInUse = true;

            Associated.Add(fileReplacement);
        }

        public void SetResolvedPath(string path)
        {
            ResolvedPath = path.ToLower().Replace('/', '\\').Replace(penumbraDirectory, "").Replace('\\', '/');
            if (!HasFileReplacement) return;

            computationTask = Task.Run(() =>
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

        public override string ToString()
        {
            StringBuilder builder = new();
            builder.AppendLine($"Modded: {HasFileReplacement} - {string.Join(",", GamePaths)} => {ResolvedPath}");
            foreach (var l1 in Associated)
            {
                builder.AppendLine($"  + Modded: {l1.HasFileReplacement} - {string.Join(",", l1.GamePaths)} => {l1.ResolvedPath}");
                foreach (var l2 in l1.Associated)
                {
                    builder.AppendLine($"    + Modded: {l2.HasFileReplacement} - {string.Join(",", l2.GamePaths)} => {l2.ResolvedPath}");
                }
            }
            return builder.ToString();
        }

        public override bool Equals(object? obj)
        {
            if (obj == null) return true;
            if (obj.GetType() == typeof(FileReplacement))
            {
                return Hash == ((FileReplacement)obj).Hash;
            }

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            int result = 13;
            result *= 397;
            result += Hash.GetHashCode();
            result += ResolvedPath.GetHashCode();

            return result;
        }
    }
}
