using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

internal class ReportPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private Pair? _reportedPair;
    private string _reportReason = string.Empty;

    public ReportPopupHandler(ApiController apiController, UiSharedService uiSharedService)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
    }

    public Vector2 PopupSize => new(500, 500);

    public void DrawContent()
    {
        using (ImRaii.PushFont(_uiSharedService.UidFont))
            UiSharedService.TextWrapped("Report " + _reportedPair!.UserData.AliasOrUID + " Mare Profile");

        ImGui.InputTextMultiline("##reportReason", ref _reportReason, 500, new Vector2(500 - ImGui.GetStyle().ItemSpacing.X * 2, 200));
        UiSharedService.TextWrapped($"Note: Sending a report will disable the offending profile globally.{Environment.NewLine}" +
            $"The report will be sent to the team of your currently connected Mare Synchronos Service.{Environment.NewLine}" +
            $"The report will include your user and your contact info (Discord User).{Environment.NewLine}" +
            $"Depending on the severity of the offense the users Mare profile or account can be permanently disabled or banned.");
        UiSharedService.ColorTextWrapped("Report spam and wrong reports will not be tolerated and can lead to permanent account suspension.", ImGuiColors.DalamudRed);
        UiSharedService.ColorTextWrapped("This is not for reporting misbehavior or Mare usage but solely for the actual profile. " +
            "Reports that are not solely for the profile will be ignored.", ImGuiColors.DalamudYellow);

        using (ImRaii.Disabled(string.IsNullOrEmpty(_reportReason)))
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.ExclamationTriangle, "Send Report"))
            {
                ImGui.CloseCurrentPopup();
                var reason = _reportReason;
                _ = _apiController.UserReportProfile(new(_reportedPair.UserData, reason));
            }
        }
    }

    public void Open(OpenReportPopupMessage msg)
    {
        _reportedPair = msg.PairToReport;
        _reportReason = string.Empty;
    }
}