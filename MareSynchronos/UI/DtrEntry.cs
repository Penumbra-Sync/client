using Dalamud.Game.Gui.Dtr;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;

namespace MareSynchronos.UI;

public sealed class DtrEntry : IDisposable, IHostedService
{
    private readonly ApiController _apiController;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ConfigurationServiceBase<MareConfig> _configService;
    private readonly DtrBarEntry _entry;
    private readonly PairManager _pairManager;
    private Task? _runTask;
    private string? _text;

    public DtrEntry(DtrBar dtrBar, ConfigurationServiceBase<MareConfig> configService, PairManager pairManager, ApiController apiController)
    {
        _entry = dtrBar.Get("Mare Synchronos");
        _configService = configService;
        _pairManager = pairManager;
        _apiController = apiController;

        Clear();
    }

    public void Dispose()
    {
        _entry.Dispose();
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
            _cancellationTokenSource.Dispose();
            Clear();
        }
    }

    private void Clear()
    {
        _text = null;

        _entry.Shown = false;
        _entry.Text = null;
    }

    private async Task RunAsync()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            Update();
            await Task.Delay(1000, _cancellationTokenSource.Token).ConfigureAwait(false);
        }
    }

    private void Update()
    {
        if (!_configService.Current.EnableDtrEntry)
        {
            if (_entry.Shown)
            {
                Clear();
            }
            return;
        }

        if (!_entry.Shown)
        {
            _entry.Shown = true;
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
            _entry.Text = text;
        }
    }
}