namespace MareSynchronos.MareConfiguration.Models;

[Serializable]
public record Authentication
{
    public string CharacterName { get; set; } = string.Empty;
    public uint WorldId { get; set; } = 0;
    public int SecretKeyIdx { get; set; } = -1;
    public string? UID { get; set; }
    public bool AutoLogin { get; set; } = true;
    public ulong? LastSeenCID { get; set; } = null;
}