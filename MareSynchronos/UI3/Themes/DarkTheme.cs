using MareSynchronos.UI3.Themes.Colors;

namespace MareSynchronos.UI3.Themes;

public class DarkTheme : ITheme
{
    public IColors Colors { get; } = new DarkColors();

    public string Name => "Dark Theme";

    public IStyle Style { get; } = new DefaultStyle();
}