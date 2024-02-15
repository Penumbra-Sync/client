using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class CreateSyncshellUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private bool _errorGroupCreate;
    private GroupJoinDto? _lastCreatedGroup;

    public CreateSyncshellUI(ILogger<CreateSyncshellUI> logger, MareMediator mareMediator, ApiController apiController, UiSharedService uiSharedService,
        PerformanceCollectorService performanceCollectorService)
        : base(logger, mareMediator, "Create new Syncshell###MareSynchronosCreateSyncshell", performanceCollectorService)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        SizeConstraints = new()
        {
            MinimumSize = new(550, 330),
            MaximumSize = new(550, 330)
        };

        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;

        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
    }

    protected override void DrawInternal()
    {
        using (ImRaii.PushFont(_uiSharedService.UidFont))
            ImGui.TextUnformatted("Create new Syncshell");

        if (_lastCreatedGroup == null)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Plus, "Create Syncshell"))
            {
                try
                {
                    _lastCreatedGroup = _apiController.GroupCreate().Result;
                }
                catch
                {
                    _lastCreatedGroup = null;
                    _errorGroupCreate = true;
                }
            }
            ImGui.SameLine();
        }

        ImGui.Separator();

        if (_lastCreatedGroup == null)
        {
            UiSharedService.TextWrapped("Creating a new Syncshell with create it defaulting to your current preferred permissions for Syncshells." + Environment.NewLine +
                "- You can own up to " + _apiController.ServerInfo.MaxGroupsCreatedByUser + " Syncshells on this server." + Environment.NewLine +
                "- You can join up to " + _apiController.ServerInfo.MaxGroupsJoinedByUser + " Syncshells on this server (including your own)" + Environment.NewLine +
                "- Syncshells on this server can have a maximum of " + _apiController.ServerInfo.MaxGroupUserCount + " users");
            ImGuiHelpers.ScaledDummy(2f);
            ImGui.TextUnformatted("Your current Syncshell preferred permissions are:");
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- Animations");
            UiSharedService.BooleanToColoredIcon(!_apiController.DefaultPermissions!.DisableGroupAnimations);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- Sounds");
            UiSharedService.BooleanToColoredIcon(!_apiController.DefaultPermissions!.DisableGroupSounds);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- VFX");
            UiSharedService.BooleanToColoredIcon(!_apiController.DefaultPermissions!.DisableGroupVFX);
            UiSharedService.TextWrapped("(Those preferred permissions can be changed anytime after Syncshell creation, your defaults can be changed anytime in the Mare Settings)");
        }
        else
        {
            _errorGroupCreate = false;
            ImGui.TextUnformatted("Syncshell ID: " + _lastCreatedGroup.Group.GID);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Syncshell Password: " + _lastCreatedGroup.Password);
            ImGui.SameLine();
            if (UiSharedService.NormalizedIconButton(FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(_lastCreatedGroup.Password);
            }
            UiSharedService.TextWrapped("You can change the Syncshell password later at any time.");
            ImGui.Separator();
            UiSharedService.TextWrapped("These settings were set based on your preferred syncshell permissions:");
            ImGuiHelpers.ScaledDummy(2f);
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped("Suggest Animation sync:");
            UiSharedService.BooleanToColoredIcon(!_lastCreatedGroup.GroupUserPreferredPermissions.IsDisableAnimations());
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped("Suggest Sounds sync:");
            UiSharedService.BooleanToColoredIcon(!_lastCreatedGroup.GroupUserPreferredPermissions.IsDisableSounds());
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped("Suggest VFX sync:");
            UiSharedService.BooleanToColoredIcon(!_lastCreatedGroup.GroupUserPreferredPermissions.IsDisableVFX());
        }

        if (_errorGroupCreate)
        {
            UiSharedService.ColorTextWrapped("Something went wrong during creation of a new Syncshell", new Vector4(1, 0, 0, 1));
        }
    }

    public override void OnOpen()
    {
        _lastCreatedGroup = null;
    }
}