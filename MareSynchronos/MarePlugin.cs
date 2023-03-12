using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.ImGuiFileDialog;
using MareSynchronos.WebAPI;
using Dalamud.Interface.Windowing;
using MareSynchronos.UI;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MareSynchronos.Services;
using MareSynchronos.PlayerData.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace MareSynchronos;

#pragma warning disable S125 // Sections of code should not be commented out
/*
                                                                    (..,,...,,,,,+/,                ,,.....,,+           
                                                              ..,,+++/((###%%%&&%%#(+,,.,,,+++,,,,//,,#&@@@@%+.         
                                                          ...+//////////(/,,,,++,.,(###((//////////,..  .,#@@%/./       
                                                       ,..+/////////+///,.,. ,&@@@@,,/////////////+,..    ,(##+,.       
                                                    ,,.+//////////++++++..     ./#%#,+/////////////+,....,/((,..,       
                                                  +..////////////+++++++...  .../##(,,////////////////++,,,+/(((+,      
                                                +,.+//////////////+++++++,.,,,/(((+.,////////////////////////((((#/,,   
                                              /+.+//////////++++/++++++++++,,...,++///////////////////////////((((##,   
                                             /,.////////+++++++++++++++++++++////////+++//////++/+++++//////////((((#(+,
                                           /+.+////////+++++++++++++++++++++++++++++++++++++++++++++++++++++/////((((##+
                                          +,.///////////////+++++++++++++++++++++++++++++++++++++++++++++++++++///((((%/
                                         /.,/////////////////+++++++++++++++++++++++++++++++++++++++++++++++++++///+/(#+
                                        +,./////////////////+++++++++++++++++++++++++++++++++++++++++++++++,,+++++///((,
                                       ...////////++/++++++++++++++++++++++++,,++++++++++++++++++++++++++++++++++++//(,,
                                       ..//+,+///++++++++++++++++++,,,,+++,,,,,,,,,,,,++++++++,,+++++++++++++++++++//,,+
                                      ..,++,.++++++++++++++++++++++,,,,,,,,,,,,,,,,,,,++++++++,,,,,,,,,,++++++++++...   
                                      ..+++,.+++++++++++++++++++,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,++,..,.     
                                     ..,++++,,+++++++++++,+,,,,,,,,,,..,+++++++++,,,,,,.....................,//+,+      
                                 ....,+++++,.,+++++++++++,,,,,,,,.+///(((((((((((((///////////////////////(((+,,,       
                          .....,++++++++++..,+++++++++++,,.,,,.////////(((((((((((((((////////////////////+,,/          
                      .....,++++++++++++,..,,+++++++++,,.,../////////////////((((((((((//////////////////,,+            
                   ...,,+++++++++++++,.,,.,,,+++++++++,.,/////////////////(((//++++++++++++++//+++++++++/,,             
                ....,++++++++++++++,.,++.,++++++++++++.,+////////////////////+++++++++++++++++++++++++///,,..           
              ...,++++++++++++++++..+++..+++++++++++++.,//////////////////////////++++++++++++///////++++......         
            ...++++++++++++++++++..++++.,++,++++++++++.+///////////////////////////////////////////++++++..,,,..        
          ...+++++++++++++++++++..+++++..,+,,+++++++++.+//////////////////////////////////////////+++++++...,,,,..      
         ..++++++++++++++++++++..++++++..,+,,+++++++++.+//////////////////////////////////////++++++++++,....,,,,..     
       ...+++//(//////+++++++++..++++++,.,+++++++++++++,..,....,,,+++///////////////////////++++++++++++..,,,,,,,,...   
      ..,++/(((((//////+++++++,.,++++++,,.,,,+++++++++++++++++++++++,.++////////////////////+++++++++++.....,,,,,,,...  
     ..,//#(((((///////+++++++..++++++++++,...,++,++++++++++++++++,...+++/////////////////////+,,,+++...  ....,,,,,,... 
   ...+//(((((//////////++++++..+++++++++++++++,......,,,,++++++,,,..+++////////////////////////+,....     ...,,,,,,,...
   ..,//((((////////////++++++..++++++/+++++++++++++,,...,,........,+/+//////////////////////((((/+,..     ....,.,,,,.. 
  ...+/////////////////////+++..++++++/+///+++++++++++++++++++++///+/+////////////////////////(((((/+...   .......,,... 
  ..++////+++//////////////++++.+++++++++///////++++++++////////////////////////////////////+++/(((((/+..    .....,,... 
  .,++++++++///////////////++++..++++//////////////////////////////////////////////////////++++++/((((++..    ........  
  .+++++++++////////////////++++,.+++/////////////////////////////////////////////////////+++++++++/((/++..             
 .,++++++++//////////////////++++,.+++//////////////////////////////////////////////////+++++++++++++//+++..            
 .++++++++//////////////////////+/,.,+++////((((////////////////////////////////////////++++++++++++++++++...           
 .++++++++///////////////////////+++..++++//((((((((///////////////////////////////////++++++++++++++++++++ .           
 .++++++///////////////////////////++,.,+++++/(((((((((/////////////////////////////+++++++++++++++++++++++,..          
 .++++++////////////////////////////+++,.,+++++++/((((((((//////////////////////////++++++++++++++++++++++++..          
 .+++++++///////////////////++////////++++,.,+++++++++///////////+////////////////+++++++++++++++++++++++++,..          
 ..++++++++++//////////////////////+++++++..+...,+++++++++++++++/++++++++++++++++++++++++++++++++++++++++++,...         
  ..++++++++++++///////////////+++++++,...,,,,,.,....,,,,+++++++++++++++++++++++++++++++++++++++++++++++,,,,...         
  ...++++++++++++++++++++++++++,,,,...,,,,,,,,,..,,++,,,.,,,,,,,,,,,,,,,,,,+++++++++++++++++++++++++,,,,,,,,..          
   ...+++++++++++++++,,,,,,,,....,,,,,,,,,,,,,,,..,,++++++,,,,,,,,,,,,,,,,+++++++++++++++++++++++++,,,,,,,,,..          
     ...++++++++++++,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,...,++++++++++++++++++++++++++++++++++++++++++++,,,,,,,,,,...         
       ,....,++++++++++++++,,,+++++++,,,,,,,,,,,,,,,,,.,++++++++++++++++++++++++++++++++++++++++++++,,,,,,,,..          

*/
#pragma warning restore S125 // Sections of code should not be commented out

