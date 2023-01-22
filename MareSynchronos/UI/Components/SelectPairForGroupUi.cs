using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Common.Math;
using ImGuiNET;
using MareSynchronos.API;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.Components;

public class SelectPairForGroupUi
{
    private bool _show = false;
    private bool _opened = false;
    private HashSet<string> _peopleInGroup = new(System.StringComparer.Ordinal);
    private string _tag = string.Empty;
    private readonly TagHandler _tagHandler;
    private readonly Configuration _configuration;

    public SelectPairForGroupUi(TagHandler tagHandler, Configuration configuration)
    {
        _tagHandler = tagHandler;
        _configuration = configuration;
    }

    public void Open(string tag)
    {
        _peopleInGroup = _tagHandler.GetOtherUidsForTag(tag);
        _tag = tag;
        _show = true;
    }

    public void Draw(List<ClientPairDto> pairs, Dictionary<string, bool> showUidForEntry)
    {
        var popupName = $"Choose Users for Group {_tag}";

        if (!_show)
        {
            _opened = false;
        }

        if (_show && !_opened)
        {
            ImGui.SetNextWindowSize(new Vector2(300, 400));
            ImGui.SetNextWindowSizeConstraints(new Vector2(300, 400), new Vector2(300, 1000));
            ImGui.OpenPopup(popupName);
            _opened = true;
        }

        if (ImGui.BeginPopupModal(popupName, ref _show))
        {
            UiShared.FontText($"Select users for group {_tag}", UiBuilder.DefaultFont);
            foreach (var item in pairs.OrderBy(p => PairName(showUidForEntry, p.OtherUID, p.VanityUID), System.StringComparer.OrdinalIgnoreCase).ToList())
            {
                var isInGroup = _peopleInGroup.Contains(item.OtherUID);
                if (ImGui.Checkbox(PairName(showUidForEntry, item.OtherUID, item.VanityUID), ref isInGroup))
                {
                    if (isInGroup)
                    {
                        _tagHandler.AddTagToPairedUid(item, _tag);
                        _peopleInGroup.Add(item.OtherUID);
                    }
                    else
                    {
                        _tagHandler.RemoveTagFromPairedUid(item, _tag);
                        _peopleInGroup.Remove(item.OtherUID);
                    }
                }
            }
        }
        else
        {
            _show = false;
        }
    }

    private string PairName(Dictionary<string, bool> showUidForEntry, string otherUid, string vanityUid)
    {
        showUidForEntry.TryGetValue(otherUid, out var showUidInsteadOfName);
        _configuration.GetCurrentServerUidComments().TryGetValue(otherUid, out var playerText);
        if (showUidInsteadOfName || string.IsNullOrEmpty(playerText))
        {
            playerText = string.IsNullOrEmpty(vanityUid) ? otherUid : vanityUid;
        }
        return playerText;
    }
}
