using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Utility;
using Dalamud.Interface;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Services.CharaData.Models;
using System.Numerics;

namespace MareSynchronos.UI;

internal sealed partial class CharaDataHubUi
{
    private string _createDescFilter = string.Empty;
    private string _createCodeFilter = string.Empty;
    private bool _createOnlyShowFav = false;
    private bool _createOnlyShowNotDownloadable = false;

    private void DrawEditCharaData(CharaDataFullExtendedDto? dataDto)
    {
        using var imguiid = ImRaii.PushId(dataDto?.Id ?? "NoData");

        if (dataDto == null)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("Select an entry above to edit its data.", ImGuiColors.DalamudYellow);
            return;
        }

        var updateDto = _charaDataManager.GetUpdateDto(dataDto.Id);

        if (updateDto == null)
        {
            UiSharedService.DrawGroupedCenteredColorText("Something went awfully wrong and there's no update DTO. Try updating Character Data via the button above.", ImGuiColors.DalamudYellow);
            return;
        }

        int otherUpdates = 0;
        foreach (var item in _charaDataManager.OwnCharaData.Values.Where(v => !string.Equals(v.Id, dataDto.Id, StringComparison.Ordinal)))
        {
            if (_charaDataManager.GetUpdateDto(item.Id)?.HasChanges ?? false)
            {
                otherUpdates++;
            }
        }

        bool canUpdate = updateDto.HasChanges;
        if (canUpdate || otherUpdates > 0 || (!_charaDataManager.CharaUpdateTask?.IsCompleted ?? false))
        {
            ImGuiHelpers.ScaledDummy(5);
        }

