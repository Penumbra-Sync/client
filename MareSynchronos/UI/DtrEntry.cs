using Dalamud.Game.Gui.Dtr;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public sealed class DtrEntry : DisposableMediatorSubscriberBase, IHostedService
{
    private readonly DtrBarEntry _entry;
    private readonly ConfigurationServiceBase<MareConfig> _configService;

    private bool _started;
    private bool _connected;
    private int _visiblePairs;
    private string? _text;

    public DtrEntry(ILogger<DtrEntry> logger, MareMediator mediator, DtrBar dtrBar, ConfigurationServiceBase<MareConfig> configService) : base(logger, mediator)
    {
        _entry = dtrBar.Get("Mare Synchronos");
        _configService = configService;

        _started = false;
        _connected = false;
        _visiblePairs = 0;
        _text = null;

        Update();

        mediator.Subscribe<PairHandlerVisibleMessage>(this, OnPairHandlerVisible);
        mediator.Subscribe<PairHandlerInvisibleMessage>(this, OnPairHandlerInvisible);
        mediator.Subscribe<ConnectedMessage>(this, OnConnected);
        mediator.Subscribe<DisconnectedMessage>(this, OnDisconnected);
        mediator.Subscribe<DtrEntryUpdateMessage>(this, (_) => Update());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _entry.Dispose();
    }

    private void Update()
    {
        _entry.Shown = _started && _configService.Current.EnableDtrEntry;
        string text;
        if (!_connected)
        {
            text = "\uE044 \uE04C";
        }
        else
        {
            text = $"\uE044 {_visiblePairs}";
        }
        if (!string.Equals(text, _text, StringComparison.Ordinal))
        {
            _text = text;
            _entry.Text = text;
        }
    }

    private void OnPairHandlerVisible(PairHandlerVisibleMessage _)
    {
        ++_visiblePairs;
        Update();
    }

    private void OnPairHandlerInvisible(PairHandlerInvisibleMessage _)
    {
        --_visiblePairs;
        Update();
    }

    private void OnConnected(ConnectedMessage _)
    {
        _connected = true;
        Update();
    }

    private void OnDisconnected(DisconnectedMessage _)
    {
        _connected = false;
        Update();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _started = true;
        Update();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;
        Update();

        return Task.CompletedTask;
    }
}