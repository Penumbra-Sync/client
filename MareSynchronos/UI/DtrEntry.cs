using Dalamud.Game.Gui.Dtr;
using Dalamud.Plugin.Services;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public sealed class DtrEntry : IDisposable, IHostedService
{
    private readonly ApiController _apiController;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ILogger<DtrEntry> _logger;
    private readonly IDtrBar _dtrBar;
    private readonly ConfigurationServiceBase<MareConfig> _configService;
    private readonly MareMediator _mareMediator;
    private Lazy<DtrBarEntry> _entry;
    private readonly PairManager _pairManager;
    private Task? _runTask;
    private string? _text;

    public DtrEntry(ILogger<DtrEntry> logger, IDtrBar dtrBar, ConfigurationServiceBase<MareConfig> configService, MareMediator mareMediator, PairManager pairManager, ApiController apiController)
    {
        _logger = logger;
        _dtrBar = dtrBar;
        _entry = new(CreateEntry);
        _configService = configService;
        _mareMediator = mareMediator;
        _pairManager = pairManager;
        _apiController = apiController;
    }

    public void Dispose()
    {
        if (_entry.IsValueCreated)
        {
            _logger.LogDebug("Disposing DtrEntry");
            Clear();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runTask = Task.Run(RunAsync, _cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource.Cancel();
        try
        {
            await _runTask!.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore cancelled
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private DtrBarEntry CreateEntry()
    {
        var entry = _dtrBar.Get("Mare Synchronos");
        entry.OnClick = () => _mareMediator.Publish(new UiToggleMessage(typeof(CompactUi)));

        return entry;
    }

    private void Clear()
    {
        if (!_entry.IsValueCreated) return;
        _text = null;
        _logger.LogInformation("Clearing entry");

        _entry.Value.Shown = false;
        _entry.Value.Text = null;
        _entry.Value.Dispose();
        _entry = new(CreateEntry);
    }

    private async Task RunAsync()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            await Task.Delay(1000, _cancellationTokenSource.Token).ConfigureAwait(false);

            Update();
        }
    }

    private void Update()
    {
        if (!_configService.Current.EnableDtrEntry || !_configService.Current.HasValidSetup())
        {
            if (_entry.IsValueCreated && _entry.Value.Shown)
            {
                _logger.LogInformation("Disabling entry");

                Clear();
            }
            return;
        }

        if (!_entry.Value.Shown)
        {
            _logger.LogInformation("Showing entry");
            _entry.Value.Shown = true;
        }

        string text;
        string tooltip;
        if (_apiController.IsConnected)
        {
            text = $"\uE044 {_pairManager.GetVisibleUserCount()}";
            tooltip = $"Mare Synchronos: Connected{Environment.NewLine}----------{Environment.NewLine}{string.Join(Environment.NewLine, _pairManager.GetOnlineUserPairs().Where(x => x.IsVisible).Select(x => string.Format("{0} ({1})", x.PlayerName, x.UserData.AliasOrUID)))}";
        }
        else
        {
            text = "\uE044 \uE04C";
            tooltip = "Mare Synchronos: Not Connected";
        }
        if (!string.Equals(text, _text, StringComparison.Ordinal))
        {
            _text = text;
            _entry.Value.Text = text;
            _entry.Value.Tooltip = tooltip;
        }
    }
}