public class MarePlugin : MediatorSubscriberBase, IHostedService
{
    private readonly DalamudPluginInterface _dalamudPluginInterface;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly CommandManager _commandManager;
    private readonly MareConfigService _mareConfigService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly ApiController _apiController;
    private readonly PeriodicFileScanner _periodicFileScanner;
    private readonly IServiceProvider _serviceProvider;
    private const string _commandName = "/mare";
    private IServiceScope? _runtimeServiceScope;

    public MarePlugin(ILogger<MarePlugin> logger, DalamudPluginInterface dalamudPluginInterface, PerformanceCollectorService performanceCollectorService,
        CommandManager commandManager, MareConfigService mareConfigService, ServerConfigurationManager serverConfigurationManager,
        ApiController apiController, PeriodicFileScanner periodicFileScanner,
        IServiceProvider serviceProvider, MareMediator mediator) : base(logger, mediator)
    {
        _dalamudPluginInterface = dalamudPluginInterface;
        _performanceCollectorService = performanceCollectorService;
        _commandManager = commandManager;
        _mareConfigService = mareConfigService;
        _serverConfigurationManager = serverConfigurationManager;
        _apiController = apiController;
        _periodicFileScanner = periodicFileScanner;
        _serviceProvider = serviceProvider;
    }

    private void DalamudUtilOnLogIn()
    {
        Logger?.LogDebug("Client login");

        _dalamudPluginInterface.UiBuilder.Draw += Draw;
        _dalamudPluginInterface.UiBuilder.OpenConfigUi += OpenUi;
        _commandManager.AddHandler(_commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Mare Synchronos UI",
        });

        if (!_mareConfigService.Current.HasValidSetup()
            || !_serverConfigurationManager.HasValidConfig())
        {
            Mediator.Publish(new SwitchToIntroUiMessage());
            return;
        }

        _periodicFileScanner.StartScan();
        Task.Run(WaitForPlayerAndLaunchCharacterManager);
    }

    private void DalamudUtilOnLogOut()
    {
        Logger?.LogDebug("Client logout");

        _runtimeServiceScope?.Dispose();
        _dalamudPluginInterface.UiBuilder.Draw -= Draw;
        _dalamudPluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
        _commandManager.RemoveHandler(_commandName);
    }

    private async Task WaitForPlayerAndLaunchCharacterManager()
    {
        var dalamudUtil = _serviceProvider.GetRequiredService<DalamudUtil>();
        while (!dalamudUtil.IsPlayerPresent)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        try
        {
            Logger?.LogDebug("Launching Managers");

            _runtimeServiceScope?.Dispose();
            _runtimeServiceScope = _serviceProvider.CreateScope();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<CacheCreationService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<TransientResourceManager>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<OnlinePlayerManager>();
        }
        catch (Exception ex)
        {
            Logger?.LogCritical(ex, "Error during launch of managers");
        }
    }

    private void Draw()
    {
        _serviceProvider?.GetService<WindowSystem>()?.Draw();
        _serviceProvider?.GetService<FileDialogManager>()?.Draw();
    }

    private void OnCommand(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);

        if (splitArgs == null || splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            OpenUi();
            return;
        }

        if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
        {
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
            Mediator.Publish(new UiToggleMessage(typeof(GposeUi)));
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
            Mediator.PrintSubscriberInfo();
        }
    }

    private void OpenUi()
    {
        if (_mareConfigService.Current.HasValidSetup())
            Mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
        else
            Mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version!;
        Logger.LogInformation("Launching {name} {major}.{minor}.{build}", "Mare Synchronos", version.Major, version.Minor, version.Build);

        _serviceProvider.GetRequiredService<Dalamud.Localization>().SetupWithLangCode("en");

        _dalamudPluginInterface.UiBuilder.DisableGposeUiHide = true;

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => Task.Run(WaitForPlayerAndLaunchCharacterManager));
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => DalamudUtilOnLogIn());
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DalamudUtilOnLogOut());

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        UnsubscribeAll();

        DalamudUtilOnLogOut();

        Logger.LogDebug("Shut down");

        return Task.CompletedTask;
    }
}
