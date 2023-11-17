using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
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

    public Vector2 PopupSize => new(600, 350);

    public void DrawContent()
    {
        using (ImRaii.PushFont(_uiSharedService.UidFont))
            UiSharedService.TextWrapped("Mare Census Opt-Out");
        ImGuiHelpers.ScaledDummy(5, 5);
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
        UiSharedService.TextWrapped("If you do not consent to the data mentioned above being sent, untick the checkbox below.");
        UiSharedService.TextWrapped("This setting can be changed anytime in the Mare Settings.");
        var sendCensus = _serverConfigurationManager.SendCensusData;
        if (ImGui.Checkbox("Allow sending census data", ref sendCensus))
        {
            _serverConfigurationManager.SendCensusData = sendCensus;
        }
    }

    public void OnClose()
    {
        _serverConfigurationManager.ShownCensusPopup = true;
    }
}
