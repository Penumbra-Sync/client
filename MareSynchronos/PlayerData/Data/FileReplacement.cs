using System.Text.RegularExpressions;
using MareSynchronos.FileCache;
using MareSynchronos.API.Data;

namespace MareSynchronos.PlayerData.Data;

public partial class FileReplacement
{
    public FileReplacement(List<string> gamePaths, string filePath, FileCacheManager fileDbManager)
    {
        GamePaths = gamePaths.Select(g => g.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        ResolvedPath = filePath.Replace('\\', '/');
        _hashLazy = new(() => !IsFileSwap ? fileDbManager.GetFileCacheByPath(ResolvedPath)?.Hash ?? string.Empty : string.Empty);
    }

    public bool Computed => IsFileSwap || !HasFileReplacement || !string.IsNullOrEmpty(Hash);

    public HashSet<string> GamePaths { get; init; } = new(StringComparer.Ordinal);

    public bool HasFileReplacement => GamePaths.Count >= 1 && GamePaths.Any(p => !string.Equals(p, ResolvedPath, StringComparison.Ordinal));

    public bool IsFileSwap => !LocalPathRegex().IsMatch(ResolvedPath) && !string.Equals(GamePaths.First(), ResolvedPath, StringComparison.Ordinal);

    public string Hash => _hashLazy.Value;

    private readonly Lazy<string> _hashLazy;

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

    [GeneratedRegex(@"^[a-zA-Z]:(/|\\)", RegexOptions.ECMAScript)]
    private static partial Regex LocalPathRegex();
}
