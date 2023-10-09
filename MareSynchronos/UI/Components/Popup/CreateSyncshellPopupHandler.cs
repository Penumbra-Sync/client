﻿using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

internal class CreateSyncshellPopupHandler : PopupHandlerBase
{
    private readonly ApiController _apiController;
    private GroupJoinDto? _lastCreatedGroup;
    private bool _errorGroupCreate;

    protected override Vector2 PopupSize => new(500, 300);

    public CreateSyncshellPopupHandler(ILogger<ReportPopupHandler> logger, MareMediator mareMediator, ApiController apiController, UiSharedService uiSharedService)
        : base("CreateSyncshellPopup", logger, mareMediator, uiSharedService)
    {
        Mediator.Subscribe<CreateSyncshellPopupMessage>(this, (msg) =>
        {
            _openPopup = true;
            _lastCreatedGroup = null;
        });
        _apiController = apiController;
    }

    protected override void DrawContent()
    {
        using (ImRaii.PushFont(_uiSharedService.UidFont))
            ImGui.TextUnformatted("Create new Syncshell");

        if (_lastCreatedGroup == null)
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Create Syncshell"))
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

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, "Close"))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();

        if (_lastCreatedGroup == null)
        {
            UiSharedService.TextWrapped("Creating a new Syncshell with create it defaulting to your current preferred permissions for Syncshells." + Environment.NewLine +
                "- You can own up to " + _apiController.ServerInfo.MaxGroupsCreatedByUser + " Syncshells on this server." + Environment.NewLine +
                "- You can join up to " + _apiController.ServerInfo.MaxGroupsJoinedByUser + " Syncshells on this server (including your own)" + Environment.NewLine +
                "- Syncshells on this server can have a maximum of " + _apiController.ServerInfo.MaxGroupUserCount + " users");
            UiSharedService.TextWrapped("Your current Syncshell preferred permissions are:" + Environment.NewLine +
                "- Animations disabled: " + _apiController.DefaultPermissions!.DisableGroupAnimations + Environment.NewLine +
                "- Sounds disabled: " + _apiController.DefaultPermissions!.DisableGroupSounds + Environment.NewLine +
                "- VFX disabled: " + _apiController.DefaultPermissions!.DisableGroupVFX);
            UiSharedService.TextWrapped("(Those preferred permissions can be changed anytime after Syncshell creation, your defaults can be changed anytime in the Mare Settings)");
        }
        else
        {
            _errorGroupCreate = false;
            ImGui.TextUnformatted("Syncshell ID: " + _lastCreatedGroup.Group.GID);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Syncshell Password: " + _lastCreatedGroup.Password);
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(_lastCreatedGroup.Password);
            }
            UiSharedService.TextWrapped("You can change the Syncshell password later at any time.");
            ImGui.Separator();
            UiSharedService.TextWrapped("These settings were set based on your preferred syncshell permissions:");
            UiSharedService.TextWrapped("Suggest disable Animations sync: " + _lastCreatedGroup.GroupUserPreferredPermissions.IsDisableAnimations());
            UiSharedService.TextWrapped("Suggest disable Sounds sync: " + _lastCreatedGroup.GroupUserPreferredPermissions.IsDisableSounds());
            UiSharedService.TextWrapped("Suggest disable VFX sync: " + _lastCreatedGroup.GroupUserPreferredPermissions.IsDisableVFX());
        }

        if (_errorGroupCreate)
        {
            UiSharedService.ColorTextWrapped("Something went wrong during creation of a new Syncshell", new Vector4(1, 0, 0, 1));
        }
    }
}