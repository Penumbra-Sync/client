using System;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI;

public class DownloadUi : Window, IDisposable
{
    private readonly WindowSystem _windowSystem;
    private readonly Configuration _pluginConfiguration;
    private readonly ApiController _apiController;

    public void Dispose()
    {
        Logger.Debug("Disposing " + nameof(DownloadUi));
        _windowSystem.RemoveWindow(this);
    }

    public DownloadUi(WindowSystem windowSystem, Configuration pluginConfiguration, ApiController apiController) : base("Mare Synchronos Downloads")
    {
        Logger.Debug("Creating " + nameof(DownloadUi));
        _windowSystem = windowSystem;
        _pluginConfiguration = pluginConfiguration;
        _apiController = apiController;

        SizeConstraints = new WindowSizeConstraints()
        {
            MaximumSize = new Vector2(300, 90),
            MinimumSize = new Vector2(300, 90)
        };

        Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground;

        windowSystem.AddWindow(this);
        IsOpen = true;
    }

    public override void Draw()
    {
        if (!_pluginConfiguration.ShowTransferWindow) return;
        if (!_apiController.IsDownloading && !_apiController.IsUploading) return;

        var drawList = ImGui.GetWindowDrawList();
        var yDistance = 20;
        var xDistance = 20;

        var basePosition = ImGui.GetWindowPos() + ImGui.GetWindowContentRegionMin();

        if (_apiController.CurrentUploads.Any())
        {
            var doneUploads = _apiController.CurrentUploads.Count(c => c.Total == c.Transferred);
            var totalUploads = _apiController.CurrentUploads.Count;
            var totalUploaded = _apiController.CurrentUploads.Sum(c => c.Transferred);
            var totalToUpload = _apiController.CurrentUploads.Sum(c => c.Total);
            UiShared.DrawOutlinedFont(drawList, "▲",
                new Vector2(basePosition.X + 0, basePosition.Y + (int)(yDistance * 0.5)),
                UiShared.Color(255, 255, 255, 255), UiShared.Color(0, 0, 0, 255), 2);
            UiShared.DrawOutlinedFont(drawList, $"Uploading {doneUploads}/{totalUploads}",
                new Vector2(basePosition.X + xDistance, basePosition.Y + yDistance * 0),
                UiShared.Color(255, 255, 255, 255), UiShared.Color(0, 0, 0, 255), 2);
            UiShared.DrawOutlinedFont(drawList, $"{UiShared.ByteToString(totalUploaded)}/{UiShared.ByteToString(totalToUpload)}",
                new Vector2(basePosition.X + xDistance, basePosition.Y + yDistance * 1),
                UiShared.Color(255, 255, 255, 255), UiShared.Color(0, 0, 0, 255), 2);
        }

        if (_apiController.CurrentDownloads.Any())
        {
            var multBase = _apiController.CurrentUploads.Any() ? 0 : 2;
            var doneDownloads = _apiController.CurrentDownloads.Count(c => c.Total == c.Transferred);
            var totalDownloads = _apiController.CurrentDownloads.Count;
            var totalDownloaded = _apiController.CurrentDownloads.Sum(c => c.Transferred);
            var totalToDownload = _apiController.CurrentDownloads.Sum(c => c.Total);
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
}