namespace MareSynchronos.Services;

public record MareProfileData(bool IsFlagged, bool IsNSFW, string Base64ProfilePicture, string Base64SupporterPicture, string Description)
{
    public Lazy<byte[]> ImageData { get; } = new Lazy<byte[]>(Convert.FromBase64String(Base64ProfilePicture));
    public Lazy<byte[]> SupporterImageData { get; } = new Lazy<byte[]>(string.IsNullOrEmpty(Base64SupporterPicture) ? [] : Convert.FromBase64String(Base64SupporterPicture));
}
