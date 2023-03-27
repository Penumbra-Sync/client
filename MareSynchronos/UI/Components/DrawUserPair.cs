using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.WebAPI;
using MareSynchronos.UI.Handlers;
using FFXIVClientStructs.FFXIV.Common.Math;
using MareSynchronos.UI.VM;

namespace MareSynchronos.UI.Components;

public class DrawUserPair : DrawPairBase
{
    private readonly DrawUserPairVM _drawUserPairVM;

    public DrawUserPair(DrawUserPairVM drawUserPairVM, UidDisplayHandler displayHandler, ApiController apiController) : base(drawUserPairVM, apiController, displayHandler)
    {
        _drawUserPairVM = drawUserPairVM;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        var connection = _drawUserPairVM.GetConnection();
        var visible = _drawUserPairVM.GetVisibility();
        ImGui.SetCursorPosY(textPosY);
        UiSharedService.ColorIcon(connection.Icon, connection.Color);
        UiSharedService.AttachToolTip(connection.PopupText);
        if (visible.Icon != FontAwesomeIcon.None)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            UiSharedService.ColorIcon(visible.Icon, visible.Color);
            UiSharedService.AttachToolTip(visible.PopupText);
        }
    }

    protected override float DrawRightSide(float textPosY, float originalY)
    {
        var pauseButton = Button.FromCommand(_drawUserPairVM.PauseCommand);
        var pauseIconSize = pauseButton.GetSize();
        var barButtonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
        var rightSideStart = 0f;

        if (!_drawUserPairVM.OneSidedPair)
        {
            if (_drawUserPairVM.HasModifiedPermissions)
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

                    if (_drawUserPairVM.SoundDisabled)
                    {
                        var userSoundsText = "Sound sync disabled with " + _drawUserPairVM.DisplayName;
                        UiSharedService.FontText(FontAwesomeIcon.VolumeOff.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userSoundsText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text("You: " + (_drawUserPairVM.SoundDisabledFromSource ? "Disabled" : "Enabled") + ", They: " + (_drawUserPairVM.SoundDisabledFromTarget ? "Disabled" : "Enabled"));
                    }

                    if (_drawUserPairVM.AnimationDisabled)
                    {
                        var userAnimText = "Animation sync disabled with " + _drawUserPairVM.DisplayName;
                        UiSharedService.FontText(FontAwesomeIcon.Stop.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userAnimText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text("You: " + (_drawUserPairVM.AnimationDisabledFromTarget ? "Disabled" : "Enabled") + ", They: " + (_drawUserPairVM.AnimationDisabledFromTarget ? "Disabled" : "Enabled"));
                    }

                    ImGui.EndTooltip();
                }
            }

            if (rightSideStart == 0f)
            {
                rightSideStart = windowEndX - barButtonSize.X - spacingX * 2 - pauseIconSize.X;
            }
            ImGui.SameLine(windowEndX - barButtonSize.X - spacingX - pauseIconSize.X);
            ImGui.SetCursorPosY(originalY);
            pauseButton.Draw();
        }

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
            UiSharedService.DrawWithID($"buttons-{_drawUserPairVM.DisplayName}", DrawPairedClientMenu);
            ImGui.EndPopup();
        }

        return rightSideStart;
    }

    private void DrawPairedClientMenu()
    {
        var profile = Button.FromCommand(_drawUserPairVM.OpenProfileCommand);
        var reload = Button.FromCommand(_drawUserPairVM.ReloadLastDataCommand);
        var cycle = Button.FromCommand(_drawUserPairVM.CyclePauseStateCommand);
        var pairGroups = Button.FromCommand(_drawUserPairVM.SelectPairGroupsCommand);
        var changeSounds = Button.FromCommand(_drawUserPairVM.ChangeSoundsCommand);
        var changeAnims = Button.FromCommand(_drawUserPairVM.ChangeAnimationsCommand);
        var removePair = Button.FromCommand(_drawUserPairVM.RemovePairCommand);
        var report = Button.FromCommand(_drawUserPairVM.ReportProfileCommand);
        var btnSizeProfile = profile.GetSize();
        var max = Enumerable.Max<float>(new[] { btnSizeProfile.X, reload.GetSize().X, cycle.GetSize().X,
            pairGroups.GetSize().X, changeSounds.GetSize().X, changeAnims.GetSize().X, removePair.GetSize().X, report.GetSize().X });
        var btnSize = new Vector2(max, btnSizeProfile.Y);

        profile.Draw(btnSize);
        reload.Draw(btnSize);
        cycle.Draw(btnSize);
        ImGui.Separator();
        pairGroups.Draw(btnSize);
        changeSounds.Draw(btnSize);
        changeAnims.Draw(btnSize);
        removePair.Draw(btnSize);
        ImGui.Separator();
        report.Draw(btnSize);
    }
}