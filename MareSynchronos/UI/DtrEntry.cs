using Dalamud.Game.Gui.Dtr;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public sealed class DtrEntry : IDisposable, IHostedService
{
    private readonly ApiController _apiController;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ILogger<DtrEntry> _logger;
    private readonly ConfigurationServiceBase<MareConfig> _configService;
    private readonly Lazy<DtrBarEntry> _entry;
    private readonly PairManager _pairManager;
    private Task? _runTask;
    private string? _text;

    public DtrEntry(ILogger<DtrEntry> logger, DtrBar dtrBar, ConfigurationServiceBase<MareConfig> configService, PairManager pairManager, ApiController apiController)
    {
        _entry = new(() => dtrBar.Get("Mare Synchronos"));
        _logger = logger;
        _configService = configService;
        _pairManager = pairManager;
        _apiController = apiController;
    }

    public void Dispose()
    {
        _entry.Value.Dispose();
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
        catch (OperationCanceledException) { }
        finally
        {
            _logger.LogDebug("Disposing DtrEntry");
            if (_entry.IsValueCreated)
            {
                Clear();

                _entry.Value.Remove();
                _entry.Value.Dispose();
            }

            _cancellationTokenSource.Dispose();
        }
    }

    private void Clear()
    {
        _text = null;

        _entry.Value.Shown = false;
        _entry.Value.Text = null;
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
        if (!_configService.Current.EnableDtrEntry)
        {
            if (_entry.Value.Shown)
            {
                Clear();
            }
            return;
        }

        if (!_entry.Value.Shown)
        {
            _entry.Value.Shown = true;
        }

        string text;
        if (_apiController.IsConnected)
        {
            text = $"\uE044 {_pairManager.GetVisibleUserCount()}";
        }
        else
        {
            text = "\uE044 \uE04C";
        }
        if (!string.Equals(text, _text, StringComparison.Ordinal))
        {
            _text = text;
            _entry.Value.Text = text;
        }
    }
}