using Dalamud.Interface.Colors;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI;
using MareSynchronos.UI.Handlers;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Services.Mediator;

namespace MareSynchronos.UI.Components;

public class DrawUngroupedGroupPair : DrawPairBase
{

    public DrawUngroupedGroupPair(string id, Pair entry, ApiController apiController, IdDisplayHandler idDisplayHandler, MareMediator mareMediator)
        : base(id, entry, apiController, idDisplayHandler, mareMediator)
    {
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
            userPairText = "User is paused";
        }
        else if (!_pair.IsOnline)
        {
            ImGui.SetCursorPosY(textPosY);
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Users.ToIconString());
            userPairText = "User is offline";
        }
        else
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Users.ToIconString());
            userPairText = "User is online";
        }

        ImGui.SetCursorPosY(textPosY);

        if (_pair.IsVisible)
        {
            var x = ImGui.GetCursorPosX();
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
        // no extras here
    }
}
