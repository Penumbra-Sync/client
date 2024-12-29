using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using static MareSynchronos.Services.CharaDataManager;

namespace MareSynchronos.UI;

internal sealed class CharaDataUi : WindowMediatorSubscriberBase
{
    private readonly CharaDataManager _charaDataManager;
    private readonly MareConfigService _configService;
    private readonly UiSharedService _uiSharedService;
    private CancellationTokenSource _closalCts = new();
    private CancellationTokenSource _disposalCts = new();
    private string _exportDescription = string.Empty;
    private Task? _exportTask;
    private bool _readExport;
    private string _selectedDtoId = string.Empty;
    private string _selectedSpecificIndividual = string.Empty;
    private string _specificIndividualAdd = string.Empty;
    private bool _uiDisabled = false;

    public CharaDataUi(ILogger<CharaDataUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
                         CharaDataManager charaDataManager, MareConfigService configService,
                         UiSharedService uiSharedService)
        : base(logger, mediator, "Mare Synchronos Character Data Hub###MareSynchronosCharaDataUI", performanceCollectorService)
    {
        SizeConstraints = new()
        {
            MinimumSize = new(1000, 500),
            MaximumSize = new(1000, 2000)
        };

        _charaDataManager = charaDataManager;
        _configService = configService;
        _uiSharedService = uiSharedService;
    }

    public override void OnClose()
    {
        if (_uiDisabled)
        {
            IsOpen = true;
            return;
        }

        _closalCts.Cancel();
        _selectedDtoId = string.Empty;
    }

    public override void OnOpen()
    {
        _closalCts = _closalCts.CancelRecreate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closalCts.CancelDispose();
            _disposalCts.CancelDispose();
        }

