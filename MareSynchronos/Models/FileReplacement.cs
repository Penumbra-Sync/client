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
using MareSynchronos.Managers;

namespace MareSynchronos.Models
{
    public class FileReplacement
    {
        private readonly FileDbManager fileDbManager;

        public FileReplacement(FileDbManager fileDbManager)
        {
            this.fileDbManager = fileDbManager;
        }

        public bool Computed => IsFileSwap || !HasFileReplacement || !string.IsNullOrEmpty(Hash);

        public List<string> GamePaths { get; set; } = new();

        public bool HasFileReplacement => GamePaths.Count >= 1 && GamePaths.Any(p => p != ResolvedPath);

        public bool IsFileSwap => !Regex.IsMatch(ResolvedPath, @"^[a-zA-Z]:(/|\\)", RegexOptions.ECMAScript) && GamePaths.First() != ResolvedPath;

        public string Hash { get; set; } = string.Empty;

        public string ResolvedPath { get; set; } = string.Empty;

        public void SetResolvedPath(string path)
        {
            ResolvedPath = path.ToLowerInvariant().Replace('\\', '/');
            if (!HasFileReplacement || IsFileSwap) return;

            _ = Task.Run(() =>
            {
                var cache = fileDbManager.GetFileCacheByPath(ResolvedPath);
                cache ??= fileDbManager.CreateFileCacheEntity(ResolvedPath);
                Hash = cache.OriginalHash;
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
    }
}
