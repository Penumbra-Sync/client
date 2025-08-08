using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.UI.Handlers;
using System.Collections.Immutable;
using System.Numerics;

namespace MareSynchronos.UI.Components;

public class DrawGroupedGroupFolder : IDrawFolder
{
    private readonly IEnumerable<IDrawFolder> _groups;
    private readonly TagHandler _tagHandler;
    private readonly UiSharedService _uiSharedService;
    private bool _wasHovered = false;

    public IImmutableList<DrawUserPair> DrawPairs => throw new NotSupportedException();
    public int OnlinePairs => _groups.SelectMany(g => g.DrawPairs).Where(g => g.Pair.IsOnline).DistinctBy(g => g.Pair.UserData.UID).Count();
    public int TotalPairs => _groups.Sum(g => g.TotalPairs);

    public DrawGroupedGroupFolder(IEnumerable<IDrawFolder> groups, TagHandler tagHandler, UiSharedService uiSharedService)
    {
        _groups = groups;
        _tagHandler = tagHandler;
        _uiSharedService = uiSharedService;
    }

    public void Draw()
    {
        if (!_groups.Any()) return;

        string _id = "__folder_syncshells";
        using var id = ImRaii.PushId(_id);
        var color = ImRaii.PushColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), _wasHovered);
        using (ImRaii.Child("folder__" + _id, new System.Numerics.Vector2(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight())))
        {
            ImGui.Dummy(new Vector2(0f, ImGui.GetFrameHeight()));
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 0f)))
                ImGui.SameLine();

            var icon = _tagHandler.IsTagOpen(_id) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;
            ImGui.AlignTextToFramePadding();

            _uiSharedService.IconText(icon);
            if (ImGui.IsItemClicked())
            {
                _tagHandler.SetTagOpen(_id, !_tagHandler.IsTagOpen(_id));
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            _uiSharedService.IconText(FontAwesomeIcon.UsersRectangle);
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing with { X = ImGui.GetStyle().ItemSpacing.X / 2f }))
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("[" + OnlinePairs.ToString() + "]");
            }
            UiSharedService.AttachToolTip(OnlinePairs + " online in all of your joined syncshells" + Environment.NewLine +
                TotalPairs + " pairs combined in all of your joined syncshells");
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("All Syncshells");
        }
        color.Dispose();
        _wasHovered = ImGui.IsItemHovered();

        ImGui.Separator();

        if (_tagHandler.IsTagOpen(_id))
        {
            using var indent = ImRaii.PushIndent(20f);
            foreach (var entry in _groups)
            {
                entry.Draw();
            }
        }
    }
}
