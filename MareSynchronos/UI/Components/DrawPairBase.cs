using Dalamud.Interface;
using Dalamud.Interface.Colors;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public abstract class DrawPairBase
{
    protected static bool _showModalReport = false;
    protected readonly ApiController _apiController;
    protected readonly UidDisplayHandler _displayHandler;
    protected Pair _pair;
    private static bool _reportPopupOpen = false;
    private static string _reportReason = string.Empty;
    private readonly string _id;

    protected DrawPairBase(string id, Pair entry, ApiController apiController, UidDisplayHandler uIDDisplayHandler)
    {
        _id = id;
        _pair = entry;
        _apiController = apiController;
        _displayHandler = uIDDisplayHandler;
    }

    public string UID => _pair.UserData.UID;

    public void DrawPairedClient()
    {
        var originalY = ImGui.GetCursorPosY();
        var pauseIconSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Play);
        var textSize = ImGui.CalcTextSize(_pair.UserData.AliasOrUID);

        var textPosY = originalY + pauseIconSize.Y / 2 - textSize.Y / 2;
        DrawLeftSide(textPosY, originalY);
        ImGui.SameLine();
        var posX = ImGui.GetCursorPosX();
        var rightSide = DrawRightSide(textPosY, originalY);
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

    protected abstract float DrawRightSide(float textPosY, float originalY);

    private void DrawName(float originalY, float leftSide, float rightSide)
    {
        _displayHandler.DrawPairText(_id, _pair, leftSide, originalY, () => rightSide - leftSide);
    }
}