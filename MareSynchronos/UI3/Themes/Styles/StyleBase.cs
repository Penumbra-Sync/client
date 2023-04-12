using ImGuiNET;
using System.Numerics;
using System.Reflection;

namespace MareSynchronos.UI3.Themes;

public class StyleBase : IStyle
{
    protected StyleBase()
    {
        ImguiFloatStyles = new();
        ImguiVectorStyles = new();
        foreach (var en in Enum.GetValues(typeof(ImGuiStyleVar)))
        {
            PropertyInfo? pi = GetType()?.GetProperty(Enum.GetName(typeof(ImGuiStyleVar), en) ?? string.Empty);
            if (pi == null) continue;
            var style = pi.GetValue(this);
            if (style == null) continue;
            var genericType = pi.PropertyType.GetGenericArguments().First();
            if (genericType == typeof(Vector2))
            {
                ImguiVectorStyles[(ImGuiStyleVar)en] = (StyleValue<Vector2>)style;
            }
            else
            {
                ImguiFloatStyles[(ImGuiStyleVar)en] = (StyleValue<float>)style;
            }
        }
    }

    public virtual StyleValue<Vector2> ButtonTextAlign { get; } = new(new(1f, 1f));
    public virtual StyleValue<Vector2> CellPadding { get; } = new(new(1f, 1f));
    public virtual StyleValue<float> ChildBorderSize { get; } = new(1f);
    public virtual StyleValue<float> ChildRounding { get; } = new(1f);
    public virtual StyleValue<float> DisplaySafeAreaPadding { get; } = new(1f);
    public virtual StyleValue<float> FrameBorderSize { get; } = new(1f);
    public virtual StyleValue<Vector2> FramePadding { get; } = new(new(1f, 1f));
    public virtual StyleValue<float> FrameRounding { get; } = new(1f);
    public virtual StyleValue<float> GrabMinSize { get; } = new(1f);
    public virtual StyleValue<float> GrabRounding { get; } = new(1f);
    public Dictionary<ImGuiStyleVar, StyleValue<float>> ImguiFloatStyles { get; }
    public Dictionary<ImGuiStyleVar, StyleValue<Vector2>> ImguiVectorStyles { get; }
    public virtual StyleValue<float> IndentSpacing { get; } = new(1f);
    public virtual StyleValue<Vector2> ItemInnerSpacing { get; } = new(new(1f, 1f));
    public virtual StyleValue<Vector2> ItemSpacing { get; } = new(new(1f, 1f));
    public virtual StyleValue<float> LogSliderDeadzone { get; } = new(1f);
    public virtual StyleValue<float> PopupBorderSize { get; } = new(1f);
    public virtual StyleValue<float> PopupRounding { get; } = new(1f);
    public virtual StyleValue<float> ScrollbarRounding { get; } = new(1f);
    public virtual StyleValue<float> ScrollbarSize { get; } = new(1f);
    public virtual StyleValue<Vector2> SelectableTextAlign { get; } = new(new(1f, 1f));
    public virtual StyleValue<float> TabBorderSize { get; } = new(1f);
    public virtual StyleValue<float> TabRounding { get; } = new(1f);
    public virtual StyleValue<float> TouchExtraPadding { get; } = new(1f);
    public virtual StyleValue<float> WindowBorderSize { get; } = new(1f);
    public virtual StyleValue<float> WindowMenuButtonPosition { get; } = new(1f);
    public virtual StyleValue<Vector2> WindowPadding { get; } = new(new(1f, 1f));
    public virtual StyleValue<float> WindowRounding { get; } = new(1f);
    public virtual StyleValue<Vector2> WindowTitleAlign { get; } = new(new(0f, .5f));
}