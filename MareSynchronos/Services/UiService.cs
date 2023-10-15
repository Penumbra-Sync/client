using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class UiService : DisposableMediatorSubscriberBase
{
    private readonly List<WindowMediatorSubscriberBase> _createdWindows = [];
    private readonly DalamudPluginInterface _dalamudPluginInterface;
    private readonly FileDialogManager _fileDialogManager;
    private readonly ILogger<UiService> _logger;
    private readonly MareConfigService _mareConfigService;
    private readonly WindowSystem _windowSystem;

    public UiService(ILogger<UiService> logger, DalamudPluginInterface dalamudPluginInterface,
        MareConfigService mareConfigService, WindowSystem windowSystem,
        IEnumerable<WindowMediatorSubscriberBase> windows, Func<Pair, StandaloneProfileUi> standaloneProfileUiFactory,
        FileDialogManager fileDialogManager, MareMediator mareMediator) : base(logger, mareMediator)
    {
        _logger = logger;
        _logger.LogTrace("Creating {type}", GetType().Name);
        _dalamudPluginInterface = dalamudPluginInterface;
        _mareConfigService = mareConfigService;
        _windowSystem = windowSystem;
        _fileDialogManager = fileDialogManager;

        _dalamudPluginInterface.UiBuilder.DisableGposeUiHide = true;
        _dalamudPluginInterface.UiBuilder.Draw += Draw;
        _dalamudPluginInterface.UiBuilder.OpenConfigUi += ToggleUi;
        _dalamudPluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        foreach (var window in windows)
        {
            _windowSystem.AddWindow(window);
        }

        Mediator.Subscribe<ProfileOpenStandaloneMessage>(this, (msg) =>
        {
            if (!_createdWindows.Exists(p => p is StandaloneProfileUi ui
                && string.Equals(ui.Pair.UserData.AliasOrUID, msg.Pair.UserData.AliasOrUID, StringComparison.Ordinal)))
            {
                var window = standaloneProfileUiFactory(msg.Pair);
                _createdWindows.Add(window);
                _windowSystem.AddWindow(window);
            }
        });

        Mediator.Subscribe<RemoveWindowMessage>(this, (msg) =>
        {
            _windowSystem.RemoveWindow(msg.Window);
            _createdWindows.Remove(msg.Window);
            msg.Window.Dispose();
        });
    }

    public void ToggleMainUi()
    {
        if (_mareConfigService.Current.HasValidSetup())
            Mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
        else
            Mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
    }

    public void ToggleUi()
    {
        if (_mareConfigService.Current.HasValidSetup())
            Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        else
            Mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _logger.LogTrace("Disposing {type}", GetType().Name);

        _windowSystem.RemoveAllWindows();

        foreach (var window in _createdWindows)
        {
            window.Dispose();
        }

        _dalamudPluginInterface.UiBuilder.Draw -= Draw;
        _dalamudPluginInterface.UiBuilder.OpenConfigUi -= ToggleUi;
    }

    private void Draw()
    {
        _windowSystem.Draw();
        _fileDialogManager.Draw();
    }
}