        base.Dispose(disposing);
    }
    protected override void DrawInternal()
    {
        _uiDisabled = !(_charaDataManager.UploadTask?.IsCompleted ?? true) || _charaDataManager.IsExportingMcdf || _charaDataManager.DataApplicationTask != null;
        using var disabled = ImRaii.Disabled(_uiDisabled);

        using var tabs = ImRaii.TabBar("Tabs");
        using (var mcdOnlineTabItem = ImRaii.TabItem("MCD Online"))
        {
            if (mcdOnlineTabItem)
            {
                DrawMcdfOnline();
            }

        }
        using (var mcdfTabItem = ImRaii.TabItem("MCDF Export"))
        {
            if (mcdfTabItem)
            {
                DrawMcdfExport();
            }
        }
    }

    private void DrawMcdfExport()
    {
        _uiSharedService.BigText("Mare Character Data File Export");

        ImGuiHelpers.ScaledDummy(10);

        UiSharedService.TextWrapped("This feature allows you to pack your character into a MCDF file and manually send it to other people. MCDF files can officially only be imported during GPose through Mare. " +
            "Be aware that the possibility exists that people write unofficial custom exporters to extract the containing data.");

        ImGui.Checkbox("##readExport", ref _readExport);
        ImGui.SameLine();
        UiSharedService.TextWrapped("I understand that by exporting my character data and sending it to other people I am giving away my current character appearance irrevocably. People I am sharing my data with have the ability to share it with other people without limitations.");

        if (_readExport)
        {
            ImGui.Indent();

            if (_exportTask == null || _exportTask.IsCompleted)
            {
                ImGui.InputTextWithHint("Export Descriptor", "This description will be shown on loading the data", ref _exportDescription, 255);
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Export Character as MCDF"))
                {
                    string defaultFileName = string.IsNullOrEmpty(_exportDescription)
                        ? "export.mcdf"
                        : string.Join('_', $"{_exportDescription}.mcdf".Split(Path.GetInvalidFileNameChars()));
                    _uiSharedService.FileDialogManager.SaveFileDialog("Export Character to file", ".mcdf", defaultFileName, ".mcdf", (success, path) =>
                    {
                        if (!success) return;

                        _configService.Current.ExportFolder = Path.GetDirectoryName(path) ?? string.Empty;
                        _configService.Save();

                        _exportTask = Task.Run(() =>
                        {
                            var desc = _exportDescription;
                            _exportDescription = string.Empty;
                            _charaDataManager.SaveMareCharaFile(desc, path);
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

            if (_exportTask?.IsFaulted ?? false)
            {
                UiSharedService.ColorTextWrapped("Export failed, check /xllog for more details.", ImGuiColors.DalamudRed);
            }

            ImGui.Unindent();
        }

        bool openInGpose = _configService.Current.OpenGposeImportOnGposeStart;
        if (ImGui.Checkbox("Open MCDF import window when GPose loads", ref openInGpose))
        {
            _configService.Current.OpenGposeImportOnGposeStart = openInGpose;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("This will automatically open the import menu when loading into Gpose. If unchecked you can open the menu manually with /mare gpose");
    }

    private void DrawMcdfOnline()
    {
        _uiSharedService.BigText("Mare Character Data Online");

        ImGuiHelpers.ScaledDummy(10);

        using (ImRaii.Disabled(_charaDataManager.GetAllDataTask != null
            || (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Download your Character Data from Server"))
            {
                _ = _charaDataManager.GetAllData(_disposalCts.Token);
            }
        }
        if (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)
        {
            UiSharedService.AttachToolTip("You can only refresh all character data from server every minute. Please wait.");
        }

        using (var table = ImRaii.Table("Own Character Data", 11, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            if (table)
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 18);
                ImGui.TableSetupColumn("Code");
                ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Created");
                ImGui.TableSetupColumn("Updated");
                ImGui.TableSetupColumn("Download Count", ImGuiTableColumnFlags.WidthFixed, 18);
                ImGui.TableSetupColumn("Downloadable", ImGuiTableColumnFlags.WidthFixed, 18);
                ImGui.TableSetupColumn("Files", ImGuiTableColumnFlags.WidthFixed, 32);
                ImGui.TableSetupColumn("Glamourer", ImGuiTableColumnFlags.WidthFixed, 18);
                ImGui.TableSetupColumn("Customize+", ImGuiTableColumnFlags.WidthFixed, 18);
                ImGui.TableSetupColumn("Expires", ImGuiTableColumnFlags.WidthFixed, 18);
                ImGui.TableHeadersRow();
                foreach (var entry in _charaDataManager.OwnCharaData.Values)
                {
                    var uDto = _charaDataManager.GetUpdateDto(entry.Id);
                    ImGui.TableNextColumn();
                    if (string.Equals(entry.Id, _selectedDtoId, StringComparison.Ordinal))
                        _uiSharedService.IconText(FontAwesomeIcon.CaretRight);

                    ImGui.TableNextColumn();
                    var idText = entry.UploaderUID + ":" + entry.Id;
                    if (uDto?.HasChanges ?? false)
                    {
                        UiSharedService.ColorText(idText, ImGuiColors.DalamudYellow);
                        UiSharedService.AttachToolTip("This entry has unsaved changes");
                    }
                    else
                    {
                        ImGui.TextUnformatted(idText);
                    }
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.Description);
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(entry.Description);

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.CreatedDate.ToLocalTime().ToString());
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.UpdatedDate.ToLocalTime().ToString());
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.DownloadCount.ToString());
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    bool isDownloadable = !entry.HasMissingFiles
                        && !string.IsNullOrEmpty(entry.GlamourerData);
                    _uiSharedService.BooleanToColoredIcon(isDownloadable, false);
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(isDownloadable ? "Can be downloaded by others" : "Cannot be downloaded: Has missing files or data, please review this entry manually");

                    ImGui.TableNextColumn();
                    var count = entry.FileGamePaths.Concat(entry.FileSwaps).Count();
                    ImGui.TextUnformatted(count.ToString());
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(count == 0 ? "No File data attached" : "Has File data attached");

                    ImGui.TableNextColumn();
                    bool hasGlamourerData = !string.IsNullOrEmpty(entry.GlamourerData);
                    _uiSharedService.BooleanToColoredIcon(hasGlamourerData, false);
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(string.IsNullOrEmpty(entry.GlamourerData) ? "No Glamourer data attached" : "Has Glamourer data attached");

                    ImGui.TableNextColumn();
                    bool hasCustomizeData = !string.IsNullOrEmpty(entry.CustomizeData);
                    _uiSharedService.BooleanToColoredIcon(hasCustomizeData, false);
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(string.IsNullOrEmpty(entry.CustomizeData) ? "No Customize+ data attached" : "Has Customize+ data attached");

                    ImGui.TableNextColumn();
                    FontAwesomeIcon eIcon = FontAwesomeIcon.None;
                    if (!Equals(DateTime.MaxValue, entry.ExpiryDate))
                        eIcon = FontAwesomeIcon.Clock;
                    _uiSharedService.IconText(eIcon, ImGuiColors.DalamudYellow);
                    if (ImGui.IsItemClicked()) _selectedDtoId = entry.Id;
                    if (eIcon != FontAwesomeIcon.None)
                    {
                        UiSharedService.AttachToolTip($"This entry will expire on {entry.ExpiryDate}");
                    }
                }
            }
        }

        using (ImRaii.Disabled(!_charaDataManager.Initialized || _charaDataManager.DataCreationTask != null))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "New Character Data Entry"))
            {
                _charaDataManager.CreateCharaData(_closalCts.Token);
            }
        }
        if (_charaDataManager.DataCreationTask != null)
        {
            UiSharedService.AttachToolTip("You can only create new character data every few seconds. Please wait.");
        }
        if (!_charaDataManager.Initialized)
        {
            UiSharedService.AttachToolTip("Please use the button \"Get Own Chara Data\" once before you can add new data entries.");
        }
        if (_charaDataManager.DataCreationTask != null && !_charaDataManager.DataCreationTask.IsCompleted)
        {
            UiSharedService.ColorTextWrapped("Creating new character data entry on server...", ImGuiColors.DalamudYellow);
        }
        else if (_charaDataManager.DataCreationTask != null && _charaDataManager.DataCreationTask.IsCompleted)
        {
            var color = _charaDataManager.DataCreationTask.Result.Success ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
            UiSharedService.ColorTextWrapped(_charaDataManager.DataCreationTask.Result.Output, color);
        }

        ImGuiHelpers.ScaledDummy(10);
        ImGui.Separator();

        _ = _charaDataManager.OwnCharaData.TryGetValue(_selectedDtoId, out var dto);
        DrawEditCharaData(dto);

        ImGuiHelpers.ScaledDummy(10);
        ImGui.Separator();
    }

    private void DrawEditCharaData(CharaDataFullExtendedDto? dataDto)
    {
        using var imguiid = ImRaii.PushId(dataDto?.Id ?? "NoData");

        if (dataDto == null)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.ColorTextWrapped("Select an entry above to edit its data.", ImGuiColors.DalamudYellow);
            return;
        }

        var updateDto = _charaDataManager.GetUpdateDto(dataDto.Id);

        if (updateDto == null)
        {
            UiSharedService.ColorTextWrapped("Something went awfully wrong and there's no update DTO. Try updating Character Data via the button above.", ImGuiColors.DalamudYellow);
            return;
        }

        bool canUpdate = updateDto.HasChanges;
        if (canUpdate || _charaDataManager.CharaUpdateTask != null)
        {
            ImGuiHelpers.ScaledDummy(5);
        }

        if (canUpdate)
        {
            ImGui.AlignTextToFramePadding();
            UiSharedService.ColorTextWrapped("Warning: You have unsaved changes!", ImGuiColors.DalamudRed);
            ImGui.SameLine();
            using (ImRaii.Disabled(_charaDataManager.CharaUpdateTask != null && !_charaDataManager.CharaUpdateTask.IsCompleted))
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleUp, "Save to Server"))
                {
                    _charaDataManager.UploadCharaData(dataDto.Id);
                }
                ImGui.SameLine();
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Undo all changes"))
                {
                    updateDto.UndoChanges();
                }
            }
            if (_charaDataManager.CharaUpdateTask != null && !_charaDataManager.CharaUpdateTask.IsCompleted)
            {
                UiSharedService.ColorTextWrapped("Updating data on server, please wait.", ImGuiColors.DalamudYellow);
            }
        }

        if (_charaDataManager.UploadTask != null)
        {
            if (_uiDisabled) ImGui.EndDisabled();

            if (_charaDataManager.UploadProgress != null)
            {
                UiSharedService.ColorTextWrapped(_charaDataManager.UploadProgress.Value ?? string.Empty, ImGuiColors.DalamudYellow);
            }
            if (!_charaDataManager.UploadTask.IsCompleted && _uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Cancel Upload"))
            {
                _charaDataManager.CancelUpload();
            }
            else if (_charaDataManager.UploadTask.IsCompleted)
            {
                var color = UiSharedService.GetBoolColor(_charaDataManager.UploadTask.Result.Success);
                UiSharedService.ColorTextWrapped(_charaDataManager.UploadTask.Result.Output, color);
            }

            if (_uiDisabled) ImGui.BeginDisabled();
        }

        if (canUpdate || _charaDataManager.CharaUpdateTask != null)
        {
            ImGuiHelpers.ScaledDummy(5);
        }

        DrawEditCharaDataGeneral(dataDto, updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataAccessAndSharing(updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataAppearance(dataDto, updateDto);

        ImGuiHelpers.ScaledDummy(5);

        using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Character Data"))
            {
                _ = _charaDataManager.DeleteCharaData(dataDto.Id);
                _selectedDtoId = string.Empty;
            }
        }
        if (!UiSharedService.CtrlPressed())
        {
            UiSharedService.AttachToolTip("Hold CTRL and click to delete the current data. This operation is irreversible.");
        }
    }

    private void DrawEditCharaDataAccessAndSharing(CharaDataExtendedUpdateDto updateDto)
    {
        _uiSharedService.BigText("Access and Sharing");

        ImGui.SetNextItemWidth(200);
        var dtoAccessType = updateDto.AccessType;
        if (ImGui.BeginCombo("Access Restrictions", GetAccessTypeString(dtoAccessType)))
        {
            foreach (var accessType in Enum.GetValues(typeof(AccessTypeDto)).Cast<AccessTypeDto>())
            {
                if (ImGui.Selectable(GetAccessTypeString(accessType), accessType == dtoAccessType))
                {
                    updateDto.AccessType = accessType;
                }
            }

            ImGui.EndCombo();
        }
        _uiSharedService.DrawHelpText("You can control who has access to your character data based on the access restrictions." + UiSharedService.TooltipSeparator
            + "Specified: Only people you directly specify in 'Specific Individuals' can access this character data" + Environment.NewLine
            + "Close Pairs: Only people you have directly paired can access this character data" + Environment.NewLine
            + "All Pairs: All people you have paired can access this character data" + Environment.NewLine
            + "Everyone: Everyone can access this character data" + UiSharedService.TooltipSeparator
            + "Note: To access your character data the person in question requires to have the code. Exceptions for 'Shared' data, see 'Sharing' below." + Environment.NewLine
            + "Note: For 'Close' and 'All Pairs' the pause state plays a role. Paused people will not be able to access your character data." + Environment.NewLine
            + "Note: Directly specified individuals in the 'Specific Individuals' list will be able to access your character data regardless of pause or pair state.");

        DrawSpecificIndividuals(updateDto);

        ImGui.SetNextItemWidth(200);
        var dtoShareType = updateDto.ShareType;
        using (ImRaii.Disabled(dtoAccessType == AccessTypeDto.Public))
        {
            if (ImGui.BeginCombo("Sharing", GetShareTypeString(dtoShareType)))
            {
                foreach (var shareType in Enum.GetValues(typeof(ShareTypeDto)).Cast<ShareTypeDto>())
                {
                    if (ImGui.Selectable(GetShareTypeString(shareType), shareType == dtoShareType))
                    {
                        updateDto.ShareType = shareType;
                    }
                }

                ImGui.EndCombo();
            }
        }
        _uiSharedService.DrawHelpText("This regulates how you want to distribute this character data." + UiSharedService.TooltipSeparator
            + "Private: People require to have the code to download this character data" + Environment.NewLine
            + "Shared: People that are allowed through 'Access Restrictions' will have this character data entry displayed in 'Shared with You'" + UiSharedService.TooltipSeparator
            + "Note: Shared is incompatible with Access Restriction 'Everyone'");

        ImGuiHelpers.ScaledDummy(10f);
    }

    private void DrawEditCharaDataAppearance(CharaDataFullExtendedDto dataDto, CharaDataExtendedUpdateDto updateDto)
    {
        _uiSharedService.BigText("Appearance");

        using (ImRaii.Disabled(dataDto.HasMissingFiles || !updateDto.IsAppearanceEqual || _charaDataManager.DataApplicationTask != null))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.CheckCircle, "Preview Saved Apperance on Self"))
            {
                _charaDataManager.ApplyDataToSelf(dataDto);
            }
        }
        _uiSharedService.DrawHelpText("This will download and apply the saved character data to yourself. Once loaded it will automatically revert itself within 15 seconds." + UiSharedService.TooltipSeparator
            + "Note: Weapons will not be displayed correctly unless using the same job as the saved data.");
        if (_uiDisabled) ImGui.EndDisabled();
        if (_charaDataManager.DataApplicationTask != null)
        {
            ImGui.SameLine();
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Cancel Application"))
            {
                _charaDataManager.CancelDataApplicationToSelf();
            }
        }
        if (!string.IsNullOrEmpty(_charaDataManager.DataApplicationProgress))
        {
            ImGui.SameLine();
            UiSharedService.ColorTextWrapped(_charaDataManager.DataApplicationProgress, ImGuiColors.DalamudYellow);
        }
        if (_charaDataManager.DataApplicationTask != null)
        {
            UiSharedService.ColorTextWrapped("WARNING: During the data application avoid switching zones, doing skills, emotes etc. to prevent potential crashes.", ImGuiColors.DalamudRed);
        }
        if (_uiDisabled) ImGui.BeginDisabled();

        ImGui.TextUnformatted("Contains Glamourer Data");
        ImGui.SameLine();
        bool hasGlamourerdata = !string.IsNullOrEmpty(updateDto.GlamourerData);
        ImGui.SameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasGlamourerdata, false);

        ImGui.TextUnformatted("Contains Files");
        var hasFiles = (updateDto.FileGamePaths ?? []).Any();
        ImGui.SameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasFiles, false);
        if (hasFiles && updateDto.IsAppearanceEqual)
        {
            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(20, 1);
            ImGui.SameLine();
            var pos = ImGui.GetCursorPosX();
            ImGui.NewLine();
            ImGui.SameLine(pos);
            ImGui.TextUnformatted($"{dataDto.FileGamePaths.DistinctBy(k => k.HashOrFileSwap).Count()} unique file hashes (original upload: {dataDto.OriginalFiles.DistinctBy(k => k.HashOrFileSwap).Count()} file hashes)");
            ImGui.NewLine();
            ImGui.SameLine(pos);
            ImGui.TextUnformatted($"{dataDto.FileGamePaths.Count} associated game paths");
            ImGui.NewLine();
            ImGui.SameLine(pos);
            ImGui.TextUnformatted($"{dataDto.FileSwaps!.Count} file swaps");
            ImGui.NewLine();
            ImGui.SameLine(pos);
            if (!dataDto.HasMissingFiles)
            {
                UiSharedService.ColorTextWrapped("All files to download this character data are present on the server", ImGuiColors.HealerGreen);
            }
            else
            {
                UiSharedService.ColorTextWrapped($"{dataDto.MissingFiles.DistinctBy(k => k.HashOrFileSwap).Count()} files to download this character data are missing on the server", ImGuiColors.DalamudRed);
                ImGui.NewLine();
                ImGui.SameLine(pos);
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleUp, "Attempt to upload missing files and restore Character Data"))
                {
                    _charaDataManager.UploadMissingFiles(dataDto.Id);
                }
            }
        }
        else if (hasFiles && !updateDto.IsAppearanceEqual)
        {
            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(20, 1);
            ImGui.SameLine();
            UiSharedService.ColorTextWrapped("New data was set. It may contain files that require to be uploaded (will happen on Saving to server)", ImGuiColors.DalamudYellow);
        }

        ImGui.TextUnformatted("Contains Manipulation Data");
        bool hasManipData = !string.IsNullOrEmpty(updateDto.ManipulationData);
        ImGui.SameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasManipData, false);

        ImGui.TextUnformatted("Contains Customize+ Data");
        ImGui.SameLine();
        bool hasCustomizeData = !string.IsNullOrEmpty(updateDto.CustomizeData);
        ImGui.SameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasCustomizeData, false);

        ImGuiHelpers.ScaledDummy(5);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Set Appearance to Current Appearance"))
        {
            _charaDataManager.SetAppearanceData(dataDto.Id);
        }
    }

    private void DrawEditCharaDataGeneral(CharaDataFullExtendedDto dataDto, CharaDataExtendedUpdateDto updateDto)
    {
        _uiSharedService.BigText("General");
        string code = dataDto.UploaderUID + ":" + dataDto.Id;
        using (ImRaii.Disabled())
        {
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("##CharaDataCode", ref code, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Chara Data Code");
        ImGui.SameLine();
        if (_uiSharedService.IconButton(FontAwesomeIcon.Copy))
        {
            ImGui.SetClipboardText(code);
        }
        UiSharedService.AttachToolTip("Copy Code to Clipboard");

        string creationTime = dataDto.CreatedDate.ToLocalTime().ToString();
        string updateTime = dataDto.UpdatedDate.ToLocalTime().ToString();
        string downloadCount = dataDto.DownloadCount.ToString();
        using (ImRaii.Disabled())
        {
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("##CreationDate", ref creationTime, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Creation Date");
        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(20);
        ImGui.SameLine();
        using (ImRaii.Disabled())
        {
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("##LastUpdate", ref updateTime, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Last Update Date");
        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(23);
        ImGui.SameLine();
        using (ImRaii.Disabled())
        {
            ImGui.SetNextItemWidth(50);
            ImGui.InputText("##DlCount", ref downloadCount, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Download Count");

        string description = updateDto.Description;
        ImGui.SetNextItemWidth(735);
        if (ImGui.InputText("##Description", ref description, 200))
        {
            updateDto.Description = description;
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Description");
        _uiSharedService.DrawHelpText("Description for this Character Data." + UiSharedService.TooltipSeparator
            + "Note: the description will be visible to anyone who can access this character data. See 'Access Restrictions' and 'Sharing' below.");

        var expiryDate = updateDto.ExpiryDate;
        bool isExpiring = expiryDate != DateTime.MaxValue;
        if (ImGui.Checkbox("Expires", ref isExpiring))
        {
            updateDto.SetExpiry(isExpiring);
        }
        _uiSharedService.DrawHelpText("If expiration is enabled, the uploaded character data will be automatically deleted from the server at the specified date.");
        using (ImRaii.Disabled(!isExpiring))
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.BeginCombo("Year", expiryDate.Year.ToString()))
            {
                for (int year = DateTime.UtcNow.Year; year < DateTime.UtcNow.Year + 4; year++)
                {
                    if (ImGui.Selectable(year.ToString(), year == expiryDate.Year))
                    {
                        updateDto.SetExpiry(year, expiryDate.Month, expiryDate.Day);
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();

            int daysInMonth = DateTime.DaysInMonth(expiryDate.Year, expiryDate.Month);
            ImGui.SetNextItemWidth(100);
            if (ImGui.BeginCombo("Month", expiryDate.Month.ToString()))
            {
                for (int month = 1; month <= 12; month++)
                {
                    if (ImGui.Selectable(month.ToString(), month == expiryDate.Month))
                    {
                        updateDto.SetExpiry(expiryDate.Year, month, expiryDate.Day);
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();

            ImGui.SetNextItemWidth(100);
            if (ImGui.BeginCombo("Day", expiryDate.Day.ToString()))
            {
                for (int day = 1; day <= daysInMonth; day++)
                {
                    if (ImGui.Selectable(day.ToString(), day == expiryDate.Day))
                    {
                        updateDto.SetExpiry(expiryDate.Year, expiryDate.Month, day);
                    }
                }
                ImGui.EndCombo();
            }
        }
    }

    private void DrawSpecificIndividuals(CharaDataExtendedUpdateDto updateDto)
    {
        using var specific = ImRaii.TreeNode("Access for Specific Individuals");
        if (!specific) return;

        ImGui.SetNextItemWidth(200);
        ImGui.InputText("##AliasToAdd", ref _specificIndividualAdd, 20);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrEmpty(_specificIndividualAdd)
            || updateDto.UserList.Any(f => string.Equals(f.UID, _specificIndividualAdd, StringComparison.Ordinal) || string.Equals(f.Alias, _specificIndividualAdd, StringComparison.Ordinal))))
        {
            if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
            {
                updateDto.AddToList(_specificIndividualAdd);
                _specificIndividualAdd = string.Empty;
            }
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("UID/Vanity ID to Add");
        _uiSharedService.DrawHelpText("Users added to this list will be able to access this character data regardless of your pause or pair state with them." + UiSharedService.TooltipSeparator
            + "Note: Mistyped entries will be automatically removed on updating data to server.");

        using (var lb = ImRaii.ListBox("Allowed Individuals", new(200, 200)))
        {
            foreach (var user in updateDto.UserList)
            {
                var userString = string.IsNullOrEmpty(user.Alias) ? user.UID : $"{user.Alias} ({user.UID})";
                if (ImGui.Selectable(userString, string.Equals(user.UID, _selectedSpecificIndividual, StringComparison.Ordinal)))
                {
                    _selectedSpecificIndividual = user.UID;
                }
            }
        }

        using (ImRaii.Disabled(string.IsNullOrEmpty(_selectedSpecificIndividual)))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove selected User"))
            {
                updateDto.RemoveFromList(_selectedSpecificIndividual);
                _selectedSpecificIndividual = string.Empty;
            }
        }

        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5);
    }

    private string GetAccessTypeString(AccessTypeDto dto) => dto switch
    {
        AccessTypeDto.AllPairs => "All Pairs",
        AccessTypeDto.ClosePairs => "Close Pairs",
        AccessTypeDto.Individuals => "Specified",
        AccessTypeDto.Public => "Everyone"
    };

    private string GetShareTypeString(ShareTypeDto dto) => dto switch
    {
        ShareTypeDto.Private => "Private",
        ShareTypeDto.Shared => "Shared"
    };
}
