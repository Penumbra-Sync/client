using MareSynchronos.API.Dto.Files;

namespace MareSynchronos.WebAPI.Utils;

public class UploadFileTransfer : FileTransfer
{
    public UploadFileTransfer(UploadFileDto dto) : base(dto) { }
    public override long Total { get; set; }
    public string LocalFile { get; set; } = string.Empty;
}
