using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.WebAPI;

namespace MareSynchronos.Services;

public sealed class CommandManagerService : IDisposable
{
    private const string _commandName = "/mare";

    private readonly ApiController _apiController;
    private readonly ICommandManager _commandManager;
    private readonly MareMediator _mediator;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly PeriodicFileScanner _periodicFileScanner;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiService _uiService;

    public CommandManagerService(ICommandManager commandManager, PerformanceCollectorService performanceCollectorService,
        UiService uiService, ServerConfigurationManager serverConfigurationManager, PeriodicFileScanner periodicFileScanner,
        ApiController apiController, MareMediator mediator)
    {
        _commandManager = commandManager;
        _performanceCollectorService = performanceCollectorService;
        _uiService = uiService;
        _serverConfigurationManager = serverConfigurationManager;
        _periodicFileScanner = periodicFileScanner;
        _apiController = apiController;
        _mediator = mediator;
        _commandManager.AddHandler(_commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Mare Synchronos UI"
        });
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(_commandName);
    }

    private void OnCommand(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);

        if (splitArgs == null || splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            _uiService.ToggleMainUi();
            return;
        }

        if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
        {
            if (_apiController.ServerState == WebAPI.SignalR.Utils.ServerState.Disconnecting)
            {
                _mediator.Publish(new NotificationMessage("Mare disconnecting", "Cannot use /toggle while Mare Synchronos is still disconnecting",
                    Dalamud.Interface.Internal.Notifications.NotificationType.Error));
            }

            if (_serverConfigurationManager.CurrentServer == null) return;
            var fullPause = splitArgs.Length > 1 ? splitArgs[1] switch
            {
                "on" => false,
                "off" => true,
                _ => !_serverConfigurationManager.CurrentServer.FullPause,
            } : !_serverConfigurationManager.CurrentServer.FullPause;

            if (fullPause != _serverConfigurationManager.CurrentServer.FullPause)
            {
                _serverConfigurationManager.CurrentServer.FullPause = fullPause;
                _serverConfigurationManager.Save();
                _ = _apiController.CreateConnections();
            }
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(GposeUi)));
        }
        else if (string.Equals(splitArgs[0], "rescan", StringComparison.OrdinalIgnoreCase))
        {
            _periodicFileScanner.InvokeScan(forced: true);
        }
        else if (string.Equals(splitArgs[0], "perf", StringComparison.OrdinalIgnoreCase))
        {
            if (splitArgs.Length > 1 && int.TryParse(splitArgs[1], out var limitBySeconds))
            {
                _performanceCollectorService.PrintPerformanceStats(limitBySeconds);
            }
            else
            {
                _performanceCollectorService.PrintPerformanceStats();
            }
        }
        else if (string.Equals(splitArgs[0], "medi", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.PrintSubscriberInfo();
        }
        else if (string.Equals(splitArgs[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new OpenDataAnalysisUiMessage());
        }
    }
}