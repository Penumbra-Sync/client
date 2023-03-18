using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.Components;

public abstract class DrawPairBase
{
    protected Pair _pair;
    private readonly UidDisplayHandler _displayHandler;

    protected DrawPairBase(Pair entry, UidDisplayHandler uIDDisplayHandler)
    {
        _pair = entry;
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
    }

    protected abstract void DrawLeftSide(float textPosY, float originalY);

    protected abstract float DrawRightSide(float textPosY, float originalY);

    private void DrawName(float originalY, float leftSide, float rightSide)
    {
        _displayHandler.DrawPairText(_pair, leftSide, originalY, () => rightSide - leftSide);
    }
}