using Dalamud.Interface.Colors;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.WebAPI;
using MareSynchronos.UI.Handlers;
using MareSynchronos.UI.VM;

namespace MareSynchronos.UI.Components;

public class DrawUserPair : DrawPairBase<DrawUserPairVM>
{
    private readonly Button _btnPause;
    private readonly DrawUserPairVM _drawUserPairVM;
    private readonly FlyoutMenu _flyoutMenu;

    public DrawUserPair(DrawUserPairVM drawUserPairVM, UidDisplayHandler displayHandler, ApiController apiController) : base(drawUserPairVM, apiController, displayHandler)
    {
        _drawUserPairVM = drawUserPairVM;
        _btnPause = Button.FromCommand(_drawUserPairVM.PauseCommand);
        _flyoutMenu = FlyoutMenu.FromCommand(_drawUserPairVM.FlyoutMenu);
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
        var pauseIconSize = _btnPause.Size;
        var barButtonSize = _flyoutMenu.Size;
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
                        ImGui.Text("You: " + (_drawUserPairVM.AnimationDisabledFromSource ? "Disabled" : "Enabled") + ", They: " + (_drawUserPairVM.AnimationDisabledFromTarget ? "Disabled" : "Enabled"));
                    }

                    if (_drawUserPairVM.VFXDisabled)
                    {
                        var userVFXText = "VFX sync disabled with " + _drawUserPairVM.DisplayName;
                        UiSharedService.FontText(FontAwesomeIcon.Circle.ToIconString(), UiBuilder.IconFont);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text(userVFXText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.Text("You: " + (_drawUserPairVM.VFXDisabledFromSource ? "Disabled" : "Enabled") + ", They: " + (_drawUserPairVM.VFXDisabledFromTarget ? "Disabled" : "Enabled"));
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
            _btnPause.Draw();
        }

        // Flyout Menu
        if (rightSideStart == 0f)
        {
            rightSideStart = windowEndX - barButtonSize.X;
        }
        ImGui.SameLine(windowEndX - barButtonSize.X);
        ImGui.SetCursorPosY(originalY);

        _flyoutMenu.Draw();

        return rightSideStart;
    }
}