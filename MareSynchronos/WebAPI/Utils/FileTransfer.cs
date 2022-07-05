using MareSynchronos.API;

namespace MareSynchronos.WebAPI.Utils;

public abstract class FileTransfer
{
    protected readonly ITransferFileDto TransferDto;

    protected FileTransfer(ITransferFileDto transferDto)
    {
        TransferDto = transferDto;
    }

    public string ForbiddenBy => TransferDto.ForbiddenBy;
    public long Transferred { get; set; } = 0;
    public abstract long Total { get; set; }
    public string Hash => TransferDto.Hash;
    public bool IsInTransfer => Transferred != Total && Transferred > 0;
    public bool IsTransferred => Transferred == Total;
    public virtual bool CanBeTransferred => !TransferDto.IsForbidden && (TransferDto is not DownloadFileDto dto || dto.FileExists);
    public bool IsForbidden => TransferDto.IsForbidden;

    public override string ToString()
    {
        return Hash;
    }
}

public class UploadFileTransfer : FileTransfer
{
    public UploadFileTransfer(UploadFileDto dto) : base(dto) { }
    public override long Total { get; set; }
    public string LocalFile { get; set; } = string.Empty;
}

public class DownloadFileTransfer : FileTransfer
{
    private DownloadFileDto Dto => (DownloadFileDto)TransferDto;
    public DownloadFileTransfer(DownloadFileDto dto) : base(dto) { }

    public override long Total
    {
        set { }
        get => Dto.Size;
    }

    public override bool CanBeTransferred => Dto.FileExists && !Dto.IsForbidden && Dto.Size > 0;
}