using Dalamud.Interface.Colors;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using System.Numerics;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.WebAPI;
using MareSynchronos.UI.Handlers;
using MareSynchronos.API.Dto.Group;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.Services.Mediator;
using Dalamud.Interface.Components;

namespace MareSynchronos.UI.Components;

public class DrawGroupPair : DrawPairBase
{
    private readonly GroupFullInfoDto _groupFullInfo;
    private bool IsPinned => _groupFullInfo.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var info) && info.IsPinned();
    private bool IsModerator => _groupFullInfo.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var info) && info.IsModerator();
    private bool IsOwner => string.Equals(_groupFullInfo.OwnerUID, _pair.UserData.UID, StringComparison.Ordinal);

    public DrawGroupPair(string id, GroupFullInfoDto groupFullInfo, Pair entry,
        IdDisplayHandler displayHandler, ApiController apiController, MareMediator mareMediator)
        : base(id, entry, apiController, displayHandler, mareMediator)
    {
        _pair = entry;
        _groupFullInfo = groupFullInfo;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        if (IsOwner)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Crown.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("Owner of this Syncshell");
        }
        else if (IsModerator)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("Moderator of this Syncshell");
        }
        else if (IsPinned)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Thumbtack.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("Pinned in this Syncshell");
        }

        FontAwesomeIcon connectionIcon;
        Vector4 connectionColor;
        string connectionText;
        if (_pair.UserPair!.OwnPermissions.IsPaused() || _pair.UserPair!.OtherPermissions.IsPaused())
        {
            connectionIcon = FontAwesomeIcon.Pause;
            connectionText = "Pairing status with " + _pair.UserData.AliasOrUID + " is paused";
            connectionColor = ImGuiColors.DalamudYellow;
        }
        else
        {
            connectionIcon = FontAwesomeIcon.Users;
            connectionText = _pair.IsOnline ? _pair.UserData.AliasOrUID + " is online" : _pair.UserData.AliasOrUID + " is offline";
            connectionColor = _pair.IsOnline ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        }

        ImGui.SetCursorPosY(textPosY);
        using (ImRaii.PushFont(UiBuilder.IconFont))
            UiSharedService.ColorText(connectionIcon.ToIconString(), connectionColor);
        UiSharedService.AttachToolTip(connectionText);
        if (_pair.UserPair.OwnPermissions.IsSticky())
        {
            var x = ImGui.GetCursorPosX();
            ImGui.SetCursorPosY(textPosY);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var iconsize = ImGui.CalcTextSize(FontAwesomeIcon.ArrowCircleUp.ToIconString()).X;
                ImGui.SameLine(x + iconsize + (ImGui.GetStyle().ItemSpacing.X));
                ImGui.TextUnformatted(FontAwesomeIcon.ArrowCircleUp.ToIconString());
            }
            UiSharedService.AttachToolTip(_pair.UserData.AliasOrUID + " has preferred permissions enabled");
        }
        if (_pair is { IsOnline: true, IsVisible: true })
        {
            var x = ImGui.GetCursorPosX();
            ImGui.SetCursorPosY(textPosY);
            var iconsize = ImGui.CalcTextSize(FontAwesomeIcon.Eye.ToIconString()).X;
            ImGui.SameLine(x + iconsize + (ImGui.GetStyle().ItemSpacing.X));
            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.Eye.ToIconString(), ImGuiColors.ParsedGreen);
            ImGui.PopFont();
            UiSharedService.AttachToolTip(_pair.UserData.AliasOrUID + " is visible: " + _pair.PlayerName!);
        }
    }

    protected override void DrawPairedClientMenu()
    {
        bool selfIsOwner = string.Equals(_apiController.UID, _groupFullInfo.Owner.UID, StringComparison.Ordinal);
        bool selfIsModerator = _groupFullInfo.GroupPairUserInfos.TryGetValue(_apiController.UID, out var selfinfo) && selfinfo.IsModerator();
        if ((selfIsOwner || selfIsModerator) && (!IsModerator))
        {
            ImGui.TextUnformatted("Syncshell Moderator Functions");
            var pinText = IsPinned ? "Unpin user" : "Pin user";
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Thumbtack, pinText))
            {
                ImGui.CloseCurrentPopup();
                if (!_groupFullInfo.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
                {
                    userinfo = API.Data.Enum.GroupPairUserInfo.IsPinned;
                }
                else
                {
                    userinfo.SetPinned(!userinfo.IsPinned());
                }
                _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(_groupFullInfo.Group, _pair.UserData, userinfo));
            }
            UiSharedService.AttachToolTip("Pin this user to the Syncshell. Pinned users will not be deleted in case of a manually initiated Syncshell clean");

            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "Remove user") && UiSharedService.CtrlPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupRemoveUser(new(_groupFullInfo.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to remove user " + (_pair.UserData.AliasOrUID) + " from Syncshell");

            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserSlash, "Ban User"))
            {
                _mediator.Publish(new OpenBanUserPopupMessage(_pair, _groupFullInfo));
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Ban user from this Syncshell");

            ImGui.Separator();
        }

        if (selfIsOwner)
        {
            ImGui.TextUnformatted("Syncshell Owner Functions");
            string modText = IsModerator ? "Demod user" : "Mod user";
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserShield, modText) && UiSharedService.CtrlPressed())
            {
                ImGui.CloseCurrentPopup();
                if (!_groupFullInfo.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var userinfo))
                {
                    userinfo = API.Data.Enum.GroupPairUserInfo.IsModerator;
                }
                else
                {
                    userinfo.SetModerator(!userinfo.IsModerator());
                }

                _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(_groupFullInfo.Group, _pair.UserData, userinfo));
            }
            UiSharedService.AttachToolTip("Hold CTRL to change the moderator status for " + (_pair.UserData.AliasOrUID) + Environment.NewLine +
                "Moderators can kick, ban/unban, pin/unpin users and clear the Syncshell.");

            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Crown, "Transfer Ownership") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
            {
                ImGui.CloseCurrentPopup();
                _ = _apiController.GroupChangeOwnership(new(_groupFullInfo.Group, _pair.UserData));
            }
            UiSharedService.AttachToolTip("Hold CTRL and SHIFT and click to transfer ownership of this Syncshell to "
                + (_pair.UserData.AliasOrUID) + Environment.NewLine + "WARNING: This action is irreversible.");

            ImGui.Separator();
        }
    }
}