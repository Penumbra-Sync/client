using Dalamud.Interface;

using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;

using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public class DrawPairMultiSync : DrawPairBase
{
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly List<GroupFullInfoDto> _syncedGroups;

    public DrawPairMultiSync(string id, List<GroupFullInfoDto> syncedGroups, Pair entry,
        ApiController apiController, IdDisplayHandler idDisplayHandler, MareMediator mareMediator,
        SelectTagForPairUi selectTagForPairUi)
        : base(id, entry, apiController, idDisplayHandler, mareMediator)
    {
        _syncedGroups = syncedGroups;
        _selectTagForPairUi = selectTagForPairUi;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        string userPairText = string.Empty;

        ImGui.SetCursorPosY(textPosY);

        if (_pair.IsPaused)
        {
            ImGui.SetCursorPosY(textPosY);
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.PauseCircle.ToIconString());
            userPairText = _pair.UserData.AliasOrUID + " is paused";
        }
        else if (!_pair.IsOnline)
        {
            ImGui.SetCursorPosY(textPosY);
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional
                ? FontAwesomeIcon.User.ToIconString() : FontAwesomeIcon.Users.ToIconString());
            userPairText = _pair.UserData.AliasOrUID + " is offline";
        }
        else
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional
                ? FontAwesomeIcon.User.ToIconString() : FontAwesomeIcon.Users.ToIconString());
            userPairText = _pair.UserData.AliasOrUID + " is online";
        }
        if (_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.OneSided)
        {
            userPairText += Environment.NewLine + "User has not added you back";
        }
        else if (_pair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional)
        {
            userPairText += Environment.NewLine + "You are directly Paired";
        }
        if (_syncedGroups.Any())
        {
            userPairText += Environment.NewLine + string.Join(Environment.NewLine, _syncedGroups.Select(g => "Paired through " + g.GroupAliasOrGID));
        }
        UiSharedService.AttachToolTip(userPairText);

        if (_pair.UserPair.OwnPermissions.IsSticky())
        {
            ImGui.SetCursorPosY(textPosY);

            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing with { X = ImGui.GetStyle().ItemSpacing.X * 3 / 4f }))
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.SameLine();
                ImGui.TextUnformatted(FontAwesomeIcon.ArrowCircleUp.ToIconString());
            }

            UiSharedService.AttachToolTip(_pair.UserData.AliasOrUID + " has preferred permissions enabled");
        }

        if (_pair.IsVisible)
        {
            ImGui.SetCursorPosY(textPosY);
            ImGui.SameLine();
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Eye.ToIconString());
            userPairText = "User is visible: " + _pair.PlayerName;
        }
        UiSharedService.AttachToolTip(userPairText);
    }

    protected override void DrawPairedClientMenu()
    {
        DrawPairIndividual.IndividualMenu(_pair, _selectTagForPairUi, _apiController);

        if (_syncedGroups.Any()) ImGui.Separator();
        foreach (var entry in _syncedGroups)
        {
            bool selfIsOwner = string.Equals(_apiController.UID, entry.Owner.UID, StringComparison.Ordinal);
            bool selfIsModerator = entry.GroupUserInfo.IsModerator();
            bool userIsModerator = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var modinfo) && modinfo.IsModerator();
            bool userIsPinned = entry.GroupPairUserInfos.TryGetValue(_pair.UserData.UID, out var info) && info.IsPinned();
            if (selfIsOwner || selfIsModerator)
            {
                if (ImGui.BeginMenu(entry.GroupAliasOrGID + " Moderation Functions"))
                {
                    DrawPairSingleGroup.DrawSyncshellMenu(entry, _apiController, _mediator, _pair, selfIsOwner, selfIsModerator, userIsPinned, userIsModerator);
                    ImGui.EndMenu();
                }
            }
        }
    }
}