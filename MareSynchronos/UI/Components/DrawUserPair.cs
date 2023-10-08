using Dalamud.Interface.Colors;
using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using System.Numerics;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.WebAPI;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.Components;

public class DrawUserPair : DrawPairBase
{
    private readonly SelectTagForPairUi _selectGroupForPairUi;

    public DrawUserPair(string id, Pair entry, ApiController apiController, IdDisplayHandler displayHandler, SelectTagForPairUi selectGroupForPairUi)
        : base(id, entry, apiController, displayHandler)
    {
        _pair = entry;
        _selectGroupForPairUi = selectGroupForPairUi;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        FontAwesomeIcon connectionIcon;
        Vector4 connectionColor;
        string connectionText;
        if (!_pair.IsDirectlyPaired)
        {
            connectionIcon = FontAwesomeIcon.ArrowsLeftRight;
            connectionText = _pair.UserData.AliasOrUID + " has not added you back";
            connectionColor = ImGuiColors.DalamudRed;
        }
        else if (_pair.UserPair!.OwnPermissions.IsPaused() || _pair.UserPair!.OtherPermissions.IsPaused())
        {
            connectionIcon = FontAwesomeIcon.Pause;
            connectionText = "Pairing status with " + _pair.UserData.AliasOrUID + " is paused";
            connectionColor = ImGuiColors.DalamudYellow;
        }
        else
        {
            connectionIcon = FontAwesomeIcon.User;
            connectionText = "You are directly paired with " + _pair.UserData.AliasOrUID + Environment.NewLine;
            if (!_pair.IsOnline) connectionText += "The user is currently offline";
            connectionColor = _pair.IsOnline ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        }

        ImGui.SetCursorPosY(textPosY);
        ImGui.PushFont(UiBuilder.IconFont);
        UiSharedService.ColorText(connectionIcon.ToIconString(), connectionColor);
        ImGui.PopFont();
        UiSharedService.AttachToolTip(connectionText);
        if (_pair.UserPair.OwnPermissions.IsSticky())
        {
            var x = ImGui.GetCursorPosX();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            var iconsize = ImGui.CalcTextSize(FontAwesomeIcon.ArrowCircleUp.ToIconString()).X;
            ImGui.SameLine(x + iconsize + (ImGui.GetStyle().ItemSpacing.X / 2));
            ImGui.Text(FontAwesomeIcon.ArrowCircleUp.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip(_pair.UserData.AliasOrUID + " has preferred permissions enabled");
        }

        if (_pair is { IsOnline: true, IsVisible: true })
        {
            var x = ImGui.GetCursorPosX();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            var iconsize = ImGui.CalcTextSize(FontAwesomeIcon.Eye.ToIconString()).X;
            ImGui.SameLine(x + iconsize + (ImGui.GetStyle().ItemSpacing.X / 2));
            UiSharedService.ColorText(FontAwesomeIcon.Eye.ToIconString(), ImGuiColors.ParsedGreen);
            ImGui.PopFont();
            UiSharedService.AttachToolTip(_pair.UserData.AliasOrUID + " is visible: " + _pair.PlayerName!);
        }
    }

    protected override void DrawPairedClientMenu()
    {
        ImGui.Text("Individual Pair Functions");
        var entryUID = _pair.UserData.AliasOrUID;
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Folder, "Pair Groups"))
        {
            _selectGroupForPairUi.Open(_pair);
        }
        UiSharedService.AttachToolTip("Choose pair groups for " + entryUID);
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Unpair Permanently") && UiSharedService.CtrlPressed())
        {
            _ = _apiController.UserRemovePair(new(_pair.UserData));
        }
        UiSharedService.AttachToolTip("Hold CTRL and click to unpair permanently from " + entryUID);
    }
}
