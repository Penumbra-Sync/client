using Dalamud.Interface.Colors;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI;
using MareSynchronos.UI.Handlers;
using Dalamud.Interface.Utility.Raii;

namespace MareSynchronos.UI.Components;

public class DrawIndeterminatePair : DrawPairBase
{
    public DrawIndeterminatePair(string id, Pair entry, ApiController apiController, IdDisplayHandler idDisplayHandler)
        : base(id, entry, apiController, idDisplayHandler)
    {
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        string userPairText = string.Empty;

        ImGui.SetCursorPosY(textPosY);

        if (_pair.IsVisible)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.Eye.ToIconString());
            userPairText = "User is visible";
        }

        if (_pair.IsPaused)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.PauseCircle.ToIconString());
            userPairText = "User is paused";
        }
        else if (!_pair.IsOnline)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.Users.ToIconString());
            userPairText = "User is offline";
        }
        UiSharedService.AttachToolTip(userPairText);
    }

    protected override void DrawPairedClientMenu()
    {
        // no extras here
    }
}
