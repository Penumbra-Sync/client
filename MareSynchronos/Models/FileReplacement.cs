using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.Models
{
    public class FileReplacement
    {
        private readonly string penumbraDirectory;

        public string GamePath { get; private set; }
        public string ReplacedPath { get; private set; } = string.Empty;

        public List<FileReplacement> Associated { get; set; } = new List<FileReplacement>();

        public bool HasFileReplacement => GamePath != ReplacedPath;
        public FileReplacement(string gamePath, string penumbraDirectory)
        {
            GamePath = gamePath;
            this.penumbraDirectory = penumbraDirectory;
        }

        public void AddAssociated(FileReplacement fileReplacement)
        {
            if (!Associated.Any(a => a.IsReplacedByThis(fileReplacement)))
            {
                Associated.Add(fileReplacement);
            }
        }

        public void SetGamePath(string path)
        {
            GamePath = path;
        }

        public void SetReplacedPath(string path)
        {
            ReplacedPath = path.ToLower().Replace('/', '\\').Replace(penumbraDirectory, "").Replace('\\', '/');
        }

        public bool IsReplacedByThis(string path)
        {
            return GamePath.ToLower() == path.ToLower() || ReplacedPath.ToLower() == path.ToLower();
        }

        public bool IsReplacedByThis(FileReplacement replacement)
        {
            return IsReplacedByThis(replacement.GamePath) || IsReplacedByThis(replacement.ReplacedPath);
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Modded: {HasFileReplacement} - {GamePath} => {ReplacedPath}");
            foreach (var l1 in Associated)
            {
                builder.AppendLine($"  + Modded: {l1.HasFileReplacement} - {l1.GamePath} => {l1.ReplacedPath}");
                foreach (var l2 in l1.Associated)
                {
                    builder.AppendLine($"    + Modded: {l2.HasFileReplacement} - {l2.GamePath} => {l2.ReplacedPath}");
                }
            }
            return builder.ToString();
        }
    }
}
