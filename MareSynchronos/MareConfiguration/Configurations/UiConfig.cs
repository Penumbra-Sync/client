namespace MareSynchronos.MareConfiguration.Configurations;

[Serializable]
public class UiConfig : IMareConfiguration
{
    public string SelectedTheme { get; set; } = "Dark";
    public int Version { get; set; } = 0;
}