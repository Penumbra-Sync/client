using MareSynchronos.API.Dto.Files;

namespace MareSynchronos.WebAPI.Files;

public class UploadFileTransfer : FileTransfer
{
    public UploadFileTransfer(UploadFileDto dto) : base(dto) { }
    public override long Total { get; set; }
    public string LocalFile { get; set; } = string.Empty;
}
