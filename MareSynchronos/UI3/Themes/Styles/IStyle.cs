using ImGuiNET;
using System.Numerics;

namespace MareSynchronos.UI3.Themes;

public interface IStyle
{
    StyleValue<Vector2> ButtonTextAlign { get; }
    StyleValue<Vector2> CellPadding { get; }
    StyleValue<float> ChildBorderSize { get; }
    StyleValue<float> ChildRounding { get; }
    StyleValue<float> DisplaySafeAreaPadding { get; }
    StyleValue<float> FrameBorderSize { get; }
    StyleValue<Vector2> FramePadding { get; }
    StyleValue<float> FrameRounding { get; }
    StyleValue<float> GrabMinSize { get; }
    StyleValue<float> GrabRounding { get; }
    Dictionary<ImGuiStyleVar, StyleValue<float>> ImguiFloatStyles { get; }
    Dictionary<ImGuiStyleVar, StyleValue<Vector2>> ImguiVectorStyles { get; }
    StyleValue<float> IndentSpacing { get; }
    StyleValue<Vector2> ItemInnerSpacing { get; }
    StyleValue<Vector2> ItemSpacing { get; }
    StyleValue<float> LogSliderDeadzone { get; }
    StyleValue<float> PopupBorderSize { get; }
    StyleValue<float> PopupRounding { get; }
    StyleValue<float> ScrollbarRounding { get; }
    StyleValue<float> ScrollbarSize { get; }
    StyleValue<Vector2> SelectableTextAlign { get; }
    StyleValue<float> TabBorderSize { get; }
    StyleValue<float> TabRounding { get; }
    StyleValue<float> TouchExtraPadding { get; }
    StyleValue<float> WindowBorderSize { get; }
    StyleValue<float> WindowMenuButtonPosition { get; }
    StyleValue<Vector2> WindowPadding { get; }
    StyleValue<float> WindowRounding { get; }
    StyleValue<Vector2> WindowTitleAlign { get; }
}