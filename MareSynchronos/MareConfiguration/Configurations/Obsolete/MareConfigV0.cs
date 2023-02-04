using MareSynchronos.MareConfiguration.Models;

namespace MareSynchronos.MareConfiguration.Configurations.Obsolete;

[Serializable]
[Obsolete("Deprecated, use MareConfig")]
public class MareConfigV0 : IMareConfiguration
{
    public int Version { get; set; } = 0;
    public Dictionary<string, ServerStorageV0> ServerStorage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> PlayerPersistentTransientCache { get; set; } = new(StringComparer.Ordinal);
    public bool AcceptedAgreement { get; set; } = false;
    public string CacheFolder { get; set; } = string.Empty;
    public double MaxLocalCacheInGiB { get; set; } = 20;
    public bool ReverseUserSort { get; set; } = false;
    public int TimeSpanBetweenScansInSeconds { get; set; } = 30;
    public bool FileScanPaused { get; set; } = false;
    public bool InitialScanComplete { get; set; } = false;
    public bool DisableOptionalPluginWarnings { get; set; } = false;
    public bool OpenGposeImportOnGposeStart { get; set; } = false;
    public bool ShowTransferWindow { get; set; } = true;
    public bool OpenPopupOnAdd { get; set; } = true;
    public string CurrentServer { get; set; } = string.Empty;
    public bool ShowOnlineNotifications { get; set; } = false;
    public bool ShowOnlineNotificationsOnlyForIndividualPairs { get; set; } = true;
    public bool ShowOnlineNotificationsOnlyForNamedPairs { get; set; } = false;
    public bool ShowCharacterNameInsteadOfNotesForVisible { get; set; } = false;
    public NotificationLocation InfoNotification { get; set; } = NotificationLocation.Toast;
    public NotificationLocation WarningNotification { get; set; } = NotificationLocation.Both;
    public NotificationLocation ErrorNotification { get; set; } = NotificationLocation.Both;

    public MareConfig ToV1()
    {
        return new MareConfig()
        {
            AcceptedAgreement = this.AcceptedAgreement,
            CacheFolder = this.CacheFolder,
            MaxLocalCacheInGiB = this.MaxLocalCacheInGiB,
            ReverseUserSort = this.ReverseUserSort,
            TimeSpanBetweenScansInSeconds = this.TimeSpanBetweenScansInSeconds,
            FileScanPaused = this.FileScanPaused,
            InitialScanComplete = this.InitialScanComplete,
            DisableOptionalPluginWarnings = this.DisableOptionalPluginWarnings,
            OpenGposeImportOnGposeStart = this.OpenGposeImportOnGposeStart,
            ShowTransferWindow = this.ShowTransferWindow,
            OpenPopupOnAdd = this.OpenPopupOnAdd,
            ShowOnlineNotifications = this.ShowOnlineNotifications,
            ShowOnlineNotificationsOnlyForIndividualPairs = this.ShowOnlineNotificationsOnlyForIndividualPairs,
            ShowCharacterNameInsteadOfNotesForVisible = this.ShowCharacterNameInsteadOfNotesForVisible,
            ShowOnlineNotificationsOnlyForNamedPairs = this.ShowOnlineNotificationsOnlyForNamedPairs,
            ErrorNotification = this.ErrorNotification,
            InfoNotification = this.InfoNotification,
            WarningNotification = this.WarningNotification,
        };
    }
}
