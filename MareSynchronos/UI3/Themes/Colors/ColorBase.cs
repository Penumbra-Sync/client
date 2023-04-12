using ImGuiNET;
using System.Reflection;

namespace MareSynchronos.UI3.Themes.Colors;

public abstract class ColorBase : IColors
{
    protected ColorBase()
    {
        ImGuiColors = new();
        foreach (var en in Enum.GetValues(typeof(ImGuiCol)))
        {
            PropertyInfo? pi = GetType()?.GetProperty(Enum.GetName(typeof(ImGuiCol), en) ?? string.Empty);
            if (pi == null) continue;
            var color = pi.GetValue(this)!;
            ImGuiColors[(ImGuiCol)en] = (Color)color;
        }
    }

    public virtual Color Border { get; } = new(255, 0, 255, 255);
    public virtual Color BorderShadow { get; } = new(255, 0, 255, 255);
    public virtual Color Button { get; } = new(255, 0, 255, 255);
    public virtual Color ButtonActive { get; } = new(255, 0, 255, 255);
    public virtual Color ButtonHovered { get; } = new(255, 0, 255, 255);
    public virtual Color CheckMark { get; } = new(255, 0, 255, 255);
    public virtual Color ChildBg { get; } = new(255, 0, 255, 255);
    public virtual Color DockingEmptyBg { get; } = new(255, 0, 255, 255);
    public virtual Color DockingPreview { get; } = new(255, 0, 255, 255);
    public virtual Color DragDropTarget { get; } = new(255, 0, 255, 255);
    public virtual Color FrameBg { get; } = new(0, 0, 255, 255);
    public virtual Color FrameBgActive { get; } = new(255, 0, 255, 255);
    public virtual Color FrameBgHovered { get; } = new(255, 0, 255, 255);
    public virtual Color Header { get; } = new(255, 0, 255, 255);
    public virtual Color HeaderActive { get; } = new(255, 0, 255, 255);
    public virtual Color HeaderHovered { get; } = new(255, 0, 255, 255);
    public Dictionary<ImGuiCol, Color> ImGuiColors { get; }
    public virtual Color MenuBarBg { get; } = new(255, 0, 255, 255);
    public virtual Color ModalWindowDimBg { get; } = new(255, 0, 255, 255);
    public virtual Color NavHighlight { get; } = new(255, 0, 255, 255);
    public virtual Color NavwindowingDimBg { get; } = new(255, 0, 255, 255);
    public virtual Color NavWindowingHighlight { get; } = new(255, 0, 255, 255);
    public virtual Color PlotHistogram { get; } = new(255, 0, 255, 255);
    public virtual Color PlotHistogramHovered { get; } = new(255, 0, 255, 255);
    public virtual Color PlotLines { get; } = new(255, 0, 255, 255);
    public virtual Color PlotLinesHovered { get; } = new(255, 0, 255, 255);
    public virtual Color ResizeGrip { get; } = new(255, 0, 255, 255);
    public virtual Color ResizeGripActive { get; } = new(255, 0, 255, 255);
    public virtual Color ResizeGripHovered { get; } = new(255, 0, 255, 255);
    public virtual Color ScrollbarBg { get; } = new(255, 0, 255, 255);
    public virtual Color ScrollbarGrab { get; } = new(255, 0, 255, 255);
    public virtual Color ScrollbarGrabActive { get; } = new(255, 0, 255, 255);
    public virtual Color ScrollbarGrabHovered { get; } = new(255, 0, 255, 255);
    public virtual Color Separator { get; } = new(255, 0, 255, 255);
    public virtual Color SeparatorActive { get; } = new(255, 0, 255, 255);
    public virtual Color SeparatorHovered { get; } = new(255, 0, 255, 255);
    public virtual Color SliderGrab { get; } = new(255, 0, 255, 255);
    public virtual Color SliderGrabActive { get; } = new(255, 0, 255, 255);
    public virtual Color Tab { get; } = new(255, 0, 255, 255);
    public virtual Color TabActive { get; } = new(255, 0, 255, 255);
    public virtual Color TabHovered { get; } = new(255, 0, 255, 255);
    public virtual Color TableBorderLight { get; } = new(255, 0, 255, 255);
    public virtual Color TableBorderStrong { get; } = new(255, 0, 255, 255);
    public virtual Color TableHeaderBg { get; } = new(255, 0, 255, 255);
    public virtual Color TableRowBg { get; } = new(255, 0, 255, 255);
    public virtual Color TableRowBgAlt { get; } = new(255, 0, 255, 255);
    public virtual Color TabUnfocused { get; } = new(255, 0, 255, 255);
    public virtual Color Text { get; } = new(255, 0, 255, 255);
    public virtual Color TextDisabled { get; } = new(255, 0, 255, 255);
    public virtual Color TextSelectedBg { get; } = new(255, 0, 255, 255);
    public virtual Color TitleBg { get; } = new(255, 0, 255, 255);
    public virtual Color TitleBgActive { get; } = new(255, 0, 255, 255);
    public virtual Color TitleBgCollapsed { get; } = new(255, 0, 255, 255);
    public virtual Color WindowBg { get; } = new(255, 0, 255, 255);
}