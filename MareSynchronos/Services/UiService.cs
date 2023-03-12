using Dalamud.Plugin;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using MareSynchronos.UI;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;
public sealed class UiService : IDisposable
{
    private readonly ILogger<UiService> _logger;
    private readonly DalamudPluginInterface _dalamudPluginInterface;
    private readonly MareConfigService _mareConfigService;
    private readonly WindowSystem _windowSystem;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareMediator _mareMediator;

    public UiService(ILogger<UiService> logger, DalamudPluginInterface dalamudPluginInterface,
        MareConfigService mareConfigService, WindowSystem windowSystem,
        IEnumerable<WindowMediatorSubscriberBase> windows,
        FileDialogManager fileDialogManager, MareMediator mareMediator)
    {
        _logger = logger;
        _logger.LogTrace("Creating {type}", GetType().Name);
        _dalamudPluginInterface = dalamudPluginInterface;
        _mareConfigService = mareConfigService;
        _windowSystem = windowSystem;
        _fileDialogManager = fileDialogManager;
        _mareMediator = mareMediator;

        _dalamudPluginInterface.UiBuilder.DisableGposeUiHide = true;
        _dalamudPluginInterface.UiBuilder.Draw += Draw;
        _dalamudPluginInterface.UiBuilder.OpenConfigUi += ToggleUi;

        foreach (var window in windows)
        {
            _windowSystem.AddWindow(window);
        }
    }

    public void ToggleUi()
    {
        if (_mareConfigService.Current.HasValidSetup())
            _mareMediator.Publish(new UiToggleMessage(typeof(CompactUi)));
        else
            _mareMediator.Publish(new UiToggleMessage(typeof(IntroUi)));
    }

    private void Draw()
    {
        _windowSystem.Draw();
        _fileDialogManager.Draw();
    }

    public void Dispose()
    {
        _logger.LogTrace("Disposing {type}", GetType().Name);

        _windowSystem.RemoveAllWindows();

        _dalamudPluginInterface.UiBuilder.Draw -= Draw;
        _dalamudPluginInterface.UiBuilder.OpenConfigUi -= ToggleUi;
    }
}
