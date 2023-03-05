namespace MareSynchronos.WebAPI.Files.Models;

public class FileDownloadStatus
{
    public DownloadStatus DownloadStatus;
    public int TotalFiles { get; set; }
    public int TransferredFiles { get; set; }
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
}
