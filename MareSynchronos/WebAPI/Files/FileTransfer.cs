using MareSynchronos.API.Dto.Files;

namespace MareSynchronos.WebAPI.Files;

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
