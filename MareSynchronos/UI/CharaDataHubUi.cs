using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

internal sealed class CharaDataHubUi : WindowMediatorSubscriberBase
{
    private const int maxPoses = 10;
    private readonly CharaDataManager _charaDataManager;
    private readonly CharaDataNearbyManager _charaDataNearbyManager;
    private readonly CharaDataConfigService _configService;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileDialogManager _fileDialogManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private CancellationTokenSource _closalCts = new();
    private string _filterCodeNote = string.Empty;
    private string _filterDescription = string.Empty;
    private bool _filterPoseOnly = false;
    private bool _filterWorldOnly = false;
    private bool _disableUI = false;
    private CancellationTokenSource _disposalCts = new();
    private string _exportDescription = string.Empty;
    private Task? _exportTask;
    private Dictionary<string, List<CharaDataMetaInfoExtendedDto>>? _filteredDict;
    private string _importCode = string.Empty;
    private bool _readExport;
    private string _selectedDtoId = string.Empty;
    private string _selectedSpecificIndividual = string.Empty;
    private string _sharedWithYouDescriptionFilter = string.Empty;
    private bool _sharedWithYouDownloadableFilter = false;
    private string _sharedWithYouOwnerFilter = string.Empty;
    private string _specificIndividualAdd = string.Empty;
    private DateTime _lastFavoriteUpdateTime = DateTime.UtcNow;

