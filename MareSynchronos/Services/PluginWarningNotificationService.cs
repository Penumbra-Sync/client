using Dalamud.Interface.Internal.Notifications;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;

namespace MareSynchronos.PlayerData.Pairs;

public class PluginWarningNotificationService
{
    private readonly Dictionary<UserData, OptionalPluginWarning> _cachedOptionalPluginWarnings = new(UserDataComparer.Instance);
    private readonly IpcManager _ipcManager;
    private readonly MareConfigService _mareConfigService;
    private readonly MareMediator _mediator;

    public PluginWarningNotificationService(MareConfigService mareConfigService, IpcManager ipcManager, MareMediator mediator)
    {
        _mareConfigService = mareConfigService;
        _ipcManager = ipcManager;
        _mediator = mediator;
    }

    public void NotifyForMissingPlugins(UserData user, string playerName, HashSet<PlayerChanges> changes)
    {
        if (!_cachedOptionalPluginWarnings.TryGetValue(user, out var warning))
        {
            _cachedOptionalPluginWarnings[user] = warning = new()
            {
                ShownCustomizePlusWarning = _mareConfigService.Current.DisableOptionalPluginWarnings,
                ShownHeelsWarning = _mareConfigService.Current.DisableOptionalPluginWarnings,
                ShownPalettePlusWarning = _mareConfigService.Current.DisableOptionalPluginWarnings,
                ShownHonorificWarning = _mareConfigService.Current.DisableOptionalPluginWarnings,
            };
        }

        List<string> missingPluginsForData = new();
        if (changes.Contains(PlayerChanges.Heels) && !warning.ShownHeelsWarning && !_ipcManager.CheckHeelsApi())
        {
            missingPluginsForData.Add("Heels");
            warning.ShownHeelsWarning = true;
        }
        if (changes.Contains(PlayerChanges.Customize) && !warning.ShownCustomizePlusWarning && !_ipcManager.CheckCustomizePlusApi())
        {
            missingPluginsForData.Add("Customize+");
            warning.ShownCustomizePlusWarning = true;
        }

        if (changes.Contains(PlayerChanges.Palette) && !warning.ShownPalettePlusWarning && !_ipcManager.CheckPalettePlusApi())
        {
            missingPluginsForData.Add("Palette+");
            warning.ShownPalettePlusWarning = true;
        }

        if (changes.Contains(PlayerChanges.Honorific) && !warning.ShownHonorificWarning && !_ipcManager.CheckHonorificApi())
        {
            missingPluginsForData.Add("Honorific");
            warning.ShownHonorificWarning = true;
        }

        if (missingPluginsForData.Any())
        {
            _mediator.Publish(new NotificationMessage("Missing plugins for " + playerName,
                $"Received data for {playerName} that contained information for plugins you have not installed. Install {string.Join(", ", missingPluginsForData)} to experience their character fully.",
                NotificationType.Warning, 10000));
        }
    }
}