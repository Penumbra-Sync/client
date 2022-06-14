using Dalamud.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using MareSynchronos.FileCacheDB;
using System.IO;
using MareSynchronos.Utils;

namespace MareSynchronos.Models
{
    [JsonObject(MemberSerialization.OptIn)]
    public class FileReplacement
    {
        private readonly string penumbraDirectory;

        [JsonProperty]
        public string GamePath { get; private set; }
        public string ResolvedPath { get; private set; } = string.Empty;
        [JsonProperty]
        public string Hash { get; set; } = string.Empty;
        public bool IsInUse { get; set; } = false;
        public List<FileReplacement> Associated { get; set; } = new List<FileReplacement>();
        [JsonProperty]
        public string ImcData { get; set; } = string.Empty;
        public bool HasFileReplacement => GamePath != ResolvedPath;

        public bool Computed => (computationTask == null || (computationTask?.IsCompleted ?? true)) && Associated.All(f => f.Computed);
        private Task? computationTask = null;
        public FileReplacement(string gamePath, string penumbraDirectory)
        {
            GamePath = gamePath;
            this.penumbraDirectory = penumbraDirectory;
        }

        public void AddAssociated(FileReplacement fileReplacement)
        {
            fileReplacement.IsInUse = true;

            if (!Associated.Any(a => a.IsReplacedByThis(fileReplacement)))
            {
                Associated.Add(fileReplacement);
            }
        }

        public void SetGamePath(string path)
        {
            GamePath = path;
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

            db.Add(new FileCache()
            {
                Hash = hash,
                Filepath = fi.FullName.ToLower(),
                LastModifiedDate = fi.LastWriteTimeUtc.Ticks.ToString()
            });
            db.SaveChanges();

            return hash;
        }

        public bool IsReplacedByThis(string path)
        {
            return GamePath.ToLower() == path.ToLower() || ResolvedPath.ToLower() == path.ToLower();
        }

        public bool IsReplacedByThis(FileReplacement replacement)
        {
            return IsReplacedByThis(replacement.GamePath) || IsReplacedByThis(replacement.ResolvedPath);
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Modded: {HasFileReplacement} - {GamePath} => {ResolvedPath}");
            foreach (var l1 in Associated)
            {
                builder.AppendLine($"  + Modded: {l1.HasFileReplacement} - {l1.GamePath} => {l1.ResolvedPath}");
                foreach (var l2 in l1.Associated)
                {
                    builder.AppendLine($"    + Modded: {l2.HasFileReplacement} - {l2.GamePath} => {l2.ResolvedPath}");
                }
            }
            return builder.ToString();
        }

        public override bool Equals(object? obj)
        {
            if (obj == null) return true;
            if (obj.GetType() == typeof(FileReplacement))
            {
                return Hash == ((FileReplacement)obj).Hash && GamePath == ((FileReplacement)obj).GamePath;
            }

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            int result = 13;
            result *= 397;
            result += Hash.GetHashCode();
            result += GamePath.GetHashCode();
            result += ImcData.GetHashCode();

            return result;
        }
    }
}
