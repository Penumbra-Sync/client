﻿using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Interface.ImGuiFileDialog;
using MareSynchronos.Managers;
using MareSynchronos.WebAPI;
using Dalamud.Interface.Windowing;
using MareSynchronos.UI;
using MareSynchronos.Utils;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MareSynchronos;

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

public class MarePlugin : MediatorSubscriberBase, IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private const string _commandName = "/mare";
    private IServiceScope? _runtimeServiceScope;

    public MarePlugin(ServiceProvider serviceProvider, MareMediator mediator) : base(mediator)
    {
        _serviceProvider = serviceProvider;

        _serviceProvider.GetRequiredService<ConfigurationMigrator>().Migrate();

        mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => Task.Run(WaitForPlayerAndLaunchCharacterManager));
        mediator.Subscribe<DalamudLoginMessage>(this, (_) => DalamudUtilOnLogIn());
        mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DalamudUtilOnLogOut());

        serviceProvider.GetRequiredService<SettingsUi>();
        serviceProvider.GetRequiredService<CompactUi>();
        serviceProvider.GetRequiredService<GposeUi>();
        serviceProvider.GetRequiredService<IntroUi>();
        serviceProvider.GetRequiredService<DownloadUi>();
        serviceProvider.GetRequiredService<NotificationService>();
    }

    public override void Dispose()
    {
        base.Dispose();

        _serviceProvider.GetRequiredService<CommandManager>().RemoveHandler(_commandName);

        _runtimeServiceScope?.Dispose();
        _serviceProvider.Dispose();

        Logger.Debug("Shut down");
    }

    private void DalamudUtilOnLogIn()
    {
        Logger.Debug("Client login");

        var pi = _serviceProvider.GetRequiredService<DalamudPluginInterface>();
        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenConfigUi += OpenUi;
        _serviceProvider.GetRequiredService<CommandManager>().AddHandler(_commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Mare Synchronos UI",
        });

        if (!_serviceProvider.GetRequiredService<MareConfigService>().Current.HasValidSetup()
            || !_serviceProvider.GetRequiredService<ServerConfigurationManager>().HasValidConfig())
        {
            _serviceProvider.GetRequiredService<MareMediator>().Publish(new SwitchToIntroUiMessage());
            return;
        }

        _serviceProvider.GetRequiredService<PeriodicFileScanner>().StartScan();
        Task.Run(WaitForPlayerAndLaunchCharacterManager);
    }

    private void DalamudUtilOnLogOut()
    {
        Logger.Debug("Client logout");
        _runtimeServiceScope?.Dispose();
        var pi = _serviceProvider.GetRequiredService<DalamudPluginInterface>();
        pi.UiBuilder.Draw -= Draw;
        pi.UiBuilder.OpenConfigUi -= OpenUi;
        _serviceProvider.GetRequiredService<CommandManager>().RemoveHandler(_commandName);
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
            Logger.Debug("Launching Managers");

            _runtimeServiceScope?.Dispose();
            _runtimeServiceScope = _serviceProvider.CreateScope();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<CacheCreationService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<TransientResourceManager>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<OnlinePlayerManager>();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex.Message);
        }
    }

    private void Draw()
    {
        _serviceProvider.GetRequiredService<WindowSystem>().Draw();
        _serviceProvider.GetRequiredService<FileDialogManager>().Draw();
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
            var serverConfigurationManager = _serviceProvider.GetRequiredService<ServerConfigurationManager>();
            if (serverConfigurationManager.CurrentServer == null) return;
            var fullPause = splitArgs.Length > 1 ? splitArgs[1] switch
            {
                "on" => false,
                "off" => true,
                _ => !serverConfigurationManager.CurrentServer.FullPause,
            } : !serverConfigurationManager.CurrentServer.FullPause;

            if (fullPause != serverConfigurationManager.CurrentServer.FullPause)
            {
                serverConfigurationManager.CurrentServer.FullPause = fullPause;
                serverConfigurationManager.Save();
                _ = _serviceProvider.GetRequiredService<ApiController>().CreateConnections();
            }
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _serviceProvider.GetRequiredService<GposeUi>().Toggle();
        }
        else if (string.Equals(splitArgs[0], "rescan", StringComparison.OrdinalIgnoreCase))
        {
            _serviceProvider.GetRequiredService<PeriodicFileScanner>().InvokeScan(forced: true);
        }
    }

    private void OpenUi()
    {

        if (_serviceProvider.GetRequiredService<MareConfigService>().Current.HasValidSetup())
            _serviceProvider.GetRequiredService<CompactUi>().Toggle();
        else
            _serviceProvider.GetRequiredService<IntroUi>().Toggle();
    }
}
