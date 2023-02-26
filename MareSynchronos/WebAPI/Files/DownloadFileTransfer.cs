using MareSynchronos.API.Dto.Files;

namespace MareSynchronos.WebAPI.Files;

public class DownloadFileTransfer : FileTransfer
{
    private DownloadFileDto Dto => (DownloadFileDto)TransferDto;
    public DownloadFileTransfer(DownloadFileDto dto) : base(dto) { }
    public Uri DownloadUri => new(Dto.Url);
    public override long Total
    {
        set { }
        get => Dto.Size;
    }

    public override bool CanBeTransferred => Dto.FileExists && !Dto.IsForbidden && Dto.Size > 0;
}