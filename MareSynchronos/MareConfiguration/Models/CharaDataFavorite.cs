namespace MareSynchronos.MareConfiguration.Models;

[Serializable]
public class CharaDataFavorite
{
    public DateTime LastDownloaded { get; set; } = DateTime.MaxValue;
    public string CustomDescription { get; set; } = string.Empty;
}