using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.UI.Handlers;
using System.Collections.Immutable;
using System.Numerics;

namespace MareSynchronos.UI.Components;

public class DrawGroupedGroupFolder : IDrawFolder
{
    private readonly IEnumerable<IDrawFolder> _groups;
    private readonly TagHandler _tagHandler;
    public IImmutableList<DrawUserPair> DrawPairs => throw new NotSupportedException();
    public int OnlinePairs => _groups.SelectMany(g => g.DrawPairs).Where(g => g.Pair.IsOnline).DistinctBy(g => g.Pair.UserData.UID).Count();
    public int TotalPairs => _groups.Sum(g => g.TotalPairs);

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

        ImGui.Dummy(new Vector2(0f, ImGui.GetFrameHeight()));
        ImGui.SameLine();

        var icon = _tagHandler.IsTagOpen(_id) ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight;
        UiSharedService.NormalizedIcon(icon);
        if (ImGui.IsItemClicked())
        {
            _tagHandler.SetTagOpen(_id, !_tagHandler.IsTagOpen(_id));
        }

        ImGui.SameLine();
        UiSharedService.NormalizedIcon(FontAwesomeIcon.UsersRectangle);
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
