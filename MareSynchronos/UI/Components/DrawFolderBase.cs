using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.Components;

public abstract class DrawFolderBase : IDrawFolder
{
    protected readonly IEnumerable<DrawUserPair> _drawPairs;
    protected readonly string _id;
    protected readonly TagHandler _tagHandler;
    private float _menuWidth = -1;
    protected DrawFolderBase(string id, IEnumerable<DrawUserPair> drawPairs, TagHandler tagHandler)
    {
        _id = id;
        _drawPairs = drawPairs;
        _tagHandler = tagHandler;
    }

    protected abstract bool RenderIfEmpty { get; }
    protected abstract bool RenderMenu { get; }

    public void Draw()
    {
        if (!RenderIfEmpty && !_drawPairs.Any()) return;

        using var id = ImRaii.PushId("folder_" + _id);
        var originalY = ImGui.GetCursorPosY();
        var pauseIconSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var textSize = ImGui.CalcTextSize(_id);
        var textPosY = originalY + pauseIconSize.Y / 2 - textSize.Y / 2;

        // draw opener
        var icon = _tagHandler.IsTagOpen(_id) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;
        ImGui.SetCursorPosY(textPosY);
        UiSharedService.FontText(icon.ToIconString(), UiBuilder.IconFont);
        if (ImGui.IsItemClicked())
        {
            _tagHandler.SetTagOpen(_id, !_tagHandler.IsTagOpen(_id));
        }

        ImGui.SameLine();
        var leftSideEnd = DrawIcon(textPosY, originalY);

        ImGui.SameLine();
        var rightSideStart = DrawRightSide(originalY);

        // draw name
        ImGui.SameLine(leftSideEnd);
        DrawName(textPosY, rightSideStart - leftSideEnd);
        ImGui.Separator();

        // if opened draw content
        if (_tagHandler.IsTagOpen(_id))
        {
            using var indent = ImRaii.PushIndent(20f);
            if (_drawPairs.Any())
            {
                foreach (var item in _drawPairs)
                {
                    item.DrawPairedClient();
                }
            }
            else
            {
                ImGui.TextUnformatted("No users (online)");
            }

            ImGui.Separator();
        }
    }

    protected abstract float DrawIcon(float textPosY, float originalY);

    protected abstract void DrawMenu(float menuWidth);

    protected abstract void DrawName(float originalY, float width);

    protected abstract float DrawRightSide(float originalY, float currentRightSideX);

    private float DrawRightSide(float originalY)
    {
        var barButtonSize = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();

        // Flyout Menu
        var rightSideStart = windowEndX - (RenderMenu ? (barButtonSize.X + spacingX) : spacingX);

        if (RenderMenu)
        {
            ImGui.SameLine(windowEndX - barButtonSize.X);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
            {
                ImGui.OpenPopup("User Flyout Menu");
            }
            if (ImGui.BeginPopup("User Flyout Menu"))
            {
                UiSharedService.DrawWithID($"buttons-{_id}", () =>
                {
                    DrawMenu(_menuWidth);
                });
                _menuWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                ImGui.EndPopup();
            }
            else
            {
                _menuWidth = 0;
            }
        }

        return DrawRightSide(originalY, rightSideStart);
    }
}