using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public sealed class DtrEntry : DisposableMediatorSubscriberBase, IHostedService
{
    private readonly DtrBarEntry _entry;

    private bool _started;
    private bool _loggedIn;
    private bool _onIntroUi;
    private bool _connected;
    private bool _reconnecting;
    private bool _switchingZone;
    private int _visiblePairs;
    private string? _text;

    public DtrEntry(ILogger<DtrEntry> logger, MareMediator mediator, DtrBar dtrBar) : base(logger, mediator)
    {
        _entry = dtrBar.Get("Mare Synchronos");

        _started = false;
        _loggedIn = false;
        _onIntroUi = false;
        _connected = false;
        _reconnecting = false;
        _switchingZone = false;
        _visiblePairs = 0;
        _text = null;

        Update();

        mediator.Subscribe<DalamudLoginMessage>(this, OnDalamudLogin);
        mediator.Subscribe<DalamudLogoutMessage>(this, OnDalamudLogout);
        mediator.Subscribe<ZoneSwitchStartMessage>(this, OnZoneSwitchStart);
        mediator.Subscribe<ZoneSwitchEndMessage>(this, OnZoneSwitchEnd);
        mediator.Subscribe<PairHandlerVisibleMessage>(this, OnPairHandlerVisible);
        mediator.Subscribe<PairHandlerInvisibleMessage>(this, OnPairHandlerInvisible);
        mediator.Subscribe<PairHandlerDisposingVisibleMessage>(this, OnPairHandlerDisposingVisible);
        mediator.Subscribe<HubReconnectingMessage>(this, OnHubReconnecting);
        mediator.Subscribe<HubReconnectedMessage>(this, OnHubReconnected);
        mediator.Subscribe<ConnectedMessage>(this, OnConnected);
        mediator.Subscribe<DisconnectedMessage>(this, OnDisconnected);
        mediator.Subscribe<SwitchToMainUiMessage>(this, OnSwitchToMainUi);
        mediator.Subscribe<SwitchToIntroUiMessage>(this, OnSwitchToIntroUi);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _entry.Dispose();
    }

    private void Update()
    {
        _entry.Shown = _started && _loggedIn && !_onIntroUi;
        string text;
        if (!_connected)
        {
            text = "\uE044 \uE04C";
        }
        else if (_reconnecting || _switchingZone)
        {
            text = "\uE044 \uE05A";
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

    private void OnDalamudLogin(DalamudLoginMessage _)
    {
        _loggedIn = true;
        Update();
    }

    private void OnDalamudLogout(DalamudLogoutMessage _)
    {
        _loggedIn = false;
        Update();
    }

    private void OnZoneSwitchStart(ZoneSwitchStartMessage _)
    {
        _switchingZone = true;
        _visiblePairs = 0;
        Update();
    }

    private void OnZoneSwitchEnd(ZoneSwitchEndMessage _)
    {
        _switchingZone = false;
        Update();
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

    private void OnPairHandlerDisposingVisible(PairHandlerDisposingVisibleMessage _)
    {
        --_visiblePairs;
        Update();
    }

    private void OnHubReconnecting(HubReconnectingMessage _)
    {
        _reconnecting = true;
        Update();
    }

    private void OnHubReconnected(HubReconnectedMessage _)
    {
        _reconnecting = false;
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
        _visiblePairs = 0;
        Update();
    }

    private void OnSwitchToMainUi(SwitchToMainUiMessage _)
    {
        _onIntroUi = false;
        Update();
    }

    private void OnSwitchToIntroUi(SwitchToIntroUiMessage _)
    {
        _onIntroUi = true;
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
