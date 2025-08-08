using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.UI.Handlers;

using System.Numerics;

namespace MareSynchronos.UI.Components;

public class SelectPairForTagUi
{
    private readonly TagHandler _tagHandler;
    private readonly IdDisplayHandler _uidDisplayHandler;
    private string _filter = string.Empty;
    private bool _opened = false;
    private HashSet<string> _peopleInGroup = new(StringComparer.Ordinal);
    private bool _show = false;
    private string _tag = string.Empty;

    public SelectPairForTagUi(TagHandler tagHandler, IdDisplayHandler uidDisplayHandler)
    {
        _tagHandler = tagHandler;
        _uidDisplayHandler = uidDisplayHandler;
    }

    public void Draw(List<Pair> pairs)
    {
        var workHeight = ImGui.GetMainViewport().WorkSize.Y / ImGuiHelpers.GlobalScale;
        var minSize = new Vector2(300, workHeight < 400 ? workHeight : 400) * ImGuiHelpers.GlobalScale;
        var maxSize = new Vector2(300, 1000) * ImGuiHelpers.GlobalScale;

        var popupName = $"Choose Users for Group {_tag}";

        if (!_show)
        {
            _opened = false;
        }

        if (_show && !_opened)
        {
            ImGui.SetNextWindowSize(minSize);
            UiSharedService.CenterNextWindow(minSize.X, minSize.Y, ImGuiCond.Always);
            ImGui.OpenPopup(popupName);
            _opened = true;
        }

        ImGui.SetNextWindowSizeConstraints(minSize, maxSize);
        if (ImGui.BeginPopupModal(popupName, ref _show, ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal))
        {
            ImGui.TextUnformatted($"Select users for group {_tag}");

            ImGui.InputTextWithHint("##filter", "Filter", ref _filter, 255, ImGuiInputTextFlags.None);
            foreach (var item in pairs
                .Where(p => string.IsNullOrEmpty(_filter) || PairName(p).Contains(_filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => PairName(p), StringComparer.OrdinalIgnoreCase)
                .ToList())
            {
                var isInGroup = _peopleInGroup.Contains(item.UserData.UID);
                if (ImGui.Checkbox(PairName(item), ref isInGroup))
                {
                    if (isInGroup)
                    {
                        _tagHandler.AddTagToPairedUid(item.UserData.UID, _tag);
                        _peopleInGroup.Add(item.UserData.UID);
                    }
                    else
                    {
                        _tagHandler.RemoveTagFromPairedUid(item.UserData.UID, _tag);
                        _peopleInGroup.Remove(item.UserData.UID);
                    }
                }
            }
            ImGui.EndPopup();
        }
        else
        {
            _filter = string.Empty;
            _show = false;
        }
    }

    public void Open(string tag)
    {
        _peopleInGroup = _tagHandler.GetOtherUidsForTag(tag);
        _tag = tag;
        _show = true;
    }

    private string PairName(Pair pair)
    {
        return _uidDisplayHandler.GetPlayerText(pair).text;
    }
}