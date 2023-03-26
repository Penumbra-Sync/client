using Dalamud.Interface;
using Dalamud.Interface.Components;
using ImGuiNET;
using MareSynchronos.MareConfiguration;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.Components;

public class PairGroupsUi
{
    private readonly Func<DrawUserPairVM, DrawUserPair> _drawUserPairFactory;
    private readonly MareConfigService _mareConfig;
    private readonly SelectPairForGroupUi _selectGroupForPairUi;
    private readonly TagHandler _tagHandler;

    public PairGroupsUi(MareConfigService mareConfig, TagHandler tagHandler, SelectPairForGroupUi selectGroupForPairUi, Func<DrawUserPairVM, DrawUserPair> drawUserPairFactory)
    {
        _mareConfig = mareConfig;
        _tagHandler = tagHandler;
        _selectGroupForPairUi = selectGroupForPairUi;
        _drawUserPairFactory = drawUserPairFactory;
    }

    public void Draw<T>(List<T> visibleUsers, List<T> onlineUsers, List<T> offlineUsers) where T : DrawPairVMBase
    {
        // Only render those tags that actually have pairs in them, otherwise
        // we can end up with a bunch of useless pair groups
        var tagsWithPairsInThem = _tagHandler.GetAllTagsSorted();
        var allUsers = visibleUsers.Concat(onlineUsers).Concat(offlineUsers).ToList();
        if (typeof(T) == typeof(DrawPairVMBase))
        {
            DrawUserPairs(tagsWithPairsInThem, allUsers.Cast<DrawUserPairVM>().ToList(), visibleUsers.Cast<DrawUserPairVM>(), onlineUsers.Cast<DrawUserPairVM>(), offlineUsers.Cast<DrawUserPairVM>());
        }
    }

    private void DrawButtons(string tag, List<DrawUserPairVM> availablePairsInThisTag)
    {
        var allArePaused = availablePairsInThisTag.All(pair => pair.IsPausedFromSource);
        var pauseButton = allArePaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var flyoutMenuX = UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars).X;
        var pauseButtonX = UiSharedService.GetIconButtonSize(pauseButton).X;
        var windowX = ImGui.GetWindowContentRegionMin().X;
        var windowWidth = UiSharedService.GetWindowContentRegionWidth();
        var spacingX = ImGui.GetStyle().ItemSpacing.X;

        var buttonPauseOffset = windowX + windowWidth - flyoutMenuX - spacingX - pauseButtonX;
        ImGui.SameLine(buttonPauseOffset);
        if (ImGuiComponents.IconButton(pauseButton))
        {
            // If all of the currently visible pairs (after applying filters to the pairs)
            // are paused we display a resume button to resume all currently visible (after filters)
            // pairs. Otherwise, we just pause all the remaining pairs.
            if (allArePaused)
            {
                // If all are paused => resume all
                ResumeAllPairs(availablePairsInThisTag);
            }
            else
            {
                // otherwise pause all remaining
                PauseRemainingPairs(availablePairsInThisTag);
            }
        }
        if (allArePaused)
        {
            UiSharedService.AttachToolTip($"Resume pairing with all pairs in {tag}");
        }
        else
        {
            UiSharedService.AttachToolTip($"Pause pairing with all pairs in {tag}");
        }

