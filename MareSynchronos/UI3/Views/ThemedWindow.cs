using Dalamud.Interface.GameFonts;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.UI3.Themes;
using System.Numerics;

namespace MareSynchronos.UI3.Views;

public abstract class ThemedWindow : Window, IDisposable
{
    protected readonly ThemeEngine ThemeEngine;

    protected ThemedWindow(ThemeEngine themeEngine, string name) : base(name)
    {
        ThemeEngine = themeEngine;
    }

    protected abstract GameFontFamilyAndSize TitleFont { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public override void Draw()
    {
        ImGui.PushFont(ThemeEngine.GetGameFont(GameFontFamilyAndSize.Axis12));
        DrawInternal();
        ImGui.PopFont();
    }

    public override void PostDraw()
    {
        base.PostDraw();
        ImGui.PopFont();
        ImGui.PopStyleColor(ThemeEngine.Current.Colors.ImGuiColors.Count);
        ImGui.PopStyleVar(ThemeEngine.Current.Style.ImguiVectorStyles.Count);
        ImGui.PopStyleVar(ThemeEngine.Current.Style.ImguiFloatStyles.Count);
    }

    public override void PreDraw()
    {
        ImGui.PushFont(ThemeEngine.GetGameFont(TitleFont));
        base.PreDraw();
        foreach (var item in ThemeEngine.Current.Colors.ImGuiColors)
        {
            ImGui.PushStyleColor(item.Key, (uint)item.Value);
        }
        foreach (var item in ThemeEngine.Current.Style.ImguiVectorStyles)
        {
            ImGui.PushStyleVar(item.Key, (Vector2)item.Value);
        }
        foreach (var item in ThemeEngine.Current.Style.ImguiFloatStyles)
        {
            ImGui.PushStyleVar(item.Key, (float)item.Value);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    protected abstract void DrawInternal();
}