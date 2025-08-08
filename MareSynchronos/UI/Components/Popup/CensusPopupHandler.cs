using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using MareSynchronos.Services.ServerConfiguration;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

public class CensusPopupHandler : IPopupHandler
{
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;

    public CensusPopupHandler(ServerConfigurationManager serverConfigurationManager, UiSharedService uiSharedService)
    {
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
    }

    private Vector2 _size = new(600, 450);
    public Vector2 PopupSize => _size;

    public bool ShowClose => false;

    public void DrawContent()
    {
        var start = 0f;
        using (_uiSharedService.UidFont.Push())
        {
            start = ImGui.GetCursorPosY() - ImGui.CalcTextSize("Mare Census Data").Y;
            UiSharedService.TextWrapped("Mare Census Participation");
        }
        ImGuiHelpers.ScaledDummy(5f);
        UiSharedService.TextWrapped("If you are seeing this popup you are updating from a Mare version that did not collect census data. Please read the following carefully.");
        ImGui.Separator();
        UiSharedService.TextWrapped("Mare Census is a data collecting service that can be used for statistical purposes. " +
            "All data collected through Mare Census is temporary and will be stored associated with your UID on the connected service as long as you are connected. " +
            "The data cannot be used for long term tracking of individuals.");
        UiSharedService.TextWrapped("If enabled, Mare Census will collect following data:" + Environment.NewLine
            + "- Currently connected World" + Environment.NewLine
            + "- Current Gender (reflecting Glamourer changes)" + Environment.NewLine
            + "- Current Race (reflecting Glamourer changes)" + Environment.NewLine
            + "- Current Clan (i.e. Seeker of the Sun, Keeper of the Moon, etc., reflecting Glamourer changes)");
        UiSharedService.TextWrapped("To consent to collecting census data press the appropriate button below.");
        UiSharedService.TextWrapped("This setting can be changed anytime in the Mare Settings.");
        var width = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
        var buttonSize = ImGuiHelpers.GetButtonSize("I consent to send my census data");
        ImGuiHelpers.ScaledDummy(5f);
        if (ImGui.Button("I consent to send my census data", new Vector2(width, buttonSize.Y * 2.5f)))
        {
            _serverConfigurationManager.SendCensusData = true;
            _serverConfigurationManager.ShownCensusPopup = true;
            ImGui.CloseCurrentPopup();
        }
        ImGuiHelpers.ScaledDummy(1f);
        if (ImGui.Button("I do not consent to send my census data", new Vector2(width, buttonSize.Y)))
        {
            _serverConfigurationManager.SendCensusData = false;
            _serverConfigurationManager.ShownCensusPopup = true;
            ImGui.CloseCurrentPopup();
        }
        var height = ImGui.GetCursorPosY() - start;
        _size = _size with { Y = height };
    }
}
