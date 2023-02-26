using Dalamud.Plugin;
using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class NotesConfigService : ConfigurationServiceBase<UidNotesConfig>
{
    public const string ConfigName = "notes.json";
    protected override string ConfigurationName => ConfigName;
    public NotesConfigService(DalamudPluginInterface pluginInterface) : base(pluginInterface) { }
}