using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.MareConfiguration;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class DownloadUi : Window, IDisposable
{
    private readonly ILogger<DownloadUi> _logger;
    private readonly WindowSystem _windowSystem;
    private readonly MareConfigService _configService;
    private readonly ApiController _apiController;
    private readonly UiShared _uiShared;
    private bool _wasOpen = false;

    public void Dispose()
    {
        _logger.LogTrace($"Disposing {GetType()}");
        _windowSystem.RemoveWindow(this);
    }

    public DownloadUi(ILogger<DownloadUi> logger, WindowSystem windowSystem, MareConfigService configService, ApiController apiController, UiShared uiShared) : base("Mare Synchronos Downloads")
    {
        _logger = logger;
        _logger.LogTrace("Creating " + nameof(DownloadUi));
        _windowSystem = windowSystem;
        _configService = configService;
        _apiController = apiController;
        _uiShared = uiShared;

        SizeConstraints = new WindowSizeConstraints()
        {
            MaximumSize = new Vector2(300, 90),
            MinimumSize = new Vector2(300, 90),
        };

        Flags |= ImGuiWindowFlags.NoMove;
        Flags |= ImGuiWindowFlags.NoBackground;
        Flags |= ImGuiWindowFlags.NoInputs;
        Flags |= ImGuiWindowFlags.NoNavFocus;
        Flags |= ImGuiWindowFlags.NoResize;
        Flags |= ImGuiWindowFlags.NoScrollbar;
        Flags |= ImGuiWindowFlags.NoTitleBar;
        Flags |= ImGuiWindowFlags.NoDecoration;

        ForceMainWindow = true;

        windowSystem.AddWindow(this);
        IsOpen = true;
    }

    public override void PreDraw()
    {
        if (_uiShared.IsInGpose)
        {
            _wasOpen = IsOpen;
            IsOpen = false;
        }


        base.PreDraw();
        if (_uiShared.EditTrackerPosition)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
            Flags &= ~ImGuiWindowFlags.NoBackground;
            Flags &= ~ImGuiWindowFlags.NoInputs;
            Flags &= ~ImGuiWindowFlags.NoResize;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
            Flags |= ImGuiWindowFlags.NoBackground;
            Flags |= ImGuiWindowFlags.NoInputs;
            Flags |= ImGuiWindowFlags.NoResize;
        }
    }

    public override void Draw()
    {
        if (!_configService.Current.ShowTransferWindow) return;
        if (!_apiController.IsDownloading && !_apiController.IsUploading) return;

        var drawList = ImGui.GetWindowDrawList();
        var yDistance = 20;
        var xDistance = 20;

        var basePosition = ImGui.GetWindowPos() + ImGui.GetWindowContentRegionMin();

        try
        {
            if (_apiController.CurrentUploads.Any())
            {
                var currentUploads = _apiController.CurrentUploads.ToList();
                var totalUploads = currentUploads.Count;

                var doneUploads = currentUploads.Count(c => c.IsTransferred);
                var totalUploaded = currentUploads.Sum(c => c.Transferred);
                var totalToUpload = currentUploads.Sum(c => c.Total);

                UiShared.DrawOutlinedFont(drawList, "▲",
                    new Vector2(basePosition.X + 0, basePosition.Y + (int)(yDistance * 0.5)),
                    UiShared.Color(255, 255, 255, 255), UiShared.Color(0, 0, 0, 255), 2);
                UiShared.DrawOutlinedFont(drawList, $"Compressing+Uploading {doneUploads}/{totalUploads}",
                    new Vector2(basePosition.X + xDistance, basePosition.Y + yDistance * 0),
                    UiShared.Color(255, 255, 255, 255), UiShared.Color(0, 0, 0, 255), 2);
                UiShared.DrawOutlinedFont(drawList, $"{UiShared.ByteToString(totalUploaded)}/{UiShared.ByteToString(totalToUpload)}",
                    new Vector2(basePosition.X + xDistance, basePosition.Y + yDistance * 1),
                    UiShared.Color(255, 255, 255, 255), UiShared.Color(0, 0, 0, 255), 2);

            }
        }
        catch { }

        try
        {
            if (_apiController.CurrentDownloads.Any())
            {
                var currentDownloads = _apiController.CurrentDownloads.Where(d => d.Value != null && d.Value.Any()).ToList().SelectMany(k => k.Value).ToList();
                var multBase = currentDownloads.Any() ? 0 : 2;
                var doneDownloads = currentDownloads.Count(c => c.IsTransferred);
                var totalDownloads = currentDownloads.Count;
                var totalDownloaded = currentDownloads.Sum(c => c.Transferred);
                var totalToDownload = currentDownloads.Sum(c => c.Total);
                UiShared.DrawOutlinedFont(drawList, "▼",
                    new Vector2(basePosition.X + 0, basePosition.Y + (int)(yDistance * multBase + (yDistance * 0.5))),
                    UiShared.Color(255, 255, 255, 255), UiShared.Color(0, 0, 0, 255), 2);
                UiShared.DrawOutlinedFont(drawList, $"Downloading {doneDownloads}/{totalDownloads}",
                    new Vector2(basePosition.X + xDistance, basePosition.Y + yDistance * multBase),
                    UiShared.Color(255, 255, 255, 255), UiShared.Color(0, 0, 0, 255), 2);
                UiShared.DrawOutlinedFont(drawList, $"{UiShared.ByteToString(totalDownloaded)}/{UiShared.ByteToString(totalToDownload)}",
                    new Vector2(basePosition.X + xDistance, basePosition.Y + yDistance * (1 + multBase)),
                    UiShared.Color(255, 255, 255, 255), UiShared.Color(0, 0, 0, 255), 2);
            }
        }
        catch { }
    }
}