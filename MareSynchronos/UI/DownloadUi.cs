using Dalamud.Game.Gui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;

namespace MareSynchronos.UI;

public class DownloadUi : WindowMediatorSubscriberBase
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly MareConfigService _configService;
    private readonly FileUploadManager _fileTransferManager;
    private readonly UiSharedService _uiShared;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();

    public DownloadUi(ILogger<DownloadUi> logger, WindowSystem windowSystem, DalamudUtilService dalamudUtilService, MareConfigService configService,
        FileUploadManager fileTransferManager, MareMediator mediator, UiSharedService uiShared) : base(logger, windowSystem, mediator, "Mare Synchronos Downloads")
    {
        _dalamudUtilService = dalamudUtilService;
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

        IsOpen = true;

        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) =>
        {
            _currentDownloads[msg.DownloadId] = msg.DownloadStatus;
        });

        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));

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
        if (!_configService.Current.ShowTransferWindow && !_configService.Current.ShowTransferBars) return;
        if (!_currentDownloads.Any() && !_fileTransferManager.CurrentUploads.Any()) return;
        if (!IsOpen) return;

        if (_configService.Current.ShowTransferWindow)
        {
            try
            {
                if (_fileTransferManager.CurrentUploads.Any())
                {
                    var currentUploads = _fileTransferManager.CurrentUploads.ToList();
                    var totalUploads = currentUploads.Count;

                    var doneUploads = currentUploads.Count(c => c.IsTransferred);
                    var totalUploaded = currentUploads.Sum(c => c.Transferred);
                    var totalToUpload = currentUploads.Sum(c => c.Total);

                    UiSharedService.DrawOutlinedFont($"▲", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                    ImGui.SameLine();
                    var xDistance = ImGui.GetCursorPosX();
                    UiSharedService.DrawOutlinedFont($"Compressing+Uploading {doneUploads}/{totalUploads}",
                        ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                    ImGui.NewLine();
                    ImGui.SameLine(xDistance);
                    UiSharedService.DrawOutlinedFont(
                        $"{UiSharedService.ByteToString(totalUploaded, addSuffix: false)}/{UiSharedService.ByteToString(totalToUpload)}",
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

                    UiSharedService.DrawOutlinedFont($"▼", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                    ImGui.SameLine();
                    UiSharedService.DrawOutlinedFont(
                        $"{item.Key.Name} [W:{dlSlot}/Q:{dlQueue}/P:{dlProg}/D:{dlDecomp}]",
                        ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                    ImGui.SameLine();
                    UiSharedService.DrawOutlinedFont(
                        $"{transferredFiles}/{totalFiles} ({UiSharedService.ByteToString(transferredBytes, addSuffix: false)}/{UiSharedService.ByteToString(totalBytes)})",
                        ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                }
            }
            catch
            {
                // ignore errors thrown from UI
            }
        }

        if (_configService.Current.ShowTransferBars)
        {

            foreach (var transfer in _currentDownloads)
            {
                var screenPos = _dalamudUtilService.WorldToScreen(transfer.Key.GameObjectLazy.Value);
                if (screenPos == Vector2.Zero) continue;

                const int dlBarWidth = 100;
                const int dlBarBorder = 3;

                var totalBytes = transfer.Value.Sum(c => c.Value.TotalBytes);
                var transferredBytes = transfer.Value.Sum(c => c.Value.TransferredBytes);

                var downloadText =
                    $"{UiSharedService.ByteToString(transferredBytes, addSuffix: false)}/{UiSharedService.ByteToString(totalBytes)}";
                var textSize = ImGui.CalcTextSize(downloadText);

                int dlBarHeight = (int)textSize.Y + 25;

                var dlBarStart = new Vector2(screenPos.X - dlBarWidth / 2f, screenPos.Y - dlBarHeight / 2f);
                var dlBarEnd = new Vector2(screenPos.X + dlBarWidth / 2f, screenPos.Y + dlBarHeight / 2f);
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddRectFilled(
                    dlBarStart with { X = dlBarStart.X - dlBarBorder - 1, Y = dlBarStart.Y - dlBarBorder - 1 },
                    dlBarEnd with { X = dlBarEnd.X + dlBarBorder + 1, Y = dlBarEnd.Y + dlBarBorder + 1 },
                    UiSharedService.Color(new Vector4(0, 0, 0, 255)), 1);
                drawList.AddRectFilled(dlBarStart with { X = dlBarStart.X - dlBarBorder, Y = dlBarStart.Y - dlBarBorder },
                    dlBarEnd with { X = dlBarEnd.X + dlBarBorder, Y = dlBarEnd.Y + dlBarBorder },
                    UiSharedService.Color(new Vector4(33, 33, 33, 255)), 1);
                drawList.AddRectFilled(dlBarStart, dlBarEnd,
                    UiSharedService.Color(new Vector4(0, 0, 0, 255)), 1);
                var dlProgressPercent = transferredBytes / (double)totalBytes;
                drawList.AddRectFilled(dlBarStart,
                    dlBarEnd with { X = dlBarStart.X + (float)(dlProgressPercent * dlBarWidth) },
                    UiSharedService.Color(50, 205, 50, 255), 1);
                UiSharedService.DrawOutlinedFont(drawList, downloadText,
                    screenPos with { X = screenPos.X - textSize.X / 2f - 1, Y = screenPos.Y - textSize.Y / 2f - 1 },
                    UiSharedService.Color(new Vector4(255, 255, 255, 255)),
                    UiSharedService.Color(new Vector4(0, 0, 0, 255)), 1);
            }
        }
    }
}