    public CharaDataHubUi(ILogger<CharaDataHubUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
                         CharaDataManager charaDataManager, CharaDataNearbyManager charaDataNearbyManager, CharaDataConfigService configService,
                         UiSharedService uiSharedService, ServerConfigurationManager serverConfigurationManager,
                         DalamudUtilService dalamudUtilService, FileDialogManager fileDialogManager)
        : base(logger, mediator, "Mare Synchronos Character Data Hub###MareSynchronosCharaDataUI", performanceCollectorService)
    {
        SetWindowSizeConstraints();

        _charaDataManager = charaDataManager;
        _charaDataNearbyManager = charaDataNearbyManager;
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
        _charaDataNearbyManager.ComputeNearbyData = false;
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
        _disableUI = !(_charaDataManager.UiBlockingComputation?.IsCompleted ?? true);
        if (DateTime.UtcNow.Subtract(_lastFavoriteUpdateTime).TotalSeconds > 2)
        {
            _lastFavoriteUpdateTime = DateTime.UtcNow;
            UpdateFilteredFavorites();
        }

        using var disabled = ImRaii.Disabled(_disableUI);

        using var tabs = ImRaii.TabBar("Tabs");
        bool smallUi = false;

        using (var nearbyPosesTabItem = ImRaii.TabItem("Poses Nearby"))
        {
            if (nearbyPosesTabItem)
            {
                smallUi |= true;
                using var id = ImRaii.PushId("nearbyPoseControls");
                _charaDataNearbyManager.ComputeNearbyData = true;

                DrawNearbyPoses();
            }
            else
            {
                _charaDataNearbyManager.ComputeNearbyData = false;
            }
        }

        using (var gposeTabItem = ImRaii.TabItem("GPose Controls"))
        {
            if (gposeTabItem)
            {
                smallUi |= true;
                using var id = ImRaii.PushId("gposeControls");
                DrawGPoseControls();
            }
        }

        SetWindowSizeConstraints(smallUi);

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

    private void DrawNearbyPoses()
    {
        _uiSharedService.BigText("Poses Nearby");

        using (var helpTree = ImRaii.TreeNode("What is this? (Explanation / Help)"))
        {
            if (helpTree)
            {
                UiSharedService.TextWrapped("This tab will show you all Shared World Poses nearby you." + Environment.NewLine + Environment.NewLine
                    + "Shared World Poses are poses in character data that have world data attached to them and are set to shared. "
                    + "This means that all data that is in 'Shared with You' that has a pose with world data attached to it will be shown here if you are nearby." + Environment.NewLine
                    + "By default all poses that are shared will be shown. Poses taken in housing areas will by default only be shown on the correct server and location." + Environment.NewLine + Environment.NewLine
                    + "Shared World Poses will appear in the world as floating whisps, as well as in the list below. You can mouse over a Shared World Pose in the list for it to get highlighted in the world." + Environment.NewLine + Environment.NewLine
                    + "You can apply Shared World Poses to yourself or spawn the associated character to pose with them." + Environment.NewLine + Environment.NewLine
                    + "You can adjust the filter and change further settings in the 'Settings & Filter' foldout.");
            }
        }

        using (var tree = ImRaii.TreeNode("Settings & Filters"))
        {
            if (tree)
            {
                string filterByUser = _charaDataNearbyManager.UserNoteFilter;
                if (ImGui.InputTextWithHint("##filterbyuser", "Filter by User", ref filterByUser, 50))
                {
                    _charaDataNearbyManager.UserNoteFilter = filterByUser;
                }
                bool onlyCurrent = _charaDataNearbyManager.OwnServerFilter;
                if (ImGui.Checkbox("Only show Poses on current server", ref onlyCurrent))
                {
                    _charaDataNearbyManager.OwnServerFilter = onlyCurrent;
                }
                _uiSharedService.DrawHelpText("Toggling this off will show you the location of all shared Poses with World Data from all Servers");
                bool ignoreHousing = _charaDataNearbyManager.IgnoreHousingLimitations;
                if (ImGui.Checkbox("Ignore Housing Limitations", ref ignoreHousing))
                {
                    _charaDataNearbyManager.IgnoreHousingLimitations = ignoreHousing;
                }
                _uiSharedService.DrawHelpText("This will display all poses in their location regardless of housing limitations." + UiSharedService.TooltipSeparator
                    + "Note: Poses that utilize housing props, furniture, etc. will not be displayed correctly if not spawned in the right location.");
                bool showWhisps = _charaDataNearbyManager.DrawWhisps;
                if (ImGui.Checkbox("Show Pose Whisps in the overworld", ref showWhisps))
                {
                    _charaDataNearbyManager.DrawWhisps = showWhisps;
                }
                _uiSharedService.DrawHelpText("This setting indicates whether or not to draw floating whisps where other's poses are in the world.");
                int poseDetectionDistance = _charaDataNearbyManager.DistanceFilter;
                ImGui.SetNextItemWidth(100);
                if (ImGui.SliderInt("Detection Distance", ref poseDetectionDistance, 5, 1000))
                {
                    _charaDataNearbyManager.DistanceFilter = poseDetectionDistance;
                }
                _uiSharedService.DrawHelpText("This setting allows you to change the distance in which poses will be shown. Set it to the maximum if you want to see all poses on the current map.");
            }
        }

        UiSharedService.DistanceSeparator();

        using var indent = ImRaii.PushIndent(5f);
        if (_charaDataNearbyManager.NearbyData.Count == 0)
        {
            UiSharedService.DrawGrouped(() =>
            {
                UiSharedService.ColorTextWrapped("No Shared World Poses found nearby", ImGuiColors.DalamudYellow);
            });
        }

        bool wasAnythingHovered = false;
        foreach (var pose in _charaDataNearbyManager.NearbyData.OrderBy(v => v.Value.Distance))
        {
            var pos = ImGui.GetCursorPos();
            var circleDiameter = 60f;
            var circleOriginX = ImGui.GetWindowContentRegionMax().X - circleDiameter;
            float circleOffsetY = 0;

            UiSharedService.DrawGrouped(() =>
            {
                string? note = _serverConfigurationManager.GetNoteForUid(pose.Key.MetaInfo.Uploader.UID);
                var noteText = note == null ? pose.Key.MetaInfo.Uploader.AliasOrUID : $"{note} ({pose.Key.MetaInfo.Uploader.AliasOrUID})";
                ImGui.TextUnformatted($"Pose by {noteText}");
                UiSharedService.ColorText("Character Data Description", ImGuiColors.DalamudGrey);
                UiSharedService.AttachToolTip(pose.Key.MetaInfo.Description);
                UiSharedService.ColorText("Description", ImGuiColors.DalamudGrey);
                ImGui.SameLine();
                UiSharedService.TextWrapped(pose.Key.Description ?? "No Pose Description was set", circleOriginX);
                var posAfterGroup = ImGui.GetCursorPos();
                var groupHeightCenter = (posAfterGroup.Y - pos.Y) / 2;
                circleOffsetY = (groupHeightCenter - circleDiameter / 2);
                if (circleOffsetY < 0) circleOffsetY = 0;
                ImGui.SetCursorPos(new Vector2(circleOriginX, pos.Y));
                ImGui.Dummy(new Vector2(circleDiameter, circleDiameter));
                UiSharedService.AttachToolTip("Click to show on map");
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _dalamudUtilService.SetMarkerAndOpenMap(pose.Key.Position, pose.Key.Map);
                }
                ImGui.SetCursorPos(posAfterGroup);
            });
            if (ImGui.IsItemHovered())
            {
                wasAnythingHovered = true;
                _nearbyHovered = pose.Key;
            }
            var drawList = ImGui.GetWindowDrawList();
            var circleRadius = circleDiameter / 2f;
            var windowPos = ImGui.GetWindowPos();
            var circleCenter = new Vector2(windowPos.X + circleOriginX + circleRadius, windowPos.Y + pos.Y + circleRadius + circleOffsetY);
            var rads = pose.Value.Direction * (Math.PI / 180);

            float halfConeAngleRadians = 15f * (float)Math.PI / 180f;
            Vector2 baseDir1 = new Vector2((float)Math.Sin(rads - halfConeAngleRadians), -(float)Math.Cos(rads - halfConeAngleRadians));
            Vector2 baseDir2 = new Vector2((float)Math.Sin(rads + halfConeAngleRadians), -(float)Math.Cos(rads + halfConeAngleRadians));

            Vector2 coneBase1 = circleCenter + baseDir1 * circleRadius;
            Vector2 coneBase2 = circleCenter + baseDir2 * circleRadius;

            // Draw the cone as a filled triangle
            drawList.AddTriangleFilled(circleCenter, coneBase1, coneBase2, UiSharedService.Color(ImGuiColors.ParsedGreen));
            drawList.AddCircle(circleCenter, circleDiameter / 2, UiSharedService.Color(ImGuiColors.DalamudWhite), 360, 2);
            var distance = pose.Value.Distance.ToString("0.0") + "y";
            var textSize = ImGui.CalcTextSize(distance);
            drawList.AddText(new Vector2(circleCenter.X - textSize.X / 2, circleCenter.Y + textSize.Y / 3f), UiSharedService.Color(ImGuiColors.DalamudWhite), distance);

            ImGuiHelpers.ScaledDummy(3);
        }

        if (!wasAnythingHovered) _nearbyHovered = null;
        _charaDataNearbyManager.SetHoveredVfx(_nearbyHovered);
    }