        var indent = ImRaii.PushIndent(10f);
        if (canUpdate || _charaDataManager.UploadTask != null)
        {
            ImGuiHelpers.ScaledDummy(5);
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

                if (!_charaDataManager.UploadTask?.IsCompleted ?? false)
                {
                    DisableDisabled(() =>
                    {
                        if (_charaDataManager.UploadProgress != null)
                        {
                            UiSharedService.ColorTextWrapped(_charaDataManager.UploadProgress.Value ?? string.Empty, ImGuiColors.DalamudYellow);
                        }
                        if ((!_charaDataManager.UploadTask?.IsCompleted ?? false) && _uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Cancel Upload"))
                        {
                            _charaDataManager.CancelUpload();
                        }
                        else if (_charaDataManager.UploadTask?.IsCompleted ?? false)
                        {
                            var color = UiSharedService.GetBoolColor(_charaDataManager.UploadTask.Result.Success);
                            UiSharedService.ColorTextWrapped(_charaDataManager.UploadTask.Result.Output, color);
                        }
                    });
                }
                else if (_charaDataManager.UploadTask?.IsCompleted ?? false)
                {
                    var color = UiSharedService.GetBoolColor(_charaDataManager.UploadTask.Result.Success);
                    UiSharedService.ColorTextWrapped(_charaDataManager.UploadTask.Result.Output, color);
                }
            });
        }

        if (otherUpdates > 0)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGrouped(() =>
            {
                ImGui.AlignTextToFramePadding();
                UiSharedService.ColorTextWrapped($"You have {otherUpdates} other entries with unsaved changes.", ImGuiColors.DalamudYellow);
                ImGui.SameLine();
                using (ImRaii.Disabled(_charaDataManager.CharaUpdateTask != null && !_charaDataManager.CharaUpdateTask.IsCompleted))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowAltCircleUp, "Save all to server"))
                    {
                        _charaDataManager.UploadAllCharaData();
                    }
                }
            });
        }
        indent.Dispose();

        if (canUpdate || otherUpdates > 0 || (!_charaDataManager.CharaUpdateTask?.IsCompleted ?? false))
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

        UiSharedService.ScaledNextItemWidth(200);
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
            + "Specified: Only people and syncshells you directly specify in 'Specific Individuals / Syncshells' can access this character data" + Environment.NewLine
            + "Direct Pairs: Only people you have directly paired can access this character data" + Environment.NewLine
            + "All Pairs: All people you have paired can access this character data" + Environment.NewLine
            + "Everyone: Everyone can access this character data" + UiSharedService.TooltipSeparator
            + "Note: To access your character data the person in question requires to have the code. Exceptions for 'Shared' data, see 'Sharing' below." + Environment.NewLine
            + "Note: For 'Direct' and 'All Pairs' the pause state plays a role. Paused people will not be able to access your character data." + Environment.NewLine
            + "Note: Directly specified Individuals or Syncshells in the 'Specific Individuals / Syncshells' list will be able to access your character data regardless of pause or pair state.");

        DrawSpecific(updateDto);

        UiSharedService.ScaledNextItemWidth(200);
        var dtoShareType = updateDto.ShareType;
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
        _uiSharedService.DrawHelpText("This regulates how you want to distribute this character data." + UiSharedService.TooltipSeparator
            + "Code Only: People require to have the code to download this character data" + Environment.NewLine
            + "Shared: People that are allowed through 'Access Restrictions' will have this character data entry displayed in 'Shared with You' (it can also be accessed through the code)" + UiSharedService.TooltipSeparator
            + "Note: Shared with Access Restriction 'Everyone' is the same as shared with Access Restriction 'All Pairs', it will not show up for everyone but just your pairs.");

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

        ImGui.TextUnformatted("Contains Glamourer Data");
        ImGui.SameLine();
        bool hasGlamourerdata = !string.IsNullOrEmpty(updateDto.GlamourerData);
        UiSharedService.ScaledSameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasGlamourerdata, false);

        ImGui.TextUnformatted("Contains Files");
        var hasFiles = (updateDto.FileGamePaths ?? []).Any() || (dataDto.OriginalFiles.Any());
        UiSharedService.ScaledSameLine(200);
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
        UiSharedService.ScaledSameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasManipData, false);

        ImGui.TextUnformatted("Contains Customize+ Data");
        ImGui.SameLine();
        bool hasCustomizeData = !string.IsNullOrEmpty(updateDto.CustomizeData);
        UiSharedService.ScaledSameLine(200);
        _uiSharedService.BooleanToColoredIcon(hasCustomizeData, false);
    }

    private void DrawEditCharaDataGeneral(CharaDataFullExtendedDto dataDto, CharaDataExtendedUpdateDto updateDto)
    {
        _uiSharedService.BigText("General");
        string code = dataDto.FullId;
        using (ImRaii.Disabled())
        {
            UiSharedService.ScaledNextItemWidth(200);
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
            UiSharedService.ScaledNextItemWidth(200);
            ImGui.InputText("##CreationDate", ref creationTime, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Creation Date");
        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(20);
        ImGui.SameLine();
        using (ImRaii.Disabled())
        {
            UiSharedService.ScaledNextItemWidth(200);
            ImGui.InputText("##LastUpdate", ref updateTime, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Last Update Date");
        ImGui.SameLine();
        ImGuiHelpers.ScaledDummy(23);
        ImGui.SameLine();
        using (ImRaii.Disabled())
        {
            UiSharedService.ScaledNextItemWidth(50);
            ImGui.InputText("##DlCount", ref downloadCount, 255, ImGuiInputTextFlags.ReadOnly);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Download Count");

        string description = updateDto.Description;
        UiSharedService.ScaledNextItemWidth(735);
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
            UiSharedService.ScaledNextItemWidth(100);
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
            UiSharedService.ScaledNextItemWidth(100);
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

            UiSharedService.ScaledNextItemWidth(100);
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
                _ = _charaDataManager.DeleteCharaData(dataDto);
                SelectedDtoId = string.Empty;
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

        if (!_uiSharedService.IsInGpose && _charaDataManager.BrioAvailable)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("To attach pose and world data you need to be in GPose.", ImGuiColors.DalamudYellow);
            ImGuiHelpers.ScaledDummy(5);
        }
        else if (!_charaDataManager.BrioAvailable)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("To attach pose and world data Brio requires to be installed.", ImGuiColors.DalamudRed);
            ImGuiHelpers.ScaledDummy(5);
        }

        foreach (var pose in updateDto.PoseList)
        {
            ImGui.AlignTextToFramePadding();
            using var id = ImRaii.PushId("pose" + poseNumber);
            ImGui.TextUnformatted(poseNumber.ToString());

            if (pose.Id == null)
            {
                UiSharedService.ScaledSameLine(50);
                _uiSharedService.IconText(FontAwesomeIcon.Plus, ImGuiColors.DalamudYellow);
                UiSharedService.AttachToolTip("This pose has not been added to the server yet. Save changes to upload this Pose data.");
            }

            bool poseHasChanges = updateDto.PoseHasChanges(pose);
            if (poseHasChanges)
            {
                UiSharedService.ScaledSameLine(50);
                _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);
                UiSharedService.AttachToolTip("This pose has changes that have not been saved to the server yet.");
            }

            UiSharedService.ScaledSameLine(75);
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

                using (ImRaii.Disabled(!_uiSharedService.IsInGpose || !(_charaDataManager.AttachingPoseTask?.IsCompleted ?? true) || !_charaDataManager.BrioAvailable))
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
                    _dalamudUtilService.SetMarkerAndOpenMap(position: new Vector3(worldData.Value.PositionX, worldData.Value.PositionY, worldData.Value.PositionZ),
                        _dalamudUtilService.MapData.Value[worldData.Value.LocationInfo.MapId].Map);
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(!_uiSharedService.IsInGpose || !(_charaDataManager.AttachingPoseTask?.IsCompleted ?? true) || !_charaDataManager.BrioAvailable))
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

    private void DrawMcdOnline()
    {
        _uiSharedService.BigText("Mare Character Data Online");

        DrawHelpFoldout("In this tab you can create, view and edit your own Mare Character Data that is stored on the server." + Environment.NewLine + Environment.NewLine
            + "Mare Character Data Online functions similar to the previous MCDF standard for exporting your character, except that you do not have to send a file to the other person but solely a code." + Environment.NewLine + Environment.NewLine
            + "There would be a bit too much to explain here on what you can do here in its entirety, however, all elements in this tab have help texts attached what they are used for. Please review them carefully." + Environment.NewLine + Environment.NewLine
            + "Be mindful that when you share your Character Data with other people there is a chance that, with the help of unsanctioned 3rd party plugins, your appearance could be stolen irreversibly, just like when using MCDF.");

        ImGuiHelpers.ScaledDummy(5);
        using (ImRaii.Disabled((!_charaDataManager.GetAllDataTask?.IsCompleted ?? false)
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
            new Vector2(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X, 140 * ImGuiHelpers.GlobalScale)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Code");
                ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Created");
                ImGui.TableSetupColumn("Updated");
                ImGui.TableSetupColumn("Download Count", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Downloadable", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Files", ImGuiTableColumnFlags.WidthFixed, 32 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Glamourer", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Customize+", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Expires", ImGuiTableColumnFlags.WidthFixed, 18 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupScrollFreeze(0, 2);
                ImGui.TableHeadersRow();

                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Checkbox("###createOnlyShowfav", ref _createOnlyShowFav);
                UiSharedService.AttachToolTip("Filter by favorites");
                ImGui.TableNextColumn();
                var x1 = ImGui.GetContentRegionAvail().X;
                ImGui.SetNextItemWidth(x1);
                ImGui.InputTextWithHint("###createFilterCode", "Filter by code", ref _createCodeFilter, 200);
                ImGui.TableNextColumn();
                var x2 = ImGui.GetContentRegionAvail().X;
                ImGui.SetNextItemWidth(x2);
                ImGui.InputTextWithHint("###createFilterDesc", "Filter by description", ref _createDescFilter, 200);
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Checkbox("###createShowNotDl", ref _createOnlyShowNotDownloadable);
                UiSharedService.AttachToolTip("Filter by not downloadable");
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));
                ImGui.TableNextColumn();
                ImGui.Dummy(new(0, 0));


                foreach (var entry in _charaDataManager.OwnCharaData.Values
                    .Where(v =>
                    {
                        bool show = true;
                        if (!string.IsNullOrWhiteSpace(_createCodeFilter))
                        {
                            show &= v.FullId.Contains(_createCodeFilter, StringComparison.OrdinalIgnoreCase);
                        }
                        if (!string.IsNullOrWhiteSpace(_createDescFilter))
                        {
                            show &= v.Description.Contains(_createDescFilter, StringComparison.OrdinalIgnoreCase);
                        }
                        if (_createOnlyShowFav)
                        {
                            show &= _configService.Current.FavoriteCodes.ContainsKey(v.FullId);
                        }
                        if (_createOnlyShowNotDownloadable)
                        {
                            show &= !(!v.HasMissingFiles && !string.IsNullOrEmpty(v.GlamourerData));
                        }

                        return show;
                    }).OrderBy(b => b.CreatedDate))
                {
                    var uDto = _charaDataManager.GetUpdateDto(entry.Id);
                    ImGui.TableNextColumn();
                    if (string.Equals(entry.Id, SelectedDtoId, StringComparison.Ordinal))
                        _uiSharedService.IconText(FontAwesomeIcon.CaretRight);

                    ImGui.TableNextColumn();
                    DrawAddOrRemoveFavorite(entry);

                    ImGui.TableNextColumn();
                    var idText = entry.FullId;
                    if (uDto?.HasChanges ?? false)
                    {
                        UiSharedService.ColorText(idText, ImGuiColors.DalamudYellow);
                        UiSharedService.AttachToolTip("This entry has unsaved changes");
                    }
                    else
                    {
                        ImGui.TextUnformatted(idText);
                    }
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.Description);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(entry.Description);

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.CreatedDate.ToLocalTime().ToString());
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.UpdatedDate.ToLocalTime().ToString());
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.DownloadCount.ToString());
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;

                    ImGui.TableNextColumn();
                    bool isDownloadable = !entry.HasMissingFiles
                        && !string.IsNullOrEmpty(entry.GlamourerData);
                    _uiSharedService.BooleanToColoredIcon(isDownloadable, false);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(isDownloadable ? "Can be downloaded by others" : "Cannot be downloaded: Has missing files or data, please review this entry manually");

                    ImGui.TableNextColumn();
                    var count = entry.FileGamePaths.Concat(entry.FileSwaps).Count();
                    ImGui.TextUnformatted(count.ToString());
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(count == 0 ? "No File data attached" : "Has File data attached");

                    ImGui.TableNextColumn();
                    bool hasGlamourerData = !string.IsNullOrEmpty(entry.GlamourerData);
                    _uiSharedService.BooleanToColoredIcon(hasGlamourerData, false);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(string.IsNullOrEmpty(entry.GlamourerData) ? "No Glamourer data attached" : "Has Glamourer data attached");

                    ImGui.TableNextColumn();
                    bool hasCustomizeData = !string.IsNullOrEmpty(entry.CustomizeData);
                    _uiSharedService.BooleanToColoredIcon(hasCustomizeData, false);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    UiSharedService.AttachToolTip(string.IsNullOrEmpty(entry.CustomizeData) ? "No Customize+ data attached" : "Has Customize+ data attached");

                    ImGui.TableNextColumn();
                    FontAwesomeIcon eIcon = FontAwesomeIcon.None;
                    if (!Equals(DateTime.MaxValue, entry.ExpiryDate))
                        eIcon = FontAwesomeIcon.Clock;
                    _uiSharedService.IconText(eIcon, ImGuiColors.DalamudYellow);
                    if (ImGui.IsItemClicked()) SelectedDtoId = entry.Id;
                    if (eIcon != FontAwesomeIcon.None)
                    {
                        UiSharedService.AttachToolTip($"This entry will expire on {entry.ExpiryDate.ToLocalTime()}");
                    }
                }
            }
        }

        using (ImRaii.Disabled(!_charaDataManager.Initialized || _charaDataManager.DataCreationTask != null || _charaDataManager.OwnCharaData.Count == _charaDataManager.MaxCreatableCharaData))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "New Character Data Entry"))
            {
                _charaDataManager.CreateCharaDataEntry(_closalCts.Token);
                _selectNewEntry = true;
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

        var charaDataEntries = _charaDataManager.OwnCharaData.Count;
        if (charaDataEntries != _dataEntries && _selectNewEntry && _charaDataManager.OwnCharaData.Any())
        {
            SelectedDtoId = _charaDataManager.OwnCharaData.OrderBy(o => o.Value.CreatedDate).Last().Value.Id;
            _selectNewEntry = false;
        }
        _dataEntries = _charaDataManager.OwnCharaData.Count;

        _ = _charaDataManager.OwnCharaData.TryGetValue(SelectedDtoId, out var dto);
        DrawEditCharaData(dto);
    }

    bool _selectNewEntry = false;
    int _dataEntries = 0;

    private void DrawSpecific(CharaDataExtendedUpdateDto updateDto)
    {
        UiSharedService.DrawTree("Access for Specific Individuals / Syncshells", () =>
        {
            using (ImRaii.PushId("user"))
            {
                using (ImRaii.Group())
                {
                    InputComboHybrid("##AliasToAdd", "##AliasToAddPicker", ref _specificIndividualAdd, _pairManager.PairsWithGroups.Keys,
                        static pair => (pair.UserData.UID, pair.UserData.Alias, pair.UserData.AliasOrUID, pair.GetNote()));
                    ImGui.SameLine();
                    using (ImRaii.Disabled(string.IsNullOrEmpty(_specificIndividualAdd)
                        || updateDto.UserList.Any(f => string.Equals(f.UID, _specificIndividualAdd, StringComparison.Ordinal) || string.Equals(f.Alias, _specificIndividualAdd, StringComparison.Ordinal))))
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                        {
                            updateDto.AddUserToList(_specificIndividualAdd);
                            _specificIndividualAdd = string.Empty;
                        }
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted("UID/Vanity UID to Add");
                    _uiSharedService.DrawHelpText("Users added to this list will be able to access this character data regardless of your pause or pair state with them." + UiSharedService.TooltipSeparator
                        + "Note: Mistyped entries will be automatically removed on updating data to server.");

                    using (var lb = ImRaii.ListBox("Allowed Individuals", new(200 * ImGuiHelpers.GlobalScale, 200 * ImGuiHelpers.GlobalScale)))
                    {
                        foreach (var user in updateDto.UserList)
                        {
                            var userString = string.IsNullOrEmpty(user.Alias) ? user.UID : $"{user.Alias} ({user.UID})";
                            if (ImGui.Selectable(userString, string.Equals(user.UID, _selectedSpecificUserIndividual, StringComparison.Ordinal)))
                            {
                                _selectedSpecificUserIndividual = user.UID;
                            }
                        }
                    }

                    using (ImRaii.Disabled(string.IsNullOrEmpty(_selectedSpecificUserIndividual)))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove selected User"))
                        {
                            updateDto.RemoveUserFromList(_selectedSpecificUserIndividual);
                            _selectedSpecificUserIndividual = string.Empty;
                        }
                    }

                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "Apply current Allowed Individuals to all MCDO entries"))
                        {
                            foreach (var own in _charaDataManager.OwnCharaData.Values.Where(k => !string.Equals(k.Id, updateDto.Id, StringComparison.Ordinal)))
                            {
                                var otherUpdateDto = _charaDataManager.GetUpdateDto(own.Id);
                                if (otherUpdateDto == null) continue;
                                foreach (var user in otherUpdateDto.UserList.Select(k => k.UID).Concat(otherUpdateDto.AllowedUsers ?? []).Distinct(StringComparer.Ordinal).ToList())
                                {
                                    otherUpdateDto.RemoveUserFromList(user);
                                }
                                foreach (var user in updateDto.UserList.Select(k => k.UID).Concat(updateDto.AllowedUsers ?? []).Distinct(StringComparer.Ordinal).ToList())
                                {
                                    otherUpdateDto.AddUserToList(user);
                                }
                            }
                        }
                    }
                    UiSharedService.AttachToolTip("This will apply the current list of allowed specific individuals to ALL of your MCDO entries." + UiSharedService.TooltipSeparator
                        + "Hold CTRL to enable.");
                }
            }
            ImGui.SameLine();
            ImGuiHelpers.ScaledDummy(20);
            ImGui.SameLine();

            using (ImRaii.PushId("group"))
            {
                using (ImRaii.Group())
                {
                    InputComboHybrid("##GroupAliasToAdd", "##GroupAliasToAddPicker", ref _specificGroupAdd, _pairManager.Groups.Keys,
                        group => (group.GID, group.Alias, group.AliasOrGID, _serverConfigurationManager.GetNoteForGid(group.GID)));
                    ImGui.SameLine();
                    using (ImRaii.Disabled(string.IsNullOrEmpty(_specificGroupAdd)
                        || updateDto.GroupList.Any(f => string.Equals(f.GID, _specificGroupAdd, StringComparison.Ordinal) || string.Equals(f.Alias, _specificGroupAdd, StringComparison.Ordinal))))
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                        {
                            updateDto.AddGroupToList(_specificGroupAdd);
                            _specificGroupAdd = string.Empty;
                        }
                    }
                    ImGui.SameLine();
                    ImGui.TextUnformatted("GID/Vanity GID to Add");
                    _uiSharedService.DrawHelpText("Users in Syncshells added to this list will be able to access this character data regardless of your pause or pair state with them." + UiSharedService.TooltipSeparator
                        + "Note: Mistyped entries will be automatically removed on updating data to server.");

                    using (var lb = ImRaii.ListBox("Allowed Syncshells", new(200 * ImGuiHelpers.GlobalScale, 200 * ImGuiHelpers.GlobalScale)))
                    {
                        foreach (var group in updateDto.GroupList)
                        {
                            var userString = string.IsNullOrEmpty(group.Alias) ? group.GID : $"{group.Alias} ({group.GID})";
                            if (ImGui.Selectable(userString, string.Equals(group.GID, _selectedSpecificGroupIndividual, StringComparison.Ordinal)))
                            {
                                _selectedSpecificGroupIndividual = group.GID;
                            }
                        }
                    }

                    using (ImRaii.Disabled(string.IsNullOrEmpty(_selectedSpecificGroupIndividual)))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove selected Syncshell"))
                        {
                            updateDto.RemoveGroupFromList(_selectedSpecificGroupIndividual);
                            _selectedSpecificGroupIndividual = string.Empty;
                        }
                    }

                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "Apply current Allowed Syncshells to all MCDO entries"))
                        {
                            foreach (var own in _charaDataManager.OwnCharaData.Values.Where(k => !string.Equals(k.Id, updateDto.Id, StringComparison.Ordinal)))
                            {
                                var otherUpdateDto = _charaDataManager.GetUpdateDto(own.Id);
                                if (otherUpdateDto == null) continue;
                                foreach (var group in otherUpdateDto.GroupList.Select(k => k.GID).Concat(otherUpdateDto.AllowedGroups ?? []).Distinct(StringComparer.Ordinal).ToList())
                                {
                                    otherUpdateDto.RemoveGroupFromList(group);
                                }
                                foreach (var group in updateDto.GroupList.Select(k => k.GID).Concat(updateDto.AllowedGroups ?? []).Distinct(StringComparer.Ordinal).ToList())
                                {
                                    otherUpdateDto.AddGroupToList(group);
                                }
                            }
                        }
                    }
                    UiSharedService.AttachToolTip("This will apply the current list of allowed specific syncshells to ALL of your MCDO entries." + UiSharedService.TooltipSeparator
                        + "Hold CTRL to enable.");
                }
            }

            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(5);
        });
    }

    private void InputComboHybrid<T>(string inputId, string comboId, ref string value, IEnumerable<T> comboEntries,
        Func<T, (string Id, string? Alias, string AliasOrId, string? Note)> parseEntry)
    {
        const float ComponentWidth = 200;
        UiSharedService.ScaledNextItemWidth(ComponentWidth - ImGui.GetFrameHeight());
        ImGui.InputText(inputId, ref value, 20);
        ImGui.SameLine(0.0f, 0.0f);

        using var combo = ImRaii.Combo(comboId, string.Empty, ImGuiComboFlags.NoPreview | ImGuiComboFlags.PopupAlignLeft);
        if (!combo)
        {
            return;
        }

        if (_openComboHybridEntries is null || !string.Equals(_openComboHybridId, comboId, StringComparison.Ordinal))
        {
            var valueSnapshot = value;
            _openComboHybridEntries = comboEntries
                .Select(parseEntry)
                .Where(entry => entry.Id.Contains(valueSnapshot, StringComparison.OrdinalIgnoreCase)
                    || (entry.Alias is not null && entry.Alias.Contains(valueSnapshot, StringComparison.OrdinalIgnoreCase))
                    || (entry.Note is not null && entry.Note.Contains(valueSnapshot, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(entry => entry.Note is null ? entry.AliasOrId : $"{entry.Note} ({entry.AliasOrId})", StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _openComboHybridId = comboId;
        }
        _comboHybridUsedLastFrame = true;

        // Is there a better way to handle this?
        var width = ComponentWidth - 2 * ImGui.GetStyle().FramePadding.X - (_openComboHybridEntries.Length > 8 ? ImGui.GetStyle().ScrollbarSize : 0);
        foreach (var (id, alias, aliasOrId, note) in _openComboHybridEntries)
        {
            var selected = !string.IsNullOrEmpty(value)
                && (string.Equals(id, value, StringComparison.Ordinal) || string.Equals(alias, value, StringComparison.Ordinal));
            using var font = ImRaii.PushFont(UiBuilder.MonoFont, note is null);
            if (ImGui.Selectable(note is null ? aliasOrId : $"{note} ({aliasOrId})", selected, ImGuiSelectableFlags.None, new(width, 0)))
            {
                value = aliasOrId;
            }
        }
    }
}