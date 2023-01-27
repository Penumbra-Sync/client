using System.Text;
using System.Text.RegularExpressions;
using MareSynchronos.FileCache;
using MareSynchronos.Managers;
using MareSynchronos.Utils;
using MareSynchronos.API.Data;

namespace MareSynchronos.Models;

public class FileReplacement
{
    private readonly FileCacheManager fileDbManager;
    private readonly IpcManager ipcManager;

    public FileReplacement(FileCacheManager fileDbManager, IpcManager ipcManager)
    {
        this.fileDbManager = fileDbManager;
        this.ipcManager = ipcManager;
    }

    public bool Computed => IsFileSwap || !HasFileReplacement || !string.IsNullOrEmpty(Hash);

    public List<string> GamePaths { get; set; } = new();

    public bool HasFileReplacement => GamePaths.Count >= 1 && GamePaths.Any(p => !string.Equals(p, ResolvedPath, StringComparison.Ordinal));

    public bool IsFileSwap => !Regex.IsMatch(ResolvedPath, @"^[a-zA-Z]:(/|\\)", RegexOptions.ECMAScript) && !string.Equals(GamePaths.First(), ResolvedPath, StringComparison.Ordinal);

    public string Hash { get; private set; } = string.Empty;

    public string ResolvedPath { get; set; } = string.Empty;

    private void SetResolvedPath(string path)
    {
        ResolvedPath = path.ToLowerInvariant().Replace('\\', '/');
        if (!HasFileReplacement || IsFileSwap) return;

        _ = Task.Run(() =>
        {
            try
            {
                var cache = fileDbManager.GetFileCacheByPath(ResolvedPath)!;
                Hash = cache.Hash;
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not set Hash for " + ResolvedPath + ", resetting to original", ex);
                ResolvedPath = GamePaths.First();
            }
        });
    }

    public bool Verify()
    {
        if (!IsFileSwap)
        {
            var cache = fileDbManager.GetFileCacheByPath(ResolvedPath);
            if (cache == null)
            {
                Logger.Warn("Replacement Failed verification: " + GamePaths.First());
                return false;
            }
            Hash = cache.Hash;
            return true;
        }

        ResolvePath(GamePaths.First());

        var success = IsFileSwap;
        if (!success)
        {
            Logger.Warn("FileSwap Failed verification: " + GamePaths.First());
        }

        return success;
    }

    public FileReplacementData ToFileReplacementDto()
    {
        return new FileReplacementData
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

    internal void ReverseResolvePath(string path)
    {
        GamePaths = ipcManager.PenumbraReverseResolvePlayer(path).ToList();
        SetResolvedPath(path);
    }

    internal void ResolvePath(string path)
    {
        GamePaths = new List<string> { path };
        SetResolvedPath(ipcManager.PenumbraResolvePath(path));
    }
}
