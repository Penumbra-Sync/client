namespace MareSynchronos.WebAPI.Utils;

public class FileTransfer
{
    public long Transferred { get; set; } = 0;
    public long Total { get; set; } = 0;
    public string Hash { get; set; } = string.Empty;
}