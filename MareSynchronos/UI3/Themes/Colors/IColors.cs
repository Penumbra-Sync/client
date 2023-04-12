using ImGuiNET;

namespace MareSynchronos.UI3.Themes.Colors;

public interface IColors
{
    Color Border { get; }
    Color BorderShadow { get; }
    Color Button { get; }
    Color ButtonActive { get; }
    Color ButtonHovered { get; }
    Color CheckMark { get; }
    Color ChildBg { get; }
    Color DockingEmptyBg { get; }
    Color DockingPreview { get; }
    Color DragDropTarget { get; }
    Color FrameBg { get; }
    Color FrameBgActive { get; }
    Color FrameBgHovered { get; }
    Color Header { get; }
    Color HeaderActive { get; }
    Color HeaderHovered { get; }
    Dictionary<ImGuiCol, Color> ImGuiColors { get; }
    Color MenuBarBg { get; }
    Color ModalWindowDimBg { get; }
    Color NavHighlight { get; }
    Color NavwindowingDimBg { get; }
    Color NavWindowingHighlight { get; }
    Color PlotHistogram { get; }
    Color PlotHistogramHovered { get; }
    Color PlotLines { get; }
    Color PlotLinesHovered { get; }
    Color ResizeGrip { get; }
    Color ResizeGripActive { get; }
    Color ResizeGripHovered { get; }
    Color ScrollbarBg { get; }
    Color ScrollbarGrab { get; }
    Color ScrollbarGrabActive { get; }
    Color ScrollbarGrabHovered { get; }
    Color Separator { get; }
    Color SeparatorActive { get; }
    Color SeparatorHovered { get; }
    Color SliderGrab { get; }
    Color SliderGrabActive { get; }
    Color Tab { get; }
    Color TabActive { get; }
    Color TabHovered { get; }
    Color TableBorderLight { get; }
    Color TableBorderStrong { get; }
    Color TableHeaderBg { get; }
    Color TableRowBg { get; }
    Color TableRowBgAlt { get; }
    Color TabUnfocused { get; }
    Color Text { get; }
    Color TextDisabled { get; }
    Color TextSelectedBg { get; }
    Color TitleBg { get; }
    Color TitleBgActive { get; }
    Color TitleBgCollapsed { get; }
    Color WindowBg { get; }
}