using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using ImGuiNET;
using MareSynchronos.API;
using MareSynchronos.UI.Handlers;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components
{
    public class PairGroupsUi
    {
        private readonly Action<ClientPairDto> _clientRenderFn;
        private readonly TagHandler _tagHandler;
        private readonly ApiController _apiController;

        public PairGroupsUi(TagHandler tagHandler, Action<ClientPairDto> clientRenderFn, ApiController apiController)
        {
            _clientRenderFn = clientRenderFn;
            _tagHandler = tagHandler;
            _apiController = apiController;
        }

        public void Draw(List<ClientPairDto> availablePairs)
        {
            // Only render those tags that actually have pairs in them, otherwise
            // we can end up with a bunch of useless pair groups
            var tagsWithPairsInThem = _tagHandler.GetAllTagsSorted()
                .Where(tag => _tagHandler.GetOtherUidsForTag(tag).Count >= 1);
            foreach (var tag in tagsWithPairsInThem)
            {
                UiShared.DrawWithID($"group-{tag}", () => DrawCategory(tag, availablePairs));
            }
        }

        public void DrawCategory(string tag, List<ClientPairDto> availablePairs)
        {
            var otherUidsTaggedWithTag = _tagHandler.GetOtherUidsForTag(tag);
            var availablePairsInThisTag = availablePairs
                .Where(pair => otherUidsTaggedWithTag.Contains(pair.OtherUID))
                .ToList();
            DrawName(tag);
            UiShared.DrawWithID($"group-{tag}-buttons", () => DrawButtons(tag, availablePairsInThisTag));
            if (_tagHandler.IsTagOpen(tag))
            {
                DrawPairs(tag, availablePairsInThisTag);
            }
        }

        private void DrawName(string tag)
        {
            var resultFolderName = $"{tag}";

            UiShared.FontText(FontAwesomeIcon.Folder.ToIconString(), UiBuilder.IconFont);
            ImGui.SameLine();
            UiShared.FontText(resultFolderName, UiBuilder.DefaultFont);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ToggleTagOpen(tag);
            }
        }

        private void DrawButtons(string tag, List<ClientPairDto> availablePairsInThisTag)
        {
            var allArePaused = availablePairsInThisTag.All(pair => pair.IsPaused);
            var pauseButton = allArePaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
            var trashButtonX = UiShared.GetIconButtonSize(FontAwesomeIcon.Trash).X;
            var pauseButtonX = UiShared.GetIconButtonSize(pauseButton).X;
            var windowX = ImGui.GetWindowContentRegionMin().X;
            var windowWidth = UiShared.GetWindowContentRegionWidth();
            var spacingX = ImGui.GetStyle().ItemSpacing.X;

            var buttonPauseOffset = windowX + windowWidth - trashButtonX - spacingX - pauseButtonX;
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

            var buttonDeleteOffset = windowX + windowWidth - trashButtonX;
            ImGui.SameLine(buttonDeleteOffset);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                _tagHandler.RemoveTag(tag);
            }
            UiShared.AttachToolTip($"Delete Group {tag} (Will not delete the pairs)");
        }

        private void DrawPairs(string tag, List<ClientPairDto> availablePairsInThisCategory)
        {
            Logger.Debug($"Available pairs in {tag}: ${availablePairsInThisCategory.ToString()}");
            ImGui.Separator();
            // These are all the OtherUIDs that are tagged with this tag
            availablePairsInThisCategory
                .ForEach(pair => UiShared.DrawWithID($"tag-{tag}-pair-${pair.OtherUID}", () => DrawPair(pair)));
            ImGui.Separator();
        }

        private void DrawPair(ClientPairDto pair)
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

        private void PauseRemainingPairs(List<ClientPairDto> availablePairs)
        {
            foreach (var pairToPause in availablePairs.Where(pair => !pair.IsPaused))
            {
                _ = _apiController.UserChangePairPauseStatus(pairToPause.OtherUID, paused: true);
            }
        }

        private void ResumeAllPairs(List<ClientPairDto> availablePairs)
        {
            foreach (var pairToPause in availablePairs)
            {
                _ = _apiController.UserChangePairPauseStatus(pairToPause.OtherUID, paused: false);
            }
        }
    }
}