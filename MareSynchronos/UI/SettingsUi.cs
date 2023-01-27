using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.WebAPI;
using System.Numerics;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Utils;
using Dalamud.Utility;
using Newtonsoft.Json;
using MareSynchronos.Export;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Data;
using MareSynchronos.Managers;
using MareSynchronos.API.Data.Comparer;

namespace MareSynchronos.UI;

public delegate void SwitchUi();
public class SettingsUi : Window, IDisposable
{
    private readonly Configuration _configuration;
    private readonly WindowSystem _windowSystem;
    private readonly ApiController _apiController;
    private readonly MareCharaFileManager _mareCharaFileManager;
    private readonly PairManager _pairManager;
    private readonly UiShared _uiShared;
    public CharacterData LastCreatedCharacterData { private get; set; }

    public event SwitchUi? SwitchToIntroUi;
    private bool _overwriteExistingLabels = false;
    private bool? _notesSuccessfullyApplied = null;
    private string _lastTab = string.Empty;
    private bool _openPopupOnAddition;
    private bool _hideInfoMessages;
    private bool _disableOptionalPluginsWarnings;
    private bool _wasOpen = false;

    public SettingsUi(WindowSystem windowSystem,
        UiShared uiShared, Configuration configuration, ApiController apiController,
        MareCharaFileManager mareCharaFileManager, PairManager pairManager) : base("Mare Synchronos Settings")
    {
        Logger.Verbose("Creating " + nameof(SettingsUi));

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(800, 400),
            MaximumSize = new Vector2(800, 2000),
        };

        _configuration = configuration;
        _windowSystem = windowSystem;
        _apiController = apiController;
        _mareCharaFileManager = mareCharaFileManager;
        _pairManager = pairManager;
        _uiShared = uiShared;
        _openPopupOnAddition = _configuration.OpenPopupOnAdd;
        _hideInfoMessages = _configuration.HideInfoMessages;
        _disableOptionalPluginsWarnings = _configuration.DisableOptionalPluginWarnings;

        _uiShared.GposeStart += _uiShared_GposeStart;
        _uiShared.GposeEnd += _uiShared_GposeEnd;

