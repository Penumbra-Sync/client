using Dalamud.Interface;
using Dalamud.Interface.Colors;
using ImGuiNET;
using MareSynchronos.UI.Handlers;
using MareSynchronos.UI.VM;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public abstract class DrawPairBase<T> : UIElementBase<T> where T : DrawPairVMBase
{
    protected readonly ApiController _apiController;
    protected readonly UidDisplayHandler _displayHandler;
    private readonly DrawPairVMBase _drawUserPairVMBase;

    protected DrawPairBase(T drawUserPairVMBase, ApiController apiController, UidDisplayHandler uIDDisplayHandler) : base(drawUserPairVMBase)
    {
        _drawUserPairVMBase = drawUserPairVMBase;
        _apiController = apiController;
        _displayHandler = uIDDisplayHandler;
    }

    public string UID => _drawUserPairVMBase.UserData.UID;

    public void DrawPairedClient()
    {
        var originalY = ImGui.GetCursorPosY();
        var pauseIconSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Play);
        var textSize = ImGui.CalcTextSize(_drawUserPairVMBase.DisplayName);

        var textPosY = originalY + pauseIconSize.Y / 2 - textSize.Y / 2;
        DrawLeftSide(textPosY, originalY);
        ImGui.SameLine();
        var posX = ImGui.GetCursorPosX();
        var rightSide = DrawRightSide(textPosY, originalY);
        DrawName(originalY, posX, rightSide);

        PopupModal.FromConditionalModal(_drawUserPairVMBase.ReportModal)
            .Draw(() =>
            {
                UiSharedService.TextWrapped("Report " + (_drawUserPairVMBase.DisplayName) + " Mare Profile");
                _drawUserPairVMBase.ExecuteWithProp<string>(nameof(DrawUserPairVM.ReportReason), (reason) =>
                {
                    ImGui.InputTextMultiline("##reportReason", ref reason, 500, new System.Numerics.Vector2(500 - ImGui.GetStyle().ItemSpacing.X * 2, 200));
                    return reason;
                });
                UiSharedService.TextWrapped($"Note: Sending a report will disable the offending profile globally.{Environment.NewLine}" +
                    $"The report will be sent to the team of your currently connected Mare Synchronos Service.{Environment.NewLine}" +
                    $"The report will include your user and your contact info (Discord User).{Environment.NewLine}" +
                    $"Depending on the severity of the offense the users Mare profile or account can be permanently disabled or banned.");
                UiSharedService.ColorText("Report spam and wrong reports will not be tolerated and can lead to permanent account suspension.", ImGuiColors.DalamudRed, true);
                if (string.IsNullOrEmpty(_drawUserPairVMBase.ReportReason)) ImGui.BeginDisabled();
                if (ImGui.Button("Send Report"))
                {
                    ImGui.CloseCurrentPopup();
                    _drawUserPairVMBase.SendReport();
                }
                if (string.IsNullOrEmpty(_drawUserPairVMBase.ReportReason)) ImGui.EndDisabled();
                UiSharedService.SetScaledWindowSize(500);
            });
    }

    protected abstract void DrawLeftSide(float textPosY, float originalY);

    protected abstract float DrawRightSide(float textPosY, float originalY);

    private void DrawName(float originalY, float leftSide, float rightSide)
    {
        _displayHandler.DrawPairText(_drawUserPairVMBase, leftSide, originalY, () => rightSide - leftSide);
    }
}