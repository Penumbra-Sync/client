using Dalamud.Interface.Components;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using System.Numerics;
using System.Globalization;

namespace MareSynchronos.UI.Components.Popup;

internal class BanListPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private GroupFullInfoDto _groupFullInfo = null!;
    private List<BannedGroupUserDto> _bannedUsers = new();

    public Vector2 PopupSize => new(700, 300);

    public BanListPopupHandler(ApiController apiController)
    {
        _apiController = apiController;
    }

    public void Open(OpenBanListPopupMessage message)
    {
        _groupFullInfo = message.GroupFullInfoDto;
    }

    public void DrawContent()
    {
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
        {
            _bannedUsers = _apiController.GroupGetBannedUsers(new GroupDto(_groupFullInfo.Group)).Result;
        }

        ImGui.SameLine();

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, "Close"))
        {
            ImGui.CloseCurrentPopup();
        }

        if (ImGui.BeginTable("bannedusertable" + _groupFullInfo.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 1);
            ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.None, 1);
            ImGui.TableSetupColumn("By", ImGuiTableColumnFlags.None, 1);
            ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.None, 2);
            ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 3);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 1);

            ImGui.TableHeadersRow();

            foreach (var bannedUser in _bannedUsers.ToList())
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(bannedUser.UID);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(bannedUser.UserAlias ?? string.Empty);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(bannedUser.BannedBy);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(bannedUser.BannedOn.ToLocalTime().ToString(CultureInfo.CurrentCulture));
                ImGui.TableNextColumn();
                UiSharedService.TextWrapped(bannedUser.Reason);
                ImGui.TableNextColumn();
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, "Unban#" + bannedUser.UID))
                {
                    _ = _apiController.GroupUnbanUser(bannedUser);
                    _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                }
            }

            ImGui.EndTable();
        }
    }
}
