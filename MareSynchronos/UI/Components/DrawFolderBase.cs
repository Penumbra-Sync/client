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
    public int OnlinePairs => _drawPairs.Count(u => u.Pair.IsOnline);
    public int TotalPairs { get; }
    protected DrawFolderBase(string id, IEnumerable<DrawUserPair> drawPairs, TagHandler tagHandler, int totalPairs)
    {
        _id = id;
        _drawPairs = drawPairs;
        _tagHandler = tagHandler;
        TotalPairs = totalPairs;
    }

    protected abstract bool RenderIfEmpty { get; }
    protected abstract bool RenderMenu { get; }

    public void Draw()
    {
        if (!RenderIfEmpty && !_drawPairs.Any()) return;

        using var id = ImRaii.PushId("folder_" + _id);

        // draw opener
        var icon = _tagHandler.IsTagOpen(_id) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;

        ImGui.AlignTextToFramePadding();

        UiSharedService.FontText(icon.ToIconString(), UiBuilder.IconFont);
        if (ImGui.IsItemClicked())
        {
            _tagHandler.SetTagOpen(_id, !_tagHandler.IsTagOpen(_id));
        }

        ImGui.SameLine();
        var leftSideEnd = DrawIcon();

        ImGui.SameLine();
        var rightSideStart = DrawRightSideInternal();

        // draw name
        ImGui.SameLine(leftSideEnd);
        DrawName(rightSideStart - leftSideEnd);
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

    protected abstract float DrawIcon();

    protected abstract void DrawMenu(float menuWidth);

    protected abstract void DrawName(float width);

    protected abstract float DrawRightSide(float currentRightSideX);

    private float DrawRightSideInternal()
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

        return DrawRightSide(rightSideStart);
    }
}