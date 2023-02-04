using MareSynchronos.MareConfiguration.Models;

namespace MareSynchronos.MareConfiguration.Configurations;

public class UidNotesConfig : IMareConfiguration
{
    public int Version { get; set; } = 0;
    public Dictionary<string, ServerNotesStorage> ServerNotes = new(StringComparer.Ordinal);
}
