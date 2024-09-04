namespace MareSynchronos.MareConfiguration.Configurations;

public class PlayerPerformanceConfig : IMareConfiguration
{
    public int Version { get; set; } = 1;
    public bool ShowPerformanceIndicator { get; set; } = true;
    public bool WarnOnExceedingThresholds { get; set; } = true;
    public bool WarnOnPreferredPermissionsExceedingThresholds { get; set; } = false;
    public int VRAMSizeWarningThresholdMiB { get; set; } = 375;
    public int TrisWarningThresholdThousands { get; set; } = 165;
    public bool AutoPausePlayersExceedingThresholds { get; set; } = false;
    public bool AutoPausePlayersWithPreferredPermissionsExceedingThresholds { get; set; } = false;
    public int VRAMSizeAutoPauseThresholdMiB { get; set; } = 550;
    public int TrisAutoPauseThresholdThousands { get; set; } = 250;
    public List<string> UIDsToIgnore { get; set; } = new();
}