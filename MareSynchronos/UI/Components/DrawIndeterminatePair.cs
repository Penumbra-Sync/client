using Dalamud.Interface.Colors;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI;
using MareSynchronos.UI.Handlers;
using MareSynchronos.API.Data;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.Group;

namespace MareSynchronos.UI.Components;

public class DrawIndeterminatePair : DrawPairBase
{
    private readonly List<GroupFullInfoDto> _groups;

    public DrawIndeterminatePair(string id, Pair entry, List<GroupFullInfoDto> groups, ApiController apiController, IdDisplayHandler idDisplayHandler)
        : base(id, entry, apiController, idDisplayHandler)
    {
        _groups = groups;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        string userPairText = string.Empty;

        if (_pair.IsVisible)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.Eye.ToIconString());
            userPairText = "User is visible";
        }

        if (!_pair.IsPaused)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.PauseCircle.ToIconString());
            userPairText = "User is offline";
        }

        if (!_pair.IsOnline)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.Unlink.ToIconString());
            userPairText = "User is offline";
        }
        UiSharedService.AttachToolTip(userPairText);
    }

    protected override void DrawPairedClientMenu()
    {
        // no extras here
    }
}
