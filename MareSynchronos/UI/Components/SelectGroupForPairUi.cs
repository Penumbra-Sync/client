using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.Models;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.Components;

public class SelectGroupForPairUi
{
    /// <summary>
    /// Should the panel show, yes/no
    /// </summary>
    private bool _show;

    /// <summary>
    /// The group UI is always open for a specific pair. This defines which pair the UI is open for.
    /// </summary>
    /// <returns></returns>
    private Pair? _pair;

    /// <summary>
    /// For the add category option, this stores the currently typed in tag name
    /// </summary>
    private string _tagNameToAdd = "";

    private readonly TagHandler _tagHandler;

    public SelectGroupForPairUi(TagHandler tagHandler)
    {
        _show = false;
        _pair = null;
        _tagHandler = tagHandler;
    }

    public void Open(Pair pair)
    {
        _pair = pair;
        // Using "_show" here to de-couple the opening of the popup
        // The popup name is derived from the name the user currently sees, which is
        // based on the showUidForEntry dictionary.
        // We'd have to derive the name here to open it popup modal here, when the Open() is called
        _show = true;
    }


    public void Draw(Dictionary<string, bool> showUidForEntry)
    {
        if (_pair == null)
        {
            return;
        }

        var name = PairName(showUidForEntry, _pair);
        var popupName = $"Choose Groups for {name}";
        // Is the popup supposed to show but did not open yet? Open it
        if (_show)
        {
            ImGui.OpenPopup(popupName);
            _show = false;
        }

        if (ImGui.BeginPopup(popupName))
        {
            var tags = _tagHandler.GetAllTagsSorted();
            var childHeight = tags.Count != 0 ? tags.Count * 25 : 1;
            var childSize = new Vector2(0, childHeight > 100 ? 100 : childHeight) * ImGuiHelpers.GlobalScale;
            
            UiShared.FontText($"Select the groups you want {name} to be in.", UiBuilder.DefaultFont);
            if (ImGui.BeginChild(name + "##listGroups", childSize))
            {
                foreach (var tag in tags)
                {
                    UiShared.DrawWithID($"groups-pair-{_pair.UserData.UID}-{tag}", () => DrawGroupName(_pair, tag));
                }
                ImGui.EndChild();
            }

            ImGui.Separator();
            UiShared.FontText($"Create a new group for {name}.", UiBuilder.DefaultFont);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            {
                HandleAddTag();
            }
            ImGui.SameLine();
            ImGui.InputTextWithHint("##category_name", "New Group", ref _tagNameToAdd, 40);
            {
                if (ImGui.IsKeyDown(ImGuiKey.Enter))
                {
                    HandleAddTag();
                }
            }
            ImGui.EndPopup();
        }
    }

    private void DrawGroupName(Pair pair, string name)
    {
        var hasTagBefore = _tagHandler.HasTag(pair.UserPair!, name);
        var hasTag = hasTagBefore;
        if (ImGui.Checkbox(name, ref hasTag))
        {
            if (hasTag)
            {
                _tagHandler.AddTagToPairedUid(pair.UserPair!, name);
            }
            else
            {
                _tagHandler.RemoveTagFromPairedUid(pair.UserPair!, name);
            }
        }
    }

    private void HandleAddTag()
    {
        if (!_tagNameToAdd.IsNullOrWhitespace())
        {
            _tagHandler.AddTag(_tagNameToAdd);
            if (_pair != null)
            {
                _tagHandler.AddTagToPairedUid(_pair.UserPair!, _tagNameToAdd);
            }
            _tagNameToAdd = string.Empty;
        }
    }

    private string PairName(Dictionary<string, bool> showUidForEntry, Pair pair)
    {
        showUidForEntry.TryGetValue(pair.UserData.UID, out var showUidInsteadOfName);
        var playerText = pair.GetNote();
        if (showUidInsteadOfName || string.IsNullOrEmpty(playerText))
        {
            playerText = pair.UserData.AliasOrUID;
        }
        return playerText;
    }
}