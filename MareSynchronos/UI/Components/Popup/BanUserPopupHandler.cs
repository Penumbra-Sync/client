using Dalamud.Interface.Components;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

internal class BanUserPopupHandler : PopupHandlerBase
{
    private readonly ApiController _apiController;
    private Pair _reportedPair = null!;
    private GroupFullInfoDto _group = null!;
    private string _banReason = string.Empty;
    protected override Vector2 PopupSize => new(500, 200);

    public BanUserPopupHandler(ILogger<ReportPopupHandler> logger, MareMediator mareMediator, ApiController apiController, UiSharedService uiSharedService)
        : base("BanUserPopup", logger, mareMediator, uiSharedService)
    {
        Mediator.Subscribe<BanUserPopupMessage>(this, (msg) =>
        {
            _openPopup = true;
            _reportedPair = msg.PairToBan;
            _group = msg.GroupFullInfoDto;
            _banReason = string.Empty;
        });
        _apiController = apiController;
    }

    protected override void DrawContent()
    {
        UiSharedService.TextWrapped("User " + (_reportedPair.UserData.AliasOrUID) + " will be banned and removed from this Syncshell.");
        ImGui.InputTextWithHint("##banreason", "Ban Reason", ref _banReason, 255);

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, "Cancel"))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserSlash, "Ban User"))
        {
            ImGui.CloseCurrentPopup();
            var reason = _banReason;
            _ = _apiController.GroupBanUser(new GroupPairDto(_group.Group, _reportedPair.UserData), reason);
            _banReason = string.Empty;
        }
        UiSharedService.TextWrapped("The reason will be displayed in the banlist. The current server-side alias if present (Vanity ID) will automatically be attached to the reason.");
    }
}

