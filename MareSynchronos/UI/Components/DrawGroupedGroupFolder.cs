using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.Components;

public class DrawGroupedGroupFolder : IDrawFolder
{
    private readonly IEnumerable<IDrawFolder> _groups;
    private readonly TagHandler _tagHandler;

    public DrawGroupedGroupFolder(IEnumerable<IDrawFolder> groups, TagHandler tagHandler)
    {
        _groups = groups;
        _tagHandler = tagHandler;
    }

    public void Draw()
    {
        if (!_groups.Any()) return;

        string _id = "__folder_syncshells";
        using var id = ImRaii.PushId(_id);

        var icon = _tagHandler.IsTagOpen(_id) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;
        UiSharedService.FontText(icon.ToIconString(), UiBuilder.IconFont);
        if (ImGui.IsItemClicked())
        {
            _tagHandler.SetTagOpen(_id, !_tagHandler.IsTagOpen(_id));
        }

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(FontAwesomeIcon.UsersRectangle.ToIconString());
        ImGui.SameLine();
        ImGui.TextUnformatted("All Syncshells");
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
