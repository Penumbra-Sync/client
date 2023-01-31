using Dalamud.Game.Gui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Internal.Notifications;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Mediator;
using MareSynchronos.Utils;

namespace MareSynchronos.Managers;
public class NotificationService
{
    private readonly UiBuilder _uiBuilder;
    private readonly ChatGui _chatGui;
    private readonly ConfigurationService _configurationService;

    public NotificationService(MareMediator mediator, UiBuilder uiBuilder, ChatGui chatGui, ConfigurationService configurationService)
    {
        _uiBuilder = uiBuilder;
        _chatGui = chatGui;
        _configurationService = configurationService;
        mediator.Subscribe<NotificationMessage>(this, (msg) => ShowNotification((NotificationMessage)msg));
    }

    private void ShowNotification(NotificationMessage msg)
    {
        Logger.Info(msg.ToString());

        switch (msg.Type)
        {
            case NotificationType.Info:
            case NotificationType.Success:
            case NotificationType.None:
                ShowNotificationLocationBased(msg, _configurationService.Current.InfoNotification);
                break;
            case NotificationType.Warning:
                ShowNotificationLocationBased(msg, _configurationService.Current.WarningNotification);
                break;
            case NotificationType.Error:
                ShowNotificationLocationBased(msg, _configurationService.Current.ErrorNotification);
                break;
        }
    }

    private void ShowNotificationLocationBased(NotificationMessage msg, NotificationLocation location)
    {
        switch (location)
        {
            case NotificationLocation.Toast:
                ShowToast(msg);
                break;
            case NotificationLocation.Chat:
                ShowChat(msg);
                break;
            case NotificationLocation.Both:
                ShowToast(msg);
                ShowChat(msg);
                break;
            case NotificationLocation.Nowhere:
                break;
        }
    }

    private void ShowToast(NotificationMessage msg)
    {
        _uiBuilder.AddNotification(msg.Message ?? string.Empty, "[Mare Synchronos] " + msg.Title, msg.Type, msg.TimeShownOnScreen);
    }

    private void ShowChat(NotificationMessage msg)
    {
        switch (msg.Type)
        {
            case NotificationType.Info:
            case NotificationType.Success:
            case NotificationType.None:
                PrintInfoChat(msg.Message);
                break;
            case NotificationType.Warning:
                PrintWarnChat(msg.Message);
                break;
            case NotificationType.Error:
                PrintErrorChat(msg.Message);
                break;
        }
    }

    private void PrintInfoChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[Mare Synchronos] Info: ").AddItalics(message ?? string.Empty);
        _chatGui.Print(se.BuiltString);
    }

    private void PrintWarnChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[Mare Synchronos] ").AddUiForeground("Warning: " + (message ?? string.Empty), 31).AddUiForegroundOff();
        _chatGui.Print(se.BuiltString);
    }

    private void PrintErrorChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[Mare Synchronos] ").AddUiForeground("Error: ", 534).AddItalicsOn().AddUiForeground(message ?? string.Empty, 534).AddUiForegroundOff().AddItalicsOff();
        _chatGui.Print(se.BuiltString);
    }
}
