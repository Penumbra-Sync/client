using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using static MareSynchronos.Services.CharaDataManager;

namespace MareSynchronos.UI;

internal sealed class CharaDataHubUi : WindowMediatorSubscriberBase
{
    private readonly CharaDataManager _charaDataManager;
    private readonly CharaDataConfigService _configService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileDialogManager _fileDialogManager;
    private readonly UiSharedService _uiSharedService;
    private CancellationTokenSource _closalCts = new();
    private string _codeNoteFilter = string.Empty;
    private string _customDescFilter = string.Empty;
    private CancellationTokenSource _disposalCts = new();
    private string _exportDescription = string.Empty;
    private Task? _exportTask;
    private Dictionary<string, List<CharaDataMetaInfoDto>>? _filteredDict;
    private string _importCode = string.Empty;
    private bool _readExport;
    private string _selectedDtoId = string.Empty;
    private string _selectedSpecificIndividual = string.Empty;
    private string _sharedWithYouDescriptionFilter = string.Empty;
    private string _sharedWithYouOwnerFilter = string.Empty;
    private string _specificIndividualAdd = string.Empty;
    private bool _disableUI = false;

    public CharaDataHubUi(ILogger<CharaDataHubUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
                         CharaDataManager charaDataManager, CharaDataConfigService configService,
                         UiSharedService uiSharedService, ServerConfigurationManager serverConfigurationManager,
                         DalamudUtilService dalamudUtilService, FileDialogManager fileDialogManager)
        : base(logger, mediator, "Mare Synchronos Character Data Hub###MareSynchronosCharaDataUI", performanceCollectorService)
    {
        SetWindowSizeConstraints();

        _charaDataManager = charaDataManager;
        _configService = configService;
        _uiSharedService = uiSharedService;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtilService = dalamudUtilService;
        _fileDialogManager = fileDialogManager;
        Mediator.Subscribe<GposeStartMessage>(this, (_) => IsOpen |= _configService.Current.OpenMareHubOnGposeStart);
    }

