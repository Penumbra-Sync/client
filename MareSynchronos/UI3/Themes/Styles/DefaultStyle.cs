using System.Numerics;

namespace MareSynchronos.UI3.Themes;

public class DefaultStyle : StyleBase
{
    public override StyleValue<float> ChildBorderSize { get; } = new(0);
    public override StyleValue<float> FrameBorderSize { get; } = new(0);
    public override StyleValue<Vector2> FramePadding { get; } = new(new(3f, 3f));
    public override StyleValue<Vector2> ItemSpacing { get; } = new(new(3f, 3f));
    public override StyleValue<float> WindowBorderSize { get; } = new(0);
    public override StyleValue<Vector2> WindowPadding { get; } = new(new Vector2(0f, 0f));
    public override StyleValue<Vector2> WindowTitleAlign { get; } = new(new Vector2(0f, 0.5f));
}