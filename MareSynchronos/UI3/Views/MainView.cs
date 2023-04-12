using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using ImGuiNET;
using MareSynchronos.UI3.Themes;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Reflection;

namespace MareSynchronos.UI3.Views;

public interface IUiComponent
{
    Vector2 AbsolutePosition { get; }
    IUiComponent? Parent { get; }
    Vector2? Position { get; }
    Vector2? Size { get; }

    void Draw();
}

public class MainView : ThemedWindow, IUiComponent
{
    private readonly ILogger<MainView> _logger;
    private Vector2 _windowPosition = new(0, 0);
    private Vector2 _winMax = new(0, 0);
    private Vector2 _winMin = new(0, 0);

    public MainView(ThemeEngine themeEngine, ILogger<MainView> logger) : base(themeEngine, "Mare UI3###MareSynchronosUI3")
    {
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(350, 400),
            MaximumSize = new Vector2(350, 2000),
        };
        _logger = logger;

#if DEBUG
        string dev = "Dev Build";
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"Mare Synchronos {dev} ({ver.Major}.{ver.Minor}.{ver.Build})###MareSynchronosMainUI3";
        Toggle();
#else
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        WindowName = "Mare Synchronos " + ver.Major + "." + ver.Minor + "." + ver.Build + "###MareSynchronosMainUI";
#endif

        Flags |= ImGuiWindowFlags.NoScrollbar;
    }

    public Vector2 AbsolutePosition { get; private set; }

    public IUiComponent? Parent => null;

    protected override GameFontFamilyAndSize TitleFont => GameFontFamilyAndSize.Jupiter16;

    protected override void DrawInternal()
    {
        AbsolutePosition = ImGui.GetWindowPos();
        _winMin = ImGui.GetWindowContentRegionMin();
        Size = ImGui.GetWindowContentRegionMax();

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilledMultiColor(ImGui.GetWindowContentRegionMin() + _windowPosition,
            ImGui.GetWindowContentRegionMax() + _windowPosition,
            ThemeEngine.Current.Colors.TitleBgActive,
            ThemeEngine.Current.Colors.TitleBgActive,
            ThemeEngine.Current.Colors.TitleBg,
            ThemeEngine.Current.Colors.TitleBg);

        if (ImGui.BeginChildFrame(100, new Vector2(_winMax.X, 60f)))
        {
            ImGui.TextUnformatted("Test text");
            ImGui.EndChildFrame();
        }
    }
}