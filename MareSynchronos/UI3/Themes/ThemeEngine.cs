using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using ImGuiNET;
using MareSynchronos.MareConfiguration;

namespace MareSynchronos.UI3.Themes;

public class ThemeEngine
{
    private readonly Dictionary<GameFontFamilyAndSize, GameFontHandle> _requestedFonts;
    private readonly List<ITheme> _themes;
    private readonly UiBuilder _uiBuilder;
    private readonly UiConfigService _uiConfigService;

    public ThemeEngine(UiConfigService uiConfigService, IEnumerable<ITheme> themes, UiBuilder uiBuilder)
    {
        _themes = themes.ToList();
        _uiConfigService = uiConfigService;
        _uiBuilder = uiBuilder;
        _requestedFonts = new();
        var initialTheme = _uiConfigService.Current.SelectedTheme;
        SelectTheme(initialTheme);
    }

    public ITheme Current { get; private set; } = null!;

    public ImFontPtr GetGameFont(GameFontFamilyAndSize gameFont, bool italic = false, bool bold = false)
    {
        if (!_requestedFonts.TryGetValue(gameFont, out var ptr))
        {
            _requestedFonts[gameFont] = ptr = _uiBuilder.GetGameFontHandle(new GameFontStyle(gameFont) { Italic = italic, Bold = bold });
        }

        return ptr.ImFont;
    }

    public void SelectTheme(string name)
    {
        Current = _themes.Find(f => string.Equals(f.Name, name, StringComparison.Ordinal)) ?? _themes.First();
        _uiConfigService.Current.SelectedTheme = Current.Name;
        _uiConfigService.Save();
    }
}