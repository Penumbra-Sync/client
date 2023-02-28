using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.WebAPI;
using System.Numerics;
using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using Microsoft.Extensions.Logging;
using MareSynchronos.WebAPI.SignalR.Utils;
using MareSynchronos.PlayerData.Pairs;
using System.Text.Json;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Services;
using MareSynchronos.WebAPI.Files;

namespace MareSynchronos.UI;

public class SettingsUi : WindowMediatorSubscriberBase, IDisposable
{
    private readonly MareConfigService _configService;
    private readonly WindowSystem _windowSystem;
    private ApiController ApiController => _uiShared.ApiController;
    private readonly MareCharaFileManager _mareCharaFileManager;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly FileTransferManager _fileTransferManager;
    private readonly UiShared _uiShared;
    public CharacterData? LastCreatedCharacterData { private get; set; }

    private bool _overwriteExistingLabels = false;
    private bool? _notesSuccessfullyApplied = null;
    private string _lastTab = string.Empty;
    private bool _wasOpen = false;

    public SettingsUi(ILogger<SettingsUi> logger, WindowSystem windowSystem,
        UiShared uiShared, MareConfigService configService,
        MareCharaFileManager mareCharaFileManager, PairManager pairManager,
        ServerConfigurationManager serverConfigurationManager,
        MareMediator mediator, PerformanceCollectorService performanceCollector,
        FileTransferManager fileTransferManager) : base(logger, mediator, "Mare Synchronos Settings")
    {
        _logger.LogTrace("Creating " + nameof(SettingsUi));

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(800, 400),
            MaximumSize = new Vector2(800, 2000),
        };

        _configService = configService;
        _windowSystem = windowSystem;
        _mareCharaFileManager = mareCharaFileManager;
        _pairManager = pairManager;
        _serverConfigurationManager = serverConfigurationManager;
        _performanceCollector = performanceCollector;
        _fileTransferManager = fileTransferManager;
        _uiShared = uiShared;

