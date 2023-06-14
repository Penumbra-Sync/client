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
    private readonly DtrBarEntry _entry;
    private readonly ConfigurationServiceBase<MareConfig> _configService;
    private readonly PairManager _pairManager;
    private readonly ApiController _apiController;

    private CancellationTokenSource? _cancellationTokenSource;
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

    private void Update()
    {
        if (!_configService.Current.EnableDtrEntry)
        {
            Clear();
            return;
        }

        _entry.Shown = true;

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

    private void Clear()
    {
        _text = null;

        _entry.Shown = false;
        _entry.Text = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Update();
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
        Clear();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        CancellationToken token = _cancellationTokenSource.Token;
        Task.Run(() => RunAsync(token));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource?.CancelDispose();

        return Task.CompletedTask;
    }
}