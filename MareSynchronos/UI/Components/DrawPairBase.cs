﻿using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.User;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace MareSynchronos.UI.Components;

public abstract class DrawPairBase
{
    // todo: not make this static
    protected bool _showModalReport = false;
    protected readonly ApiController _apiController;
    protected readonly IdDisplayHandler _displayHandler;
    protected Pair _pair;
    private bool _reportPopupOpen = false;
    private string _reportReason = string.Empty;
    private readonly string _id;
    public UserPairDto UserPair => _pair.UserPair!;

    protected DrawPairBase(string id, Pair entry, ApiController apiController, IdDisplayHandler uIDDisplayHandler)
    {
        _id = id;
        _pair = entry;
        _apiController = apiController;
        _displayHandler = uIDDisplayHandler;
    }

    public string UID => _pair.UserData.UID;

    public void DrawPairedClient()
    {
        using var id = ImRaii.PushId(GetType().Name + _id);
        var originalY = ImGui.GetCursorPosY();
        var pauseIconSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var textSize = ImGui.CalcTextSize(_pair.UserData.AliasOrUID);

        var textPosY = originalY + pauseIconSize.Y / 2 - textSize.Y / 2;
        DrawLeftSide(textPosY, originalY);
        ImGui.SameLine();
        var posX = ImGui.GetCursorPosX();
        var rightSide = DrawRightSide(originalY);
        DrawName(originalY, posX, rightSide);

        if (_showModalReport && !_reportPopupOpen)
        {
            ImGui.OpenPopup("Report Profile");
            _reportPopupOpen = true;
        }

        if (!_showModalReport) _reportPopupOpen = false;

        if (ImGui.BeginPopupModal("Report Profile", ref _showModalReport, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("Report " + (_pair.UserData.AliasOrUID) + " Mare Profile");
            ImGui.InputTextMultiline("##reportReason", ref _reportReason, 500, new System.Numerics.Vector2(500 - ImGui.GetStyle().ItemSpacing.X * 2, 200));
            UiSharedService.TextWrapped($"Note: Sending a report will disable the offending profile globally.{Environment.NewLine}" +
                $"The report will be sent to the team of your currently connected Mare Synchronos Service.{Environment.NewLine}" +
                $"The report will include your user and your contact info (Discord User).{Environment.NewLine}" +
                $"Depending on the severity of the offense the users Mare profile or account can be permanently disabled or banned.");
            UiSharedService.ColorTextWrapped("Report spam and wrong reports will not be tolerated and can lead to permanent account suspension.", ImGuiColors.DalamudRed);
            if (string.IsNullOrEmpty(_reportReason)) ImGui.BeginDisabled();
            if (ImGui.Button("Send Report"))
            {
                ImGui.CloseCurrentPopup();
                var reason = _reportReason;
                _ = _apiController.UserReportProfile(new(_pair.UserData, reason));
                _reportReason = string.Empty;
                _showModalReport = false;
                _reportPopupOpen = false;
            }
            if (string.IsNullOrEmpty(_reportReason)) ImGui.EndDisabled();
            UiSharedService.SetScaledWindowSize(500);
            ImGui.EndPopup();
        }
    }

    protected abstract void DrawLeftSide(float textPosY, float originalY);
    protected abstract void DrawPairedClientMenu();

    private float DrawRightSide(float originalY)
    {
        var pauseIcon = _pair.UserPair!.OwnPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseIconSize = UiSharedService.GetIconButtonSize(pauseIcon);
        var barButtonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var entryUID = _pair.UserData.AliasOrUID;
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        var rightSideStart = 0f;

        if (_pair.UserPair!.OwnPermissions.IsPaired() && _pair.UserPair!.OtherPermissions.IsPaired())
        {
            var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
            var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
            var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);

            if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled)
            {
                var infoIconPosDist = windowEndX - barButtonSize.X - spacingX - pauseIconSize.X - spacingX;
                var icon = FontAwesomeIcon.ExclamationTriangle;
                var iconwidth = UiSharedService.GetIconSize(icon);

                rightSideStart = infoIconPosDist - iconwidth.X;
                ImGui.SameLine(infoIconPosDist - iconwidth.X);

                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                UiSharedService.FontText(icon.ToIconString(), UiBuilder.IconFont);
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();

                    ImGui.Text("Individual User permissions");

                    if (individualSoundsDisabled)
                    {
                        var userSoundsText = "Sound sync disabled with " + _pair.UserData.AliasOrUID;
                        UiSharedService.FontText(FontAwesomeIcon.VolumeOff.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userSoundsText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text("You: " + (_pair.UserPair!.OwnPermissions.IsDisableSounds() ? "Disabled" : "Enabled") + ", They: " + (_pair.UserPair!.OtherPermissions.IsDisableSounds() ? "Disabled" : "Enabled"));
                    }

                    if (individualAnimDisabled)
                    {
                        var userAnimText = "Animation sync disabled with " + _pair.UserData.AliasOrUID;
                        UiSharedService.FontText(FontAwesomeIcon.Stop.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userAnimText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text("You: " + (_pair.UserPair!.OwnPermissions.IsDisableAnimations() ? "Disabled" : "Enabled") + ", They: " + (_pair.UserPair!.OtherPermissions.IsDisableAnimations() ? "Disabled" : "Enabled"));
                    }

                    if (individualVFXDisabled)
                    {
                        var userVFXText = "VFX sync disabled with " + _pair.UserData.AliasOrUID;
                        UiSharedService.FontText(FontAwesomeIcon.Circle.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userVFXText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text("You: " + (_pair.UserPair!.OwnPermissions.IsDisableVFX() ? "Disabled" : "Enabled") + ", They: " + (_pair.UserPair!.OtherPermissions.IsDisableVFX() ? "Disabled" : "Enabled"));
                    }

                    ImGui.EndTooltip();
                }
            }
        }

        rightSideStart = windowEndX - barButtonSize.X - spacingX * 2 - pauseIconSize.X;
        ImGui.SameLine(windowEndX - barButtonSize.X - spacingX - pauseIconSize.X);
        ImGui.SetCursorPosY(originalY);
        if (ImGuiComponents.IconButton(pauseIcon))
        {
            var perm = _pair.UserPair!.OwnPermissions;
            perm.SetPaused(!perm.IsPaused());
            _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
        }
        UiSharedService.AttachToolTip(!_pair.UserPair!.OwnPermissions.IsPaused()
            ? "Pause pairing with " + entryUID
            : "Resume pairing with " + entryUID);

        // Flyout Menu
        if (rightSideStart == 0f)
        {
            rightSideStart = windowEndX - barButtonSize.X;
        }
        ImGui.SameLine(windowEndX - barButtonSize.X);
        ImGui.SetCursorPosY(originalY);

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("User Flyout Menu");
        }
        if (ImGui.BeginPopup("User Flyout Menu"))
        {
            UiSharedService.DrawWithID($"buttons-{_pair.UserData.UID}", () =>
            {
                ImGui.Text("Common Pair Functions");
                DrawCommonClientMenu();
                ImGui.Separator();
                DrawPairedClientMenu();
            });
            ImGui.EndPopup();
        }

        return rightSideStart;
    }