        Mediator.Subscribe<OpenSettingsUiMessage>(this, (_) => Toggle());
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiShared_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiShared_GposeEnd());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) => LastCreatedCharacterData = ((CharacterDataCreatedMessage)msg).CharacterData);

        windowSystem.AddWindow(this);
    }

    private void UiShared_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiShared_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }

    public override void Dispose()
    {
        base.Dispose();
        _windowSystem.RemoveWindow(this);
    }

    public override void Draw()
    {
        _ = _uiShared.DrawOtherPluginState();

        DrawSettingsContent();
    }

    private void DrawSettingsContent()
    {
        _uiShared.PrintServerState();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Community and Support:");
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

            if (ApiController.ServerState is ServerState.Connected)
            {
                if (ImGui.BeginTabItem("Transfers"))
                {
                    DrawCurrentTransfers();
                    ImGui.EndTabItem();
                }
            }

            if (ImGui.BeginTabItem("Blocked Transfers"))
            {
                DrawBlockedTransfers();
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

    private void DrawServerConfiguration()
    {
        _lastTab = "Service Settings";
        if (ApiController.ServerAlive)
        {
            UiShared.FontText("Service Actions", _uiShared.UidFont);

            if (ImGui.Button("Delete all my files"))
            {
                _deleteFilesPopupModalShown = true;
                ImGui.OpenPopup("Delete all your files?");
            }

            UiShared.DrawHelpText("Completely deletes all your uploaded files on the service.");

            if (ImGui.BeginPopupModal("Delete all your files?", ref _deleteFilesPopupModalShown, UiShared.PopupWindowFlags))
            {
                UiShared.TextWrapped(
                    "All your own uploaded files on the service will be deleted.\nThis operation cannot be undone.");
                ImGui.Text("Are you sure you want to continue?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                 ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("Delete everything", new Vector2(buttonSize, 0)))
                {
                    Task.Run(() => _fileTransferManager.DeleteAllFiles());
                    _deleteFilesPopupModalShown = false;
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteFilesPopupModalShown = false;
                }

                UiShared.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Delete account"))
            {
                _deleteAccountPopupModalShown = true;
                ImGui.OpenPopup("Delete your account?");
            }

            UiShared.DrawHelpText("Completely deletes your account and all uploaded files to the service.");

            if (ImGui.BeginPopupModal("Delete your account?", ref _deleteAccountPopupModalShown, UiShared.PopupWindowFlags))
            {
                UiShared.TextWrapped(
                    "Your account and all associated files and data on the service will be deleted.");
                UiShared.TextWrapped("Your UID will be removed from all pairing lists.");
                ImGui.Text("Are you sure you want to continue?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                  ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("Delete account", new Vector2(buttonSize, 0)))
                {
                    Task.Run(() => ApiController.UserDelete());
                    _deleteAccountPopupModalShown = false;
                    Mediator.Publish(new SwitchToIntroUiMessage());
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteAccountPopupModalShown = false;
                }

                UiShared.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.Separator();

        }

        UiShared.FontText("Service & Character Settings", _uiShared.UidFont);

        var idx = _uiShared.DrawServiceSelection();

        ImGui.Dummy(new Vector2(10, 10));

        var selectedServer = _serverConfigurationManager.GetServerByIndex(idx);
        if (selectedServer == _serverConfigurationManager.CurrentServer)
        {
            UiShared.ColorTextWrapped("For any changes to be applied to the current service you need to reconnect to the service.", ImGuiColors.DalamudYellow);
        }


        if (ImGui.BeginTabBar("serverTabBar"))
        {
            if (ImGui.BeginTabItem("Character Management"))
            {
                UiShared.ColorTextWrapped("Characters listed here will automatically connect to the selected Mare service with the settings as provided below." +
                    " Make sure to enter the character names correctly or use the 'Add current character' button at the bottom.", ImGuiColors.DalamudYellow);
                int i = 0;
                foreach (var item in selectedServer.Authentications.ToList())
                {
                    UiShared.DrawWithID("selectedChara" + i, () =>
                    {
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

                            if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Character"))
                            {
                                if (UiShared.CtrlPressed())
                                    _serverConfigurationManager.RemoveCharacterFromServer(idx, item);
                            }
                            UiShared.AttachToolTip("Hold CTRL to delete this entry.");

                            ImGui.TreePop();
                        }
                    });

                    i++;

                }

                ImGui.Separator();
                if (!selectedServer.Authentications.Any(c => string.Equals(c.CharacterName, _uiShared.PlayerName, StringComparison.Ordinal)
                    && c.WorldId == _uiShared.WorldId))
                {
                    if (UiShared.IconTextButton(FontAwesomeIcon.User, "Add current character"))
                    {
                        _serverConfigurationManager.AddCurrentCharacterToServer(idx);
                    }
                    ImGui.SameLine();
                }

                if (UiShared.IconTextButton(FontAwesomeIcon.Plus, "Add new character"))
                {
                    _serverConfigurationManager.AddEmptyCharacterToServer(idx);
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Secret Key Management"))
            {
                foreach (var item in selectedServer.SecretKeys.ToList())
                {
                    UiShared.DrawWithID("key" + item.Key, () =>
                    {
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
                        if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Secret Key"))
                        {
                            if (UiShared.CtrlPressed())
                            {
                                selectedServer.SecretKeys.Remove(item.Key);
                                _serverConfigurationManager.Save();
                            }
                        }
                        UiShared.AttachToolTip("Hold CTRL to delete this secret key entry");
                    });

                    if (item.Key != selectedServer.SecretKeys.Keys.LastOrDefault())
                        ImGui.Separator();
                }

                ImGui.Separator();
                if (UiShared.IconTextButton(FontAwesomeIcon.Plus, "Add new Secret Key"))
                {
                    selectedServer.SecretKeys.Add(selectedServer.SecretKeys.LastOrDefault().Key + 1, new SecretKey()
                    {
                        FriendlyName = "New Secret Key",
                    });
                    _serverConfigurationManager.Save();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Service Settings"))
            {
                var serverUri = selectedServer.ServerUri;
                ImGui.InputText("Service URI", ref serverUri, 255, ImGuiInputTextFlags.ReadOnly);
                UiShared.DrawHelpText("You cannot edit the service URI. Add a new service if you need to edit the URI.");
                var serverName = selectedServer.ServerName;
                var isMain = string.Equals(serverName, ApiController.MainServer, StringComparison.OrdinalIgnoreCase);
                var flags = isMain ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;
                if (ImGui.InputText("Service Name", ref serverName, 255, flags))
                {
                    selectedServer.ServerName = serverName;
                    _serverConfigurationManager.Save();
                }
                if (isMain)
                {
                    UiShared.DrawHelpText("You cannot edit the name of the main service.");
                }
                if (!isMain)
                {
                    if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete Service"))
                    {
                        if (UiShared.CtrlPressed())
                        {
                            _serverConfigurationManager.DeleteServer(selectedServer);
                        }
                    }
                    UiShared.DrawHelpText("Hold CTRL to delete this service");
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawGeneral()
    {
        if (!string.Equals(_lastTab, "General", StringComparison.OrdinalIgnoreCase))
        {
            _notesSuccessfullyApplied = null;
        }

        _lastTab = "General";
        UiShared.FontText("Notes", _uiShared.UidFont);
        if (UiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Export all your user notes to clipboard"))
        {
            ImGui.SetClipboardText(UiShared.GetNotes(_pairManager.DirectPairs.UnionBy(_pairManager.GroupPairs.SelectMany(p => p.Value), p => p.UserData, UserDataComparer.Instance).ToList()));
        }
        if (UiShared.IconTextButton(FontAwesomeIcon.FileImport, "Import notes from clipboard"))
        {
            _notesSuccessfullyApplied = null;
            var notes = ImGui.GetClipboardText();
            _notesSuccessfullyApplied = _uiShared.ApplyNotesFromClipboard(notes, _overwriteExistingLabels);
        }

        ImGui.SameLine();
        ImGui.Checkbox("Overwrite existing notes", ref _overwriteExistingLabels);
        UiShared.DrawHelpText("If this option is selected all already existing notes for UIDs will be overwritten by the imported notes.");
        if (_notesSuccessfullyApplied.HasValue && _notesSuccessfullyApplied.Value)
        {
            UiShared.ColorTextWrapped("User Notes successfully imported", ImGuiColors.HealerGreen);
        }
        else if (_notesSuccessfullyApplied.HasValue && !_notesSuccessfullyApplied.Value)
        {
            UiShared.ColorTextWrapped("Attempt to import notes from clipboard failed. Check formatting and try again", ImGuiColors.DalamudRed);
        }

        var openPopupOnAddition = _configService.Current.OpenPopupOnAdd;

        if (ImGui.Checkbox("Open Notes Popup on user addition", ref openPopupOnAddition))
        {
            _configService.Current.OpenPopupOnAdd = openPopupOnAddition;
            _configService.Save();
        }
        UiShared.DrawHelpText("This will open a popup that allows you to set the notes for a user after successfully adding them to your individual pairs.");

        ImGui.Separator();
        UiShared.FontText("UI", _uiShared.UidFont);
        var showNameInsteadOfNotes = _configService.Current.ShowCharacterNameInsteadOfNotesForVisible;
        var reverseUserSort = _configService.Current.ReverseUserSort;
        if (ImGui.Checkbox("Show player name instead of note for visible players", ref showNameInsteadOfNotes))
        {
            _configService.Current.ShowCharacterNameInsteadOfNotesForVisible = showNameInsteadOfNotes;
            _configService.Save();
        }
        UiShared.DrawHelpText("This will show the character name instead of custom set note when a character is visible");

        if (ImGui.Checkbox("Reverse user sort", ref reverseUserSort))
        {
            _configService.Current.ReverseUserSort = reverseUserSort;
            _configService.Save();
        }
        UiShared.DrawHelpText("This reverses the user sort from A->Z to Z->A");

        ImGui.Separator();

        var disableOptionalPluginWarnings = _configService.Current.DisableOptionalPluginWarnings;
        var onlineNotifs = _configService.Current.ShowOnlineNotifications;
        var onlineNotifsPairsOnly = _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs;
        var onlineNotifsNamedOnly = _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs;
        var warnNotifLocation = _configService.Current.WarningNotification;
        var errorNotifLocation = _configService.Current.ErrorNotification;
        UiShared.FontText("Notifications", _uiShared.UidFont);

        _uiShared.DrawCombo("Info Notification Display##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.InfoNotification = i;
            _configService.Save();
        }, _configService.Current.InfoNotification);
        UiShared.DrawHelpText("The location where \"Info\" notifications will display."
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
        UiShared.DrawHelpText("The location where \"Warning\" notifications will display."
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
        UiShared.DrawHelpText("The location where \"Error\" notifications will display."
                              + Environment.NewLine + "'Nowhere' will not show any Error notifications"
                              + Environment.NewLine + "'Chat' will print Error notifications in chat"
                              + Environment.NewLine + "'Toast' will show Error toast notifications in the bottom right corner"
                              + Environment.NewLine + "'Both' will show chat as well as the toast notification");

        if (ImGui.Checkbox("Disable optional plugin warnings", ref disableOptionalPluginWarnings))
        {
            _configService.Current.DisableOptionalPluginWarnings = disableOptionalPluginWarnings;
            _configService.Save();
        }
        UiShared.DrawHelpText("Enabling this will not show any \"Warning\" labeled messages for missing optional plugins.");
        if (ImGui.Checkbox("Enable online notifications", ref onlineNotifs))
        {
            _configService.Current.ShowOnlineNotifications = onlineNotifs;
            _configService.Save();
        }
        UiShared.DrawHelpText("Enabling this will show a small notification (type: Info) in the bottom right corner when pairs go online.");

        if (!onlineNotifs) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Notify only for individual pairs", ref onlineNotifsPairsOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs = onlineNotifsPairsOnly;
            _configService.Save();
        }
        UiShared.DrawHelpText("Enabling this will only show online notifications (type: Info) for individual pairs.");
        if (ImGui.Checkbox("Notify only for named pairs", ref onlineNotifsNamedOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs = onlineNotifsNamedOnly;
            _configService.Save();
        }
        UiShared.DrawHelpText("Enabling this will only show online notifications (type: Info) for pairs where you have set an individual note.");
        if (!onlineNotifs) ImGui.EndDisabled();
    }

    private bool _deleteFilesPopupModalShown = false;
    private bool _deleteAccountPopupModalShown = false;
    private void DrawDebug()
    {
        _lastTab = "Debug";

        UiShared.FontText("Debug", _uiShared.UidFont);

        if (UiShared.IconTextButton(FontAwesomeIcon.Copy, "[DEBUG] Copy Last created Character Data to clipboard"))
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
        UiShared.AttachToolTip("Use this when reporting mods being rejected from the server.");

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
        UiShared.DrawHelpText("Enabling this can incur a (slight) performance impact. Enabling this for extended periods of time is not recommended.");

        if (!logPerformance) ImGui.BeginDisabled();
        if (UiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats to /xllog"))
        {
            _performanceCollector.PrintPerformanceStats();
        }
        ImGui.SameLine();
        if (UiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Print Performance Stats (last 60s) to /xllog"))
        {
            _performanceCollector.PrintPerformanceStats(60);
        }
        if (!logPerformance) ImGui.EndDisabled();
    }

    private void DrawBlockedTransfers()
    {
        _lastTab = "BlockedTransfers";
        UiShared.ColorTextWrapped("Files that you attempted to upload or download that were forbidden to be transferred by their creators will appear here. " +
                             "If you see file paths from your drive here, then those files were not allowed to be uploaded. If you see hashes, those files were not allowed to be downloaded. " +
                             "Ask your paired friend to send you the mod in question through other means, acquire the mod yourself or pester the mod creator to allow it to be sent over Mare.",
            ImGuiColors.DalamudGrey);

        if (ImGui.BeginTable("TransfersTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(
                $"Hash/Filename");
            ImGui.TableSetupColumn($"Forbidden by");

            ImGui.TableHeadersRow();

            foreach (var item in _fileTransferManager.ForbiddenTransfers)
            {
                ImGui.TableNextColumn();
                if (item is UploadFileTransfer transfer)
                {
                    ImGui.Text(transfer.LocalFile);
                }
                else
                {
                    ImGui.Text(item.Hash);
                }
                ImGui.TableNextColumn();
                ImGui.Text(item.ForbiddenBy);
            }
            ImGui.EndTable();
        }
    }

    private void DrawCurrentTransfers()
    {
        _lastTab = "Transfers";
        bool showTransferWindow = _configService.Current.ShowTransferWindow;
        if (ImGui.Checkbox("Show separate Transfer window while transfers are active", ref showTransferWindow))
        {
            _configService.Current.ShowTransferWindow = showTransferWindow;
            _configService.Save();
        }

        if (_configService.Current.ShowTransferWindow)
        {
            ImGui.Indent();
            bool editTransferWindowPosition = _uiShared.EditTrackerPosition;
            if (ImGui.Checkbox("Edit Transfer Window position", ref editTransferWindowPosition))
            {
                _uiShared.EditTrackerPosition = editTransferWindowPosition;
            }
            ImGui.Unindent();
        }

        if (ImGui.BeginTable("TransfersTable", 2))
        {
            ImGui.TableSetupColumn(
                $"Uploads ({UiShared.ByteToString(_fileTransferManager.CurrentUploads.Sum(a => a.Transferred))} / {UiShared.ByteToString(_fileTransferManager.CurrentUploads.Sum(a => a.Total))})");
            ImGui.TableSetupColumn($"Downloads ({UiShared.ByteToString(_fileTransferManager.CurrentDownloads.SelectMany(k => k.Value).ToList().Sum(a => a.Transferred))} / {UiShared.ByteToString(_fileTransferManager.CurrentDownloads.SelectMany(k => k.Value).ToList().Sum(a => a.Total))})");

            ImGui.TableHeadersRow();

            ImGui.TableNextColumn();
            if (ImGui.BeginTable("UploadsTable", 3))
            {
                ImGui.TableSetupColumn("File");
                ImGui.TableSetupColumn("Uploaded");
                ImGui.TableSetupColumn("Size");
                ImGui.TableHeadersRow();
                foreach (var transfer in _fileTransferManager.CurrentUploads.ToArray())
                {
                    var color = UiShared.UploadColor((transfer.Transferred, transfer.Total));
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGui.TableNextColumn();
                    ImGui.Text(transfer.Hash);
                    ImGui.TableNextColumn();
                    ImGui.Text(UiShared.ByteToString(transfer.Transferred));
                    ImGui.TableNextColumn();
                    ImGui.Text(UiShared.ByteToString(transfer.Total));
                    ImGui.PopStyleColor();
                    ImGui.TableNextRow();
                }

                ImGui.EndTable();
            }

            ImGui.TableNextColumn();
            if (ImGui.BeginTable("DownloadsTable", 3))
            {
                ImGui.TableSetupColumn("File");
                ImGui.TableSetupColumn("Downloaded");
                ImGui.TableSetupColumn("Size");
                ImGui.TableHeadersRow();
                foreach (var transfer in _fileTransferManager.CurrentDownloads.SelectMany(k => k.Value).ToArray())
                {
                    var color = UiShared.UploadColor((transfer.Transferred, transfer.Total));
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGui.TableNextColumn();
                    ImGui.Text(transfer.Hash);
                    ImGui.TableNextColumn();
                    ImGui.Text(UiShared.ByteToString(transfer.Transferred));
                    ImGui.TableNextColumn();
                    ImGui.Text(UiShared.ByteToString(transfer.Total));
                    ImGui.PopStyleColor();
                    ImGui.TableNextRow();
                }

                ImGui.EndTable();
            }

            ImGui.EndTable();
        }
    }

    private bool _readExport = false;
    private string _exportDescription = string.Empty;

    private void DrawFileStorageSettings()
    {
        _lastTab = "FileCache";

        UiShared.FontText("Export MCDF", _uiShared.UidFont);

        UiShared.TextWrapped("This feature allows you to pack your character into a MCDF file and manually send it to other people. MCDF files can officially only be imported during GPose through Mare. " +
            "Be aware that the possibility exists that people write unoffocial custom exporters to extract the containing data.");

        ImGui.Checkbox("##readExport", ref _readExport);
        ImGui.SameLine();
        UiShared.TextWrapped("I understand that by exporting my character data and sending it to other people I am giving away my current character appearance irrevocably. People I am sharing my data with have the ability to share it with other people without limitations.");

        if (_readExport)
        {
            ImGui.Indent();

            if (!_mareCharaFileManager.CurrentlyWorking)
            {
                ImGui.InputTextWithHint("Export Descriptor", "This description will be shown on loading the data", ref _exportDescription, 255);
                if (UiShared.IconTextButton(FontAwesomeIcon.Save, "Export Character as MCDF"))
                {
                    _uiShared.FileDialogManager.SaveFileDialog("Export Character to file", ".mcdf", "export.mcdf", ".mcdf", (success, path) =>
                    {
                        if (!success) return;

                        Task.Run(() =>
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
                    });
                }
                UiShared.ColorTextWrapped("Note: For best results make sure you have everything you want to be shared as well as the correct character appearance" +
                    " equipped and redraw your character before exporting.", ImGuiColors.DalamudYellow);
            }
            else
            {
                UiShared.ColorTextWrapped("Export in progress", ImGuiColors.DalamudYellow);
            }

            ImGui.Unindent();
        }
        bool openInGpose = _configService.Current.OpenGposeImportOnGposeStart;
        if (ImGui.Checkbox("Open MCDF import window when GPose loads", ref openInGpose))
        {
            _configService.Current.OpenGposeImportOnGposeStart = openInGpose;
            _configService.Save();
        }
        UiShared.DrawHelpText("This will automatically open the import menu when loading into Gpose. If unchecked you can open the menu manually with /mare gpose");


        ImGui.Separator();

        UiShared.FontText("Storage", _uiShared.UidFont);

        UiShared.TextWrapped("Mare stores downloaded files from paired people permanently. This is to improve loading performance and requiring less downloads. " +
            "The storage governs itself by clearing data beyond the set storage size. Please set the storage size accordingly. It is not necessary to manually clear the storage.");

        _uiShared.DrawFileScanState();
        _uiShared.DrawTimeSpanBetweenScansSetting();
        _uiShared.DrawCacheDirectorySetting();
        ImGui.Text($"Currently utilized local storage: {UiShared.ByteToString(_uiShared.FileCacheSize)}");
        ImGui.Dummy(new Vector2(10, 10));
        ImGui.Text("To clear the local storage accept the following disclaimer");
        ImGui.Indent();
        ImGui.Checkbox("##readClearCache", ref _readClearCache);
        ImGui.SameLine();
        UiShared.TextWrapped("I understand that: " + Environment.NewLine + "- By clearing the local storage I put the file servers of my connected service under extra strain by having to redownload all data."
            + Environment.NewLine + "- This is not a step to try to fix sync issues."
            + Environment.NewLine + "- This can make the situation of not getting other players data worse in situations of heavy file server load.");
        if (!_readClearCache)
            ImGui.BeginDisabled();
        if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Clear local storage"))
        {
            if (UiShared.CtrlPressed() && _readClearCache)
            {
                Task.Run(() =>
                {
                    foreach (var file in Directory.GetFiles(_configService.Current.CacheFolder))
                    {
                        File.Delete(file);
                    }

                    _uiShared.RecalculateFileCacheSize();
                });
            }
        }
        UiShared.AttachToolTip("You normally do not need to do this. THIS IS NOT SOMETHING YOU SHOULD BE DOING TO TRY TO FIX SYNC ISSUES." + Environment.NewLine
            + "This will solely remove all downloaded data from all players and will require you to re-download everything again." + Environment.NewLine
            + "Mares storage is self-clearing and will not surpass the limit you have set it to." + Environment.NewLine
            + "If you still think you need to do this hold CTRL while pressing the button.");
        if (!_readClearCache)
            ImGui.EndDisabled();
        ImGui.Unindent();
    }

    private bool _readClearCache = false;

    public override void OnClose()
    {
        _uiShared.EditTrackerPosition = false;
        base.OnClose();
    }
}
