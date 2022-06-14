using Dalamud.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using MareSynchronos.FileCacheDB;

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

            Task.Run(() =>
            {
                using FileCacheContext db = new FileCacheContext();
                var fileCache = db.FileCaches.SingleOrDefault(f => f.Filepath == path.ToLower());
                if (fileCache != null)
                    Hash = fileCache.Hash;
            });
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
    }
}