        windowSystem.AddWindow(this);
    }

    private void _uiShared_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void _uiShared_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }

    public void Dispose()
    {
        Logger.Verbose("Disposing " + nameof(SettingsUi));

        _uiShared.GposeStart -= _uiShared_GposeStart;
        _uiShared.GposeEnd -= _uiShared_GposeEnd;

        _windowSystem.RemoveWindow(this);
    }

    public override void Draw()
    {
        var pluginState = _uiShared.DrawOtherPluginState();

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

            if (_apiController.ServerState is ServerState.Connected)
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

            if (ImGui.BeginTabItem("User Administration"))
            {
                DrawUserAdministration(_apiController.IsConnected);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private string _forbiddenFileHashEntry = string.Empty;
    private string _forbiddenFileHashForbiddenBy = string.Empty;
    private string _bannedUserHashEntry = string.Empty;
    private string _bannedUserReasonEntry = string.Empty;

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
            ImGui.SetClipboardText(_uiShared.GetNotes(_pairManager.DirectPairs.UnionBy(_pairManager.GroupPairs.SelectMany(p => p.Value), p => p.UserData, UserDataComparer.Instance).ToList()));
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
        if (ImGui.Checkbox("Open Notes Popup on user addition", ref _openPopupOnAddition))
        {
            _apiController.LastAddedUser = null;
            _configuration.OpenPopupOnAdd = _openPopupOnAddition;
            _configuration.Save();
        }
        UiShared.DrawHelpText("This will open a popup that allows you to set the notes for a user after successfully adding them to your individual pairs.");

        ImGui.Separator();
        UiShared.FontText("Server Messages", _uiShared.UidFont);
        if (ImGui.Checkbox("Hide Server Info Messages", ref _hideInfoMessages))
        {
            _configuration.HideInfoMessages = _hideInfoMessages;
            _configuration.Save();
        }
        UiShared.DrawHelpText("Enabling this will not print any \"Info\" labeled messages into the game chat.");
        if (ImGui.Checkbox("Disable optional plugin warnings", ref _disableOptionalPluginsWarnings))
        {
            _configuration.DisableOptionalPluginWarnings = _disableOptionalPluginsWarnings;
            _configuration.Save();
        }
        UiShared.DrawHelpText("Enabling this will not print any \"Warning\" labeled messages for missing optional plugins Heels or Customize+ in the game chat.");
    }

    private bool _deleteFilesPopupModalShown = false;
    private bool _deleteAccountPopupModalShown = false;

    private void DrawUserAdministration(bool serverAlive)
    {
        _lastTab = "UserAdministration";
        if (serverAlive)
        {
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
                    Task.Run(() => _apiController.FilesDeleteAll());
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
                    Task.Run(() => _apiController.UserDelete());
                    _deleteAccountPopupModalShown = false;
                    SwitchToIntroUi?.Invoke();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteAccountPopupModalShown = false;
                }

                UiShared.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
        }

        if (!_configuration.FullPause)
        {
            UiShared.ColorTextWrapped("Note: to change servers or your secret key you need to disconnect from your current Mare Synchronos server.", ImGuiColors.DalamudYellow);
        }

        var marePaused = _configuration.FullPause;

        if (_configuration.HasValidSetup())
        {
            if (ImGui.Checkbox("Disconnect Mare Synchronos", ref marePaused))
            {
                _configuration.FullPause = marePaused;
                _configuration.Save();
                Task.Run(() => _apiController.CreateConnections(false));
            }

            UiShared.DrawHelpText("Completely pauses the sync and clears your current data (not uploaded files) on the service.");
        }
        else
        {
            UiShared.ColorText("You cannot reconnect without a valid account on the service.", ImGuiColors.DalamudYellow);
        }

        if (marePaused)
        {
            _uiShared.DrawServiceSelection(() => { });
        }

        ImGui.Separator();

        UiShared.FontText("Debug", _uiShared.UidFont);

        if (UiShared.IconTextButton(FontAwesomeIcon.Copy, "[DEBUG] Copy Last created Character Data to clipboard"))
        {
            if (LastCreatedCharacterData != null)
            {
                ImGui.SetClipboardText(JsonConvert.SerializeObject(LastCreatedCharacterData, Formatting.Indented));
            }
            else
            {
                ImGui.SetClipboardText("ERROR: No created character data, cannot copy.");
            }
        }
        UiShared.AttachToolTip("Use this when reporting mods being rejected from the server.");
    }

    private string _charaFileSavePath = string.Empty;
    private string _charaFileLoadPath = string.Empty;

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

            foreach (var item in _apiController.ForbiddenTransfers)
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
        bool showTransferWindow = _configuration.ShowTransferWindow;
        if (ImGui.Checkbox("Show separate Transfer window while transfers are active", ref showTransferWindow))
        {
            _configuration.ShowTransferWindow = showTransferWindow;
            _configuration.Save();
        }

        if (_configuration.ShowTransferWindow)
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
                $"Uploads ({UiShared.ByteToString(_apiController.CurrentUploads.Sum(a => a.Transferred))} / {UiShared.ByteToString(_apiController.CurrentUploads.Sum(a => a.Total))})");
            ImGui.TableSetupColumn($"Downloads ({UiShared.ByteToString(_apiController.CurrentDownloads.SelectMany(k => k.Value).ToList().Sum(a => a.Transferred))} / {UiShared.ByteToString(_apiController.CurrentDownloads.SelectMany(k => k.Value).ToList().Sum(a => a.Total))})");

            ImGui.TableHeadersRow();

            ImGui.TableNextColumn();
            if (ImGui.BeginTable("UploadsTable", 3))
            {
                ImGui.TableSetupColumn("File");
                ImGui.TableSetupColumn("Uploaded");
                ImGui.TableSetupColumn("Size");
                ImGui.TableHeadersRow();
                foreach (var transfer in _apiController.CurrentUploads.ToArray())
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
                foreach (var transfer in _apiController.CurrentDownloads.SelectMany(k => k.Value).ToArray())
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
                                Logger.Error("Error saving data", ex);
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
        bool openInGpose = _configuration.OpenGposeImportOnGposeStart;
        if (ImGui.Checkbox("Open MCDF import window when GPose loads", ref openInGpose))
        {
            _configuration.OpenGposeImportOnGposeStart = openInGpose;
            _configuration.Save();
        }
        UiShared.DrawHelpText("This will automatically open the import menu when loading into Gpose. If unchecked you can open the menu manually with /mare gpose");


        ImGui.Separator();

        UiShared.FontText("Storage", _uiShared.UidFont);

        UiShared.TextWrapped("Mare stores downloaded files from paired people permanently. This is to improve loading performance and requiring less downloads. " +
            "The storage governs itself by clearing data beyond the set storage size. Please set the storage size accordingly. It is not necessary to manually clear the storage.");

        _uiShared.DrawFileScanState();
        _uiShared.DrawTimeSpanBetweenScansSetting();
        _uiShared.DrawCacheDirectorySetting();
        ImGui.Text($"Local storage size: {UiShared.ByteToString(_uiShared.FileCacheSize)}");
        ImGui.SameLine();
        if (ImGui.Button("Clear local storage"))
        {
            if (UiShared.CtrlPressed())
            {
                Task.Run(() =>
                {
                    foreach (var file in Directory.GetFiles(_configuration.CacheFolder))
                    {
                        File.Delete(file);
                    }

                    _uiShared.RecalculateFileCacheSize();
                });
            }
        }
        UiShared.AttachToolTip("You normally do not need to do this. This will solely remove all downloaded data from all players and will require you to re-download everything again." + Environment.NewLine
            + "Mares storage is self-clearing and will not surpass the limit you have set it to." + Environment.NewLine
            + "If you still think you need to do this hold CTRL while pressing the button.");
    }

    public override void OnClose()
    {
        _uiShared.EditTrackerPosition = false;
        base.OnClose();
    }
}