    public override void OnClose()
    {
        if (_disableUI)
        {
            IsOpen = true;
            return;
        }

        _closalCts.Cancel();
        _selectedDtoId = string.Empty;
        _filteredDict = null;
        _sharedWithYouOwnerFilter = string.Empty;
        _importCode = string.Empty;
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

    private void SetWindowSizeConstraints(bool? inGposeTab = null)
    {
        SizeConstraints = new()
        {
            MinimumSize = new((inGposeTab ?? false) ? 400 : 1000, 500),
            MaximumSize = new((inGposeTab ?? false) ? 400 : 1000, 2000)
        };
    }

    protected override void DrawInternal()
    {
        _disableUI = !(_charaDataManager.UiBlockingComputation?.IsCompleted ?? true) || _charaDataManager.IsExportingMcdf;
        using var disabled = ImRaii.Disabled(_disableUI);

        using var tabs = ImRaii.TabBar("Tabs");

        using (var gposeTabItem = ImRaii.TabItem("GPose Controls"))
        {
            bool inGposeTab = false;
            if (gposeTabItem)
            {
                inGposeTab = true;
                using var id = ImRaii.PushId("gposeControls");
                DrawGPoseControls();
            }

            SetWindowSizeConstraints(inGposeTab);
        }

        bool isHandlingSelf = _charaDataManager.HandledCharaData.Any(c => c.IsSelf);
        using (ImRaii.Disabled(isHandlingSelf))
        {
            using (var mcdOnlineTabItem = ImRaii.TabItem("MCD Online"))
            {
                if (mcdOnlineTabItem)
                {
                    using var id = ImRaii.PushId("mcdOnline");
                    DrawMcdfOnline();
                }
            }
            if (isHandlingSelf)
            {
                UiSharedService.AttachToolTip("Cannot use MCD Online while having Character Data applied to self.");
            }

            using (var mcdfTabItem = ImRaii.TabItem("MCDF Export"))
            {
                if (mcdfTabItem)
                {
                    using var id = ImRaii.PushId("mcdfExport");
                    DrawMcdfExport();
                }
            }
            if (isHandlingSelf)
            {
                UiSharedService.AttachToolTip("Cannot use MCDF Export while having Character Data applied to self.");
            }
        }

        using (var settingsTabItem = ImRaii.TabItem("Settings"))
        {
            if (settingsTabItem)
            {
                using var id = ImRaii.PushId("settings");
                DrawSettings();
            }
        }
    }

    private static string GetAccessTypeString(AccessTypeDto dto) => dto switch
    {
        AccessTypeDto.AllPairs => "All Pairs",
        AccessTypeDto.ClosePairs => "Close Pairs",
        AccessTypeDto.Individuals => "Specified",
        AccessTypeDto.Public => "Everyone"
    };

    private static string GetShareTypeString(ShareTypeDto dto) => dto switch
    {
        ShareTypeDto.Private => "Private",
        ShareTypeDto.Shared => "Shared"
    };

    private void DrawAddOrRemoveFavorite(CharaDataFullDto dto)
    {
        DrawFavorite(dto.UploaderUID + ":" + dto.Id);
    }

    private void DrawAddOrRemoveFavorite(CharaDataMetaInfoDto? dto)
    {
        DrawFavorite((dto?.UploaderUID ?? string.Empty) + ":" + (dto?.Id ?? string.Empty));
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

        var indent = ImRaii.PushIndent(10f);
        UiSharedService.DrawGrouped(() =>
        {
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
                if (_disableUI) ImGui.EndDisabled();

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

                if (_disableUI) ImGui.BeginDisabled();
            }
        });
        indent.Dispose();

        if (canUpdate || _charaDataManager.CharaUpdateTask != null)
        {
            ImGuiHelpers.ScaledDummy(5);
        }

        DrawEditCharaDataGeneral(dataDto, updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataAccessAndSharing(updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataAppearance(dataDto, updateDto);
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

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Set Appearance to Current Appearance"))
        {
            _charaDataManager.SetAppearanceData(dataDto.Id);
        }
        _uiSharedService.DrawHelpText("This will overwrite the appearance data currently stored in this Character Data entry with your current appearance.");
        ImGui.SameLine();
        using (ImRaii.Disabled(dataDto.HasMissingFiles || !updateDto.IsAppearanceEqual || _charaDataManager.DataApplicationTask != null))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.CheckCircle, "Preview Saved Apperance on Self"))
            {
                _charaDataManager.ApplyDataToSelf(dataDto);
            }
        }
        _uiSharedService.DrawHelpText("This will download and apply the saved character data to yourself. Once loaded it will automatically revert itself within 15 seconds." + UiSharedService.TooltipSeparator
            + "Note: Weapons will not be displayed correctly unless using the same job as the saved data.");
        if (_disableUI) ImGui.EndDisabled();
        if (_charaDataManager.DataApplicationTask != null)
        {
            ImGui.SameLine();
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Cancel Application"))
            {
                _charaDataManager.CancelDataApplication();
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
        if (_disableUI) ImGui.BeginDisabled();

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

    private void DrawFavorite(string id)
    {
        bool isFavorite = _configService.Current.FavoriteCodes.TryGetValue(id, out var favorite);
        if (_configService.Current.FavoriteCodes.ContainsKey(id))
        {
            _uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.ParsedGold);
            UiSharedService.AttachToolTip($"Custom Description: {favorite?.CustomDescription ?? string.Empty}" + UiSharedService.TooltipSeparator
                + "Click to remove from Favorites");
        }
        else
        {
            _uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.DalamudGrey);
            UiSharedService.AttachToolTip("Click to add to Favorites");
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (isFavorite) _configService.Current.FavoriteCodes.Remove(id);
            else _configService.Current.FavoriteCodes[id] = new();
            _configService.Save();
        }
    }

