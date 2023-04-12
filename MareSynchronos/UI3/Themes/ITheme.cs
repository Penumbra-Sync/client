using MareSynchronos.UI3.Themes.Colors;

namespace MareSynchronos.UI3.Themes;

public interface ITheme
{
    IColors Colors { get; }
    string Name { get; }
    IStyle Style { get; }
}