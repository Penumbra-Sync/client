using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.UI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.MareConfiguration.Configurations;

[Serializable]
public class MareConfig : IMareConfiguration
{
    public bool AcceptedAgreement { get; set; } = false;
    public string CacheFolder { get; set; } = string.Empty;
    public bool DisableOptionalPluginWarnings { get; set; } = false;
    public bool EnableDtrEntry { get; set; } = false;
    public bool ShowUidInDtrTooltip { get; set; } = true;
    public bool PreferNoteInDtrTooltip { get; set; } = false;
    public bool UseColorsInDtr { get; set; } = true;
    public DtrEntry.Colors DtrColorsDefault { get; set; } = default;
    public DtrEntry.Colors DtrColorsNotConnected { get; set; } = new(Glow: 0x0428FFu);
    public DtrEntry.Colors DtrColorsPairsInRange { get; set; } = new(Glow: 0xFFBA47u);
    public bool EnableRightClickMenus { get; set; } = true;
    public NotificationLocation ErrorNotification { get; set; } = NotificationLocation.Both;
    public string ExportFolder { get; set; } = string.Empty;
    public bool FileScanPaused { get; set; } = false;
    public NotificationLocation InfoNotification { get; set; } = NotificationLocation.Toast;
    public bool InitialScanComplete { get; set; } = false;
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    public bool LogPerformance { get; set; } = false;
    public double MaxLocalCacheInGiB { get; set; } = 20;
    public bool OpenGposeImportOnGposeStart { get; set; } = false;
    public bool OpenPopupOnAdd { get; set; } = true;
    public int ParallelDownloads { get; set; } = 10;
    public int DownloadSpeedLimitInBytes { get; set; } = 0;
    public DownloadSpeeds DownloadSpeedType { get; set; } = DownloadSpeeds.MBps;
    public bool PreferNotesOverNamesForVisible { get; set; } = false;
    public float ProfileDelay { get; set; } = 1.5f;
    public bool ProfilePopoutRight { get; set; } = false;
    public bool ProfilesAllowNsfw { get; set; } = false;
    public bool ProfilesShow { get; set; } = true;
    public bool ShowSyncshellUsersInVisible { get; set; } = true;
    public bool ShowCharacterNameInsteadOfNotesForVisible { get; set; } = false;
    public bool ShowOfflineUsersSeparately { get; set; } = true;
    public bool ShowSyncshellOfflineUsersSeparately { get; set; } = true;
    public bool GroupUpSyncshells { get; set; } = true;
    public bool ShowOnlineNotifications { get; set; } = false;
    public bool ShowOnlineNotificationsOnlyForIndividualPairs { get; set; } = true;
    public bool ShowOnlineNotificationsOnlyForNamedPairs { get; set; } = false;
    public bool ShowTransferBars { get; set; } = true;
    public bool ShowTransferWindow { get; set; } = false;
    public bool ShowUploading { get; set; } = true;
    public bool ShowUploadingBigText { get; set; } = true;
    public bool ShowVisibleUsersSeparately { get; set; } = true;
    public int TimeSpanBetweenScansInSeconds { get; set; } = 30;
    public int TransferBarsHeight { get; set; } = 12;
    public bool TransferBarsShowText { get; set; } = true;
    public int TransferBarsWidth { get; set; } = 250;
    public bool UseAlternativeFileUpload { get; set; } = false;
    public bool UseCompactor { get; set; } = false;
    public bool DebugStopWhining { get; set; } = false;
    public bool AutoPopulateEmptyNotesFromCharaName { get; set; } = false;
    public int Version { get; set; } = 1;
    public NotificationLocation WarningNotification { get; set; } = NotificationLocation.Both;
    public bool UseFocusTarget { get; set; } = false;
}