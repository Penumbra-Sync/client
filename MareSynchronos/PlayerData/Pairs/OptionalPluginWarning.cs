namespace MareSynchronos.PlayerData.Pairs;

public record OptionalPluginWarning
{
    public bool ShownHeelsWarning { get; set; } = false;
    public bool ShownCustomizePlusWarning { get; set; } = false;
    public bool ShownHonorificWarning { get; set; } = false;
    public bool ShownMoodlesWarning { get; set; } = false;
    public bool ShowPetNicknamesWarning { get; set; } = false;
}