    private void DrawGPoseControls()
    {
        bool anyApplicationStatus = false;
        if (_disableUI) ImGui.EndDisabled();
        if (_charaDataManager.DataApplicationTask != null)
        {
            ImGuiHelpers.ScaledDummy(5);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Applying Data to Actor");
            ImGui.SameLine();
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Cancel Application"))
            {
                _charaDataManager.CancelDataApplication();
            }
            anyApplicationStatus = true;
        }
        if (!string.IsNullOrEmpty(_charaDataManager.DataApplicationProgress))
        {
            UiSharedService.ColorTextWrapped(_charaDataManager.DataApplicationProgress, ImGuiColors.DalamudYellow);
        }
        if (_charaDataManager.DataApplicationTask != null)
        {
            UiSharedService.ColorTextWrapped("WARNING: During the data application avoid interacting with this actor to prevent potential crashes.", ImGuiColors.DalamudRed);
        }
        if (_disableUI) ImGui.BeginDisabled();

        if (anyApplicationStatus)
        {
            ImGuiHelpers.ScaledDummy(5);
            ImGui.Separator();
        }

        ImGuiHelpers.ScaledDummy(5);

        using (var handledByMare = ImRaii.TreeNode("GPose Actors"))
        {
            if (handledByMare)
            {
                ImGuiHelpers.ScaledDummy(5);

                foreach (var actor in _dalamudUtilService.GetGposeCharactersFromObjectTable())
                {
                    if (actor == null) continue;
                    using var actorId = ImRaii.PushId(actor.Name.TextValue);
                    UiSharedService.DrawGrouped(() =>
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Crosshairs))
                        {
                            unsafe
                            {
                                _dalamudUtilService.GposeTarget = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)actor.Address;
                            }
                        }
                        ImGui.SameLine();
                        UiSharedService.AttachToolTip($"Target the GPose Character {actor.Name.TextValue}");
                        ImGui.AlignTextToFramePadding();
                        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, actor.Address == (_dalamudUtilService.GposeTargetGameObject?.Address ?? nint.Zero)))
                        {
                            ImGui.TextUnformatted(actor.Name.TextValue);
                        }
                        ImGui.SameLine(250);
                        var handled = _charaDataManager.HandledCharaData.FirstOrDefault(c => string.Equals(c.Name, actor.Name.TextValue, StringComparison.Ordinal));
                        using (ImRaii.Disabled(handled == null))
                        {
                            _uiSharedService.IconText(FontAwesomeIcon.InfoCircle);
                            UiSharedService.AttachToolTip($"Applied Data: {handled?.DataId ?? "No data applied"}");

                            ImGui.SameLine();
                            if (_uiSharedService.IconButton(FontAwesomeIcon.Undo))
                            {
                                _charaDataManager.RevertChara(actor.Name.TextValue);
                            }
                            UiSharedService.AttachToolTip($"Revert applied data from {actor.Name.TextValue}");
                        }
                    });

                    ImGuiHelpers.ScaledDummy(2);
                }
            }
        }

        ImGuiHelpers.ScaledDummy(5);

        _uiSharedService.BigText("Apply Character Appearance");

        ImGuiHelpers.ScaledDummy(5);

        bool canApplyInGpose = _charaDataManager.CanApplyInGpose(out string gposeTargetName);
        ImGui.TextUnformatted("GPose Target");
        ImGui.SameLine(200);
        UiSharedService.ColorText(gposeTargetName, UiSharedService.GetBoolColor(canApplyInGpose));

        ImGuiHelpers.ScaledDummy(10);

        using var tabs = ImRaii.TabBar("Tabs");

        using (var byFavoriteTabItem = ImRaii.TabItem("Favorites"))
        {
            if (byFavoriteTabItem)
            {
                using var id = ImRaii.PushId("byFavorite");

                ImGuiHelpers.ScaledDummy(5);

                var max = ImGui.GetWindowContentRegionMax();
                using (var tree = ImRaii.TreeNode("Filters"))
                {
                    if (tree)
                    {
                        ImGui.SetNextItemWidth(max.X - ImGui.GetCursorPosX());
                        ImGui.InputTextWithHint("##ownFilter", "Code/Owner Filter", ref _codeNoteFilter, 100);
                        ImGui.SetNextItemWidth(max.X - ImGui.GetCursorPosX());
                        ImGui.InputTextWithHint("##descFilter", "Custom Description Filter", ref _customDescFilter, 100);
                    }
                }

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
                using var scrollableChild = ImRaii.Child("favorite");
                ImGuiHelpers.ScaledDummy(5);
                using var totalIndent = ImRaii.PushIndent(5f);
                var cursorPos = ImGui.GetCursorPos();
                max = ImGui.GetWindowContentRegionMax();
                foreach (var favorite in _configService.Current.FavoriteCodes
                    .Where(c =>
                        (string.IsNullOrEmpty(_codeNoteFilter)
                            || ((_serverConfigurationManager.GetNoteForUid(c.Key.Split(":")[0]) ?? string.Empty).Contains(_codeNoteFilter, StringComparison.OrdinalIgnoreCase)
                                || c.Key.Contains(_codeNoteFilter, StringComparison.OrdinalIgnoreCase)))
                        && (string.IsNullOrEmpty(_customDescFilter) || c.Value.CustomDescription.Contains(_customDescFilter, StringComparison.OrdinalIgnoreCase)))
                        .OrderByDescending(c => c.Value.LastDownloaded))
                {
                    UiSharedService.DrawGrouped(() =>
                    {
                        using var tableid = ImRaii.PushId(favorite.Key);
                        ImGui.AlignTextToFramePadding();
                        DrawFavorite(favorite.Key);
                        using var innerIndent = ImRaii.PushIndent(25f);
                        ImGui.SameLine();
                        var xPos = ImGui.GetCursorPosX();
                        var maxPos = (max.X - cursorPos.X);

                        bool metaInfoDownloaded = _charaDataManager.TryGetMetaInfo(favorite.Key, out var metaInfo);

                        ImGui.AlignTextToFramePadding();
                        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, !metaInfoDownloaded))
                        using (ImRaii.PushColor(ImGuiCol.Text, UiSharedService.GetBoolColor(metaInfo != null), metaInfoDownloaded))
                            ImGui.TextUnformatted(favorite.Key);

                        var iconSize = _uiSharedService.GetIconSize(FontAwesomeIcon.Check);
                        var refreshButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowsSpin);
                        var applyButtonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.ArrowRight, "Apply");
                        var offsetFromRight = maxPos - (iconSize.X + refreshButtonSize.X + applyButtonSize + (ImGui.GetStyle().ItemSpacing.X * 2.5f));

                        ImGui.SameLine();
                        ImGui.SetCursorPosX(offsetFromRight);
                        if (metaInfoDownloaded)
                        {
                            _uiSharedService.BooleanToColoredIcon(metaInfo != null, false);
                            if (metaInfo != null)
                            {
                                UiSharedService.AttachToolTip("Metainfo present" + UiSharedService.TooltipSeparator
                                    + $"Last Updated: {metaInfo!.UpdatedDate}" + Environment.NewLine
                                    + $"Description: {metaInfo!.Description}");
                            }
                            else
                            {
                                UiSharedService.AttachToolTip("Metainfo could not be downloaded." + UiSharedService.TooltipSeparator
                                    + "The data associated with the code is either not present on the server anymore or you have no access to it");
                            }
                        }
                        else
                        {
                            _uiSharedService.IconText(FontAwesomeIcon.QuestionCircle, ImGuiColors.DalamudGrey);
                            UiSharedService.AttachToolTip("Unknown accessibility state. Click the button on the right to refresh.");
                        }

                        ImGui.SameLine();
                        bool isInTimeout = _charaDataManager.IsInTimeout(favorite.Key);
                        using (ImRaii.Disabled(isInTimeout))
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowsSpin))
                            {
                                _charaDataManager.DownloadMetaInfo(favorite.Key, false);
                            }
                        }
                        UiSharedService.AttachToolTip(isInTimeout ? "Timeout for refreshing active, please wait before refreshing again"
                            : "Refresh data for this entry from Server.");

                        ImGui.SameLine();
                        using (ImRaii.Disabled(!canApplyInGpose || (!metaInfo?.CanBeDownloaded ?? true)))
                        {
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, $"Apply"))
                            {
                                _charaDataManager.ApplyOtherDataToGposeTarget(metaInfo!);
                            }
                        }
                        if (metaInfo == null)
                        {
                            UiSharedService.AttachToolTip("Cannot apply data before refreshing accessibility (button on the left)");
                        }

                        var uid = favorite.Key.Split(":")[0];
                        var note = _serverConfigurationManager.GetNoteForUid(uid);
                        if (note != null)
                        {
                            ImGui.TextUnformatted($"({note})");
                        }

                        ImGui.TextUnformatted("Last Use: ");
                        ImGui.SameLine();
                        ImGui.TextUnformatted(favorite.Value.LastDownloaded == DateTime.MaxValue ? "Never" : favorite.Value.LastDownloaded.ToString());

                        var desc = favorite.Value.CustomDescription;
                        ImGui.SetNextItemWidth(maxPos - xPos);
                        if (ImGui.InputTextWithHint("##desc", "Custom Description for Favorite", ref desc, 100))
                        {
                            favorite.Value.CustomDescription = desc;
                            _configService.Save();
                        }
                    });

                    ImGuiHelpers.ScaledDummy(5);
                }

                if (_configService.Current.FavoriteCodes.Count == 0)
                {
                    UiSharedService.ColorTextWrapped("You have no favorites added. Add Favorites through the other tabs before you can use this tab.", ImGuiColors.DalamudYellow);
                }
            }
        }

        using (var byCodeTabItem = ImRaii.TabItem("Code"))
        {
            using var id = ImRaii.PushId("byCodeTab");
            if (byCodeTabItem)
            {
                using var child = ImRaii.Child("sharedWithYouByCode", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                ImGuiHelpers.ScaledDummy(5);
                using (var tree = ImRaii.TreeNode("What is this? (Explanation / Help)"))
                {
                    if (tree)
                    {
                        UiSharedService.TextWrapped("You can apply character data you have a code for in this tab. Provide the code in it's given format \"OwnerUID:DataId\" into the field below and click on " +
                            "\"Get Info from Code\". This will provide you basic information about the data behind the code. Afterwards select an actor in GPose and press on \"Download and apply to <actor>\"." + Environment.NewLine + Environment.NewLine
                            + "Description: as set by the owner of the code to give you more or additional information of what this code may contain." + Environment.NewLine
                            + "Last Update: the date and time the owner of the code has last updated the data." + Environment.NewLine
                            + "Is Downloadable: whether or not the code is downloadable and applicable. If the code is not downloadable, contact the owner so they can attempt to fix it." + Environment.NewLine + Environment.NewLine
                            + "To download a code the code requires correct access permissions to be set by the owner. If getting info from the code fails, contact the owner to make sure they set their Access Permissions for the code correctly.");
                    }
                }

                ImGuiHelpers.ScaledDummy(5);
                ImGui.InputTextWithHint("##importCode", "Enter Data Code", ref _importCode, 100);
                using (ImRaii.Disabled(string.IsNullOrEmpty(_importCode)))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Get Info from Code"))
                    {
                        _charaDataManager.DownloadMetaInfo(_importCode);
                    }
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!canApplyInGpose || (!_charaDataManager.LastDownloadedMetaInfo?.CanBeDownloaded ?? true)))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, $"Download and Apply"))
                    {
                        _charaDataManager.ApplyOtherDataToGposeTarget(_charaDataManager.LastDownloadedMetaInfo!);
                    }
                }
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                DrawAddOrRemoveFavorite(_charaDataManager.LastDownloadedMetaInfo);

                if (!_charaDataManager.DownloadMetaInfoTask?.IsCompleted ?? false)
                {
                    UiSharedService.ColorTextWrapped("Downloading meta info. Please wait.", ImGuiColors.DalamudYellow);
                }
                if ((_charaDataManager.DownloadMetaInfoTask?.IsCompleted ?? false) && !_charaDataManager.DownloadMetaInfoTask.Result.Success)
                {
                    UiSharedService.ColorTextWrapped(_charaDataManager.DownloadMetaInfoTask.Result.Result, ImGuiColors.DalamudRed);
                }

                using (ImRaii.Disabled(_charaDataManager.LastDownloadedMetaInfo == null))
                {
                    ImGuiHelpers.ScaledDummy(5);
                    var metaInfo = _charaDataManager.LastDownloadedMetaInfo;
                    ImGui.TextUnformatted("Description");
                    ImGui.SameLine(150);
                    UiSharedService.TextWrapped(string.IsNullOrEmpty(metaInfo?.Description) ? "-" : metaInfo.Description);
                    ImGui.TextUnformatted("Last Update");
                    ImGui.SameLine(150);
                    ImGui.TextUnformatted(metaInfo?.UpdatedDate.ToLocalTime().ToString() ?? "-");
                    ImGui.TextUnformatted("Is Downloadable");
                    ImGui.SameLine(150);
                    _uiSharedService.BooleanToColoredIcon(metaInfo?.CanBeDownloaded ?? false, inline: false);
                }
            }
        }

        using (var yourOwnTabItem = ImRaii.TabItem("Your Own"))
        {
            using var id = ImRaii.PushId("yourOwnTab");
            if (yourOwnTabItem)
            {
                ImGuiHelpers.ScaledDummy(5);
                using (var tree = ImRaii.TreeNode("What is this? (Explanation / Help)"))
                {
                    if (tree)
                    {
                        UiSharedService.TextWrapped("You can apply character data you created yourself in this tab. If the list is not populated press on \"Download your Character Data\"." + Environment.NewLine + Environment.NewLine
                             + "To create new and edit your existing character data use the \"MCD Online\" tab.");
                    }
                }

                ImGuiHelpers.ScaledDummy(5);

                using (ImRaii.Disabled(_charaDataManager.GetAllDataTask != null
                    || (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Download your Character Data"))
                    {
                        _ = _charaDataManager.GetAllData(_disposalCts.Token);
                    }
                }
                if (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)
                {
                    UiSharedService.AttachToolTip("You can only refresh all character data from server every minute. Please wait.");
                }

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();

                using var child = ImRaii.Child("ownDataChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                using var indent = ImRaii.PushIndent(10f);
                foreach (var data in _charaDataManager.OwnCharaData.Values)
                {
                    DrawMetaInfoData(canApplyInGpose, new(data.Id, data.UploaderUID)
                    {
                        CanBeDownloaded = !data.HasMissingFiles
                                && !string.IsNullOrEmpty(data.GlamourerData),
                        Description = data.Description,
                        UpdatedDate = data.UpdatedDate,
                    });
                }
            }
        }

        using (var sharedWithYouTabItem = ImRaii.TabItem("Shared With You"))
        {
            using var id = ImRaii.PushId("sharedWithYouTab");
            if (sharedWithYouTabItem)
            {
                ImGuiHelpers.ScaledDummy(5);
                ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
                using (var tree = ImRaii.TreeNode("What is this? (Explanation / Help)"))
                {
                    if (tree)
                    {
                        UiSharedService.TextWrapped("You can apply character data shared with you implicitly in this tab. Shared Character Data are Character Data entries that have \"Sharing\" set to \"Shared\" and you have access through those by meeting the access restrictions, " +
                            "i.e. you were specified by your UID to gain access or are paired with the other user according to the Access Restrictions setting." + Environment.NewLine + Environment.NewLine
                            + "Filter if needed to find a specific entry, then just press on \"Apply to <actor>\" and it will download and apply the Character Data to the currently targeted GPose actor.");
                    }
                }

                ImGuiHelpers.ScaledDummy(5);

                using (ImRaii.Disabled(_charaDataManager.GetAllDataTask != null
                    || (_charaDataManager.GetSharedWithYouTimeoutTask != null && !_charaDataManager.GetSharedWithYouTimeoutTask.IsCompleted)))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Download Character Data Shared With You"))
                    {
                        _ = _charaDataManager.GetAllSharedData(_disposalCts.Token);
                        _filteredDict = null;
                    }
                }
                if (_charaDataManager.GetSharedWithYouTimeoutTask != null && !_charaDataManager.GetSharedWithYouTimeoutTask.IsCompleted)
                {
                    UiSharedService.AttachToolTip("You can only refresh all character data from server every minute. Please wait.");
                }
                using (var filterNode = ImRaii.TreeNode("Filters"))
                {
                    if (filterNode)
                    {
                        var filterWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                        ImGui.SetNextItemWidth(filterWidth);
                        if (ImGui.InputTextWithHint("##filter", "Filter by UID/Note", ref _sharedWithYouOwnerFilter, 30))
                        {
                            UpdateFilteredItems();
                        }
                        ImGui.SetNextItemWidth(filterWidth);
                        if (ImGui.InputTextWithHint("##filterDesc", "Filter by Description", ref _sharedWithYouDescriptionFilter, 50))
                        {
                            UpdateFilteredItems();
                        }
                        if (ImGui.Checkbox("Only show downloadable", ref _sharedWithYouDownloadableFilter))
                        {
                            UpdateFilteredItems();
                        }
                    }
                }


                if (_filteredDict == null && _charaDataManager.GetSharedWithYouTask == null)
                {
                    _filteredDict = _charaDataManager.SharedWithYouData
                        .ToDictionary(k =>
                        {
                            var note = _serverConfigurationManager.GetNoteForUid(k.Key);
                            if (note == null) return k.Key;
                            return $"{note} ({k.Key})";
                        }, k => k.Value, StringComparer.OrdinalIgnoreCase)
                        .Where(k => string.IsNullOrEmpty(_sharedWithYouOwnerFilter) || k.Key.Contains(_sharedWithYouOwnerFilter))
                        .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary();
                }

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
                using var child = ImRaii.Child("sharedWithYouChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);

                ImGuiHelpers.ScaledDummy(5);
                foreach (var entry in _filteredDict ?? [])
                {
                    using var tree = ImRaii.TreeNode(entry.Key);
                    if (!tree) continue;

                    foreach (var data in entry.Value)
                    {
                        DrawMetaInfoData(canApplyInGpose, data);
                    }
                }
            }
        }

        using (var mcdfTabItem = ImRaii.TabItem("From MCDF"))
        {
            using var id = ImRaii.PushId("applyMcdfTab");
            if (mcdfTabItem)
            {
                using var child = ImRaii.Child("applyMcdf", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                ImGuiHelpers.ScaledDummy(5);
                using (var tree = ImRaii.TreeNode("What is this? (Explanation / Help)"))
                {
                    if (tree)
                    {
                        UiSharedService.TextWrapped("You can apply character data shared with you using a MCDF file in this tab." + Environment.NewLine + Environment.NewLine
                            + "Load the MCDF first via the \"Load MCDF\" button which will give you the basic description that the owner has set during export." + Environment.NewLine
                            + "You can then apply it to any handled GPose actor." + Environment.NewLine + Environment.NewLine
                            + "MCDF to share with others can be generated using the \"MCDF Export\" tab at the top.");
                    }
                }

                ImGuiHelpers.ScaledDummy(5);

                if (_charaDataManager.McdfHeaderLoadingTask == null || _charaDataManager.McdfHeaderLoadingTask.IsCompleted)
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.FolderOpen, "Load MCDF"))
                    {
                        _fileDialogManager.OpenFileDialog("Pick MCDF file", ".mcdf", (success, paths) =>
                        {
                            if (!success) return;
                            if (paths.FirstOrDefault() is not string path) return;

                            _configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
                            _configService.Save();

                            _charaDataManager.LoadMcdf(path);
                        }, 1, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
                    }
                    UiSharedService.AttachToolTip("Load MCDF Metadata into memory");
                    if (_charaDataManager.LoadedCharaFile != null && (_charaDataManager.McdfHeaderLoadingTask?.IsCompleted ?? false))
                    {
                        ImGui.TextUnformatted("Loaded file");
                        ImGui.SameLine(200);
                        UiSharedService.TextWrapped(_charaDataManager.LoadedCharaFile.FilePath);
                        ImGui.Text("Description");
                        ImGui.SameLine(200);
                        UiSharedService.TextWrapped(_charaDataManager.LoadedCharaFile.CharaFileData.Description);

                        ImGuiHelpers.ScaledDummy(5);

                        using (ImRaii.Disabled(!_charaDataManager.CanApplyInGpose(out var targetName)))
                        {
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Apply"))
                            {
                                _charaDataManager.McdfApplyToGposeTarget();
                            }
                            UiSharedService.AttachToolTip($"Apply to {targetName}");
                        }
                    }
                    if ((_charaDataManager.McdfHeaderLoadingTask?.IsFaulted ?? false) || (_charaDataManager.McdfApplicationTask?.IsFaulted ?? false))
                    {
                        UiSharedService.ColorTextWrapped("Failure to read MCDF file. MCDF file is possibly corrupt. Re-export the MCDF file and try again.",
                            ImGuiColors.DalamudRed);
                        UiSharedService.ColorTextWrapped("Note: if this is your MCDF, try redrawing yourself, wait and re-export the file. " +
                            "If you received it from someone else have them do the same.", ImGuiColors.DalamudYellow);
                    }
                }
                else
                {
                    UiSharedService.ColorTextWrapped("Loading Character...", ImGuiColors.DalamudYellow);
                }
            }
        }
    }

    private bool _sharedWithYouDownloadableFilter = false;

    private void UpdateFilteredItems()
    {
        if (_charaDataManager.GetSharedWithYouTask == null)
        {
            _filteredDict = _charaDataManager.SharedWithYouData
                .SelectMany(k => k.Value)
                .Where(k =>
                    (!_sharedWithYouDownloadableFilter || k.CanBeDownloaded)
                    && (string.IsNullOrEmpty(_sharedWithYouDescriptionFilter) || k.Description.Contains(_sharedWithYouDescriptionFilter, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(k => k.UploaderUID, StringComparer.Ordinal)
                .ToDictionary(k =>
                {
                    var note = _serverConfigurationManager.GetNoteForUid(k.Key);
                    if (note == null) return k.Key;
                    return $"{note} ({k.Key})";
                }, k => k.ToList(), StringComparer.OrdinalIgnoreCase)
                .Where(k => (string.IsNullOrEmpty(_sharedWithYouOwnerFilter) || k.Key.Contains(_sharedWithYouOwnerFilter, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary();
        }
    }

    private void DrawMetaInfoData(bool canApplyInGpose, CharaDataMetaInfoDto data)
    {
        ImGuiHelpers.ScaledDummy(5);
        using var entryId = ImRaii.PushId(data.UploaderUID + ":" + data.Id);

        var startPos = ImGui.GetCursorPosX();
        var maxPos = ImGui.GetWindowContentRegionMax().X;
        var availableWidth = maxPos - startPos;
        UiSharedService.DrawGrouped(() =>
        {
            ImGui.AlignTextToFramePadding();
            DrawAddOrRemoveFavorite(data);

            ImGui.SameLine();
            var favPos = ImGui.GetCursorPosX();
            ImGui.AlignTextToFramePadding();
            UiSharedService.ColorText(data.UploaderUID + ":" + data.Id, UiSharedService.GetBoolColor(data.CanBeDownloaded));
            if (!data.CanBeDownloaded)
            {
                UiSharedService.AttachToolTip("This data is incomplete on the server and cannot be downloaded. Contact the owner so they can fix it. If you are the owner, review the data in the MCD Online tab.");
            }

            var offsetFromRight = availableWidth - _uiSharedService.GetIconSize(FontAwesomeIcon.Calendar).X - _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.ArrowRight, "Apply") - ImGui.GetStyle().ItemSpacing.X;

            ImGui.SameLine();
            ImGui.SetCursorPosX(offsetFromRight);
            _uiSharedService.IconText(FontAwesomeIcon.Calendar);
            UiSharedService.AttachToolTip($"Last Update: {data.UpdatedDate}");

            ImGui.SameLine();
            using (ImRaii.Disabled(!canApplyInGpose || !data.CanBeDownloaded))
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, $"Apply"))
                {
                    _charaDataManager.ApplyOtherDataToGposeTarget(data);
                }
            }
            UiSharedService.AttachToolTip("Applies the data to selected GPose actor");

            ImGui.Dummy(new(0, 0));
            ImGui.SameLine(favPos - startPos);

            if (string.IsNullOrEmpty(data.Description))
            {
                UiSharedService.ColorTextWrapped("No description set", ImGuiColors.DalamudGrey, availableWidth);
            }
            else
            {
                UiSharedService.TextWrapped(data.Description, availableWidth);
            }
        });
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

                        _configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
                        _configService.Save();

                        _exportTask = Task.Run(async () =>
                        {
                            var desc = _exportDescription;
                            _exportDescription = string.Empty;
                            await _charaDataManager.SaveMareCharaFile(desc, path).ConfigureAwait(false);
                        });
                    }, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
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

        using (var table = ImRaii.Table("Own Character Data", 12, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY,
            new System.Numerics.Vector2(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X, 100)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 18);
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
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                foreach (var entry in _charaDataManager.OwnCharaData.Values)
                {
                    var uDto = _charaDataManager.GetUpdateDto(entry.Id);
                    ImGui.TableNextColumn();
                    if (string.Equals(entry.Id, _selectedDtoId, StringComparison.Ordinal))
                        _uiSharedService.IconText(FontAwesomeIcon.CaretRight);

                    ImGui.TableNextColumn();
                    DrawAddOrRemoveFavorite(entry);

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

        using (ImRaii.Disabled(!_charaDataManager.Initialized || _charaDataManager.DataCreationTask != null || _charaDataManager.OwnCharaData.Count == _charaDataManager.MaxCreatableCharaData))
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

        if (_charaDataManager.Initialized)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped($"Chara Data Entries on Server: {_charaDataManager.OwnCharaData.Count}/{_charaDataManager.MaxCreatableCharaData}");
            if (_charaDataManager.OwnCharaData.Count == _charaDataManager.MaxCreatableCharaData)
            {
                ImGui.AlignTextToFramePadding();
                UiSharedService.ColorTextWrapped("You have reached the maximum Character Data entries and cannot create more.", ImGuiColors.DalamudYellow);
            }
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

        using var child = ImRaii.Child("editChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
        _ = _charaDataManager.OwnCharaData.TryGetValue(_selectedDtoId, out var dto);
        DrawEditCharaData(dto);
    }

    private void DrawSettings()
    {
        ImGuiHelpers.ScaledDummy(5);
        _uiSharedService.BigText("Settings");
        ImGuiHelpers.ScaledDummy(5);
        bool openInGpose = _configService.Current.OpenMareHubOnGposeStart;
        if (ImGui.Checkbox("Open Character Data Hub when GPose loads", ref openInGpose))
        {
            _configService.Current.OpenMareHubOnGposeStart = openInGpose;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("This will automatically open the import menu when loading into Gpose. If unchecked you can open the menu manually with /mare gpose");
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Last Export Folder");
        ImGui.SameLine(300);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(string.IsNullOrEmpty(_configService.Current.LastSavedCharaDataLocation) ? "Not set" : _configService.Current.LastSavedCharaDataLocation);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Clear Last Export Folder"))
        {
            _configService.Current.LastSavedCharaDataLocation = string.Empty;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("Use this if the Load or Save MCDF file dialog does not open");
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
}
