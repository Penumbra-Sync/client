using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using System.Collections.Immutable;

namespace MareSynchronos.UI.Components;

public class DrawFolderTag : DrawFolderBase
{
    private readonly ApiController _apiController;
    private readonly SelectPairForTagUi _selectPairForTagUi;

    public DrawFolderTag(string id, IImmutableList<DrawUserPair> drawPairs, IImmutableList<Pair> allPairs,
        TagHandler tagHandler, ApiController apiController, SelectPairForTagUi selectPairForTagUi, UiSharedService uiSharedService)
        : base(id, drawPairs, allPairs, tagHandler, uiSharedService)
    {
        _apiController = apiController;
        _selectPairForTagUi = selectPairForTagUi;
    }

    protected override bool RenderIfEmpty => _id switch
    {
        TagHandler.CustomUnpairedTag => false,
        TagHandler.CustomOnlineTag => false,
        TagHandler.CustomOfflineTag => false,
        TagHandler.CustomVisibleTag => false,
        TagHandler.CustomAllTag => true,
        TagHandler.CustomOfflineSyncshellTag => false,
        _ => true,
    };

    protected override bool RenderMenu => _id switch
    {
        TagHandler.CustomUnpairedTag => false,
        TagHandler.CustomOnlineTag => false,
        TagHandler.CustomOfflineTag => false,
        TagHandler.CustomVisibleTag => false,
        TagHandler.CustomAllTag => false,
        TagHandler.CustomOfflineSyncshellTag => false,
        _ => true,
    };

    private bool RenderPause => _id switch
    {
        TagHandler.CustomUnpairedTag => false,
        TagHandler.CustomOnlineTag => false,
        TagHandler.CustomOfflineTag => false,
        TagHandler.CustomVisibleTag => false,
        TagHandler.CustomAllTag => false,
        TagHandler.CustomOfflineSyncshellTag => false,
        _ => true,
    } && _allPairs.Any();

    private bool RenderCount => _id switch
    {
        TagHandler.CustomUnpairedTag => false,
        TagHandler.CustomOnlineTag => false,
        TagHandler.CustomOfflineTag => false,
        TagHandler.CustomVisibleTag => false,
        TagHandler.CustomAllTag => false,
        TagHandler.CustomOfflineSyncshellTag => false,
        _ => true
    };

    protected override float DrawIcon()
    {
        var icon = _id switch
        {
            TagHandler.CustomUnpairedTag => FontAwesomeIcon.ArrowsLeftRight,
            TagHandler.CustomOnlineTag => FontAwesomeIcon.Link,
            TagHandler.CustomOfflineTag => FontAwesomeIcon.Unlink,
            TagHandler.CustomOfflineSyncshellTag => FontAwesomeIcon.Unlink,
            TagHandler.CustomVisibleTag => FontAwesomeIcon.Eye,
            TagHandler.CustomAllTag => FontAwesomeIcon.User,
            _ => FontAwesomeIcon.Folder
        };

        ImGui.AlignTextToFramePadding();
        _uiSharedService.IconText(icon);

        if (RenderCount)
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing with { X = ImGui.GetStyle().ItemSpacing.X / 2f }))
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();

                ImGui.TextUnformatted("[" + OnlinePairs.ToString() + "]");
            }
            UiSharedService.AttachToolTip(OnlinePairs + " online" + Environment.NewLine + TotalPairs + " total");
        }
        ImGui.SameLine();
        return ImGui.GetCursorPosX();
    }

    protected override void DrawMenu(float menuWidth)
    {
        ImGui.TextUnformatted("Group Menu");
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Users, "Select Pairs", menuWidth, true))
        {
            _selectPairForTagUi.Open(_id);
        }
        UiSharedService.AttachToolTip("Select Individual Pairs for this Pair Group");
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Pair Group", menuWidth, true) && UiSharedService.CtrlPressed())
        {
            _tagHandler.RemoveTag(_id);
        }
        UiSharedService.AttachToolTip("Hold CTRL to remove this Group permanently." + Environment.NewLine +
            "Note: this will not unpair with users in this Group.");
    }

    protected override void DrawName(float width)
    {
        ImGui.AlignTextToFramePadding();

        string name = _id switch
        {
            TagHandler.CustomUnpairedTag => "One-sided Individual Pairs",
            TagHandler.CustomOnlineTag => "Online / Paused by you",
            TagHandler.CustomOfflineTag => "Offline / Paused by other",
            TagHandler.CustomOfflineSyncshellTag => "Offline Syncshell Users",
            TagHandler.CustomVisibleTag => "Visible",
            TagHandler.CustomAllTag => "Users",
            _ => _id
        };

        ImGui.TextUnformatted(name);
    }

    protected override float DrawRightSide(float currentRightSideX)
    {
        if (!RenderPause) return currentRightSideX;

        var allArePaused = _allPairs.All(pair => pair.UserPair!.OwnPermissions.IsPaused());
        var pauseButton = allArePaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseButtonX = _uiSharedService.GetIconButtonSize(pauseButton).X;

        var buttonPauseOffset = currentRightSideX - pauseButtonX;
        ImGui.SameLine(buttonPauseOffset);
        if (_uiSharedService.IconButton(pauseButton))
        {
            if (allArePaused)
            {
                ResumeAllPairs(_allPairs);
            }
            else
            {
                PauseRemainingPairs(_allPairs);
            }
        }
        if (allArePaused)
        {
            UiSharedService.AttachToolTip($"Resume pairing with all pairs in {_id}");
        }
        else
        {
            UiSharedService.AttachToolTip($"Pause pairing with all pairs in {_id}");
        }

        return currentRightSideX;
    }

    private void PauseRemainingPairs(IEnumerable<Pair> availablePairs)
    {
        _ = _apiController.SetBulkPermissions(new(availablePairs
            .ToDictionary(g => g.UserData.UID, g =>
        {
            var perm = g.UserPair.OwnPermissions;
            perm.SetPaused(paused: true);
            return perm;
        }, StringComparer.Ordinal), new(StringComparer.Ordinal)))
            .ConfigureAwait(false);
    }

    private void ResumeAllPairs(IEnumerable<Pair> availablePairs)
    {
        _ = _apiController.SetBulkPermissions(new(availablePairs
            .ToDictionary(g => g.UserData.UID, g =>
            {
                var perm = g.UserPair.OwnPermissions;
                perm.SetPaused(paused: false);
                return perm;
            }, StringComparer.Ordinal), new(StringComparer.Ordinal)))
            .ConfigureAwait(false);
    }
}