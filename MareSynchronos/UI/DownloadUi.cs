using Dalamud.Interface.Colors;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.Managers;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using System.Collections.Concurrent;

namespace MareSynchronos.UI;

public class DownloadUi : WindowMediatorSubscriberBase, IDisposable
{
    private readonly WindowSystem _windowSystem;
    private readonly ConfigurationService _configService;
    private readonly ApiController _apiController;
    private readonly UiShared _uiShared;
    private bool _wasOpen = false;
    private readonly ConcurrentDictionary<string, TransferManager.DownloadFileStatus> _currentDownloads = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _downloadPlayerNames = new(StringComparer.Ordinal);

    public override void Dispose()
    {
        base.Dispose();
        _windowSystem.RemoveWindow(this);
    }

    public DownloadUi(WindowSystem windowSystem, ConfigurationService configService, ApiController apiController, UiShared uiShared, MareMediator mediator) : base(mediator, "Mare Synchronos Downloads")
    {
        Logger.Verbose("Creating " + nameof(DownloadUi));
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

        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) =>
        {
            var actualMsg = (DownloadStartedMessage)msg;
            _currentDownloads[actualMsg.UID] =
                new TransferManager.DownloadFileStatus(0, 0, 0, actualMsg.TotalFiles);
            _downloadPlayerNames[actualMsg.UID] = actualMsg.PlayerName;
        });
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) =>
        {
            var uid = ((DownloadFinishedMessage)msg).UID;
            _currentDownloads.Remove(uid, out _);
        });
        Mediator.Subscribe<DownloadUpdateMessage>(this, (msg) =>
        {
            var actualMsg = (DownloadUpdateMessage)msg;
            _currentDownloads[actualMsg.UID] = actualMsg.Status;
        });

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

        if (_apiController.CurrentUploads.Any())
        {
            var currentUploads = _apiController.CurrentUploads.ToList();
            var totalUploads = currentUploads.Count;

            var doneUploads = currentUploads.Count(c => c.IsTransferred);
            var totalUploaded = currentUploads.Sum(c => c.Transferred);
            var totalToUpload = currentUploads.Sum(c => c.Total);

            UiShared.DrawOutlinedFont("▲", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 1), 2);
            var xPosU = ImGui.GetCursorPosX();
            ImGui.SameLine();

            ImGui.SetCursorPosX(xPosU);
            UiShared.DrawOutlinedFont(
                $"Compressing + Uploading {doneUploads}/{totalUploads})",
                ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 1), 2);
            ImGui.SetCursorPosX(xPosU);
            UiShared.DrawOutlinedFont(
                $"{UiShared.ByteToString(totalUploaded)}/{UiShared.ByteToString(totalToUpload)}",
                ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 1), 2);

        }

        if (!_currentDownloads.Any()) return;

        UiShared.DrawOutlinedFont("▼", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 1), 2);
        var xPosD = ImGui.GetCursorPosX();
        ImGui.SameLine();

        foreach (var item in _currentDownloads.ToList())
        {
            if (!_downloadPlayerNames.ContainsKey(item.Key)) continue;
            ImGui.SetCursorPosX(xPosD);
            UiShared.DrawOutlinedFont(
                $"Downloading {_downloadPlayerNames[item.Key]} ({item.Value.DownloadedFiles}/{item.Value.TotalFiles})",
                ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 1), 2);
            ImGui.SetCursorPosX(xPosD);
            UiShared.DrawOutlinedFont(
                $"{UiShared.ByteToString(item.Value.DownloadedBytes)}/{UiShared.ByteToString(item.Value.TotalBytes)}",
                ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 1), 2);
        }
    }
}