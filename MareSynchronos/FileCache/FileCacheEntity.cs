#nullable disable

namespace MareSynchronos.FileCache;

public class FileCacheEntity
{
    public FileCacheEntity(string hash, string path, string lastModifiedDateTicks)
    {
        Hash = hash;
        PrefixedFilePath = path;
        LastModifiedDateTicks = lastModifiedDateTicks;
    }

    public string CsvEntry => $"{Hash}{FileCacheManager.CsvSplit}{PrefixedFilePath}{FileCacheManager.CsvSplit}{LastModifiedDateTicks}";
    public string Hash { get; set; }
    public string LastModifiedDateTicks { get; set; }
    public string PrefixedFilePath { get; init; }
    public string ResolvedFilepath { get; private set; } = string.Empty;

    public void SetResolvedFilePath(string filePath)
    {
        ResolvedFilepath = filePath.ToLowerInvariant().Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}