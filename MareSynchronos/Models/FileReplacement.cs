using System.Text.RegularExpressions;
using MareSynchronos.FileCache;
using MareSynchronos.API.Data;

namespace MareSynchronos.Models;

public class FileReplacement
{
    public FileReplacement(List<string> gamePaths, string filePath, FileCacheManager fileDbManager)
    {
        GamePaths = gamePaths.Select(g => g.Replace('\\', '/')).ToList();
        ResolvedPath = filePath.Replace('\\', '/');
        HashLazy = new(() => !IsFileSwap ? fileDbManager.GetFileCacheByPath(ResolvedPath)?.Hash ?? string.Empty : string.Empty);
    }

    public bool Computed => IsFileSwap || !HasFileReplacement || !string.IsNullOrEmpty(Hash);

    public List<string> GamePaths { get; init; } = new();

    public bool HasFileReplacement => GamePaths.Count >= 1 && GamePaths.Any(p => !string.Equals(p, ResolvedPath, StringComparison.Ordinal));

    public bool IsFileSwap => !Regex.IsMatch(ResolvedPath, @"^[a-zA-Z]:(/|\\)", RegexOptions.ECMAScript) && !string.Equals(GamePaths[0], ResolvedPath, StringComparison.Ordinal);

    public string Hash => HashLazy.Value;

    private Lazy<string> HashLazy;

    public string ResolvedPath { get; init; } = string.Empty;

    public FileReplacementData ToFileReplacementDto()
    {
        return new FileReplacementData
        {
            GamePaths = GamePaths.ToArray(),
            Hash = Hash,
            FileSwapPath = IsFileSwap ? ResolvedPath : string.Empty,
        };
    }

    public override string ToString()
    {
        return $"Modded: {HasFileReplacement} - {string.Join(",", GamePaths)} => {ResolvedPath}";
    }
}
