using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class DownloadUi : WindowMediatorSubscriberBase
{
    private readonly WindowSystem _windowSystem;
    private readonly MareConfigService _configService;
    private readonly FileUploadManager _fileTransferManager;
    private readonly UiShared _uiShared;
    private readonly ConcurrentDictionary<string, Dictionary<string, FileDownloadStatus>> _currentDownloads = new(StringComparer.Ordinal);

    public override void Dispose()
    {
        base.Dispose();
        _windowSystem.RemoveWindow(this);
    }

    public DownloadUi(ILogger<DownloadUi> logger, WindowSystem windowSystem, MareConfigService configService,
        FileUploadManager fileTransferManager, MareMediator mediator, UiShared uiShared) : base(logger, mediator, "Mare Synchronos Downloads")
    {
        _windowSystem = windowSystem;
        _configService = configService;
        _fileTransferManager = fileTransferManager;
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

        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) =>
        {
            var actualMsg = ((DownloadStartedMessage)msg);
            _currentDownloads[actualMsg.DownloadId] = actualMsg.DownloadStatus;
        });

        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(((DownloadFinishedMessage)msg).DownloadId, out _));

        Mediator.Subscribe<GposeStartMessage>(this, (_) =>
        {
            IsOpen = false;
        });

        Mediator.Subscribe<GposeEndMessage>(this, (_) =>
        {
            IsOpen = true;
        });
    }

    public override void PreDraw()
    {
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

        var maxHeight = ImGui.GetTextLineHeight() * (_configService.Current.ParallelDownloads + 3);
        SizeConstraints = new()
        {
            MinimumSize = new Vector2(300, maxHeight),
            MaximumSize = new Vector2(300, maxHeight),
        };
    }

    public override void Draw()
    {
        if (!_configService.Current.ShowTransferWindow) return;
        if (!_currentDownloads.Any() && !_fileTransferManager.CurrentUploads.Any()) return;

        var drawList = ImGui.GetWindowDrawList();

        var basePosition = ImGui.GetWindowPos() + ImGui.GetWindowContentRegionMin();

        try
        {
            if (_fileTransferManager.CurrentUploads.Any())
            {
                var currentUploads = _fileTransferManager.CurrentUploads.ToList();
                var totalUploads = currentUploads.Count;

                var doneUploads = currentUploads.Count(c => c.IsTransferred);
                var totalUploaded = currentUploads.Sum(c => c.Transferred);
                var totalToUpload = currentUploads.Sum(c => c.Total);

                UiShared.DrawOutlinedFont($"▲", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                ImGui.SameLine();
                var xDistance = ImGui.GetCursorPosX();
                UiShared.DrawOutlinedFont("Compressing+Uploading {doneUploads}/{totalUploads}", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                ImGui.NewLine();
                ImGui.SameLine(xDistance);
                UiShared.DrawOutlinedFont($"{UiShared.ByteToString(totalUploaded, false)}/{UiShared.ByteToString(totalToUpload)}",
                    ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);

                if (_currentDownloads.Any()) ImGui.Separator();
            }
        }
        catch { }

        try
        {
            foreach (var item in _currentDownloads)
            {
                var dlSlot = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.WaitingForSlot);
                var dlQueue = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.WaitingForQueue);
                var dlProg = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.Downloading);
                var dlDecomp = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.Decompressing);
                var totalFiles = item.Value.Sum(c => c.Value.TotalFiles);
                var transferredFiles = item.Value.Sum(c => c.Value.TransferredFiles);
                var totalBytes = item.Value.Sum(c => c.Value.TotalBytes);
                var transferredBytes = item.Value.Sum(c => c.Value.TransferredBytes);

                UiShared.DrawOutlinedFont($"▼", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                ImGui.SameLine();
                var xDistance = ImGui.GetCursorPosX();
                UiShared.DrawOutlinedFont($"{item.Key} [W:{dlSlot}/Q:{dlQueue}/P:{dlProg}/D:{dlDecomp}]",
                    ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                ImGui.NewLine();
                ImGui.SameLine(xDistance);
                UiShared.DrawOutlinedFont($"{transferredFiles}/{totalFiles} Files ({UiShared.ByteToString(transferredBytes, false)}/{UiShared.ByteToString(totalBytes)})",
                    ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
            }
        }
        catch { }
    }
}