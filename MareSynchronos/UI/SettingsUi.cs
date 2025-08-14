using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MareSynchronos.UI;

public class SettingsUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly CacheMonitor _cacheMonitor;
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly HttpClient _httpClient;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileUploadManager _fileTransferManager;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly IpcManager _ipcManager;
    private readonly PairManager _pairManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiShared;
    private readonly IProgress<(int, int, FileCacheEntity)> _validationProgress;
    private (int, int, FileCacheEntity) _currentProgress;
    private bool _deleteAccountPopupModalShown = false;
    private bool _deleteFilesPopupModalShown = false;
    private string _lastTab = string.Empty;
    private bool? _notesSuccessfullyApplied = null;
    private bool _overwriteExistingLabels = false;
    private bool _readClearCache = false;
    private int _selectedEntry = -1;
    private string _uidToAddForIgnore = string.Empty;
    private CancellationTokenSource? _validationCts;
    private Task<List<FileCacheEntity>>? _validationTask;
    private bool _wasOpen = false;

    public SettingsUi(ILogger<SettingsUi> logger,
        UiSharedService uiShared, MareConfigService configService,
        PairManager pairManager,
        ServerConfigurationManager serverConfigurationManager,
        PlayerPerformanceConfigService playerPerformanceConfigService,
        MareMediator mediator, PerformanceCollectorService performanceCollector,
        FileUploadManager fileTransferManager,
        FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager,
        FileCompactor fileCompactor, ApiController apiController,
        IpcManager ipcManager, CacheMonitor cacheMonitor,
        DalamudUtilService dalamudUtilService, HttpClient httpClient) : base(logger, mediator, "Mare Synchronos Settings", performanceCollector)
    {
        _configService = configService;
        _pairManager = pairManager;
        _serverConfigurationManager = serverConfigurationManager;
        _playerPerformanceConfigService = playerPerformanceConfigService;
        _performanceCollector = performanceCollector;
        _fileTransferManager = fileTransferManager;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _apiController = apiController;
        _ipcManager = ipcManager;
        _cacheMonitor = cacheMonitor;
        _dalamudUtilService = dalamudUtilService;
        _httpClient = httpClient;
        _fileCompactor = fileCompactor;
        _uiShared = uiShared;
        AllowClickthrough = false;
        AllowPinning = false;
        _validationProgress = new Progress<(int, int, FileCacheEntity)>(v => _currentProgress = v);

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

    public override void OnOpen()
    {
        _uiShared.ResetOAuthTasksState();
        _speedTestCts = new();
    }

    public override void OnClose()
    {
        _uiShared.EditTrackerPosition = false;
        _uidToAddForIgnore = string.Empty;
        _secretKeysConversionCts = _secretKeysConversionCts.CancelRecreate();
        _downloadServersTask = null;
        _speedTestTask = null;
        _speedTestCts?.Cancel();
        _speedTestCts?.Dispose();
        _speedTestCts = null;

        base.OnClose();
    }

    protected override void DrawInternal()
    {
        _ = _uiShared.DrawOtherPluginState();

        DrawSettingsContent();
    }
    private static bool InputDtrColors(string label, ref DtrEntry.Colors colors)
    {
        using var id = ImRaii.PushId(label);
        var innerSpacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var foregroundColor = ConvertColor(colors.Foreground);
        var glowColor = ConvertColor(colors.Glow);

        var ret = ImGui.ColorEdit3("###foreground", ref foregroundColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.Uint8);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Foreground Color - Set to pure black (#000000) to use the default color");

        ImGui.SameLine(0.0f, innerSpacing);
        ret |= ImGui.ColorEdit3("###glow", ref glowColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.Uint8);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Glow Color - Set to pure black (#000000) to use the default color");

        ImGui.SameLine(0.0f, innerSpacing);
        ImGui.TextUnformatted(label);

        if (ret)
            colors = new(ConvertBackColor(foregroundColor), ConvertBackColor(glowColor));

        return ret;

        static Vector3 ConvertColor(uint color)
            => unchecked(new((byte)color / 255.0f, (byte)(color >> 8) / 255.0f, (byte)(color >> 16) / 255.0f));

        static uint ConvertBackColor(Vector3 color)
            => byte.CreateSaturating(color.X * 255.0f) | ((uint)byte.CreateSaturating(color.Y * 255.0f) << 8) | ((uint)byte.CreateSaturating(color.Z * 255.0f) << 16);
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
        _uiShared.BigText("Transfer Settings");

        int maxParallelDownloads = _configService.Current.ParallelDownloads;
        bool useAlternativeUpload = _configService.Current.UseAlternativeFileUpload;
        int downloadSpeedLimit = _configService.Current.DownloadSpeedLimitInBytes;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Global Download Speed Limit");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("###speedlimit", ref downloadSpeedLimit))
        {
            _configService.Current.DownloadSpeedLimitInBytes = downloadSpeedLimit;
            _configService.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        _uiShared.DrawCombo("###speed", [DownloadSpeeds.Bps, DownloadSpeeds.KBps, DownloadSpeeds.MBps],
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
        _uiShared.DrawHelpText("This will attempt to upload files in one go instead of a stream. Typically not necessary to enable. Use if you have upload issues.");

        ImGui.Separator();
        _uiShared.BigText("Transfer UI");

        bool showTransferWindow = _configService.Current.ShowTransferWindow;
        if (ImGui.Checkbox("Show separate transfer window", ref showTransferWindow))
        {
            _configService.Current.ShowTransferWindow = showTransferWindow;
            _configService.Save();
        }
        _uiShared.DrawHelpText($"The download window will show the current progress of outstanding downloads.{Environment.NewLine}{Environment.NewLine}" +
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
        _uiShared.DrawHelpText("This will render a progress bar during the download at the feet of the player you are downloading from.");

        if (!showTransferBars) ImGui.BeginDisabled();
        ImGui.Indent();
        bool transferBarShowText = _configService.Current.TransferBarsShowText;
        if (ImGui.Checkbox("Show Download Text", ref transferBarShowText))
        {
            _configService.Current.TransferBarsShowText = transferBarShowText;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Shows download text (amount of MiB downloaded) in the transfer bars");
        int transferBarWidth = _configService.Current.TransferBarsWidth;
        if (ImGui.SliderInt("Transfer Bar Width", ref transferBarWidth, 10, 500))
        {
            _configService.Current.TransferBarsWidth = transferBarWidth;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Width of the displayed transfer bars (will never be less wide than the displayed text)");
        int transferBarHeight = _configService.Current.TransferBarsHeight;
        if (ImGui.SliderInt("Transfer Bar Height", ref transferBarHeight, 2, 50))
        {
            _configService.Current.TransferBarsHeight = transferBarHeight;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Height of the displayed transfer bars (will never be less tall than the displayed text)");
        bool showUploading = _configService.Current.ShowUploading;
        if (ImGui.Checkbox("Show 'Uploading' text below players that are currently uploading", ref showUploading))
        {
            _configService.Current.ShowUploading = showUploading;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will render an 'Uploading' text at the feet of the player that is in progress of uploading data.");

        ImGui.Unindent();
        if (!showUploading) ImGui.BeginDisabled();
        ImGui.Indent();
        bool showUploadingBigText = _configService.Current.ShowUploadingBigText;
        if (ImGui.Checkbox("Large font for 'Uploading' text", ref showUploadingBigText))
        {
            _configService.Current.ShowUploadingBigText = showUploadingBigText;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will render an 'Uploading' text in a larger font.");

        ImGui.Unindent();

        if (!showUploading) ImGui.EndDisabled();
        if (!showTransferBars) ImGui.EndDisabled();

        if (_apiController.IsConnected)
        {
            ImGuiHelpers.ScaledDummy(5);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(10);
            using var tree = ImRaii.TreeNode("Speed Test to Servers");
            if (tree)
            {
                if (_downloadServersTask == null || ((_downloadServersTask?.IsCompleted ?? false) && (!_downloadServersTask?.IsCompletedSuccessfully ?? false)))
                {
                    if (_uiShared.IconTextButton(FontAwesomeIcon.GroupArrowsRotate, "Update Download Server List"))
                    {
                        _downloadServersTask = GetDownloadServerList();
                    }
                }
                if (_downloadServersTask != null && _downloadServersTask.IsCompleted && !_downloadServersTask.IsCompletedSuccessfully)
                {
                    UiSharedService.ColorTextWrapped("Failed to get download servers from service, see /xllog for more information", ImGuiColors.DalamudRed);
                }
                if (_downloadServersTask != null && _downloadServersTask.IsCompleted && _downloadServersTask.IsCompletedSuccessfully)
                {
                    if (_speedTestTask == null || _speedTestTask.IsCompleted)
                    {
                        if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowRight, "Start Speedtest"))
                        {
                            _speedTestTask = RunSpeedTest(_downloadServersTask.Result!, _speedTestCts?.Token ?? CancellationToken.None);
                        }
                    }
                    else if (!_speedTestTask.IsCompleted)
                    {
                        UiSharedService.ColorTextWrapped("Running Speedtest to File Servers...", ImGuiColors.DalamudYellow);
                        UiSharedService.ColorTextWrapped("Please be patient, depending on usage and load this can take a while.", ImGuiColors.DalamudYellow);
                        if (_uiShared.IconTextButton(FontAwesomeIcon.Ban, "Cancel speedtest"))
                        {
                            _speedTestCts?.Cancel();
                            _speedTestCts?.Dispose();
                            _speedTestCts = new();
                        }
                    }
                    if (_speedTestTask != null && _speedTestTask.IsCompleted)
                    {
                        if (_speedTestTask.Result != null && _speedTestTask.Result.Count != 0)
                        {
                            foreach (var result in _speedTestTask.Result)
                            {
                                UiSharedService.TextWrapped(result);
                            }
                        }
                        else
                        {
                            UiSharedService.ColorTextWrapped("Speedtest completed with no results", ImGuiColors.DalamudYellow);
                        }
                    }
                }
            }
            ImGuiHelpers.ScaledDummy(10);
        }

        ImGui.Separator();
        _uiShared.BigText("Current Transfers");

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

    private Task<List<string>?>? _downloadServersTask = null;
    private Task<List<string>?>? _speedTestTask = null;
    private CancellationTokenSource? _speedTestCts;

    private async Task<List<string>?> RunSpeedTest(List<string> servers, CancellationToken token)
    {
        List<string> speedTestResults = new();
        foreach (var server in servers)
        {
            HttpResponseMessage? result = null;
            Stopwatch? st = null;
            try
            {
                result = await _fileTransferOrchestrator.SendRequestAsync(HttpMethod.Get, new Uri(new Uri(server), "speedtest/run"), token, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                result.EnsureSuccessStatusCode();
                using CancellationTokenSource speedtestTimeCts = new();
                speedtestTimeCts.CancelAfter(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(speedtestTimeCts.Token, token);
                long readBytes = 0;
                st = Stopwatch.StartNew();
                try
                {
                    var stream = await result.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
                    byte[] buffer = new byte[8192];
                    while (!speedtestTimeCts.Token.IsCancellationRequested)
                    {
                        var currentBytes = await stream.ReadAsync(buffer, linkedCts.Token).ConfigureAwait(false);
                        if (currentBytes == 0)
                            break;
                        readBytes += currentBytes;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Speedtest to {server} cancelled", server);
                }
                st.Stop();
                _logger.LogInformation("Downloaded {bytes} from {server} in {time}", UiSharedService.ByteToString(readBytes), server, st.Elapsed);
                var bps = (long)((readBytes) / st.Elapsed.TotalSeconds);
                speedTestResults.Add($"{server}: ~{UiSharedService.ByteToString(bps)}/s");
            }
            catch (HttpRequestException ex)
            {
                if (result != null)
                {
                    var res = await result!.Content.ReadAsStringAsync().ConfigureAwait(false);
                    speedTestResults.Add($"{server}: {ex.Message} - {res}");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Speedtest on {server} cancelled", server);
                speedTestResults.Add($"{server}: Cancelled by user");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Some exception");
            }
            finally
            {
                st?.Stop();
            }
        }
        return speedTestResults;
    }

    private async Task<List<string>?> GetDownloadServerList()
    {
        try
        {
            var result = await _fileTransferOrchestrator.SendRequestAsync(HttpMethod.Get, new Uri(_fileTransferOrchestrator.FilesCdnUri!, "files/downloadServers"), CancellationToken.None).ConfigureAwait(false);
            result.EnsureSuccessStatusCode();
            return await JsonSerializer.DeserializeAsync<List<string>>(await result.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get download server list");
            throw;
        }
    }

    private void DrawDebug()
    {
        _lastTab = "Debug";

        _uiShared.BigText("Debug");
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
        if (_uiShared.IconTextButton(FontAwesomeIcon.Copy, "[DEBUG] Copy Last created Character Data to clipboard"))
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
        _uiShared.DrawHelpText("Enabling this can incur a (slight) performance impact. Enabling this for extended periods of time is not recommended.");

        using (ImRaii.Disabled(!logPerformance))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats to /xllog"))
            {
                _performanceCollector.PrintPerformanceStats();
            }
            ImGui.SameLine();
            if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats (last 60s) to /xllog"))
            {
                _performanceCollector.PrintPerformanceStats(60);
            }
        }

        bool stopWhining = _configService.Current.DebugStopWhining;
        if (ImGui.Checkbox("Do not notify for modified game files or enabled LOD", ref stopWhining))
        {
            _configService.Current.DebugStopWhining = stopWhining;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Having modified game files will still mark your logs with UNSUPPORTED and you will not receive support, message shown or not." + UiSharedService.TooltipSeparator
            + "Keeping LOD enabled can lead to more crashes. Use at your own risk.");
    }

    private void DrawFileStorageSettings()
    {
        _lastTab = "FileCache";

        _uiShared.BigText("Export MCDF");

        ImGuiHelpers.ScaledDummy(10);

        UiSharedService.ColorTextWrapped("Exporting MCDF has moved.", ImGuiColors.DalamudYellow);
        ImGuiHelpers.ScaledDummy(5);
        UiSharedService.TextWrapped("It is now found in the Main UI under \"Your User Menu\" (");
        ImGui.SameLine();
        _uiShared.IconText(FontAwesomeIcon.UserCog);
        ImGui.SameLine();
        UiSharedService.TextWrapped(") -> \"Character Data Hub\".");
        if (_uiShared.IconTextButton(FontAwesomeIcon.Running, "Open Mare Character Data Hub"))
        {
            Mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
        UiSharedService.TextWrapped("Note: this entry will be removed in the near future. Please use the Main UI to open the Character Data Hub.");
        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();

        _uiShared.BigText("Storage");

        UiSharedService.TextWrapped("Mare stores downloaded files from paired people permanently. This is to improve loading performance and requiring less downloads. " +
            "The storage governs itself by clearing data beyond the set storage size. Please set the storage size accordingly. It is not necessary to manually clear the storage.");

        _uiShared.DrawFileScanState();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Monitoring Penumbra Folder: " + (_cacheMonitor.PenumbraWatcher?.Path ?? "Not monitoring"));
        if (string.IsNullOrEmpty(_cacheMonitor.PenumbraWatcher?.Path))
        {
            ImGui.SameLine();
            using var id = ImRaii.PushId("penumbraMonitor");
            if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowsToCircle, "Try to reinitialize Monitor"))
            {
                _cacheMonitor.StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
            }
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Monitoring Mare Storage Folder: " + (_cacheMonitor.MareWatcher?.Path ?? "Not monitoring"));
        if (string.IsNullOrEmpty(_cacheMonitor.MareWatcher?.Path))
        {
            ImGui.SameLine();
            using var id = ImRaii.PushId("mareMonitor");
            if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowsToCircle, "Try to reinitialize Monitor"))
            {
                _cacheMonitor.StartMareWatcher(_configService.Current.CacheFolder);
            }
        }
        if (_cacheMonitor.MareWatcher == null || _cacheMonitor.PenumbraWatcher == null)
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Play, "Resume Monitoring"))
            {
                _cacheMonitor.StartMareWatcher(_configService.Current.CacheFolder);
                _cacheMonitor.StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
                _cacheMonitor.InvokeScan();
            }
            UiSharedService.AttachToolTip("Attempts to resume monitoring for both Penumbra and Mare Storage. "
                + "Resuming the monitoring will also force a full scan to run." + Environment.NewLine
                + "If the button remains present after clicking it, consult /xllog for errors");
        }
        else
        {
            using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
            {
                if (_uiShared.IconTextButton(FontAwesomeIcon.Stop, "Stop Monitoring"))
                {
                    _cacheMonitor.StopMonitoring();
                }
            }
            UiSharedService.AttachToolTip("Stops the monitoring for both Penumbra and Mare Storage. "
                + "Do not stop the monitoring, unless you plan to move the Penumbra and Mare Storage folders, to ensure correct functionality of Mare." + Environment.NewLine
                + "If you stop the monitoring to move folders around, resume it after you are finished moving the files."
                + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");
        }

        _uiShared.DrawCacheDirectorySetting();
        ImGui.AlignTextToFramePadding();
        if (_cacheMonitor.FileCacheSize >= 0)
            ImGui.TextUnformatted($"Currently utilized local storage: {UiSharedService.ByteToString(_cacheMonitor.FileCacheSize)}");
        else
            ImGui.TextUnformatted($"Currently utilized local storage: Calculating...");
        ImGui.TextUnformatted($"Remaining space free on drive: {UiSharedService.ByteToString(_cacheMonitor.FileCacheDriveFree)}");
        bool useFileCompactor = _configService.Current.UseCompactor;
        bool isLinux = _dalamudUtilService.IsWine;
        if (!useFileCompactor && !isLinux)
        {
            UiSharedService.ColorTextWrapped("Hint: To free up space when using Mare consider enabling the File Compactor", ImGuiColors.DalamudYellow);
        }
        if (isLinux || !_cacheMonitor.StorageisNTFS) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Use file compactor", ref useFileCompactor))
        {
            _configService.Current.UseCompactor = useFileCompactor;
            _configService.Save();
        }
        _uiShared.DrawHelpText("The file compactor can massively reduce your saved files. It might incur a minor penalty on loading files on a slow CPU." + Environment.NewLine
            + "It is recommended to leave it enabled to save on space.");
        ImGui.SameLine();
        if (!_fileCompactor.MassCompactRunning)
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.FileArchive, "Compact all files in storage"))
            {
                _ = Task.Run(() =>
                {
                    _fileCompactor.CompactStorage(compress: true);
                    _cacheMonitor.RecalculateFileCacheSize(CancellationToken.None);
                });
            }
            UiSharedService.AttachToolTip("This will run compression on all files in your current Mare Storage." + Environment.NewLine
                + "You do not need to run this manually if you keep the file compactor enabled.");
            ImGui.SameLine();
            if (_uiShared.IconTextButton(FontAwesomeIcon.File, "Decompact all files in storage"))
            {
                _ = Task.Run(() =>
                {
                    _fileCompactor.CompactStorage(compress: false);
                    _cacheMonitor.RecalculateFileCacheSize(CancellationToken.None);
                });
            }
            UiSharedService.AttachToolTip("This will run decompression on all files in your current Mare Storage.");
        }
        else
        {
            UiSharedService.ColorText($"File compactor currently running ({_fileCompactor.Progress})", ImGuiColors.DalamudYellow);
        }
        if (isLinux || !_cacheMonitor.StorageisNTFS)
        {
            ImGui.EndDisabled();
            ImGui.TextUnformatted("The file compactor is only available on Windows and NTFS drives.");
        }
        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        ImGui.Separator();
        UiSharedService.TextWrapped("File Storage validation can make sure that all files in your local Mare Storage are valid. " +
            "Run the validation before you clear the Storage for no reason. " + Environment.NewLine +
            "This operation, depending on how many files you have in your storage, can take a while and will be CPU and drive intensive.");
        using (ImRaii.Disabled(_validationTask != null && !_validationTask.IsCompleted))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Check, "Start File Storage Validation"))
            {
                _validationCts?.Cancel();
                _validationCts?.Dispose();
                _validationCts = new();
                var token = _validationCts.Token;
                _validationTask = Task.Run(() => _fileCacheManager.ValidateLocalIntegrity(_validationProgress, token));
            }
        }
        if (_validationTask != null && !_validationTask.IsCompleted)
        {
            ImGui.SameLine();
            if (_uiShared.IconTextButton(FontAwesomeIcon.Times, "Cancel"))
            {
                _validationCts?.Cancel();
            }
        }

        if (_validationTask != null)
        {
            using (ImRaii.PushIndent(20f))
            {
                if (_validationTask.IsCompleted)
                {
                    UiSharedService.TextWrapped($"The storage validation has completed and removed {_validationTask.Result.Count} invalid files from storage.");
                }
                else
                {

                    UiSharedService.TextWrapped($"Storage validation is running: {_currentProgress.Item1}/{_currentProgress.Item2}");
                    UiSharedService.TextWrapped($"Current item: {_currentProgress.Item3.ResolvedFilepath}");
                }
            }
        }
        ImGui.Separator();

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
        if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Clear local storage") && UiSharedService.CtrlPressed() && _readClearCache)
        {
            _ = Task.Run(() =>
            {
                foreach (var file in Directory.GetFiles(_configService.Current.CacheFolder))
                {
                    File.Delete(file);
                }
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
        //UiSharedService.FontText("Experimental", _uiShared.UidFont);
        //ImGui.Separator();

        _uiShared.BigText("Notes");
        if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Export all your user notes to clipboard"))
        {
            ImGui.SetClipboardText(UiSharedService.GetNotes(_pairManager.DirectPairs.UnionBy(_pairManager.GroupPairs.SelectMany(p => p.Value), p => p.UserData, UserDataComparer.Instance).ToList()));
        }
        if (_uiShared.IconTextButton(FontAwesomeIcon.FileImport, "Import notes from clipboard"))
        {
            _notesSuccessfullyApplied = null;
            var notes = ImGui.GetClipboardText();
            _notesSuccessfullyApplied = _uiShared.ApplyNotesFromClipboard(notes, _overwriteExistingLabels);
        }

        ImGui.SameLine();
        ImGui.Checkbox("Overwrite existing notes", ref _overwriteExistingLabels);
        _uiShared.DrawHelpText("If this option is selected all already existing notes for UIDs will be overwritten by the imported notes.");
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
        _uiShared.DrawHelpText("This will open a popup that allows you to set the notes for a user after successfully adding them to your individual pairs.");

        var autoPopulateNotes = _configService.Current.AutoPopulateEmptyNotesFromCharaName;
        if (ImGui.Checkbox("Automatically populate notes using player names", ref autoPopulateNotes))
        {
            _configService.Current.AutoPopulateEmptyNotesFromCharaName = autoPopulateNotes;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will automatically populate user notes using the first encountered player name if the note was not set prior");

        ImGui.Separator();
        _uiShared.BigText("UI");
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
        var useColorsInDtr = _configService.Current.UseColorsInDtr;
        var dtrColorsDefault = _configService.Current.DtrColorsDefault;
        var dtrColorsNotConnected = _configService.Current.DtrColorsNotConnected;
        var dtrColorsPairsInRange = _configService.Current.DtrColorsPairsInRange;
        var preferNotesInsteadOfName = _configService.Current.PreferNotesOverNamesForVisible;
        var useFocusTarget = _configService.Current.UseFocusTarget;
        var groupUpSyncshells = _configService.Current.GroupUpSyncshells;
        var groupInVisible = _configService.Current.ShowSyncshellUsersInVisible;
        var syncshellOfflineSeparate = _configService.Current.ShowSyncshellOfflineUsersSeparately;

        if (ImGui.Checkbox("Enable Game Right Click Menu Entries", ref enableRightClickMenu))
        {
            _configService.Current.EnableRightClickMenus = enableRightClickMenu;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will add Mare related right click menu entries in the game UI on paired players.");

        if (ImGui.Checkbox("Display status and visible pair count in Server Info Bar", ref enableDtrEntry))
        {
            _configService.Current.EnableDtrEntry = enableDtrEntry;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will add Mare connection status and visible pair count in the Server Info Bar.\nYou can further configure this through your Dalamud Settings.");

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

            if (ImGui.Checkbox("Color-code the Server Info Bar entry according to status", ref useColorsInDtr))
            {
                _configService.Current.UseColorsInDtr = useColorsInDtr;
                _configService.Save();
            }

            using (ImRaii.Disabled(!useColorsInDtr))
            {
                using var indent2 = ImRaii.PushIndent();
                if (InputDtrColors("Default", ref dtrColorsDefault))
                {
                    _configService.Current.DtrColorsDefault = dtrColorsDefault;
                    _configService.Save();
                }

                ImGui.SameLine();
                if (InputDtrColors("Not Connected", ref dtrColorsNotConnected))
                {
                    _configService.Current.DtrColorsNotConnected = dtrColorsNotConnected;
                    _configService.Save();
                }

                ImGui.SameLine();
                if (InputDtrColors("Pairs in Range", ref dtrColorsPairsInRange))
                {
                    _configService.Current.DtrColorsPairsInRange = dtrColorsPairsInRange;
                    _configService.Save();
                }
            }
        }

        if (ImGui.Checkbox("Show separate Visible group", ref showVisibleSeparate))
        {
            _configService.Current.ShowVisibleUsersSeparately = showVisibleSeparate;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("This will show all currently visible users in a special 'Visible' group in the main UI.");

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
        _uiShared.DrawHelpText("This will show all currently offline users in a special 'Offline' group in the main UI.");

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
        _uiShared.DrawHelpText("This will group up all Syncshells in a special 'All Syncshells' folder in the main UI.");

        if (ImGui.Checkbox("Show player name for visible players", ref showNameInsteadOfNotes))
        {
            _configService.Current.ShowCharacterNameInsteadOfNotesForVisible = showNameInsteadOfNotes;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("This will show the character name instead of custom set note when a character is visible");

        ImGui.Indent();
        if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Prefer notes over player names for visible players", ref preferNotesInsteadOfName))
        {
            _configService.Current.PreferNotesOverNamesForVisible = preferNotesInsteadOfName;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("If you set a note for a player it will be shown instead of the player name");
        if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.EndDisabled();
        ImGui.Unindent();

        if (ImGui.Checkbox("Set visible pairs as focus targets when clicking the eye", ref useFocusTarget))
        {
            _configService.Current.UseFocusTarget = useFocusTarget;
            _configService.Save();
        }

        if (ImGui.Checkbox("Show Mare Profiles on Hover", ref showProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesShow = showProfiles;
            _configService.Save();
        }
        _uiShared.DrawHelpText("This will show the configured user profile after a set delay");
        ImGui.Indent();
        if (!showProfiles) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Popout profiles on the right", ref profileOnRight))
        {
            _configService.Current.ProfilePopoutRight = profileOnRight;
            _configService.Save();
            Mediator.Publish(new CompactUiChange(Vector2.Zero, Vector2.Zero));
        }
        _uiShared.DrawHelpText("Will show profiles on the right side of the main UI");
        if (ImGui.SliderFloat("Hover Delay", ref profileDelay, 1, 10))
        {
            _configService.Current.ProfileDelay = profileDelay;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Delay until the profile should be displayed");
        if (!showProfiles) ImGui.EndDisabled();
        ImGui.Unindent();
        if (ImGui.Checkbox("Show profiles marked as NSFW", ref showNsfwProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesAllowNsfw = showNsfwProfiles;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Will show profiles that have the NSFW tag enabled");

        ImGui.Separator();

        var disableOptionalPluginWarnings = _configService.Current.DisableOptionalPluginWarnings;
        var onlineNotifs = _configService.Current.ShowOnlineNotifications;
        var onlineNotifsPairsOnly = _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs;
        var onlineNotifsNamedOnly = _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs;
        _uiShared.BigText("Notifications");

        _uiShared.DrawCombo("Info Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.InfoNotification = i;
            _configService.Save();
        }, _configService.Current.InfoNotification);
        _uiShared.DrawHelpText("The location where \"Info\" notifications will display."
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
        _uiShared.DrawHelpText("The location where \"Warning\" notifications will display."
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
        _uiShared.DrawHelpText("The location where \"Error\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Error notifications"
                              + Environment.NewLine + "'Chat' will print Error notifications in chat"
                              + Environment.NewLine + "'Toast' will show Error toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        if (ImGui.Checkbox("Disable optional plugin warnings", ref disableOptionalPluginWarnings))
        {
            _configService.Current.DisableOptionalPluginWarnings = disableOptionalPluginWarnings;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will not show any \"Warning\" labeled messages for missing optional plugins.");
        if (ImGui.Checkbox("Enable online notifications", ref onlineNotifs))
        {
            _configService.Current.ShowOnlineNotifications = onlineNotifs;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will show a small notification (type: Info) in the bottom right corner when pairs go online.");

        using var disabled = ImRaii.Disabled(!onlineNotifs);
        if (ImGui.Checkbox("Notify only for individual pairs", ref onlineNotifsPairsOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs = onlineNotifsPairsOnly;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will only show online notifications (type: Info) for individual pairs.");
        if (ImGui.Checkbox("Notify only for named pairs", ref onlineNotifsNamedOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs = onlineNotifsNamedOnly;
            _configService.Save();
        }
        _uiShared.DrawHelpText("Enabling this will only show online notifications (type: Info) for pairs where you have set an individual note.");
    }

    private void DrawPerformance()
    {
        _uiShared.BigText("Performance Settings");
        UiSharedService.TextWrapped("The configuration options here are to give you more informed warnings and automation when it comes to other performance-intensive synced players.");
        ImGui.Dummy(new Vector2(10));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(10));
        bool showPerformanceIndicator = _playerPerformanceConfigService.Current.ShowPerformanceIndicator;
        if (ImGui.Checkbox("Show performance indicator", ref showPerformanceIndicator))
        {
            _playerPerformanceConfigService.Current.ShowPerformanceIndicator = showPerformanceIndicator;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("Will show a performance indicator when players exceed defined thresholds in Mares UI." + Environment.NewLine + "Will use warning thresholds.");
        bool warnOnExceedingThresholds = _playerPerformanceConfigService.Current.WarnOnExceedingThresholds;
        if (ImGui.Checkbox("Warn on loading in players exceeding performance thresholds", ref warnOnExceedingThresholds))
        {
            _playerPerformanceConfigService.Current.WarnOnExceedingThresholds = warnOnExceedingThresholds;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("Mare will print a warning in chat once per session of meeting those people. Will not warn on players with preferred permissions.");
        using (ImRaii.Disabled(!warnOnExceedingThresholds && !showPerformanceIndicator))
        {
            using var indent = ImRaii.PushIndent();
            var warnOnPref = _playerPerformanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds;
            if (ImGui.Checkbox("Warn/Indicate also on players with preferred permissions", ref warnOnPref))
            {
                _playerPerformanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds = warnOnPref;
                _playerPerformanceConfigService.Save();
            }
            _uiShared.DrawHelpText("Mare will also print warnings and show performance indicator for players where you enabled preferred permissions. If warning in general is disabled, this will not produce any warnings.");
        }
        using (ImRaii.Disabled(!showPerformanceIndicator && !warnOnExceedingThresholds))
        {
            var vram = _playerPerformanceConfigService.Current.VRAMSizeWarningThresholdMiB;
            var tris = _playerPerformanceConfigService.Current.TrisWarningThresholdThousands;
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Warning VRAM threshold", ref vram))
            {
                _playerPerformanceConfigService.Current.VRAMSizeWarningThresholdMiB = vram;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(MiB)");
            _uiShared.DrawHelpText("Limit in MiB of approximate VRAM usage to trigger warning or performance indicator on UI." + UiSharedService.TooltipSeparator
                + "Default: 375 MiB");
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Warning Triangle threshold", ref tris))
            {
                _playerPerformanceConfigService.Current.TrisWarningThresholdThousands = tris;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(thousand triangles)");
            _uiShared.DrawHelpText("Limit in approximate used triangles from mods to trigger warning or performance indicator on UI." + UiSharedService.TooltipSeparator
                + "Default: 165 thousand");
        }
        ImGui.Dummy(new Vector2(10));
        bool autoPause = _playerPerformanceConfigService.Current.AutoPausePlayersExceedingThresholds;
        bool autoPauseEveryone = _playerPerformanceConfigService.Current.AutoPausePlayersWithPreferredPermissionsExceedingThresholds;
        if (ImGui.Checkbox("Automatically pause players exceeding thresholds", ref autoPause))
        {
            _playerPerformanceConfigService.Current.AutoPausePlayersExceedingThresholds = autoPause;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("When enabled, it will automatically pause all players without preferred permissions that exceed the thresholds defined below." + Environment.NewLine
            + "Will print a warning in chat when a player got paused automatically."
            + UiSharedService.TooltipSeparator + "Warning: this will not automatically unpause those people again, you will have to do this manually.");
        using (ImRaii.Disabled(!autoPause))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("Automatically pause also players with preferred permissions", ref autoPauseEveryone))
            {
                _playerPerformanceConfigService.Current.AutoPausePlayersWithPreferredPermissionsExceedingThresholds = autoPauseEveryone;
                _playerPerformanceConfigService.Save();
            }
            _uiShared.DrawHelpText("When enabled, will automatically pause all players regardless of preferred permissions that exceed thresholds defined below." + UiSharedService.TooltipSeparator +
                "Warning: this will not automatically unpause those people again, you will have to do this manually.");
            var vramAuto = _playerPerformanceConfigService.Current.VRAMSizeAutoPauseThresholdMiB;
            var trisAuto = _playerPerformanceConfigService.Current.TrisAutoPauseThresholdThousands;
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Auto Pause VRAM threshold", ref vramAuto))
            {
                _playerPerformanceConfigService.Current.VRAMSizeAutoPauseThresholdMiB = vramAuto;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(MiB)");
            _uiShared.DrawHelpText("When a loading in player and their VRAM usage exceeds this amount, automatically pauses the synced player." + UiSharedService.TooltipSeparator
                + "Default: 550 MiB");
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("Auto Pause Triangle threshold", ref trisAuto))
            {
                _playerPerformanceConfigService.Current.TrisAutoPauseThresholdThousands = trisAuto;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(thousand triangles)");
            _uiShared.DrawHelpText("When a loading in player and their triangle count exceeds this amount, automatically pauses the synced player." + UiSharedService.TooltipSeparator
                + "Default: 250 thousand");
        }
        ImGui.Dummy(new Vector2(10));
        _uiShared.BigText("Whitelisted UIDs");
        UiSharedService.TextWrapped("The entries in the list below will be ignored for all warnings and auto pause operations.");
        ImGui.Dummy(new Vector2(10));
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.InputText("##ignoreuid", ref _uidToAddForIgnore, 20);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrEmpty(_uidToAddForIgnore)))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Add UID/Vanity ID to whitelist"))
            {
                if (!_playerPerformanceConfigService.Current.UIDsToIgnore.Contains(_uidToAddForIgnore, StringComparer.Ordinal))
                {
                    _playerPerformanceConfigService.Current.UIDsToIgnore.Add(_uidToAddForIgnore);
                    _playerPerformanceConfigService.Save();
                }
                _uidToAddForIgnore = string.Empty;
            }
        }
        _uiShared.DrawHelpText("Hint: UIDs are case sensitive.");
        var playerList = _playerPerformanceConfigService.Current.UIDsToIgnore;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        using (var lb = ImRaii.ListBox("UID whitelist"))
        {
            if (lb)
            {
                for (int i = 0; i < playerList.Count; i++)
                {
                    bool shouldBeSelected = _selectedEntry == i;
                    if (ImGui.Selectable(playerList[i] + "##" + i, shouldBeSelected))
                    {
                        _selectedEntry = i;
                    }
                }
            }
        }
        using (ImRaii.Disabled(_selectedEntry == -1))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete selected UID"))
            {
                _playerPerformanceConfigService.Current.UIDsToIgnore.RemoveAt(_selectedEntry);
                _selectedEntry = -1;
                _playerPerformanceConfigService.Save();
            }
        }
    }

    private void DrawServerConfiguration()
    {
        _lastTab = "Service Settings";
        if (ApiController.ServerAlive)
        {
            _uiShared.BigText("Service Actions");
            ImGuiHelpers.ScaledDummy(new Vector2(5, 5));
            if (ImGui.Button("Delete all my files"))
            {
                _deleteFilesPopupModalShown = true;
                ImGui.OpenPopup("Delete all your files?");
            }

            _uiShared.DrawHelpText("Completely deletes all your uploaded files on the service.");

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

            _uiShared.DrawHelpText("Completely deletes your account and all uploaded files to the service.");

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

        _uiShared.BigText("Service & Character Settings");
        ImGuiHelpers.ScaledDummy(new Vector2(5, 5));
        var sendCensus = _serverConfigurationManager.SendCensusData;
        if (ImGui.Checkbox("Send Statistical Census Data", ref sendCensus))
        {
            _serverConfigurationManager.SendCensusData = sendCensus;
        }
        _uiShared.DrawHelpText("This will allow sending census data to the currently connected service." + UiSharedService.TooltipSeparator
            + "Census data contains:" + Environment.NewLine
            + "- Current World" + Environment.NewLine
            + "- Current Gender" + Environment.NewLine
            + "- Current Race" + Environment.NewLine
            + "- Current Clan (this is not your Free Company, this is e.g. Keeper or Seeker for Miqo'te)" + UiSharedService.TooltipSeparator
            + "The census data is only saved temporarily and will be removed from the server on disconnect. It is stored temporarily associated with your UID while you are connected." + UiSharedService.TooltipSeparator
            + "If you do not wish to participate in the statistical census, untick this box and reconnect to the server.");
        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        var idx = _uiShared.DrawServiceSelection();
        if (_lastSelectedServerIndex != idx)
        {
            _uiShared.ResetOAuthTasksState();
            _secretKeysConversionCts = _secretKeysConversionCts.CancelRecreate();
            _secretKeysConversionTask = null;
            _lastSelectedServerIndex = idx;
        }

        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        var selectedServer = _serverConfigurationManager.GetServerByIndex(idx);
        if (selectedServer == _serverConfigurationManager.CurrentServer)
        {
            UiSharedService.ColorTextWrapped("For any changes to be applied to the current service you need to reconnect to the service.", ImGuiColors.DalamudYellow);
        }

        bool useOauth = selectedServer.UseOAuth2;

        if (ImGui.BeginTabBar("serverTabBar"))
        {
            if (ImGui.BeginTabItem("Character Management"))
            {
                if (selectedServer.SecretKeys.Any() || useOauth)
                {
                    UiSharedService.ColorTextWrapped("Characters listed here will automatically connect to the selected Mare service with the settings as provided below." +
                        " Make sure to enter the character names correctly or use the 'Add current character' button at the bottom.", ImGuiColors.DalamudYellow);
                    int i = 0;
                    _uiShared.DrawUpdateOAuthUIDsButton(selectedServer);

                    if (selectedServer.UseOAuth2 && !string.IsNullOrEmpty(selectedServer.OAuthToken))
                    {
                        bool hasSetSecretKeysButNoUid = selectedServer.Authentications.Exists(u => u.SecretKeyIdx != -1 && string.IsNullOrEmpty(u.UID));
                        if (hasSetSecretKeysButNoUid)
                        {
                            ImGui.Dummy(new(5f, 5f));
                            UiSharedService.TextWrapped("Some entries have been detected that have previously been assigned secret keys but not UIDs. " +
                                "Press this button below to attempt to convert those entries.");
                            using (ImRaii.Disabled(_secretKeysConversionTask != null && !_secretKeysConversionTask.IsCompleted))
                            {
                                if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowsLeftRight, "Try to Convert Secret Keys to UIDs"))
                                {
                                    _secretKeysConversionTask = ConvertSecretKeysToUIDs(selectedServer, _secretKeysConversionCts.Token);
                                }
                            }
                            if (_secretKeysConversionTask != null && !_secretKeysConversionTask.IsCompleted)
                            {
                                UiSharedService.ColorTextWrapped("Converting Secret Keys to UIDs", ImGuiColors.DalamudYellow);
                            }
                            if (_secretKeysConversionTask != null && _secretKeysConversionTask.IsCompletedSuccessfully)
                            {
                                Vector4? textColor = null;
                                if (_secretKeysConversionTask.Result.PartialSuccess)
                                {
                                    textColor = ImGuiColors.DalamudYellow;
                                }
                                if (!_secretKeysConversionTask.Result.Success)
                                {
                                    textColor = ImGuiColors.DalamudRed;
                                }
                                string text = $"Conversion has completed: {_secretKeysConversionTask.Result.Result}";
                                if (textColor == null)
                                {
                                    UiSharedService.TextWrapped(text);
                                }
                                else
                                {
                                    UiSharedService.ColorTextWrapped(text, textColor!.Value);
                                }
                                if (!_secretKeysConversionTask.Result.Success || _secretKeysConversionTask.Result.PartialSuccess)
                                {
                                    UiSharedService.TextWrapped("In case of conversion failures, please set the UIDs for the failed conversions manually.");
                                }
                            }
                        }
                    }
                    ImGui.Separator();
                    string youName = _dalamudUtilService.GetPlayerName();
                    uint youWorld = _dalamudUtilService.GetHomeWorldId();
                    ulong youCid = _dalamudUtilService.GetCID();
                    if (!selectedServer.Authentications.Exists(a => string.Equals(a.CharacterName, youName, StringComparison.Ordinal) && a.WorldId == youWorld))
                    {
                        _uiShared.BigText("Your Character is not Configured", ImGuiColors.DalamudRed);
                        UiSharedService.ColorTextWrapped("You have currently no character configured that corresponds to your current name and world.", ImGuiColors.DalamudRed);
                        var authWithCid = selectedServer.Authentications.Find(f => f.LastSeenCID == youCid);
                        if (authWithCid != null)
                        {
                            ImGuiHelpers.ScaledDummy(5);
                            UiSharedService.ColorText("A potential rename/world change from this character was detected:", ImGuiColors.DalamudYellow);
                            using (ImRaii.PushIndent(10f))
                                UiSharedService.ColorText("Entry: " + authWithCid.CharacterName + " - " + _dalamudUtilService.WorldData.Value[(ushort)authWithCid.WorldId], ImGuiColors.ParsedGreen);
                            UiSharedService.ColorText("Press the button below to adjust that entry to your current character:", ImGuiColors.DalamudYellow);
                            using (ImRaii.PushIndent(10f))
                                UiSharedService.ColorText("Current: " + youName + " - " + _dalamudUtilService.WorldData.Value[(ushort)youWorld], ImGuiColors.ParsedGreen);
                            ImGuiHelpers.ScaledDummy(5);
                            if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowRight, "Update Entry to Current Character"))
                            {
                                authWithCid.CharacterName = youName;
                                authWithCid.WorldId = youWorld;
                                _serverConfigurationManager.Save();
                            }
                        }
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.Separator();
                        ImGuiHelpers.ScaledDummy(5);
                    }
                    foreach (var item in selectedServer.Authentications.ToList())
                    {
                        using var charaId = ImRaii.PushId("selectedChara" + i);

                        var worldIdx = (ushort)item.WorldId;
                        var data = _uiShared.WorldData.OrderBy(u => u.Value, StringComparer.Ordinal).ToDictionary(k => k.Key, k => k.Value);
                        if (!data.TryGetValue(worldIdx, out string? worldPreview))
                        {
                            worldPreview = data.First().Value;
                        }

                        Dictionary<int, SecretKey> keys = [];

                        if (!useOauth)
                        {
                            var secretKeyIdx = item.SecretKeyIdx;
                            keys = selectedServer.SecretKeys;
                            if (!keys.TryGetValue(secretKeyIdx, out var secretKey))
                            {
                                secretKey = new();
                            }
                        }

                        bool thisIsYou = false;
                        if (string.Equals(youName, item.CharacterName, StringComparison.OrdinalIgnoreCase)
                            && youWorld == worldIdx)
                        {
                            thisIsYou = true;
                        }
                        bool misManaged = false;
                        if (selectedServer.UseOAuth2 && !string.IsNullOrEmpty(selectedServer.OAuthToken) && string.IsNullOrEmpty(item.UID))
                        {
                            misManaged = true;
                        }
                        if (!selectedServer.UseOAuth2 && item.SecretKeyIdx == -1)
                        {
                            misManaged = true;
                        }
                        Vector4 color = ImGuiColors.ParsedGreen;
                        string text = thisIsYou ? "Your Current Character" : string.Empty;
                        if (misManaged)
                        {
                            text += " [MISMANAGED (" + (selectedServer.UseOAuth2 ? "No UID Set" : "No Secret Key Set") + ")]";
                            color = ImGuiColors.DalamudRed;
                        }
                        if (selectedServer.Authentications.Where(e => e != item).Any(e => string.Equals(e.CharacterName, item.CharacterName, StringComparison.Ordinal)
                            && e.WorldId == item.WorldId))
                        {
                            text += " [DUPLICATE]";
                            color = ImGuiColors.DalamudRed;
                        }

                        if (!string.IsNullOrEmpty(text))
                        {
                            text = text.Trim();
                            _uiShared.BigText(text, color);
                        }

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

                        if (!useOauth)
                        {
                            _uiShared.DrawCombo("Secret Key###" + item.CharacterName + i, keys, (w) => w.Value.FriendlyName,
                                (w) =>
                                {
                                    if (w.Key != item.SecretKeyIdx)
                                    {
                                        item.SecretKeyIdx = w.Key;
                                        _serverConfigurationManager.Save();
                                    }
                                }, EqualityComparer<KeyValuePair<int, SecretKey>>.Default.Equals(keys.FirstOrDefault(f => f.Key == item.SecretKeyIdx), default) ? keys.First() : keys.First(f => f.Key == item.SecretKeyIdx));
                        }
                        else
                        {
                            _uiShared.DrawUIDComboForAuthentication(i, item, selectedServer.ServerUri, _logger);
                        }
                        bool isAutoLogin = item.AutoLogin;
                        if (ImGui.Checkbox("Automatically login to Mare", ref isAutoLogin))
                        {
                            item.AutoLogin = isAutoLogin;
                            _serverConfigurationManager.Save();
                        }
                        _uiShared.DrawHelpText("When enabled and logging into this character in XIV, Mare will automatically connect to the current service.");
                        if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Character") && UiSharedService.CtrlPressed())
                            _serverConfigurationManager.RemoveCharacterFromServer(idx, item);
                        UiSharedService.AttachToolTip("Hold CTRL to delete this entry.");

                        i++;
                        if (item != selectedServer.Authentications.ToList()[^1])
                        {
                            ImGuiHelpers.ScaledDummy(5);
                            ImGui.Separator();
                            ImGuiHelpers.ScaledDummy(5);
                        }
                    }

                    if (selectedServer.Authentications.Any())
                        ImGui.Separator();

                    if (!selectedServer.Authentications.Exists(c => string.Equals(c.CharacterName, youName, StringComparison.Ordinal)
                        && c.WorldId == youWorld))
                    {
                        if (_uiShared.IconTextButton(FontAwesomeIcon.User, "Add current character"))
                        {
                            _serverConfigurationManager.AddCurrentCharacterToServer(idx);
                        }
                        ImGui.SameLine();
                    }

                    if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Add new character"))
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

            if (!useOauth && ImGui.BeginTabItem("Secret Key Management"))
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
                        if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Secret Key") && UiSharedService.CtrlPressed())
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
                if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Add new Secret Key"))
                {
                    selectedServer.SecretKeys.Add(selectedServer.SecretKeys.Any() ? selectedServer.SecretKeys.Max(p => p.Key) + 1 : 0, new SecretKey()
                    {
                        FriendlyName = "New Secret Key",
                    });
                    _serverConfigurationManager.Save();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Service Configuration"))
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
                    _uiShared.DrawHelpText("You cannot edit the URI of the main service.");
                }

                if (ImGui.InputText("Service Name", ref serverName, 255, flags))
                {
                    selectedServer.ServerName = serverName;
                    _serverConfigurationManager.Save();
                }
                if (isMain)
                {
                    _uiShared.DrawHelpText("You cannot edit the name of the main service.");
                }

                ImGui.SetNextItemWidth(200);
                var serverTransport = _serverConfigurationManager.GetTransport();
                _uiShared.DrawCombo("Server Transport Type", Enum.GetValues<HttpTransportType>().Where(t => t != HttpTransportType.None),
                    (v) => v.ToString(),
                    onSelected: (t) => _serverConfigurationManager.SetTransportType(t),
                    serverTransport);
                _uiShared.DrawHelpText("You normally do not need to change this, if you don't know what this is or what it's for, keep it to WebSockets." + Environment.NewLine
                    + "If you run into connection issues with e.g. VPNs, try ServerSentEvents first before trying out LongPolling." + UiSharedService.TooltipSeparator
                    + "Note: if the server does not support a specific Transport Type it will fall through to the next automatically: WebSockets > ServerSentEvents > LongPolling");

                if (_dalamudUtilService.IsWine)
                {
                    bool forceWebSockets = selectedServer.ForceWebSockets;
                    if (ImGui.Checkbox("[wine only] Force WebSockets", ref forceWebSockets))
                    {
                        selectedServer.ForceWebSockets = forceWebSockets;
                        _serverConfigurationManager.Save();
                    }
                    _uiShared.DrawHelpText("On wine, Mare will automatically fall back to ServerSentEvents/LongPolling, even if WebSockets is selected. "
                        + "WebSockets are known to crash XIV entirely on wine 8.5 shipped with Dalamud. "
                        + "Only enable this if you are not running wine 8.5." + Environment.NewLine
                        + "Note: If the issue gets resolved at some point this option will be removed.");
                }

                ImGuiHelpers.ScaledDummy(5);

                if (ImGui.Checkbox("Use Discord OAuth2 Authentication", ref useOauth))
                {
                    selectedServer.UseOAuth2 = useOauth;
                    _serverConfigurationManager.Save();
                }
                _uiShared.DrawHelpText("Use Discord OAuth2 Authentication to identify with this server instead of secret keys");
                if (useOauth)
                {
                    _uiShared.DrawOAuth(selectedServer);
                    if (string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)))
                    {
                        ImGuiHelpers.ScaledDummy(10f);
                        UiSharedService.ColorTextWrapped("You have enabled OAuth2 but it is not linked. Press the buttons Check, then Authenticate to link properly.", ImGuiColors.DalamudRed);
                    }
                    if (!string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer))
                        && selectedServer.Authentications.TrueForAll(u => string.IsNullOrEmpty(u.UID)))
                    {
                        ImGuiHelpers.ScaledDummy(10f);
                        UiSharedService.ColorTextWrapped("You have enabled OAuth2 but no characters configured. Set the correct UIDs for your characters in \"Character Management\".",
                            ImGuiColors.DalamudRed);
                    }
                }

                if (!isMain && selectedServer != _serverConfigurationManager.CurrentServer)
                {
                    ImGui.Separator();
                    if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Service") && UiSharedService.CtrlPressed())
                    {
                        _serverConfigurationManager.DeleteServer(selectedServer);
                    }
                    _uiShared.DrawHelpText("Hold CTRL to delete this service");
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Permission Settings"))
            {
                _uiShared.BigText("Default Permission Settings");
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
                    _uiShared.DrawHelpText("The preferred attribute means that the permissions to that user will never change through any of your permission changes to Syncshells " +
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
                    _uiShared.DrawHelpText("This setting will disable sound sync for all new individual pairs.");
                    if (ImGui.Checkbox("Disable individual pair animations", ref disableIndividualAnimations))
                    {
                        perms.DisableIndividualAnimations = disableIndividualAnimations;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("This setting will disable animation sync for all new individual pairs.");
                    if (ImGui.Checkbox("Disable individual pair VFX", ref disableIndividualVFX))
                    {
                        perms.DisableIndividualVFX = disableIndividualVFX;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("This setting will disable VFX sync for all new individual pairs.");
                    ImGuiHelpers.ScaledDummy(5f);
                    bool disableGroundSounds = perms.DisableGroupSounds;
                    bool disableGroupAnimations = perms.DisableGroupAnimations;
                    bool disableGroupVFX = perms.DisableGroupVFX;
                    if (ImGui.Checkbox("Disable Syncshell pair sounds", ref disableGroundSounds))
                    {
                        perms.DisableGroupSounds = disableGroundSounds;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("This setting will disable sound sync for all non-sticky pairs in newly joined syncshells.");
                    if (ImGui.Checkbox("Disable Syncshell pair animations", ref disableGroupAnimations))
                    {
                        perms.DisableGroupAnimations = disableGroupAnimations;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("This setting will disable animation sync for all non-sticky pairs in newly joined syncshells.");
                    if (ImGui.Checkbox("Disable Syncshell pair VFX", ref disableGroupVFX))
                    {
                        perms.DisableGroupVFX = disableGroupVFX;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("This setting will disable VFX sync for all non-sticky pairs in newly joined syncshells.");
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

    private int _lastSelectedServerIndex = -1;
    private Task<(bool Success, bool PartialSuccess, string Result)>? _secretKeysConversionTask = null;
    private CancellationTokenSource _secretKeysConversionCts = new CancellationTokenSource();

    private async Task<(bool Success, bool partialSuccess, string Result)> ConvertSecretKeysToUIDs(ServerStorage serverStorage, CancellationToken token)
    {
        List<Authentication> failedConversions = serverStorage.Authentications.Where(u => u.SecretKeyIdx == -1 && string.IsNullOrEmpty(u.UID)).ToList();
        List<Authentication> conversionsToAttempt = serverStorage.Authentications.Where(u => u.SecretKeyIdx != -1 && string.IsNullOrEmpty(u.UID)).ToList();
        List<Authentication> successfulConversions = [];
        Dictionary<string, List<Authentication>> secretKeyMapping = new(StringComparer.Ordinal);
        foreach (var authEntry in conversionsToAttempt)
        {
            if (!serverStorage.SecretKeys.TryGetValue(authEntry.SecretKeyIdx, out var secretKey))
            {
                failedConversions.Add(authEntry);
                continue;
            }

            if (!secretKeyMapping.TryGetValue(secretKey.Key, out List<Authentication>? authList))
            {
                secretKeyMapping[secretKey.Key] = authList = [];
            }

            authList.Add(authEntry);
        }

        if (secretKeyMapping.Count == 0)
        {
            return (false, false, $"Failed to convert {failedConversions.Count} entries: " + string.Join(", ", failedConversions.Select(k => k.CharacterName)));
        }

        var baseUri = serverStorage.ServerUri.Replace("wss://", "https://").Replace("ws://", "http://");
        var oauthCheckUri = MareAuth.GetUIDsBasedOnSecretKeyFullPath(new Uri(baseUri));
        var requestContent = JsonContent.Create(secretKeyMapping.Select(k => k.Key).ToList());
        HttpRequestMessage requestMessage = new(HttpMethod.Post, oauthCheckUri);
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serverStorage.OAuthToken);
        requestMessage.Content = requestContent;

        using var response = await _httpClient.SendAsync(requestMessage, token).ConfigureAwait(false);
        Dictionary<string, string>? secretKeyUidMapping = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>
            (await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
        if (secretKeyUidMapping == null)
        {
            return (false, false, $"Failed to parse the server response. Failed to convert all entries.");
        }

        foreach (var entry in secretKeyMapping)
        {
            if (!secretKeyUidMapping.TryGetValue(entry.Key, out var assignedUid) || string.IsNullOrEmpty(assignedUid))
            {
                failedConversions.AddRange(entry.Value);
                continue;
            }

            foreach (var auth in entry.Value)
            {
                auth.UID = assignedUid;
                successfulConversions.Add(auth);
            }
        }

        if (successfulConversions.Count > 0)
            _serverConfigurationManager.Save();

        StringBuilder sb = new();
        sb.Append("Conversion complete." + Environment.NewLine);
        sb.Append($"Successfully converted {successfulConversions.Count} entries." + Environment.NewLine);
        if (failedConversions.Count > 0)
        {
            sb.Append($"Failed to convert {failedConversions.Count} entries, assign those manually: ");
            sb.Append(string.Join(", ", failedConversions.Select(k => k.CharacterName)));
        }

        return (true, failedConversions.Count != 0, sb.ToString());
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

            if (ImGui.BeginTabItem("Performance"))
            {
                DrawPerformance();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Storage"))
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