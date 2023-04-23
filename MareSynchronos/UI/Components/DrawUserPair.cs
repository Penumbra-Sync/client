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
    private readonly Button _btnChangeAnims;
    private readonly Button _btnChangeSounds;
    private readonly Button _btnCycle;
    private readonly Button _btnPairGroups;
    private readonly Button _btnPause;
    private readonly Button _btnProfile;
    private readonly Button _btnReload;
    private readonly Button _btnRemovePair;
    private readonly Button _btnReport;
    private readonly DrawUserPairVM _drawUserPairVM;

    public DrawUserPair(DrawUserPairVM drawUserPairVM, UidDisplayHandler displayHandler, ApiController apiController) : base(drawUserPairVM, apiController, displayHandler)
    {
        _drawUserPairVM = drawUserPairVM;
        _btnProfile = Button.FromCommand(_drawUserPairVM.OpenProfileCommand);
        _btnReload = Button.FromCommand(_drawUserPairVM.ReloadLastDataCommand);
        _btnCycle = Button.FromCommand(_drawUserPairVM.CyclePauseStateCommand);
        _btnPairGroups = Button.FromCommand(_drawUserPairVM.SelectPairGroupsCommand);
        _btnChangeSounds = Button.FromCommand(_drawUserPairVM.ChangeSoundsCommand);
        _btnChangeAnims = Button.FromCommand(_drawUserPairVM.ChangeAnimationsCommand);
        _btnRemovePair = Button.FromCommand(_drawUserPairVM.RemovePairCommand);
        _btnReport = Button.FromCommand(_drawUserPairVM.ReportProfileCommand);
        _btnPause = Button.FromCommand(_drawUserPairVM.PauseCommand);
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
        var btnSizeProfile = _btnProfile.Size;
        var max = Enumerable.Max<float>(new[] { btnSizeProfile.X, _btnReload.Size.X, _btnCycle.Size.X,
            _btnPairGroups.Size.X, _btnChangeSounds.Size.X, _btnChangeAnims.Size.X, _btnRemovePair.Size.X, _btnReport.Size.X });
        var btnSize = new Vector2(max, btnSizeProfile.Y);

        _btnProfile.Draw(btnSize);
        _btnReload.Draw(btnSize);
        _btnCycle.Draw(btnSize);
        ImGui.Separator();
        _btnPairGroups.Draw(btnSize);
        _btnChangeSounds.Draw(btnSize);
        _btnChangeAnims.Draw(btnSize);
        _btnRemovePair.Draw(btnSize);
        ImGui.Separator();
        _btnReport.Draw(btnSize);
    }
}