    private void DrawCommonClientMenu()
    {
        if (!_pair.IsPaused)
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.User, "Open Profile"))
            {
                _displayHandler.OpenProfile(_pair);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Opens the profile for this user in a new window");
        }
        if (_pair.IsVisible)
        {
            if (UiSharedService.IconTextButton(FontAwesomeIcon.Sync, "Reload last data"))
            {
                _pair.ApplyLastReceivedData(forced: true);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("This reapplies the last received character data to this character");
        }

        if (UiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "Cycle pause state"))
        {
            _ = _apiController.CyclePause(_pair.UserData);
            ImGui.CloseCurrentPopup();
        }
        ImGui.Separator();

        ImGui.Text("Pair Permission Functions");
        var isSticky = _pair.UserPair!.OwnPermissions.IsSticky();
        string stickyText = isSticky ? "Disable Preferred Permissions" : "Enable Preferred Permissions";
        var stickyIcon = isSticky ? FontAwesomeIcon.ArrowCircleDown : FontAwesomeIcon.ArrowCircleUp;
        if (UiSharedService.IconTextButton(stickyIcon, stickyText))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetSticky(!isSticky);
            _ = _apiController.UserSetPairPermissions(new(_pair.UserData, permissions));
        }
        UiSharedService.AttachToolTip("Preferred permissions means that this pair will not be affected by any syncshell permission changes through you.");

        var isDisableSounds = _pair.UserPair!.OwnPermissions.IsDisableSounds();
        string disableSoundsText = isDisableSounds ? "Enable sound sync" : "Disable sound sync";
        var disableSoundsIcon = isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute;
        if (UiSharedService.IconTextButton(disableSoundsIcon, disableSoundsText))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableSounds(!isDisableSounds);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }

        var isDisableAnims = _pair.UserPair!.OwnPermissions.IsDisableAnimations();
        string disableAnimsText = isDisableAnims ? "Enable animation sync" : "Disable animation sync";
        var disableAnimsIcon = isDisableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop;
        if (UiSharedService.IconTextButton(disableAnimsIcon, disableAnimsText))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableAnimations(!isDisableAnims);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }

        var isDisableVFX = _pair.UserPair!.OwnPermissions.IsDisableVFX();
        string disableVFXText = isDisableVFX ? "Enable VFX sync" : "Disable VFX sync";
        var disableVFXIcon = isDisableVFX ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle;
        if (UiSharedService.IconTextButton(disableVFXIcon, disableVFXText))
        {
            var permissions = _pair.UserPair.OwnPermissions;
            permissions.SetDisableVFX(!isDisableVFX);
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(_pair.UserData, permissions));
        }

        if (!_pair.IsPaused)
        {
            ImGui.Separator();
            ImGui.Text("Pair reporting");
            if (UiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, "Report Mare Profile"))
            {
                ImGui.CloseCurrentPopup();
                _showModalReport = true;
            }
            UiSharedService.AttachToolTip("Report this users Mare Profile to the administrative team");
        }
    }

    private void DrawName(float originalY, float leftSide, float rightSide)
    {
        _displayHandler.DrawPairText(_id, _pair, leftSide, originalY, () => rightSide - leftSide);
    }
}