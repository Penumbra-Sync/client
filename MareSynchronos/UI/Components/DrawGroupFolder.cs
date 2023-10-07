using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public class DrawGroupFolder : DrawFolderBase
{
    private readonly ApiController _apiController;
    private readonly IdDisplayHandler _idDisplayHandler;
    private readonly GroupFullInfoDto _groupFullInfoDto;
    protected override bool RenderIfEmpty => true;
    protected override bool RenderMenu => true;

    public DrawGroupFolder(string id, GroupFullInfoDto groupFullInfoDto, ApiController apiController,
        IEnumerable<DrawGroupPair> drawPairs, TagHandler tagHandler, IdDisplayHandler idDisplayHandler) :
        base(id, drawPairs, tagHandler)
    {
        _groupFullInfoDto = groupFullInfoDto;
        _apiController = apiController;
        _idDisplayHandler = idDisplayHandler;
    }

    protected override float DrawIcon(float textPosY, float originalY)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        ImGui.SetCursorPosY(textPosY);
        ImGui.Text(FontAwesomeIcon.Users.ToIconString());
        if (string.Equals(_groupFullInfoDto.OwnerUID, _apiController.UID, StringComparison.Ordinal))
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.Text(FontAwesomeIcon.Crown.ToIconString());
        }
        else if (_groupFullInfoDto.GroupPairUserInfos[_apiController.UID].IsModerator())
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.Text(FontAwesomeIcon.UserShield.ToIconString());
        }
        ImGui.SameLine();
        return ImGui.GetCursorPosX();
    }

    protected override void DrawMenu()
    {
        ImGui.Text("Syncshell Menu");
        // todo group pair menu
    }

    protected override void DrawName(float originalY, float width)
    {
        _idDisplayHandler.DrawGroupText(_id, _groupFullInfoDto, ImGui.GetCursorPosX(), originalY, () => width);
    }

    protected override float DrawRightSide(float originalY, float currentRightSideX)
    {
        // todo status icon, pause
        return currentRightSideX;
    }
}
