using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class DownloadUi : WindowMediatorSubscriberBase, IHostedService
{
    private readonly MareConfigService _configService;
    private readonly FileUploadManager _fileTransferManager;
    private readonly UiShared _uiShared;
    private readonly ConcurrentDictionary<string, Dictionary<string, FileDownloadStatus>> _currentDownloads = new(StringComparer.Ordinal);

    public DownloadUi(ILogger<DownloadUi> logger, WindowSystem windowSystem, MareConfigService configService,
        FileUploadManager fileTransferManager, MareMediator mediator, UiShared uiShared) : base(logger, windowSystem, mediator, "Mare Synchronos Downloads")
    {
        _configService = configService;
        _fileTransferManager = fileTransferManager;
        _uiShared = uiShared;
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
                UiShared.DrawOutlinedFont($"Compressing+Uploading {doneUploads}/{totalUploads}", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                ImGui.NewLine();
                ImGui.SameLine(xDistance);
                UiShared.DrawOutlinedFont($"{UiShared.ByteToString(totalUploaded, addSuffix: false)}/{UiShared.ByteToString(totalToUpload)}",
                    ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);

                if (_currentDownloads.Any()) ImGui.Separator();
            }
        }
        catch
        {
            // ignore errors thrown from UI
        }

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
                UiShared.DrawOutlinedFont($"{transferredFiles}/{totalFiles} Files ({UiShared.ByteToString(transferredBytes, addSuffix: false)}/{UiShared.ByteToString(totalBytes)})",
                    ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
            }
        }
        catch
        {
            // ignore errors thrown from UI
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        base.StartAsync(cancellationToken);

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

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        base.StopAsync(cancellationToken);
        return Task.CompletedTask;
    }
}