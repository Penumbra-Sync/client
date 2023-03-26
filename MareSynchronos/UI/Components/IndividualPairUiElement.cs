using System.Numerics;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.VM;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI.Components;

public class IndividualPairUiElement : WindowElementVMBase<ImguiVM>
{
    private readonly PairGroupsUi _pairGroupsUi;
    private readonly IndividualPairVM _pairVM;
    private readonly SelectGroupForPairUi _selectGroupForPairUi;
    private readonly SelectPairForGroupUi _selectPairForGroupUi;

    public IndividualPairUiElement(IndividualPairVM pairVM, ILogger<IndividualPairUiElement> logger, MareMediator mediator,
        PairGroupsUi pairGroupsUi, SelectPairForGroupUi selectPairForGroupUi, SelectGroupForPairUi selectGroupForPairUi) : base(pairVM, logger, mediator)
    {
        _pairVM = pairVM;
        _pairGroupsUi = pairGroupsUi;
        _selectPairForGroupUi = selectPairForGroupUi;
        _selectGroupForPairUi = selectGroupForPairUi;
    }

    public float DrawPairList(float transferPartHeight, float windowContentWidth)
    {
        PopupModal.FromConditionalModal(_pairVM.LastAddedUserModal).Draw(() =>
        {
            VM.ExecuteWithProp<string>(nameof(_pairVM.LastAddedUserComment), (comment) =>
            {
                UiSharedService.TextWrapped($"You have successfully added {_pairVM.LastAddedUser!.UserData.AliasOrUID}. Set a local note for the user in the field below:");
                ImGui.InputTextWithHint("##noteforuser", $"Note for {_pairVM.LastAddedUser.UserData.AliasOrUID}", ref comment, 100);
                Button.FromCommand(_pairVM.SetNoteForLastAddedUserCommand).Draw();
                return comment;
            });
            UiSharedService.SetScaledWindowSize(275);
        });

        UiSharedService.DrawWithID("group-user-popup", () => _selectPairForGroupUi.Draw(_pairVM.FilteredUsers.Value));
        UiSharedService.DrawWithID("grouping-popup", () => _selectGroupForPairUi.Draw());
        UiSharedService.DrawWithID("addpair", DrawAddPair);
        UiSharedService.DrawWithID("pairs", () => DrawPairs(transferPartHeight, windowContentWidth));
        var filter = ImGui.GetCursorPosY();
        UiSharedService.DrawWithID("filter", () => DrawFilter(windowContentWidth));
        return ImGui.GetCursorPosY() - filter;
    }

    private void DrawAddPair()
    {
        var addPairButton = Button.FromCommand(_pairVM.AddPairCommand);
        var addPairButtonSize = addPairButton.GetSize();

        ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - addPairButtonSize.X);
        VM.ExecuteWithProp<string>(nameof(IndividualPairVM.PairToAdd), (pairToAdd) =>
        {
            ImGui.InputTextWithHint("##otheruid", "Other players UID/Alias", ref pairToAdd, 20);
            return pairToAdd;
        });

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - addPairButtonSize.X);
        addPairButton.Draw();

        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawFilter(float windowContentWidth)
    {
        var pauseAllButton = Button.FromCommand(_pairVM.PauseAllCommand);
        var reverseButton = Button.FromCommand(_pairVM.ReverseSortCommand);
        var buttonSize = pauseAllButton.GetSize();
        var reverseButtonSize = reverseButton.GetSize();

        reverseButton.Draw();

        ImGui.SameLine();

        ImGui.SetNextItemWidth(windowContentWidth - buttonSize.X - reverseButtonSize.X - ImGui.GetStyle().ItemSpacing.X * 2);
        VM.ExecuteWithProp<string>(nameof(IndividualPairVM.CharacterOrCommentFilter), (str) =>
        {
            ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref str, 255);
            return str;
        });

        ImGui.SameLine();

        pauseAllButton.Draw();
    }

    private void DrawPairs(float transferPartHeight, float windowContentWidth)
    {
        var ySize = transferPartHeight == 0
            ? 1
            : ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y - transferPartHeight - ImGui.GetCursorPosY();

        ImGui.BeginChild("list", new Vector2(windowContentWidth, ySize), border: false);

        _pairGroupsUi.Draw(_pairVM.VisibleUsers.Value, _pairVM.OnlineUsers.Value, _pairVM.OfflineUsers.Value);

        ImGui.EndChild();
    }
}