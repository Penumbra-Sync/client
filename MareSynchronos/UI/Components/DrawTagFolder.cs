using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public class DrawTagFolder : DrawFolderBase
{
    private readonly ApiController _apiController;
    protected override bool RenderIfEmpty => _id switch
    {
        TagHandler.CustomUnpairedTag => false,
        TagHandler.CustomOnlineTag => false,
        TagHandler.CustomOfflineTag => false,
        TagHandler.CustomVisibleTag => false,
        TagHandler.CustomAllTag => true,
        _ => true,
    };

    protected override bool RenderMenu => _id switch
    {
        TagHandler.CustomUnpairedTag => false,
        TagHandler.CustomOnlineTag => false,
        TagHandler.CustomOfflineTag => false,
        TagHandler.CustomVisibleTag => false,
        TagHandler.CustomAllTag => false,
        _ => true,
    };

    private bool RenderPause => _id switch
    {
        TagHandler.CustomUnpairedTag => false,
        TagHandler.CustomOnlineTag => false,
        TagHandler.CustomOfflineTag => false,
        TagHandler.CustomVisibleTag => false,
        TagHandler.CustomAllTag => false,
        _ => true,
    };

    public DrawTagFolder(string id, IEnumerable<DrawPairBase> drawPairs, TagHandler tagHandler, ApiController apiController)
        : base(id, drawPairs, tagHandler)
    {
        _apiController = apiController;
    }

    protected override float DrawIcon(float textPosY, float originalY)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        var icon = _id switch
        {
            TagHandler.CustomUnpairedTag => FontAwesomeIcon.ArrowsLeftRight.ToIconString(),
            TagHandler.CustomOnlineTag => FontAwesomeIcon.Link.ToIconString(),
            TagHandler.CustomOfflineTag => FontAwesomeIcon.Unlink.ToIconString(),
            TagHandler.CustomVisibleTag => FontAwesomeIcon.Eye.ToIconString(),
            TagHandler.CustomAllTag => FontAwesomeIcon.User.ToIconString(),
            _ => FontAwesomeIcon.Folder.ToIconString()
        };

        ImGui.SetCursorPosY(textPosY);
        ImGui.Text(icon);
        ImGui.SameLine();
        return ImGui.GetCursorPosX();
    }

    protected override void DrawMenu()
    {
        ImGui.Text("Group Menu");

        // todo tag handler menu
    }

    protected override void DrawName(float originalY, float width)
    {
        ImGui.SetCursorPosY(originalY);
        string name = _id switch
        {
            TagHandler.CustomUnpairedTag => "Unpaired",
            TagHandler.CustomOnlineTag => "Online / Paused by you",
            TagHandler.CustomOfflineTag => "Offline / Paused by them",
            TagHandler.CustomVisibleTag => "Visible",
            TagHandler.CustomAllTag => "All Users",
            _ => _id
        };

        ImGui.TextUnformatted(name);
    }

    protected override float DrawRightSide(float originalY, float currentRightSideX)
    {
        if (!RenderPause) return currentRightSideX;

        var allArePaused = _drawPairs.All(pair => pair.UserPair!.OwnPermissions.IsPaused());
        var pauseButton = allArePaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseButtonX = UiSharedService.GetIconButtonSize(pauseButton).X;

        var buttonPauseOffset = currentRightSideX - pauseButtonX;
        ImGui.SameLine(buttonPauseOffset);
        if (ImGuiComponents.IconButton(pauseButton))
        {
            if (allArePaused)
            {
                ResumeAllPairs(_drawPairs);
            }
            else
            {
                PauseRemainingPairs(_drawPairs);
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

    private void PauseRemainingPairs(IEnumerable<DrawPairBase> availablePairs)
    {
        foreach (var pairToPause in availablePairs.Where(pair => !pair.UserPair!.OwnPermissions.IsPaused()))
        {
            var perm = pairToPause.UserPair!.OwnPermissions;
            perm.SetPaused(paused: true);
            _ = _apiController.UserSetPairPermissions(new(new(pairToPause.UID), perm));
        }
    }

    private void ResumeAllPairs(IEnumerable<DrawPairBase> availablePairs)
    {
        foreach (var pairToPause in availablePairs)
        {
            var perm = pairToPause.UserPair!.OwnPermissions;
            perm.SetPaused(paused: false);
            _ = _apiController.UserSetPairPermissions(new(new(pairToPause.UID), perm));
        }
    }
}
