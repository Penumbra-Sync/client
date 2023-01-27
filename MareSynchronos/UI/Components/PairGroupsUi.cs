using Dalamud.Interface;
using Dalamud.Interface.Components;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.Models;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components
{
    public class PairGroupsUi
    {
        private readonly Action<Pair> _clientRenderFn;
        private readonly TagHandler _tagHandler;
        private readonly ApiController _apiController;
        private readonly SelectPairForGroupUi _selectGroupForPairUi;

        public PairGroupsUi(TagHandler tagHandler, Action<Pair> clientRenderFn, ApiController apiController, SelectPairForGroupUi selectGroupForPairUi)
        {
            _clientRenderFn = clientRenderFn;
            _tagHandler = tagHandler;
            _apiController = apiController;
            _selectGroupForPairUi = selectGroupForPairUi;
        }

        public void Draw(List<Pair> availablePairs)
        {
            // Only render those tags that actually have pairs in them, otherwise
            // we can end up with a bunch of useless pair groups
            var tagsWithPairsInThem = _tagHandler.GetAllTagsSorted();
            foreach (var tag in tagsWithPairsInThem)
            {
                UiShared.DrawWithID($"group-{tag}", () => DrawCategory(tag, availablePairs));
            }
        }

        public void DrawCategory(string tag, List<Pair> availablePairs)
        {
            var otherUidsTaggedWithTag = _tagHandler.GetOtherUidsForTag(tag);
            var availablePairsInThisTag = availablePairs
                .Where(pair => otherUidsTaggedWithTag.Contains(pair.UserData.UID))
                .ToList();
            if (availablePairsInThisTag.Any())
            {
                DrawName(tag);
                UiShared.DrawWithID($"group-{tag}-buttons", () => DrawButtons(tag, availablePairsInThisTag));
                if (_tagHandler.IsTagOpen(tag))
                {
                    DrawPairs(tag, availablePairsInThisTag);
                }
            }
        }

        private void DrawName(string tag)
        {
            var resultFolderName = $"{tag}";

            //  FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight
            var icon = _tagHandler.IsTagOpen(tag) ? FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight;
            UiShared.FontText(icon.ToIconString(), UiBuilder.IconFont);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ToggleTagOpen(tag);
            }
            ImGui.SameLine();
            UiShared.FontText(resultFolderName, UiBuilder.DefaultFont);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ToggleTagOpen(tag);
            }
        }

        private void DrawButtons(string tag, List<Pair> availablePairsInThisTag)
        {
            var allArePaused = availablePairsInThisTag.All(pair => pair.UserPair!.OwnPermissions.IsPaused());
            var pauseButton = allArePaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
            var flyoutMenuX = UiShared.GetIconButtonSize(FontAwesomeIcon.Bars).X;
            var pauseButtonX = UiShared.GetIconButtonSize(pauseButton).X;
            var windowX = ImGui.GetWindowContentRegionMin().X;
            var windowWidth = UiShared.GetWindowContentRegionWidth();
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
                UiShared.AttachToolTip($"Resume pairing with all pairs in {tag}");
            }
            else
            {
                UiShared.AttachToolTip($"Pause pairing with all pairs in {tag}");
            }

            var buttonDeleteOffset = windowX + windowWidth - flyoutMenuX;
            ImGui.SameLine(buttonDeleteOffset);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
            {
                ImGui.OpenPopup("Group Flyout Menu");

            }

            if (ImGui.BeginPopup("Group Flyout Menu"))
            {
                UiShared.DrawWithID($"buttons-{tag}", () => DrawGroupMenu(tag));
                ImGui.EndPopup();
            }
        }

        private void DrawGroupMenu(string tag)
        {
            if (UiShared.IconTextButton(FontAwesomeIcon.Users, "Add people to " + tag))
            {
                _selectGroupForPairUi.Open(tag);
            }
            UiShared.AttachToolTip($"Add more users to Group {tag}");

            if (UiShared.IconTextButton(FontAwesomeIcon.Trash, "Delete " + tag))
            {
                if (UiShared.CtrlPressed())
                {
                    _tagHandler.RemoveTag(tag);
                }
            }
            UiShared.AttachToolTip($"Delete Group {tag} (Will not delete the pairs)" + Environment.NewLine + "Hold CTRL to delete");
        }

        private void DrawPairs(string tag, List<Pair> availablePairsInThisCategory)
        {
            ImGui.Separator();
            // These are all the OtherUIDs that are tagged with this tag
            availablePairsInThisCategory
                .ForEach(pair => UiShared.DrawWithID($"tag-{tag}-pair-${pair.UserData.UID}", () => DrawPair(pair)));
            ImGui.Separator();
        }

        private void DrawPair(Pair pair)
        {
            // This is probably just dumb. Somehow, just setting the cursor position to the icon lenght
            // does not really push the child rendering further. So we'll just add two whitespaces and call it a day?
            UiShared.FontText("    ", UiBuilder.DefaultFont);
            ImGui.SameLine();
            _clientRenderFn(pair);
        }

        private void ToggleTagOpen(string tag)
        {
            bool open = !_tagHandler.IsTagOpen(tag);
            _tagHandler.SetTagOpen(tag, open);
        }

        private void PauseRemainingPairs(List<Pair> availablePairs)
        {
            foreach (var pairToPause in availablePairs.Where(pair => !pair.UserPair!.OwnPermissions.IsPaused()))
            {
                var perm = pairToPause.UserPair!.OwnPermissions;
                perm.SetPaused(true);
                _ = _apiController.UserSetPairPermissions(new(pairToPause.UserData, perm));
            }
        }

        private void ResumeAllPairs(List<Pair> availablePairs)
        {
            foreach (var pairToPause in availablePairs)
            {
                var perm = pairToPause.UserPair!.OwnPermissions;
                perm.SetPaused(false);
                _ = _apiController.UserSetPairPermissions(new(pairToPause.UserData, perm));
            }
        }
    }
}