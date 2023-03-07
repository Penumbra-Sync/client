#nullable disable


using System.Globalization;

namespace MareSynchronos.FileCache;

public class FileCacheEntity
{
    public string ResolvedFilepath { get; private set; } = string.Empty;
    public string Hash { get; set; }
    public string PrefixedFilePath { get; init; }
    public string LastModifiedDateTicks { get; set; }

    public FileCacheEntity(string hash, string path, string lastModifiedDateTicks)
    {
        Hash = hash;
        PrefixedFilePath = path;
        LastModifiedDateTicks = lastModifiedDateTicks;
    }

    public void SetResolvedFilePath(string filePath)
    {
        ResolvedFilepath = filePath.ToLowerInvariant().Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    public string CsvEntry => $"{Hash}{FileCacheManager.CsvSplit}{PrefixedFilePath}{FileCacheManager.CsvSplit}{LastModifiedDateTicks}";
}
