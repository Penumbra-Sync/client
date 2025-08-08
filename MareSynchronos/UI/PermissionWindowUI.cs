using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class PermissionWindowUI : WindowMediatorSubscriberBase
{
    public Pair Pair { get; init; }

    private readonly UiSharedService _uiSharedService;
    private readonly ApiController _apiController;
    private UserPermissions _ownPermissions;

    public PermissionWindowUI(ILogger<PermissionWindowUI> logger, Pair pair, MareMediator mediator, UiSharedService uiSharedService,
        ApiController apiController, PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "Permissions for " + pair.UserData.AliasOrUID + "###MareSynchronosPermissions" + pair.UserData.UID, performanceCollectorService)
    {
        Pair = pair;
        _uiSharedService = uiSharedService;
        _apiController = apiController;
        _ownPermissions = pair.UserPair.OwnPermissions.DeepClone();
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize;
        SizeConstraints = new()
        {
            MinimumSize = new(450, 100),
            MaximumSize = new(450, 500)
        };
        IsOpen = true;
    }

    protected override void DrawInternal()
    {
        var sticky = _ownPermissions.IsSticky();
        var paused = _ownPermissions.IsPaused();
        var disableSounds = _ownPermissions.IsDisableSounds();
        var disableAnimations = _ownPermissions.IsDisableAnimations();
        var disableVfx = _ownPermissions.IsDisableVFX();
        var style = ImGui.GetStyle();
        var indentSize = ImGui.GetFrameHeight() + style.ItemSpacing.X;

        _uiSharedService.BigText("Permissions for " + Pair.UserData.AliasOrUID);
        ImGuiHelpers.ScaledDummy(1f);

        if (ImGui.Checkbox("Preferred Permissions", ref sticky))
        {
            _ownPermissions.SetSticky(sticky);
        }
        _uiSharedService.DrawHelpText("Preferred Permissions, when enabled, will exclude this user from any permission changes on any syncshells you share with this user.");

        ImGuiHelpers.ScaledDummy(1f);


        if (ImGui.Checkbox("Pause Sync", ref paused))
        {
            _ownPermissions.SetPaused(paused);
        }
        _uiSharedService.DrawHelpText("Pausing will completely cease any sync with this user." + UiSharedService.TooltipSeparator
            + "Note: this is bidirectional, either user pausing will cease sync completely.");
        var otherPerms = Pair.UserPair.OtherPermissions;

        var otherIsPaused = otherPerms.IsPaused();
        var otherDisableSounds = otherPerms.IsDisableSounds();
        var otherDisableAnimations = otherPerms.IsDisableAnimations();
        var otherDisableVFX = otherPerms.IsDisableVFX();

        using (ImRaii.PushIndent(indentSize, false))
        {
            _uiSharedService.BooleanToColoredIcon(!otherIsPaused, false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Pair.UserData.AliasOrUID + " has " + (!otherIsPaused ? "not " : string.Empty) + "paused you");
        }

        ImGuiHelpers.ScaledDummy(0.5f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0.5f);

        if (ImGui.Checkbox("Disable Sounds", ref disableSounds))
        {
            _ownPermissions.SetDisableSounds(disableSounds);
        }
        _uiSharedService.DrawHelpText("Disabling sounds will remove all sounds synced with this user on both sides." + UiSharedService.TooltipSeparator
            + "Note: this is bidirectional, either user disabling sound sync will stop sound sync on both sides.");
        using (ImRaii.PushIndent(indentSize, false))
        {
            _uiSharedService.BooleanToColoredIcon(!otherDisableSounds, false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Pair.UserData.AliasOrUID + " has " + (!otherDisableSounds ? "not " : string.Empty) + "disabled sound sync with you");
        }

        if (ImGui.Checkbox("Disable Animations", ref disableAnimations))
        {
            _ownPermissions.SetDisableAnimations(disableAnimations);
        }
        _uiSharedService.DrawHelpText("Disabling sounds will remove all animations synced with this user on both sides." + UiSharedService.TooltipSeparator
            + "Note: this is bidirectional, either user disabling animation sync will stop animation sync on both sides.");
        using (ImRaii.PushIndent(indentSize, false))
        {
            _uiSharedService.BooleanToColoredIcon(!otherDisableAnimations, false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Pair.UserData.AliasOrUID + " has " + (!otherDisableAnimations ? "not " : string.Empty) + "disabled animation sync with you");
        }

        if (ImGui.Checkbox("Disable VFX", ref disableVfx))
        {
            _ownPermissions.SetDisableVFX(disableVfx);
        }
        _uiSharedService.DrawHelpText("Disabling sounds will remove all VFX synced with this user on both sides." + UiSharedService.TooltipSeparator
            + "Note: this is bidirectional, either user disabling VFX sync will stop VFX sync on both sides.");
        using (ImRaii.PushIndent(indentSize, false))
        {
            _uiSharedService.BooleanToColoredIcon(!otherDisableVFX, false);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Pair.UserData.AliasOrUID + " has " + (!otherDisableVFX ? "not " : string.Empty) + "disabled VFX sync with you");
        }

        ImGuiHelpers.ScaledDummy(0.5f);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(0.5f);

        bool hasChanges = _ownPermissions != Pair.UserPair.OwnPermissions;

        using (ImRaii.Disabled(!hasChanges))
            if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Save, "Save"))
            {
                _ = _apiController.SetBulkPermissions(new(
                    new(StringComparer.Ordinal)
                    {
                        { Pair.UserData.UID, _ownPermissions }
                    },
                    new(StringComparer.Ordinal)
                ));
            }
        UiSharedService.AttachToolTip("Save and apply all changes");

        var rightSideButtons = _uiSharedService.GetIconTextButtonSize(Dalamud.Interface.FontAwesomeIcon.Undo, "Revert") +
            _uiSharedService.GetIconTextButtonSize(Dalamud.Interface.FontAwesomeIcon.ArrowsSpin, "Reset to Default");
        var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;

        ImGui.SameLine(availableWidth - rightSideButtons);

        using (ImRaii.Disabled(!hasChanges))
            if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Undo, "Revert"))
            {
                _ownPermissions = Pair.UserPair.OwnPermissions.DeepClone();
            }
        UiSharedService.AttachToolTip("Revert all changes");

        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(Dalamud.Interface.FontAwesomeIcon.ArrowsSpin, "Reset to Default"))
        {
            var defaultPermissions = _apiController.DefaultPermissions!;
            _ownPermissions.SetSticky(Pair.IsDirectlyPaired || defaultPermissions.IndividualIsSticky);
            _ownPermissions.SetPaused(false);
            _ownPermissions.SetDisableVFX(Pair.IsDirectlyPaired ? defaultPermissions.DisableIndividualVFX : defaultPermissions.DisableGroupVFX);
            _ownPermissions.SetDisableSounds(Pair.IsDirectlyPaired ? defaultPermissions.DisableIndividualSounds : defaultPermissions.DisableGroupSounds);
            _ownPermissions.SetDisableAnimations(Pair.IsDirectlyPaired ? defaultPermissions.DisableIndividualAnimations : defaultPermissions.DisableGroupAnimations);
            _ = _apiController.SetBulkPermissions(new(
                new(StringComparer.Ordinal)
                {
                    { Pair.UserData.UID, _ownPermissions }
                },
                new(StringComparer.Ordinal)
            ));
        }
        UiSharedService.AttachToolTip("This will set all permissions to your defined default permissions in the Mare Settings");

        var ySize = ImGui.GetCursorPosY() + style.FramePadding.Y * ImGuiHelpers.GlobalScale + style.FrameBorderSize;
        ImGui.SetWindowSize(new(400, ySize));
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}
