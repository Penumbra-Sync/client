using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
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
    private readonly ConfigurationServiceBase<MareConfig> _configService;
    private readonly IDtrBar _dtrBar;
    private readonly Lazy<IDtrBarEntry> _entry;
    private readonly ILogger<DtrEntry> _logger;
    private readonly MareMediator _mareMediator;
    private readonly PairManager _pairManager;
    private Task? _runTask;
    private string? _text;
    private string? _tooltip;
    private ushort _color;

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
            _entry.Value.Remove();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting DtrEntry");
        _runTask = Task.Run(RunAsync, _cancellationTokenSource.Token);
        _logger.LogInformation("Started DtrEntry");
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

    private void Clear()
    {
        if (!_entry.IsValueCreated) return;
        _logger.LogInformation("Clearing entry");
        _text = null;
        _tooltip = null;
        _color = default;

        _entry.Value.Shown = false;
    }

    private IDtrBarEntry CreateEntry()
    {
        _logger.LogTrace("Creating new DtrBar entry");
        var entry = _dtrBar.Get("Mare Synchronos");
        entry.OnClick = () => _mareMediator.Publish(new UiToggleMessage(typeof(CompactUi)));

        return entry;
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
        ushort color;
        if (_apiController.IsConnected)
        {
            var pairCount = _pairManager.GetVisibleUserCount();
            text = $"\uE044 {pairCount}";
            if (pairCount > 0)
            {
                IEnumerable<string> visiblePairs;
                if (_configService.Current.ShowUidInDtrTooltip)
                {
                    visiblePairs = _pairManager.GetOnlineUserPairs()
                        .Where(x => x.IsVisible)
                        .Select(x => string.Format("{0} ({1})", _configService.Current.PreferNoteInDtrTooltip ? x.GetNote() ?? x.PlayerName : x.PlayerName, x.UserData.AliasOrUID));
                }
                else
                {
                    visiblePairs = _pairManager.GetOnlineUserPairs()
                        .Where(x => x.IsVisible)
                        .Select(x => string.Format("{0}", _configService.Current.PreferNoteInDtrTooltip ? x.GetNote() ?? x.PlayerName : x.PlayerName));
                }

                tooltip = $"Mare Synchronos: Connected{Environment.NewLine}----------{Environment.NewLine}{string.Join(Environment.NewLine, visiblePairs)}";
                color = _configService.Current.DtrColorPairsInRange;
            }
            else
            {
                tooltip = "Mare Synchronos: Connected";
                color = _configService.Current.DtrColorDefault;
            }
        }
        else
        {
            text = "\uE044 \uE04C";
            tooltip = "Mare Synchronos: Not Connected";
            color = _configService.Current.DtrColorNotConnected;
        }

        if (!_configService.Current.UseColorsInDtr)
            color = default;

        if (!string.Equals(text, _text, StringComparison.Ordinal) || !string.Equals(tooltip, _tooltip, StringComparison.Ordinal) || color != _color)
        {
            _text = text;
            _tooltip = tooltip;
            _color = color;
            _entry.Value.Text = color != default ? BuildColoredSeString(text, color) : text;
            _entry.Value.Tooltip = tooltip;
        }
    }

    private static SeString BuildColoredSeString(string text, ushort color)
        => new SeStringBuilder().AddUiGlow(text, color).Build();
}