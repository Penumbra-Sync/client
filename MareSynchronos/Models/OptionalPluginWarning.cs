namespace MareSynchronos.Models;

public record OptionalPluginWarning
{
    public bool ShownHeelsWarning { get; set; } = false;
    public bool ShownCustomizePlusWarning { get; set; } = false;
}
