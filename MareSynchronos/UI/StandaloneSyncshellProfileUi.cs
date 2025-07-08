using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using ImGuiNET;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text;
using static FFXIVClientStructs.FFXIV.Component.GUI.AtkResNode.Delegates;

namespace MareSynchronos.UI;

public class StandaloneSyncshellProfileUi : WindowMediatorSubscriberBase
{
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private bool _adjustedForScrollBars = false;

    private string _description;

    public StandaloneSyncshellProfileUi(ILogger<StandaloneSyncshellProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        PairManager pairManager, GroupFullInfoDto groupFullInfo,
        PerformanceCollectorService performanceCollector)
        : base(logger, mediator, "Mare Syncshell Profile of " + groupFullInfo.GroupAliasOrGID + "##MareSynchronosStandaloneProfileUI" + groupFullInfo.Group.GID, performanceCollector)
    {
        _description = "";
        _uiSharedService = uiBuilder;
        _pairManager = pairManager;
        GroupFullInfo = groupFullInfo;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize;

        decodeGroupDescription();

        var spacing = ImGui.GetStyle().ItemSpacing;

        Size = new(512 + spacing.X * 3 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 512);

        IsOpen = true;
    }

    public GroupFullInfoDto GroupFullInfo { get; init; }

    protected void decodeGroupDescription()
    {
        if (GroupFullInfo == null)
        {
            return;
        }

        if (GroupFullInfo.GroupDescription != null)
        {
            byte[] decodedDescBytes = Convert.FromBase64String(GroupFullInfo.GroupDescription);
            _description = Encoding.UTF8.GetString(decodedDescBytes);
        }
    }

    protected override void DrawInternal()
    {
        try
        {
            var spacing = ImGui.GetStyle().ItemSpacing;

            var drawList = ImGui.GetWindowDrawList();
            var rectMin = drawList.GetClipRectMin();
            var rectMax = drawList.GetClipRectMax();
            var headerSize = ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y;

            using (_uiSharedService.UidFont.Push())
                UiSharedService.ColorText(GroupFullInfo.GroupAliasOrGID, ImGuiColors.HealerGreen);

            ImGuiHelpers.ScaledDummy(new Vector2(spacing.Y, spacing.Y));
            ImGui.Separator();

            Pair? ownerPair = _pairManager.GetPairByUID(GroupFullInfo.OwnerUID);
            
            Vector4 oStatusColor = ImGuiColors.DalamudGrey3;
            string oStatus = "Unknown";
            string? oNote = null; 
            if (ownerPair != null)
            {
                oNote = ownerPair.GetNote();
                if (ownerPair.IsVisible)
                {
                    oStatus = "Visible";
                    oStatusColor = ImGuiColors.ParsedGreen;
                } else
                {
                    oStatus = ownerPair.IsOnline ? "Online" : "Offline";
                    oStatusColor = ownerPair.IsOnline ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
                }
            }

            UiSharedService.ColorText("Owner: " + GroupFullInfo.OwnerAliasOrUID, oStatusColor);
            ImGui.SameLine();
            UiSharedService.ColorText(oStatus, oStatusColor);
            if (oNote != null)
            {
                ImGui.NewLine();
                ImGui.TextUnformatted("your Note: " + oNote);
            }

            ImGui.Separator();
            var pos = ImGui.GetCursorPos() with { Y = ImGui.GetCursorPosY() - headerSize };
            ImGui.SameLine();
            var descriptionTextSize = ImGui.CalcTextSize(_description, 256f);
            var descriptionChildHeight = rectMax.Y - pos.Y - rectMin.Y - spacing.Y * 2;
            if (descriptionTextSize.Y > descriptionChildHeight && !_adjustedForScrollBars)
            {
                Size = Size!.Value with { X = Size.Value.X + ImGui.GetStyle().ScrollbarSize };
                _adjustedForScrollBars = true;
            }
            else if (descriptionTextSize.Y < descriptionChildHeight && _adjustedForScrollBars)
            {
                Size = Size!.Value with { X = Size.Value.X - ImGui.GetStyle().ScrollbarSize };
                _adjustedForScrollBars = false;
            }
            var childFrame = ImGuiHelpers.ScaledVector2(rectMax.X - (ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize), descriptionChildHeight);
            childFrame = childFrame with
            {
                X = childFrame.X + (_adjustedForScrollBars ? ImGui.GetStyle().ScrollbarSize : 0),
                Y = childFrame.Y / ImGuiHelpers.GlobalScale
            };
            if (ImGui.BeginChildFrame(1001, childFrame))
            {
                using var _ = _uiSharedService.GameFont.Push();
                ImGui.TextWrapped(_description);
            }
            ImGui.EndChildFrame();

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during draw tooltip");
        }
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}