    private PoseEntryExtended? _nearbyHovered;

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
        DrawFavorite(dto.Uploader.UID + ":" + dto.Id);
    }

    private void DrawAddOrRemoveFavorite(CharaDataMetaInfoDto? dto)
    {
        DrawFavorite((dto?.Uploader.UID ?? string.Empty) + ":" + (dto?.Id ?? string.Empty));
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
        if (canUpdate)
        {
            UiSharedService.DrawGrouped(() =>
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
        }
        indent.Dispose();

        if (canUpdate || _charaDataManager.CharaUpdateTask != null)
        {
            ImGuiHelpers.ScaledDummy(5);
        }

        using var child = ImRaii.Child("editChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);

        DrawEditCharaDataGeneral(dataDto, updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataAccessAndSharing(updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataAppearance(dataDto, updateDto);
        ImGuiHelpers.ScaledDummy(5);
        DrawEditCharaDataPoses(updateDto);
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
        string code = dataDto.Uploader.UID + ":" + dataDto.Id;
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

    private void DrawEditCharaDataPoses(CharaDataExtendedUpdateDto updateDto)
    {
        _uiSharedService.BigText("Poses");
        var poseCount = updateDto.PoseList.Count();
        using (ImRaii.Disabled(poseCount >= maxPoses))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Add new Pose"))
            {
                updateDto.AddPose();
            }
        }
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow, poseCount == maxPoses))
            ImGui.TextUnformatted($"{poseCount}/{maxPoses} poses attached");
        ImGuiHelpers.ScaledDummy(5);

        using var indent = ImRaii.PushIndent(10f);
        int poseNumber = 1;
        foreach (var pose in updateDto.PoseList)
        {
            ImGui.AlignTextToFramePadding();
            using var id = ImRaii.PushId("pose" + poseNumber);
            ImGui.TextUnformatted(poseNumber.ToString());

            if (pose.Id == null)
            {
                ImGui.SameLine(50);
                _uiSharedService.IconText(FontAwesomeIcon.Plus, ImGuiColors.DalamudYellow);
                UiSharedService.AttachToolTip("This pose has not been added to the server yet. Save changes to upload this Pose data.");
            }

            bool poseHasChanges = updateDto.PoseHasChanges(pose);
            if (poseHasChanges)
            {
                ImGui.SameLine(50);
                _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);
                UiSharedService.AttachToolTip("This pose has changes that have not been saved to the server yet.");
            }

            ImGui.SameLine(75);
            if (pose.Description == null && pose.WorldData == null && pose.PoseData == null)
            {
                UiSharedService.ColorText("Pose scheduled for deletion", ImGuiColors.DalamudYellow);
            }
            else
            {
                var desc = pose.Description;
                if (ImGui.InputTextWithHint("##description", "Description", ref desc, 100))
                {
                    pose.Description = desc;
                    updateDto.UpdatePoseList();
                }
                ImGui.SameLine();
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete"))
                {
                    updateDto.RemovePose(pose);
                }

                ImGui.SameLine();
                ImGuiHelpers.ScaledDummy(10, 1);
                ImGui.SameLine();
                bool hasPoseData = !string.IsNullOrEmpty(pose.PoseData);
                _uiSharedService.IconText(FontAwesomeIcon.Running, UiSharedService.GetBoolColor(hasPoseData));
                UiSharedService.AttachToolTip(hasPoseData
                    ? "This Pose entry has pose data attached"
                    : "This Pose entry has no pose data attached");
                ImGui.SameLine();
                using (ImRaii.Disabled(!_uiSharedService.IsInGpose || !(_charaDataManager.AttachingPoseTask?.IsCompleted ?? true)))
                {
                    using var poseid = ImRaii.PushId("poseSet" + poseNumber);
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                    {
                        _charaDataManager.AttachPoseData(pose, updateDto);
                    }
                    UiSharedService.AttachToolTip("Apply current pose data to pose");
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!hasPoseData))
                {
                    using var poseid = ImRaii.PushId("poseDelete" + poseNumber);
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                    {
                        pose.PoseData = string.Empty;
                        updateDto.UpdatePoseList();
                    }
                    UiSharedService.AttachToolTip("Delete current pose data from pose");
                }

                ImGui.SameLine();
                ImGuiHelpers.ScaledDummy(10, 1);
                ImGui.SameLine();
                var worldData = pose.WorldData;
                bool hasWorldData = (worldData ?? default) != default;
                _uiSharedService.IconText(FontAwesomeIcon.Globe, UiSharedService.GetBoolColor(hasWorldData));
                var tooltipText = !hasWorldData ? "This Pose has no world data attached." : "This Pose has world data attached.";
                if (hasWorldData)
                {
                    tooltipText += UiSharedService.TooltipSeparator + "Click to show location on map";
                }
                UiSharedService.AttachToolTip(tooltipText);
                if (hasWorldData && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _dalamudUtilService.SetMarkerAndOpenMap(position: new System.Numerics.Vector3(worldData.Value.PositionX, worldData.Value.PositionY, worldData.Value.PositionZ),
                        _dalamudUtilService.MapData.Value[worldData.Value.LocationInfo.MapId].Map);
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!_uiSharedService.IsInGpose || !(_charaDataManager.AttachingPoseTask?.IsCompleted ?? true)))
                {
                    using var worldId = ImRaii.PushId("worldSet" + poseNumber);
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                    {
                        _charaDataManager.AttachWorldData(pose, updateDto);
                    }
                    UiSharedService.AttachToolTip("Apply current world position data to pose");
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!hasWorldData))
                {
                    using var worldId = ImRaii.PushId("worldDelete" + poseNumber);
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                    {
                        pose.WorldData = default(WorldData);
                        updateDto.UpdatePoseList();
                    }
                    UiSharedService.AttachToolTip("Delete current world position data from pose");
                }
            }

            if (poseHasChanges)
            {
                ImGui.SameLine();
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Undo, "Undo"))
                {
                    updateDto.RevertDeletion(pose);
                }
            }

            poseNumber++;
        }
    }

    private static string GetWorldDataTooltipText(PoseEntryExtended poseEntry)
    {
        if (!poseEntry.HasWorldData) return "This Pose has no world data attached.";
        return poseEntry.WorldDataDescriptor;
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
                            var id = string.IsNullOrEmpty(handled?.MetaInfo.Uploader.UID) ? handled?.MetaInfo.Id : handled.MetaInfo.Uploader.UID + ":" + handled.MetaInfo.Id;
                            UiSharedService.AttachToolTip($"Applied Data: {id ?? "No data applied"}");

                            ImGui.SameLine();
                            if (_uiSharedService.IconButton(FontAwesomeIcon.Undo))
                            {
                                _charaDataManager.RevertChara(handled);
                            }
                            UiSharedService.AttachToolTip($"Revert applied data from {actor.Name.TextValue}");
                            DrawPoseData(handled?.MetaInfo, (entry) =>
                            {
                                _ = _charaDataManager.ApplyPoseDataToTarget(entry, actor.Name.TextValue);
                            }, (entry) => true, "Click to apply pose to actor");
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

        if (!canApplyInGpose)
        {
            UiSharedService.ColorTextWrapped("To apply any data you must be in GPose and have a valid application target selected. Pets (i.e. Carbunkle) are not counting as valid targets. " +
                "If more actors are needed, use Brio or other tools to spawn in valid application targets. To see all currently valid targets check the \"GPose Actors\" above.", ImGuiColors.DalamudYellow);
        }

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
                        var maxIndent = ImGui.GetWindowContentRegionMax();
                        ImGui.SetNextItemWidth(maxIndent.X - ImGui.GetCursorPosX());
                        ImGui.InputTextWithHint("##ownFilter", "Code/Owner Filter", ref _filterCodeNote, 100);
                        ImGui.SetNextItemWidth(maxIndent.X - ImGui.GetCursorPosX());
                        ImGui.InputTextWithHint("##descFilter", "Custom Description Filter", ref _filterDescription, 100);
                        ImGui.Checkbox("Only show entries with pose data", ref _filterPoseOnly);
                        ImGui.Checkbox("Only show entries with world data", ref _filterWorldOnly);
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Reset Filter"))
                        {
                            _filterCodeNote = string.Empty;
                            _filterDescription = string.Empty;
                            _filterPoseOnly = false;
                            _filterWorldOnly = false;
                        }
                    }
                }

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
                using var scrollableChild = ImRaii.Child("favorite");
                ImGuiHelpers.ScaledDummy(5);
                using var totalIndent = ImRaii.PushIndent(5f);
                var cursorPos = ImGui.GetCursorPos();
                max = ImGui.GetWindowContentRegionMax();
                foreach (var favorite in _filteredFavorites.OrderByDescending(k => k.Value.Favorite.LastDownloaded))
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

                        bool metaInfoDownloaded = favorite.Value.DownloadedMetaInfo;
                        var metaInfo = favorite.Value.MetaInfo;

                        ImGui.AlignTextToFramePadding();
                        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, !metaInfoDownloaded))
                        using (ImRaii.PushColor(ImGuiCol.Text, UiSharedService.GetBoolColor(metaInfo != null), metaInfoDownloaded))
                            ImGui.TextUnformatted(favorite.Key);

                        var iconSize = _uiSharedService.GetIconSize(FontAwesomeIcon.Check);
                        var refreshButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowsSpin);
                        var applyButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight);
                        var addButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
                        var offsetFromRight = maxPos - (iconSize.X + refreshButtonSize.X + applyButtonSize.X + addButtonSize.X + (ImGui.GetStyle().ItemSpacing.X * 3.5f));

                        ImGui.SameLine();
                        ImGui.SetCursorPosX(offsetFromRight);
                        if (metaInfoDownloaded)
                        {
                            _uiSharedService.BooleanToColoredIcon(metaInfo != null, false);
                            if (metaInfo != null)
                            {
                                UiSharedService.AttachToolTip("Metainfo present" + UiSharedService.TooltipSeparator
                                    + $"Last Updated: {metaInfo!.UpdatedDate}" + Environment.NewLine
                                    + $"Description: {metaInfo!.Description}" + Environment.NewLine
                                    + $"Poses: {metaInfo!.PoseData.Count}");
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
                                UpdateFilteredItems();
                            }
                        }
                        UiSharedService.AttachToolTip(isInTimeout ? "Timeout for refreshing active, please wait before refreshing again"
                            : "Refresh data for this entry from Server.");

                        ImGui.SameLine();
                        using (ImRaii.Disabled(!canApplyInGpose || (!metaInfo?.CanBeDownloaded ?? true)))
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
                            {
                                _ = _charaDataManager.ApplyOtherDataToGposeTarget(metaInfo!);
                            }
                            ImGui.SameLine();
                            if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                            {
                                _ = _charaDataManager.SpawnAndApplyOtherDataToGposeTarget(metaInfo!);
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
                        ImGui.TextUnformatted(favorite.Value.Favorite.LastDownloaded == DateTime.MaxValue ? "Never" : favorite.Value.Favorite.LastDownloaded.ToString());

                        var desc = favorite.Value.Favorite.CustomDescription;
                        ImGui.SetNextItemWidth(maxPos - xPos);
                        if (ImGui.InputTextWithHint("##desc", "Custom Description for Favorite", ref desc, 100))
                        {
                            favorite.Value.Favorite.CustomDescription = desc;
                            _configService.Save();
                        }

                        DrawPoseData(metaInfo, (pose) =>
                        {
                            if (!pose.HasWorldData) return;
                            _dalamudUtilService.SetMarkerAndOpenMap(pose.Position, pose.Map);
                        }, (entry) => (entry.WorldData ?? default) != default, "Click to show location on map");
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
                        _ = _charaDataManager.ApplyOtherDataToGposeTarget(_charaDataManager.LastDownloadedMetaInfo!);
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
                    DrawMetaInfoData(canApplyInGpose, new(data.Id, data.Uploader)
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
                            var note = _serverConfigurationManager.GetNoteForUid(k.Key.UID);
                            if (note == null) return k.Key.AliasOrUID;
                            return $"{note} ({k.Key.AliasOrUID})";
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

                if (_charaDataManager.LoadedMcdfHeader == null || _charaDataManager.LoadedMcdfHeader.IsCompleted)
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
                    if ((_charaDataManager.LoadedMcdfHeader?.IsCompleted ?? false))
                    {
                        ImGui.TextUnformatted("Loaded file");
                        ImGui.SameLine(200);
                        UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.FilePath);
                        ImGui.Text("Description");
                        ImGui.SameLine(200);
                        UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.CharaFileData.Description);

                        ImGuiHelpers.ScaledDummy(5);

                        using (ImRaii.Disabled(!_charaDataManager.CanApplyInGpose(out var targetName)))
                        {
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Apply"))
                            {
                                _charaDataManager.McdfApplyToGposeTarget();
                            }
                            UiSharedService.AttachToolTip($"Apply to {targetName}");
                            ImGui.SameLine();
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Spawn Actor and Apply"))
                            {
                                _charaDataManager.McdfSpawnApplyToGposeTarget();
                            }
                        }
                    }
                    if ((_charaDataManager.LoadedMcdfHeader?.IsFaulted ?? false) || (_charaDataManager.McdfApplicationTask?.IsFaulted ?? false))
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

    private Dictionary<string, (CharaDataFavorite Favorite, CharaDataMetaInfoExtendedDto? MetaInfo, bool DownloadedMetaInfo)> _filteredFavorites = [];

    private void UpdateFilteredFavorites()
    {
        _ = Task.Run(async () =>
        {
            if (_charaDataManager.DownloadMetaInfoTask != null)
            {
                await _charaDataManager.DownloadMetaInfoTask.ConfigureAwait(false);
            }
            Dictionary<string, (CharaDataFavorite, CharaDataMetaInfoExtendedDto?, bool)> newFiltered = [];
            foreach (var favorite in _configService.Current.FavoriteCodes)
            {
                var uid = favorite.Key.Split(":")[0];
                var note = _serverConfigurationManager.GetNoteForUid(uid) ?? string.Empty;
                bool hasMetaInfo = _charaDataManager.TryGetMetaInfo(favorite.Key, out var metaInfo);
                bool addFavorite =
                    (string.IsNullOrEmpty(_filterCodeNote)
                        || (note.Contains(_filterCodeNote, StringComparison.OrdinalIgnoreCase)
                        || uid.Contains(_filterCodeNote, StringComparison.OrdinalIgnoreCase)))
                    && (string.IsNullOrEmpty(_filterDescription)
                        || (favorite.Value.CustomDescription.Contains(_filterDescription, StringComparison.OrdinalIgnoreCase)
                        || (metaInfo != null && metaInfo!.Description.Contains(_filterDescription, StringComparison.OrdinalIgnoreCase))))
                    && (!_filterPoseOnly
                        || (metaInfo != null && metaInfo!.HasPoses))
                    && (!_filterWorldOnly
                        || (metaInfo != null && metaInfo!.HasWorldData));
                if (addFavorite)
                {
                    newFiltered[favorite.Key] = (favorite.Value, metaInfo, hasMetaInfo);
                }
            }

            _filteredFavorites = newFiltered;
        });
    }

    private void DrawPoseData(CharaDataMetaInfoExtendedDto? metaInfo, Action<PoseEntryExtended> onClick, Func<PoseEntryExtended, bool> canClick, string onClickDescription)
    {
        if (metaInfo?.PoseData != null)
        {
            ImGui.NewLine();
            foreach (var item in metaInfo.PoseExtended)
            {
                if (string.IsNullOrEmpty(item.PoseData)) continue;

                bool hasWorldData = item.WorldData!.Value != default;
                ImGui.SameLine();
                var posX = ImGui.GetCursorPosX();
                _uiSharedService.IconText(hasWorldData ? FontAwesomeIcon.Circle : FontAwesomeIcon.Running);
                if (hasWorldData)
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(posX);
                    using var col = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.WindowBg));
                    _uiSharedService.IconText(FontAwesomeIcon.Running);
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(posX);
                    _uiSharedService.IconText(FontAwesomeIcon.Running);
                }

                bool canClickItem = canClick(item);
                var tooltipText = string.IsNullOrEmpty(item.Description) ? "No description set" : "Pose Description: " + item.Description + UiSharedService.TooltipSeparator
                    + GetWorldDataTooltipText(item) + (canClickItem ? UiSharedService.TooltipSeparator + onClickDescription : string.Empty);
                UiSharedService.AttachToolTip(tooltipText);
                if (canClick(item) && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    onClick(item);
                }
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

                        _configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
                        _configService.Save();

                        _charaDataManager.SaveMareCharaFile(_exportDescription, path);
                        _exportDescription = string.Empty;
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
                    var idText = entry.Uploader.UID + ":" + entry.Id;
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
                _charaDataManager.CreateCharaDataEntry(_closalCts.Token);
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

        _ = _charaDataManager.OwnCharaData.TryGetValue(_selectedDtoId, out var dto);
        DrawEditCharaData(dto);
    }

    private void DrawMetaInfoData(bool canApplyInGpose, CharaDataMetaInfoDto data)
    {
        ImGuiHelpers.ScaledDummy(5);
        using var entryId = ImRaii.PushId(data.Uploader.AliasOrUID + ":" + data.Id);

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
            UiSharedService.ColorText(data.Uploader.UID + ":" + data.Id, UiSharedService.GetBoolColor(data.CanBeDownloaded));
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
        bool downloadDataOnConnection = _configService.Current.DownloadMcdDataOnConnection;
        if (ImGui.Checkbox("Download MCD Online Data on connecting", ref downloadDataOnConnection))
        {
            _configService.Current.DownloadMcdDataOnConnection = downloadDataOnConnection;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("This will automatically download MCD Online data (Your Own and Shared with You) once a connection is established to the server.");

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

    private void SetWindowSizeConstraints(bool? inGposeTab = null)
    {
        SizeConstraints = new()
        {
            MinimumSize = new((inGposeTab ?? false) ? 400 : 1000, 500),
            MaximumSize = new((inGposeTab ?? false) ? 400 : 1000, 2000)
        };
    }
    private void UpdateFilteredItems()
    {
        if (_charaDataManager.GetSharedWithYouTask == null)
        {
            _filteredDict = _charaDataManager.SharedWithYouData
                .SelectMany(k => k.Value)
                .Where(k =>
                    (!_sharedWithYouDownloadableFilter || k.CanBeDownloaded)
                    && (string.IsNullOrEmpty(_sharedWithYouDescriptionFilter) || k.Description.Contains(_sharedWithYouDescriptionFilter, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(k => k.Uploader)
                .ToDictionary(k =>
                {
                    var note = _serverConfigurationManager.GetNoteForUid(k.Key.UID);
                    if (note == null) return k.Key.AliasOrUID;
                    return $"{note} ({k.Key.AliasOrUID})";
                }, k => k.ToList(), StringComparer.OrdinalIgnoreCase)
                .Where(k => (string.IsNullOrEmpty(_sharedWithYouOwnerFilter) || k.Key.Contains(_sharedWithYouOwnerFilter, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary();
        }
    }
}