        var buttonDeleteOffset = windowX + windowWidth - flyoutMenuX;
        ImGui.SameLine(buttonDeleteOffset);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("Group Flyout Menu");
        }

        if (ImGui.BeginPopup("Group Flyout Menu"))
        {
            UiSharedService.DrawWithID($"buttons-{tag}", () => DrawGroupMenu(tag));
            ImGui.EndPopup();
        }
    }

    private void DrawCategory(string tag, IEnumerable<DrawUserPairVM> onlineUsers, IEnumerable<DrawUserPairVM> allUsers, IEnumerable<DrawUserPairVM>? visibleUsers = null)
    {
        IEnumerable<DrawUserPairVM> usersInThisTag;
        HashSet<string>? otherUidsTaggedWithTag = null;
        bool isSpecialTag = false;
        int visibleInThisTag = 0;
        if (tag is TagHandler.CustomOfflineTag or TagHandler.CustomOnlineTag or TagHandler.CustomVisibleTag or TagHandler.CustomUnpairedTag)
        {
            usersInThisTag = onlineUsers;
            isSpecialTag = true;
        }
        else
        {
            otherUidsTaggedWithTag = _tagHandler.GetOtherUidsForTag(tag);
            usersInThisTag = onlineUsers
                .Where(pair => otherUidsTaggedWithTag.Contains(pair.UserData.UID))
                .ToList();
            visibleInThisTag = visibleUsers?.Count(p => otherUidsTaggedWithTag.Contains(p.UserData.UID)) ?? 0;
        }

        if (isSpecialTag && !usersInThisTag.Any()) return;

        DrawName(tag, isSpecialTag, visibleInThisTag, usersInThisTag.Count(), otherUidsTaggedWithTag?.Count);
        if (!isSpecialTag && onlineUsers.Any())
        {
            UiSharedService.DrawWithID($"group-{tag}-buttons", () => DrawButtons(tag, allUsers.Where(p => otherUidsTaggedWithTag!.Contains(p.UserData.UID)).ToList()));
        }

        if (!_tagHandler.IsTagOpen(tag)) return;

        ImGui.Indent(15);
        DrawPairs(tag, usersInThisTag);
        ImGui.Unindent(15);
    }

    private void DrawGroupMenu(string tag)
    {
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Users, "Add people to " + tag))
        {
            _selectGroupForPairUi.Open(tag);
        }
        UiSharedService.AttachToolTip($"Add more users to Group {tag}");

        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete " + tag) && UiSharedService.CtrlPressed())
        {
            _tagHandler.RemoveTag(tag);
        }
        UiSharedService.AttachToolTip($"Delete Group {tag} (Will not delete the pairs)" + Environment.NewLine + "Hold CTRL to delete");
    }

    private void DrawName(string tag, bool isSpecialTag, int visible, int online, int? total)
    {
        string displayedName = tag switch
        {
            TagHandler.CustomUnpairedTag => "Unpaired",
            TagHandler.CustomOfflineTag => "Offline",
            TagHandler.CustomOnlineTag => _mareConfig.Current.ShowOfflineUsersSeparately ? "Online/Paused" : "Contacts",
            TagHandler.CustomVisibleTag => "Visible",
            _ => tag
        };

        string resultFolderName = !isSpecialTag ? $"{displayedName} ({visible}/{online}/{total} Pairs)" : $"{displayedName} ({online} Pairs)";

        //  FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight
        var icon = _tagHandler.IsTagOpen(tag) ? FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight;
        UiSharedService.FontText(icon.ToIconString(), UiBuilder.IconFont);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ToggleTagOpen(tag);
        }
        ImGui.SameLine();
        UiSharedService.FontText(resultFolderName, UiBuilder.DefaultFont);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ToggleTagOpen(tag);
        }

        if (!isSpecialTag && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted($"Group {tag}");
            ImGui.Separator();
            ImGui.TextUnformatted($"{visible} Pairs visible");
            ImGui.TextUnformatted($"{online} Pairs online/paused");
            ImGui.TextUnformatted($"{total} Pairs total");
            ImGui.EndTooltip();
        }
    }

    private void DrawPairs(string tag, IEnumerable<DrawUserPairVM> availablePairsInThisCategory)
    {
        // These are all the OtherUIDs that are tagged with this tag
        foreach (var pair in availablePairsInThisCategory)
        {
            UiSharedService.DrawWithID($"tag-{tag}-pair-${pair.UserData.UID}", () => _drawUserPairFactory(pair).DrawPairedClient());
        }
        ImGui.Separator();
    }

    private void DrawUserPairs(List<string> tagsWithPairsInThem, List<DrawUserPairVM> allUsers, IEnumerable<DrawUserPairVM> visibleUsers, IEnumerable<DrawUserPairVM> onlineUsers, IEnumerable<DrawUserPairVM> offlineUsers)
    {
        if (_mareConfig.Current.ShowVisibleUsersSeparately)
        {
            UiSharedService.DrawWithID("$group-VisibleCustomTag", () => DrawCategory(TagHandler.CustomVisibleTag, visibleUsers, allUsers));
        }
        foreach (var tag in tagsWithPairsInThem)
        {
            if (_mareConfig.Current.ShowOfflineUsersSeparately)
            {
                UiSharedService.DrawWithID($"group-{tag}", () => DrawCategory(tag, onlineUsers, allUsers, visibleUsers));
            }
            else
            {
                UiSharedService.DrawWithID($"group-{tag}", () => DrawCategory(tag, onlineUsers.Concat(offlineUsers).ToList(), allUsers, visibleUsers));
            }
        }
        if (_mareConfig.Current.ShowOfflineUsersSeparately)
        {
            UiSharedService.DrawWithID($"group-OnlineCustomTag", () => DrawCategory(TagHandler.CustomOnlineTag,
                onlineUsers.Where(u => !_tagHandler.HasAnyTag(u.UserData.UID)).ToList(), allUsers));
            UiSharedService.DrawWithID($"group-OfflineCustomTag", () => DrawCategory(TagHandler.CustomOfflineTag,
                offlineUsers.Where(u => !u.IsPausedFromTarget).ToList(), allUsers));
        }
        else
        {
            UiSharedService.DrawWithID($"group-OnlineCustomTag", () => DrawCategory(TagHandler.CustomOnlineTag,
                onlineUsers.Concat(offlineUsers).Where(u => !u.OneSidedPair && !_tagHandler.HasAnyTag(u.UserData.UID)).ToList(), allUsers));
        }
        UiSharedService.DrawWithID($"group-UnpairedCustomTag", () => DrawCategory(TagHandler.CustomUnpairedTag,
            offlineUsers.Where(u => u.OneSidedPair).ToList(), allUsers));
    }

    private void PauseRemainingPairs(List<DrawUserPairVM> availablePairs)
    {
        foreach (var pairToPause in availablePairs.Where(pair => !pair.IsPausedFromSource))
        {
            pairToPause.SetPaused(true);
        }
    }

    private void ResumeAllPairs(List<DrawUserPairVM> availablePairs)
    {
        foreach (var pairToPause in availablePairs.Where(pair => pair.IsPausedFromSource))
        {
            pairToPause.SetPaused(false);
        }
    }

    private void ToggleTagOpen(string tag)
    {
        bool open = !_tagHandler.IsTagOpen(tag);
        _tagHandler.SetTagOpen(tag, open);
    }
}