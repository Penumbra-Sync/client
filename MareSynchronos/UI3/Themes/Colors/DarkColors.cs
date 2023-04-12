namespace MareSynchronos.UI3.Themes.Colors;

public class DarkColors : ColorBase
{
    public override Color Border { get; } = new(110, 110, 128, 128);
    public override Color FrameBg { get; } = new(10, 10, 10, 0);
    public override Color FrameBgActive { get; } = new(20, 20, 20, 0);
    public override Color Header { get; } = new(200, 200, 200, 255);
    public override Color Text { get; } = new(240, 240, 240, 255);
    public override Color TitleBg { get; } = new(60, 60, 60, 255);
    public override Color TitleBgActive { get; } = new(50, 50, 50, 255);
}