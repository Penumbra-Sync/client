#nullable disable

namespace MareSynchronos.FileCache;

public class FileCacheEntity
{
    public FileCacheEntity(string hash, string path, string lastModifiedDateTicks, long? size = null, long? compressedSize = null)
    {
        Size = size;
        CompressedSize = compressedSize;
        Hash = hash;
        PrefixedFilePath = path;
        LastModifiedDateTicks = lastModifiedDateTicks;
    }

    public long? CompressedSize { get; set; }
    public string CsvEntry => $"{Hash}{FileCacheManager.CsvSplit}{PrefixedFilePath}{FileCacheManager.CsvSplit}{LastModifiedDateTicks}|{Size ?? -1}|{CompressedSize ?? -1}";
    public string Hash { get; set; }
    public bool IsCacheEntry => PrefixedFilePath.StartsWith(FileCacheManager.CachePrefix, StringComparison.OrdinalIgnoreCase);
    public string LastModifiedDateTicks { get; set; }
    public string PrefixedFilePath { get; init; }
    public string ResolvedFilepath { get; private set; } = string.Empty;
    public long? Size { get; set; }

    public void SetResolvedFilePath(string filePath)
    {
        ResolvedFilepath = filePath.ToLowerInvariant().Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}