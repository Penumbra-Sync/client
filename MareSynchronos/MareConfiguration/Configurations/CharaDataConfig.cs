using MareSynchronos.MareConfiguration.Models;

namespace MareSynchronos.MareConfiguration.Configurations;

public class CharaDataConfig : IMareConfiguration
{
    public bool OpenMareHubOnGposeStart { get; set; } = false;
    public string LastSavedCharaDataLocation { get; set; } = string.Empty;
    public Dictionary<string, CharaDataFavorite> FavoriteCodes { get; set; } = [];
    public int Version { get; set; } = 0;
}