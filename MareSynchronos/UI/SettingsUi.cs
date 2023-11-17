using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace MareSynchronos.UI;

public class SettingsUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly FileCompactor _fileCompactor;
    private readonly FileUploadManager _fileTransferManager;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly MareCharaFileManager _mareCharaFileManager;
    private readonly PairManager _pairManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiShared;
    private bool _deleteAccountPopupModalShown = false;
    private bool _deleteFilesPopupModalShown = false;
    private string _exportDescription = string.Empty;
    private string _lastTab = string.Empty;
    private bool? _notesSuccessfullyApplied = null;
    private bool _overwriteExistingLabels = false;
    private bool _readClearCache = false;
    private bool _readExport = false;
    private bool _wasOpen = false;

    public SettingsUi(ILogger<SettingsUi> logger,
        UiSharedService uiShared, MareConfigService configService,
        MareCharaFileManager mareCharaFileManager, PairManager pairManager,
        ServerConfigurationManager serverConfigurationManager,
        MareMediator mediator, PerformanceCollectorService performanceCollector,
        FileUploadManager fileTransferManager,
        FileTransferOrchestrator fileTransferOrchestrator,
        FileCompactor fileCompactor, ApiController apiController) : base(logger, mediator, "Mare Synchronos Settings")
    {
        _configService = configService;
        _mareCharaFileManager = mareCharaFileManager;
        _pairManager = pairManager;
        _serverConfigurationManager = serverConfigurationManager;
        _performanceCollector = performanceCollector;
        _fileTransferManager = fileTransferManager;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _apiController = apiController;
        _fileCompactor = fileCompactor;
        _uiShared = uiShared;
        AllowClickthrough = false;
        AllowPinning = false;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(800, 400),
            MaximumSize = new Vector2(800, 2000),
        };

        Mediator.Subscribe<OpenSettingsUiMessage>(this, (_) => Toggle());
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) => LastCreatedCharacterData = msg.CharacterData);
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));
    }

    public CharacterData? LastCreatedCharacterData { private get; set; }
    private ApiController ApiController => _uiShared.ApiController;

    public override void Draw()
    {
        _ = _uiShared.DrawOtherPluginState();

        DrawSettingsContent();
    }

    public override void OnClose()
    {
        _uiShared.EditTrackerPosition = false;
        base.OnClose();
    }

    private void DrawBlockedTransfers()
    {
        _lastTab = "BlockedTransfers";
        UiSharedService.ColorTextWrapped("Files that you attempted to upload or download that were forbidden to be transferred by their creators will appear here. " +
                             "If you see file paths from your drive here, then those files were not allowed to be uploaded. If you see hashes, those files were not allowed to be downloaded. " +
                             "Ask your paired friend to send you the mod in question through other means, acquire the mod yourself or pester the mod creator to allow it to be sent over Mare.",
            ImGuiColors.DalamudGrey);

        if (ImGui.BeginTable("TransfersTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(
                $"Hash/Filename");
            ImGui.TableSetupColumn($"Forbidden by");

            ImGui.TableHeadersRow();

            foreach (var item in _fileTransferOrchestrator.ForbiddenTransfers)
            {
                ImGui.TableNextColumn();
                if (item is UploadFileTransfer transfer)
                {
                    ImGui.TextUnformatted(transfer.LocalFile);
                }
                else
                {
                    ImGui.TextUnformatted(item.Hash);
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(item.ForbiddenBy);
            }
            ImGui.EndTable();
        }
    }

    private void DrawCurrentTransfers()
    {
        _lastTab = "Transfers";
        UiSharedService.FontText("Transfer Settings", _uiShared.UidFont);

        int maxParallelDownloads = _configService.Current.ParallelDownloads;
        bool useAlternativeUpload = _configService.Current.UseAlternativeFileUpload;
        int downloadSpeedLimit = _configService.Current.DownloadSpeedLimitInBytes;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Global Download Speed Limit");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("###speedlimit", ref downloadSpeedLimit))
        {
            _configService.Current.DownloadSpeedLimitInBytes = downloadSpeedLimit;
            _configService.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        _uiShared.DrawCombo("###speed", new[] { DownloadSpeeds.Bps, DownloadSpeeds.KBps, DownloadSpeeds.MBps },
            (s) => s switch
            {
                DownloadSpeeds.Bps => "Byte/s",
                DownloadSpeeds.KBps => "KB/s",
                DownloadSpeeds.MBps => "MB/s",
                _ => throw new NotSupportedException()
            }, (s) =>
            {
                _configService.Current.DownloadSpeedType = s;
                _configService.Save();
                Mediator.Publish(new DownloadLimitChangedMessage());
            }, _configService.Current.DownloadSpeedType);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("0 = No limit/infinite");

        if (ImGui.SliderInt("Maximum Parallel Downloads", ref maxParallelDownloads, 1, 10))
        {
            _configService.Current.ParallelDownloads = maxParallelDownloads;
            _configService.Save();
        }

        if (ImGui.Checkbox("Use Alternative Upload Method", ref useAlternativeUpload))
        {
            _configService.Current.UseAlternativeFileUpload = useAlternativeUpload;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will attempt to upload files in one go instead of a stream. Typically not necessary to enable. Use if you have upload issues.");

        ImGui.Separator();
        UiSharedService.FontText("Transfer UI", _uiShared.UidFont);

        bool showTransferWindow = _configService.Current.ShowTransferWindow;
        if (ImGui.Checkbox("Show separate transfer window", ref showTransferWindow))
        {
            _configService.Current.ShowTransferWindow = showTransferWindow;
            _configService.Save();
        }
        UiSharedService.DrawHelpText($"The download window will show the current progress of outstanding downloads.{Environment.NewLine}{Environment.NewLine}" +
            $"What do W/Q/P/D stand for?{Environment.NewLine}W = Waiting for Slot (see Maximum Parallel Downloads){Environment.NewLine}" +
            $"Q = Queued on Server, waiting for queue ready signal{Environment.NewLine}" +
            $"P = Processing download (aka downloading){Environment.NewLine}" +
            $"D = Decompressing download");
        if (!_configService.Current.ShowTransferWindow) ImGui.BeginDisabled();
        ImGui.Indent();
        bool editTransferWindowPosition = _uiShared.EditTrackerPosition;
        if (ImGui.Checkbox("Edit Transfer Window position", ref editTransferWindowPosition))
        {
            _uiShared.EditTrackerPosition = editTransferWindowPosition;
        }
        ImGui.Unindent();
        if (!_configService.Current.ShowTransferWindow) ImGui.EndDisabled();

        bool showTransferBars = _configService.Current.ShowTransferBars;
        if (ImGui.Checkbox("Show transfer bars rendered below players", ref showTransferBars))
        {
            _configService.Current.ShowTransferBars = showTransferBars;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will render a progress bar during the download at the feet of the player you are downloading from.");

        if (!showTransferBars) ImGui.BeginDisabled();
        ImGui.Indent();
        bool transferBarShowText = _configService.Current.TransferBarsShowText;
        if (ImGui.Checkbox("Show Download Text", ref transferBarShowText))
        {
            _configService.Current.TransferBarsShowText = transferBarShowText;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Shows download text (amount of MiB downloaded) in the transfer bars");
        int transferBarWidth = _configService.Current.TransferBarsWidth;
        if (ImGui.SliderInt("Transfer Bar Width", ref transferBarWidth, 10, 500))
        {
            _configService.Current.TransferBarsWidth = transferBarWidth;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Width of the displayed transfer bars (will never be less wide than the displayed text)");
        int transferBarHeight = _configService.Current.TransferBarsHeight;
        if (ImGui.SliderInt("Transfer Bar Height", ref transferBarHeight, 2, 50))
        {
            _configService.Current.TransferBarsHeight = transferBarHeight;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Height of the displayed transfer bars (will never be less tall than the displayed text)");
        bool showUploading = _configService.Current.ShowUploading;
        if (ImGui.Checkbox("Show 'Uploading' text below players that are currently uploading", ref showUploading))
        {
            _configService.Current.ShowUploading = showUploading;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will render an 'Uploading' text at the feet of the player that is in progress of uploading data.");

        ImGui.Unindent();
        if (!showUploading) ImGui.BeginDisabled();
        ImGui.Indent();
        bool showUploadingBigText = _configService.Current.ShowUploadingBigText;
        if (ImGui.Checkbox("Large font for 'Uploading' text", ref showUploadingBigText))
        {
            _configService.Current.ShowUploadingBigText = showUploadingBigText;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will render an 'Uploading' text in a larger font.");

        ImGui.Unindent();

        if (!showUploading) ImGui.EndDisabled();
        if (!showTransferBars) ImGui.EndDisabled();

        ImGui.Separator();
        UiSharedService.FontText("Current Transfers", _uiShared.UidFont);

        if (ImGui.BeginTabBar("TransfersTabBar"))
        {
            if (ApiController.ServerState is ServerState.Connected && ImGui.BeginTabItem("Transfers"))
            {
                ImGui.TextUnformatted("Uploads");
                if (ImGui.BeginTable("UploadsTable", 3))
                {
                    ImGui.TableSetupColumn("File");
                    ImGui.TableSetupColumn("Uploaded");
                    ImGui.TableSetupColumn("Size");
                    ImGui.TableHeadersRow();
                    foreach (var transfer in _fileTransferManager.CurrentUploads.ToArray())
                    {
                        var color = UiSharedService.UploadColor((transfer.Transferred, transfer.Total));
                        var col = ImRaii.PushColor(ImGuiCol.Text, color);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(transfer.Hash);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(UiSharedService.ByteToString(transfer.Transferred));
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(UiSharedService.ByteToString(transfer.Total));
                        col.Dispose();
                        ImGui.TableNextRow();
                    }

                    ImGui.EndTable();
                }
                ImGui.Separator();
                ImGui.TextUnformatted("Downloads");
                if (ImGui.BeginTable("DownloadsTable", 4))
                {
                    ImGui.TableSetupColumn("User");
                    ImGui.TableSetupColumn("Server");
                    ImGui.TableSetupColumn("Files");
                    ImGui.TableSetupColumn("Download");
                    ImGui.TableHeadersRow();

                    foreach (var transfer in _currentDownloads.ToArray())
                    {
                        var userName = transfer.Key.Name;
                        foreach (var entry in transfer.Value)
                        {
                            var color = UiSharedService.UploadColor((entry.Value.TransferredBytes, entry.Value.TotalBytes));
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(userName);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(entry.Key);
                            var col = ImRaii.PushColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(entry.Value.TransferredFiles + "/" + entry.Value.TotalFiles);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(UiSharedService.ByteToString(entry.Value.TransferredBytes) + "/" + UiSharedService.ByteToString(entry.Value.TotalBytes));
                            ImGui.TableNextColumn();
                            col.Dispose();
                            ImGui.TableNextRow();
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Blocked Transfers"))
            {
                DrawBlockedTransfers();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawDebug()
    {
        _lastTab = "Debug";

        UiSharedService.FontText("Debug", _uiShared.UidFont);
#if DEBUG
        if (LastCreatedCharacterData != null && ImGui.TreeNode("Last created character data"))
        {
            foreach (var l in JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }).Split('\n'))
            {
                ImGui.TextUnformatted($"{l}");
            }

            ImGui.TreePop();
        }
#endif
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Copy, "[DEBUG] Copy Last created Character Data to clipboard"))
        {
            if (LastCreatedCharacterData != null)
            {
                ImGui.SetClipboardText(JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }));
            }
            else
            {
                ImGui.SetClipboardText("ERROR: No created character data, cannot copy.");
            }
        }
        UiSharedService.AttachToolTip("Use this when reporting mods being rejected from the server.");

        _uiShared.DrawCombo("Log Level", Enum.GetValues<LogLevel>(), (l) => l.ToString(), (l) =>
        {
            _configService.Current.LogLevel = l;
            _configService.Save();
        }, _configService.Current.LogLevel);

        bool logPerformance = _configService.Current.LogPerformance;
        if (ImGui.Checkbox("Log Performance Counters", ref logPerformance))
        {
            _configService.Current.LogPerformance = logPerformance;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Enabling this can incur a (slight) performance impact. Enabling this for extended periods of time is not recommended.");

        using var disabled = ImRaii.Disabled(!logPerformance);
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats to /xllog"))
        {
            _performanceCollector.PrintPerformanceStats();
        }
        ImGui.SameLine();
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats (last 60s) to /xllog"))
        {
            _performanceCollector.PrintPerformanceStats(60);
        }
    }

    private void DrawFileStorageSettings()
    {
        _lastTab = "FileCache";

        UiSharedService.FontText("Export MCDF", _uiShared.UidFont);

        UiSharedService.TextWrapped("This feature allows you to pack your character into a MCDF file and manually send it to other people. MCDF files can officially only be imported during GPose through Mare. " +
            "Be aware that the possibility exists that people write unofficial custom exporters to extract the containing data.");

        ImGui.Checkbox("##readExport", ref _readExport);
        ImGui.SameLine();
        UiSharedService.TextWrapped("I understand that by exporting my character data and sending it to other people I am giving away my current character appearance irrevocably. People I am sharing my data with have the ability to share it with other people without limitations.");

        if (_readExport)
        {
            ImGui.Indent();

            if (!_mareCharaFileManager.CurrentlyWorking)
            {
                ImGui.InputTextWithHint("Export Descriptor", "This description will be shown on loading the data", ref _exportDescription, 255);
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Save, "Export Character as MCDF"))
                {
                    string defaultFileName = string.IsNullOrEmpty(_exportDescription)
                        ? "export.mcdf"
                        : string.Join('_', $"{_exportDescription}.mcdf".Split(Path.GetInvalidFileNameChars()));
                    _uiShared.FileDialogManager.SaveFileDialog("Export Character to file", ".mcdf", defaultFileName, ".mcdf", (success, path) =>
                    {
                        if (!success) return;

                        _configService.Current.ExportFolder = Path.GetDirectoryName(path) ?? string.Empty;
                        _configService.Save();

                        _ = Task.Run(() =>
                        {
                            try
                            {
                                _mareCharaFileManager.SaveMareCharaFile(LastCreatedCharacterData, _exportDescription, path);
                                _exportDescription = string.Empty;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogCritical(ex, "Error saving data");
                            }
                        });
                    }, Directory.Exists(_configService.Current.ExportFolder) ? _configService.Current.ExportFolder : null);
                }
                UiSharedService.ColorTextWrapped("Note: For best results make sure you have everything you want to be shared as well as the correct character appearance" +
                    " equipped and redraw your character before exporting.", ImGuiColors.DalamudYellow);
            }
            else
            {
                UiSharedService.ColorTextWrapped("Export in progress", ImGuiColors.DalamudYellow);
            }

            ImGui.Unindent();
        }
        bool openInGpose = _configService.Current.OpenGposeImportOnGposeStart;
        if (ImGui.Checkbox("Open MCDF import window when GPose loads", ref openInGpose))
        {
            _configService.Current.OpenGposeImportOnGposeStart = openInGpose;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will automatically open the import menu when loading into Gpose. If unchecked you can open the menu manually with /mare gpose");

        ImGui.Separator();

        UiSharedService.FontText("Storage", _uiShared.UidFont);

        UiSharedService.TextWrapped("Mare stores downloaded files from paired people permanently. This is to improve loading performance and requiring less downloads. " +
            "The storage governs itself by clearing data beyond the set storage size. Please set the storage size accordingly. It is not necessary to manually clear the storage.");

        _uiShared.DrawFileScanState();
        _uiShared.DrawTimeSpanBetweenScansSetting();
        _uiShared.DrawCacheDirectorySetting();
        ImGui.TextUnformatted($"Currently utilized local storage: {UiSharedService.ByteToString(_uiShared.FileCacheSize)}");
        bool isLinux = Util.IsWine();
        if (isLinux) ImGui.BeginDisabled();
        bool useFileCompactor = _configService.Current.UseCompactor;
        if (ImGui.Checkbox("Use file compactor", ref useFileCompactor))
        {
            _configService.Current.UseCompactor = useFileCompactor;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("The file compactor can massively reduce your saved files. It might incur a minor penalty on loading files on a slow CPU." + Environment.NewLine
            + "It is recommended to leave it enabled to save on space.");
        ImGui.SameLine();
        if (!_fileCompactor.MassCompactRunning)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.FileArchive, "Compact all files in storage"))
            {
                _ = Task.Run(() => _fileCompactor.CompactStorage(compress: true));
            }
            UiSharedService.AttachToolTip("This will run compression on all files in your current Mare Storage." + Environment.NewLine
                + "You do not need to run this manually if you keep the file compactor enabled.");
            ImGui.SameLine();
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.File, "Decompact all files in storage"))
            {
                _ = Task.Run(() => _fileCompactor.CompactStorage(compress: false));
            }
            UiSharedService.AttachToolTip("This will run decompression on all files in your current Mare Storage.");
        }
        else
        {
            UiSharedService.ColorText($"File compactor currently running ({_fileCompactor.Progress})", ImGuiColors.DalamudYellow);
        }
        if (isLinux)
        {
            ImGui.EndDisabled();
            ImGui.TextUnformatted("The file compactor is only available on Windows.");
        }

        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));
        ImGui.TextUnformatted("To clear the local storage accept the following disclaimer");
        ImGui.Indent();
        ImGui.Checkbox("##readClearCache", ref _readClearCache);
        ImGui.SameLine();
        UiSharedService.TextWrapped("I understand that: " + Environment.NewLine + "- By clearing the local storage I put the file servers of my connected service under extra strain by having to redownload all data."
            + Environment.NewLine + "- This is not a step to try to fix sync issues."
            + Environment.NewLine + "- This can make the situation of not getting other players data worse in situations of heavy file server load.");
        if (!_readClearCache)
            ImGui.BeginDisabled();
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Clear local storage") && UiSharedService.CtrlPressed() && _readClearCache)
        {
            _ = Task.Run(() =>
            {
                foreach (var file in Directory.GetFiles(_configService.Current.CacheFolder))
                {
                    File.Delete(file);
                }

                _uiShared.RecalculateFileCacheSize();
            });
        }
        UiSharedService.AttachToolTip("You normally do not need to do this. THIS IS NOT SOMETHING YOU SHOULD BE DOING TO TRY TO FIX SYNC ISSUES." + Environment.NewLine
            + "This will solely remove all downloaded data from all players and will require you to re-download everything again." + Environment.NewLine
            + "Mares storage is self-clearing and will not surpass the limit you have set it to." + Environment.NewLine
            + "If you still think you need to do this hold CTRL while pressing the button.");
        if (!_readClearCache)
            ImGui.EndDisabled();
        ImGui.Unindent();
    }

    private void DrawGeneral()
    {
        if (!string.Equals(_lastTab, "General", StringComparison.OrdinalIgnoreCase))
        {
            _notesSuccessfullyApplied = null;
        }

        _lastTab = "General";
        UiSharedService.FontText("Notes", _uiShared.UidFont);
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.StickyNote, "Export all your user notes to clipboard"))
        {
            ImGui.SetClipboardText(UiSharedService.GetNotes(_pairManager.DirectPairs.UnionBy(_pairManager.GroupPairs.SelectMany(p => p.Value), p => p.UserData, UserDataComparer.Instance).ToList()));
        }
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.FileImport, "Import notes from clipboard"))
        {
            _notesSuccessfullyApplied = null;
            var notes = ImGui.GetClipboardText();
            _notesSuccessfullyApplied = _uiShared.ApplyNotesFromClipboard(notes, _overwriteExistingLabels);
        }

        ImGui.SameLine();
        ImGui.Checkbox("Overwrite existing notes", ref _overwriteExistingLabels);
        UiSharedService.DrawHelpText("If this option is selected all already existing notes for UIDs will be overwritten by the imported notes.");
        if (_notesSuccessfullyApplied.HasValue && _notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("User Notes successfully imported", ImGuiColors.HealerGreen);
        }
        else if (_notesSuccessfullyApplied.HasValue && !_notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("Attempt to import notes from clipboard failed. Check formatting and try again", ImGuiColors.DalamudRed);
        }

        var openPopupOnAddition = _configService.Current.OpenPopupOnAdd;

        if (ImGui.Checkbox("Open Notes Popup on user addition", ref openPopupOnAddition))
        {
            _configService.Current.OpenPopupOnAdd = openPopupOnAddition;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will open a popup that allows you to set the notes for a user after successfully adding them to your individual pairs.");

        ImGui.Separator();
        UiSharedService.FontText("UI", _uiShared.UidFont);
        var showNameInsteadOfNotes = _configService.Current.ShowCharacterNameInsteadOfNotesForVisible;
        var showVisibleSeparate = _configService.Current.ShowVisibleUsersSeparately;
        var showOfflineSeparate = _configService.Current.ShowOfflineUsersSeparately;
        var showProfiles = _configService.Current.ProfilesShow;
        var showNsfwProfiles = _configService.Current.ProfilesAllowNsfw;
        var profileDelay = _configService.Current.ProfileDelay;
        var profileOnRight = _configService.Current.ProfilePopoutRight;
        var enableRightClickMenu = _configService.Current.EnableRightClickMenus;
        var enableDtrEntry = _configService.Current.EnableDtrEntry;
        var showUidInDtrTooltip = _configService.Current.ShowUidInDtrTooltip;
        var preferNoteInDtrTooltip = _configService.Current.PreferNoteInDtrTooltip;
        var preferNotesInsteadOfName = _configService.Current.PreferNotesOverNamesForVisible;
        var groupUpSyncshells = _configService.Current.GroupUpSyncshells;
        var groupInVisible = _configService.Current.ShowSyncshellUsersInVisible;
        var syncshellOfflineSeparate = _configService.Current.ShowSyncshellOfflineUsersSeparately;

        if (ImGui.Checkbox("Enable Game Right Click Menu Entries", ref enableRightClickMenu))
        {
            _configService.Current.EnableRightClickMenus = enableRightClickMenu;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will add Mare related right click menu entries in the game UI on paired players.");

        if (ImGui.Checkbox("Display status and visible pair count in Server Info Bar", ref enableDtrEntry))
        {
            _configService.Current.EnableDtrEntry = enableDtrEntry;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will add Mare connection status and visible pair count in the Server Info Bar.\nYou can further configure this through your Dalamud Settings.");

        using (ImRaii.Disabled(!enableDtrEntry))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("Show visible character's UID in tooltip", ref showUidInDtrTooltip))
            {
                _configService.Current.ShowUidInDtrTooltip = showUidInDtrTooltip;
                _configService.Save();
            }

            if (ImGui.Checkbox("Prefer notes over player names in tooltip", ref preferNoteInDtrTooltip))
            {
                _configService.Current.PreferNoteInDtrTooltip = preferNoteInDtrTooltip;
                _configService.Save();
            }
        }

        if (ImGui.Checkbox("Show separate Visible group", ref showVisibleSeparate))
        {
            _configService.Current.ShowVisibleUsersSeparately = showVisibleSeparate;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        UiSharedService.DrawHelpText("This will show all currently visible users in a special 'Visible' group in the main UI.");

        using (ImRaii.Disabled(!showVisibleSeparate))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("Show Syncshell Users in Visible Group", ref groupInVisible))
            {
                _configService.Current.ShowSyncshellUsersInVisible = groupInVisible;
                _configService.Save();
                Mediator.Publish(new RefreshUiMessage());
            }
        }

        if (ImGui.Checkbox("Show separate Offline group", ref showOfflineSeparate))
        {
            _configService.Current.ShowOfflineUsersSeparately = showOfflineSeparate;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        UiSharedService.DrawHelpText("This will show all currently offline users in a special 'Offline' group in the main UI.");

        using (ImRaii.Disabled(!showOfflineSeparate))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("Show separate Offline group for Syncshell users", ref syncshellOfflineSeparate))
            {
                _configService.Current.ShowSyncshellOfflineUsersSeparately = syncshellOfflineSeparate;
                _configService.Save();
                Mediator.Publish(new RefreshUiMessage());
            }
        }

        if (ImGui.Checkbox("Group up all syncshells in one folder", ref groupUpSyncshells))
        {
            _configService.Current.GroupUpSyncshells = groupUpSyncshells;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        UiSharedService.DrawHelpText("This will group up all Syncshells in a special 'All Syncshells' folder in the main UI.");

        if (ImGui.Checkbox("Show player name for visible players", ref showNameInsteadOfNotes))
        {
            _configService.Current.ShowCharacterNameInsteadOfNotesForVisible = showNameInsteadOfNotes;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        UiSharedService.DrawHelpText("This will show the character name instead of custom set note when a character is visible");

        ImGui.Indent();
        if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Prefer notes over player names for visible players", ref preferNotesInsteadOfName))
        {
            _configService.Current.PreferNotesOverNamesForVisible = preferNotesInsteadOfName;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        UiSharedService.DrawHelpText("If you set a note for a player it will be shown instead of the player name");
        if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.EndDisabled();
        ImGui.Unindent();

        if (ImGui.Checkbox("Show Mare Profiles on Hover", ref showProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesShow = showProfiles;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("This will show the configured user profile after a set delay");
        ImGui.Indent();
        if (!showProfiles) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Popout profiles on the right", ref profileOnRight))
        {
            _configService.Current.ProfilePopoutRight = profileOnRight;
            _configService.Save();
            Mediator.Publish(new CompactUiChange(Vector2.Zero, Vector2.Zero));
        }
        UiSharedService.DrawHelpText("Will show profiles on the right side of the main UI");
        if (ImGui.SliderFloat("Hover Delay", ref profileDelay, 1, 10))
        {
            _configService.Current.ProfileDelay = profileDelay;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Delay until the profile should be displayed");
        if (!showProfiles) ImGui.EndDisabled();
        ImGui.Unindent();
        if (ImGui.Checkbox("Show profiles marked as NSFW", ref showNsfwProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesAllowNsfw = showNsfwProfiles;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Will show profiles that have the NSFW tag enabled");

        ImGui.Separator();

        var disableOptionalPluginWarnings = _configService.Current.DisableOptionalPluginWarnings;
        var onlineNotifs = _configService.Current.ShowOnlineNotifications;
        var onlineNotifsPairsOnly = _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs;
        var onlineNotifsNamedOnly = _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs;
        UiSharedService.FontText("Notifications", _uiShared.UidFont);

        _uiShared.DrawCombo("Info Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.InfoNotification = i;
            _configService.Save();
        }, _configService.Current.InfoNotification);
        UiSharedService.DrawHelpText("The location where \"Info\" notifications will display."
                      + Environment.NewLine + "'Nowhere' will not show any Info notifications"
                      + Environment.NewLine + "'Chat' will print Info notifications in chat"
                      + Environment.NewLine + "'Toast' will show Warning toast notifications in the bottom right corner"
                      + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        _uiShared.DrawCombo("Warning Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.WarningNotification = i;
            _configService.Save();
        }, _configService.Current.WarningNotification);
        UiSharedService.DrawHelpText("The location where \"Warning\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Warning notifications"
                              + Environment.NewLine + "'Chat' will print Warning notifications in chat"
                              + Environment.NewLine + "'Toast' will show Warning toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        _uiShared.DrawCombo("Error Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.ErrorNotification = i;
            _configService.Save();
        }, _configService.Current.ErrorNotification);
        UiSharedService.DrawHelpText("The location where \"Error\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Error notifications"
                              + Environment.NewLine + "'Chat' will print Error notifications in chat"
                              + Environment.NewLine + "'Toast' will show Error toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        if (ImGui.Checkbox("Disable optional plugin warnings", ref disableOptionalPluginWarnings))
        {
            _configService.Current.DisableOptionalPluginWarnings = disableOptionalPluginWarnings;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Enabling this will not show any \"Warning\" labeled messages for missing optional plugins.");
        if (ImGui.Checkbox("Enable online notifications", ref onlineNotifs))
        {
            _configService.Current.ShowOnlineNotifications = onlineNotifs;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Enabling this will show a small notification (type: Info) in the bottom right corner when pairs go online.");

        using var disabled = ImRaii.Disabled(!onlineNotifs);
        if (ImGui.Checkbox("Notify only for individual pairs", ref onlineNotifsPairsOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs = onlineNotifsPairsOnly;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Enabling this will only show online notifications (type: Info) for individual pairs.");
        if (ImGui.Checkbox("Notify only for named pairs", ref onlineNotifsNamedOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs = onlineNotifsNamedOnly;
            _configService.Save();
        }
        UiSharedService.DrawHelpText("Enabling this will only show online notifications (type: Info) for pairs where you have set an individual note.");
    }

    private void DrawServerConfiguration()
    {
        _lastTab = "Service Settings";
        if (ApiController.ServerAlive)
        {
            UiSharedService.FontText("Service Actions", _uiShared.UidFont);
            ImGuiHelpers.ScaledDummy(new Vector2(5, 5));
            if (ImGui.Button("Delete all my files"))
            {
                _deleteFilesPopupModalShown = true;
                ImGui.OpenPopup("Delete all your files?");
            }

            UiSharedService.DrawHelpText("Completely deletes all your uploaded files on the service.");

            if (ImGui.BeginPopupModal("Delete all your files?", ref _deleteFilesPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "All your own uploaded files on the service will be deleted.\nThis operation cannot be undone.");
                ImGui.TextUnformatted("Are you sure you want to continue?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                 ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("Delete everything", new Vector2(buttonSize, 0)))
                {
                    _ = Task.Run(_fileTransferManager.DeleteAllFiles);
                    _deleteFilesPopupModalShown = false;
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteFilesPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Delete account"))
            {
                _deleteAccountPopupModalShown = true;
                ImGui.OpenPopup("Delete your account?");
            }

            UiSharedService.DrawHelpText("Completely deletes your account and all uploaded files to the service.");

            if (ImGui.BeginPopupModal("Delete your account?", ref _deleteAccountPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "Your account and all associated files and data on the service will be deleted.");
                UiSharedService.TextWrapped("Your UID will be removed from all pairing lists.");
                ImGui.TextUnformatted("Are you sure you want to continue?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                  ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("Delete account", new Vector2(buttonSize, 0)))
                {
                    _ = Task.Run(ApiController.UserDelete);
                    _deleteAccountPopupModalShown = false;
                    Mediator.Publish(new SwitchToIntroUiMessage());
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteAccountPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.Separator();
        }

        UiSharedService.FontText("Service & Character Settings", _uiShared.UidFont);
        ImGuiHelpers.ScaledDummy(new Vector2(5, 5));
        var sendCensus = _serverConfigurationManager.SendCensusData;
        if (ImGui.Checkbox("Send Statistical Census Data", ref sendCensus))
        {
            _serverConfigurationManager.SendCensusData = sendCensus;
        }
        UiSharedService.DrawHelpText("This will allow sending census data to the currently connected service." + UiSharedService.TooltipSeparator
            + "Census data contains:" + Environment.NewLine
            + "- Current World" + Environment.NewLine
            + "- Current Gender" + Environment.NewLine
            + "- Current Race" + Environment.NewLine
            + "- Current Clan (this is not your Free Company, this is e.g. Keeper or Seeker for Miqo'te)" + UiSharedService.TooltipSeparator
            + "The census data is only saved temporarily and will be removed from the server on disconnect. It is stored temporarily associated with your UID while you are connected." + UiSharedService.TooltipSeparator
            + "If you do not wish to participate in the statistical census, untick this box and reconnect to the server.");
        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        var idx = _uiShared.DrawServiceSelection();

        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        var selectedServer = _serverConfigurationManager.GetServerByIndex(idx);
        if (selectedServer == _serverConfigurationManager.CurrentServer)
        {
            UiSharedService.ColorTextWrapped("For any changes to be applied to the current service you need to reconnect to the service.", ImGuiColors.DalamudYellow);
        }

        if (ImGui.BeginTabBar("serverTabBar"))
        {
            if (ImGui.BeginTabItem("Character Management"))
            {
                if (selectedServer.SecretKeys.Any())
                {
                    UiSharedService.ColorTextWrapped("Characters listed here will automatically connect to the selected Mare service with the settings as provided below." +
                        " Make sure to enter the character names correctly or use the 'Add current character' button at the bottom.", ImGuiColors.DalamudYellow);
                    int i = 0;
                    foreach (var item in selectedServer.Authentications.ToList())
                    {
                        using var charaId = ImRaii.PushId("selectedChara" + i);

                        var worldIdx = (ushort)item.WorldId;
                        var data = _uiShared.WorldData.OrderBy(u => u.Value, StringComparer.Ordinal).ToDictionary(k => k.Key, k => k.Value);
                        if (!data.TryGetValue(worldIdx, out string? worldPreview))
                        {
                            worldPreview = data.First().Value;
                        }

                        var secretKeyIdx = item.SecretKeyIdx;
                        var keys = selectedServer.SecretKeys;
                        if (!keys.TryGetValue(secretKeyIdx, out var secretKey))
                        {
                            secretKey = new();
                        }
                        var friendlyName = secretKey.FriendlyName;

                        if (ImGui.TreeNode($"chara", $"Character: {item.CharacterName}, World: {worldPreview}, Secret Key: {friendlyName}"))
                        {
                            var charaName = item.CharacterName;
                            if (ImGui.InputText("Character Name", ref charaName, 64))
                            {
                                item.CharacterName = charaName;
                                _serverConfigurationManager.Save();
                            }

                            _uiShared.DrawCombo("World##" + item.CharacterName + i, data, (w) => w.Value,
                                (w) =>
                                {
                                    if (item.WorldId != w.Key)
                                    {
                                        item.WorldId = w.Key;
                                        _serverConfigurationManager.Save();
                                    }
                                }, EqualityComparer<KeyValuePair<ushort, string>>.Default.Equals(data.FirstOrDefault(f => f.Key == worldIdx), default) ? data.First() : data.First(f => f.Key == worldIdx));

                            _uiShared.DrawCombo("Secret Key##" + item.CharacterName + i, keys, (w) => w.Value.FriendlyName,
                                (w) =>
                                {
                                    if (w.Key != item.SecretKeyIdx)
                                    {
                                        item.SecretKeyIdx = w.Key;
                                        _serverConfigurationManager.Save();
                                    }
                                }, EqualityComparer<KeyValuePair<int, SecretKey>>.Default.Equals(keys.FirstOrDefault(f => f.Key == item.SecretKeyIdx), default) ? keys.First() : keys.First(f => f.Key == item.SecretKeyIdx));

                            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Delete Character") && UiSharedService.CtrlPressed())
                                _serverConfigurationManager.RemoveCharacterFromServer(idx, item);
                            UiSharedService.AttachToolTip("Hold CTRL to delete this entry.");

                            ImGui.TreePop();
                        }

                        i++;
                    }

                    ImGui.Separator();
                    if (!selectedServer.Authentications.Exists(c => string.Equals(c.CharacterName, _uiShared.PlayerName, StringComparison.Ordinal)
                        && c.WorldId == _uiShared.WorldId))
                    {
                        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.User, "Add current character"))
                        {
                            _serverConfigurationManager.AddCurrentCharacterToServer(idx);
                        }
                        ImGui.SameLine();
                    }

                    if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Plus, "Add new character"))
                    {
                        _serverConfigurationManager.AddEmptyCharacterToServer(idx);
                    }
                }
                else
                {
                    UiSharedService.ColorTextWrapped("You need to add a Secret Key first before adding Characters.", ImGuiColors.DalamudYellow);
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Secret Key Management"))
            {
                foreach (var item in selectedServer.SecretKeys.ToList())
                {
                    using var id = ImRaii.PushId("key" + item.Key);
                    var friendlyName = item.Value.FriendlyName;
                    if (ImGui.InputText("Secret Key Display Name", ref friendlyName, 255))
                    {
                        item.Value.FriendlyName = friendlyName;
                        _serverConfigurationManager.Save();
                    }
                    var key = item.Value.Key;
                    if (ImGui.InputText("Secret Key", ref key, 64))
                    {
                        item.Value.Key = key;
                        _serverConfigurationManager.Save();
                    }
                    if (!selectedServer.Authentications.Exists(p => p.SecretKeyIdx == item.Key))
                    {
                        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Delete Secret Key") && UiSharedService.CtrlPressed())
                        {
                            selectedServer.SecretKeys.Remove(item.Key);
                            _serverConfigurationManager.Save();
                        }
                        UiSharedService.AttachToolTip("Hold CTRL to delete this secret key entry");
                    }
                    else
                    {
                        UiSharedService.ColorTextWrapped("This key is in use and cannot be deleted", ImGuiColors.DalamudYellow);
                    }

                    if (item.Key != selectedServer.SecretKeys.Keys.LastOrDefault())
                        ImGui.Separator();
                }

                ImGui.Separator();
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Plus, "Add new Secret Key"))
                {
                    selectedServer.SecretKeys.Add(selectedServer.SecretKeys.Any() ? selectedServer.SecretKeys.Max(p => p.Key) + 1 : 0, new SecretKey()
                    {
                        FriendlyName = "New Secret Key",
                    });
                    _serverConfigurationManager.Save();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Service Settings"))
            {
                var serverName = selectedServer.ServerName;
                var serverUri = selectedServer.ServerUri;
                var isMain = string.Equals(serverName, ApiController.MainServer, StringComparison.OrdinalIgnoreCase);
                var flags = isMain ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;

                if (ImGui.InputText("Service URI", ref serverUri, 255, flags))
                {
                    selectedServer.ServerUri = serverUri;
                }
                if (isMain)
                {
                    UiSharedService.DrawHelpText("You cannot edit the URI of the main service.");
                }

                if (ImGui.InputText("Service Name", ref serverName, 255, flags))
                {
                    selectedServer.ServerName = serverName;
                    _serverConfigurationManager.Save();
                }
                if (isMain)
                {
                    UiSharedService.DrawHelpText("You cannot edit the name of the main service.");
                }

                if (!isMain && selectedServer != _serverConfigurationManager.CurrentServer)
                {
                    if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Trash, "Delete Service") && UiSharedService.CtrlPressed())
                    {
                        _serverConfigurationManager.DeleteServer(selectedServer);
                    }
                    UiSharedService.DrawHelpText("Hold CTRL to delete this service");
                }
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Permission Settings"))
            {
                UiSharedService.FontText("Default Permission Settings", _uiShared.UidFont);
                if (selectedServer == _serverConfigurationManager.CurrentServer && _apiController.IsConnected)
                {
                    UiSharedService.TextWrapped("Note: The default permissions settings here are not applied retroactively to existing pairs or joined Syncshells.");
                    UiSharedService.TextWrapped("Note: The default permissions settings here are sent and stored on the connected service.");
                    ImGuiHelpers.ScaledDummy(5f);
                    var perms = _apiController.DefaultPermissions!;
                    bool individualIsSticky = perms.IndividualIsSticky;
                    bool disableIndividualSounds = perms.DisableIndividualSounds;
                    bool disableIndividualAnimations = perms.DisableIndividualAnimations;
                    bool disableIndividualVFX = perms.DisableIndividualVFX;
                    if (ImGui.Checkbox("Individually set permissions become preferred permissions", ref individualIsSticky))
                    {
                        perms.IndividualIsSticky = individualIsSticky;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    UiSharedService.DrawHelpText("The preferred attribute means that the permissions to that user will never change through any of your permission changes to Syncshells " +
                        "(i.e. if you have paused one specific user in a Syncshell and they become preferred permissions, then pause and unpause the same Syncshell, the user will remain paused - " +
                        "if a user does not have preferred permissions, it will follow the permissions of the Syncshell and be unpaused)." + Environment.NewLine + Environment.NewLine +
                        "This setting means:" + Environment.NewLine +
                        "  - All new individual pairs get their permissions defaulted to preferred permissions." + Environment.NewLine +
                        "  - All individually set permissions for any pair will also automatically become preferred permissions. This includes pairs in Syncshells." + Environment.NewLine + Environment.NewLine +
                        "It is possible to remove or set the preferred permission state for any pair at any time." + Environment.NewLine + Environment.NewLine +
                        "If unsure, leave this setting off.");
                    ImGuiHelpers.ScaledDummy(3f);

                    if (ImGui.Checkbox("Disable individual pair sounds", ref disableIndividualSounds))
                    {
                        perms.DisableIndividualSounds = disableIndividualSounds;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    UiSharedService.DrawHelpText("This setting will disable sound sync for all new individual pairs.");
                    if (ImGui.Checkbox("Disable individual pair animations", ref disableIndividualAnimations))
                    {
                        perms.DisableIndividualAnimations = disableIndividualAnimations;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    UiSharedService.DrawHelpText("This setting will disable animation sync for all new individual pairs.");
                    if (ImGui.Checkbox("Disable individual pair VFX", ref disableIndividualVFX))
                    {
                        perms.DisableIndividualVFX = disableIndividualVFX;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    UiSharedService.DrawHelpText("This setting will disable VFX sync for all new individual pairs.");
                    ImGuiHelpers.ScaledDummy(5f);
                    bool disableGroundSounds = perms.DisableGroupSounds;
                    bool disableGroupAnimations = perms.DisableGroupAnimations;
                    bool disableGroupVFX = perms.DisableGroupVFX;
                    if (ImGui.Checkbox("Disable Syncshell pair sounds", ref disableGroundSounds))
                    {
                        perms.DisableGroupSounds = disableGroundSounds;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    UiSharedService.DrawHelpText("This setting will disable sound sync for all non-sticky pairs in newly joined syncshells.");
                    if (ImGui.Checkbox("Disable Syncshell pair animations", ref disableGroupAnimations))
                    {
                        perms.DisableGroupAnimations = disableGroupAnimations;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    UiSharedService.DrawHelpText("This setting will disable animation sync for all non-sticky pairs in newly joined syncshells.");
                    if (ImGui.Checkbox("Disable Syncshell pair VFX", ref disableGroupVFX))
                    {
                        perms.DisableGroupVFX = disableGroupVFX;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    UiSharedService.DrawHelpText("This setting will disable VFX sync for all non-sticky pairs in newly joined syncshells.");
                }
                else
                {
                    UiSharedService.ColorTextWrapped("Default Permission Settings unavailable for this service. " +
                        "You need to connect to this service to change the default permissions since they are stored on the service.", ImGuiColors.DalamudYellow);
                }

                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawSettingsContent()
    {
        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.TextUnformatted("Service " + _serverConfigurationManager.CurrentServer!.ServerName + ":");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedGreen, "Available");
            ImGui.SameLine();
            ImGui.TextUnformatted("(");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedGreen, _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture));
            ImGui.SameLine();
            ImGui.TextUnformatted("Users Online");
            ImGui.SameLine();
            ImGui.TextUnformatted(")");
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Community and Support:");
        ImGui.SameLine();
        if (ImGui.Button("Mare Synchronos Discord"))
        {
            Util.OpenLink("https://discord.gg/mpNdkrTRjW");
        }
        ImGui.Separator();
        if (ImGui.BeginTabBar("mainTabBar"))
        {
            if (ImGui.BeginTabItem("General"))
            {
                DrawGeneral();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Export & Storage"))
            {
                DrawFileStorageSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Transfers"))
            {
                DrawCurrentTransfers();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Service Settings"))
            {
                DrawServerConfiguration();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Debug"))
            {
                DrawDebug();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void UiSharedService_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiSharedService_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }
}