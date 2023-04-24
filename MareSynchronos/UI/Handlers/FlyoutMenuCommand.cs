namespace MareSynchronos.UI.Handlers;

public class FlyoutMenuCommand
{
    public string CommandId { get; } = Guid.NewGuid().ToString("n");
    public Dictionary<string, List<ButtonCommand>> MenuEntries { get; private set; } = new(StringComparer.Ordinal);
    public float Scale { get; private set; } = 1f;

    public FlyoutMenuCommand WithCommand(string category, ButtonCommand command)
    {
        if (!MenuEntries.TryGetValue(category, out var entries))
        {
            MenuEntries[category] = entries = new();
        }

        entries.Add(command);
        return this;
    }

    public FlyoutMenuCommand WithScale(float scale)
    {
        Scale = scale;
        return this;
    }
}
