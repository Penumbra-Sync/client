using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI;
using System.Globalization;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

internal class SyncshellAdminPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private readonly List<string> _oneTimeInvites = [];
    private readonly PairManager _pairManager;
    private readonly UiSharedService _uiSharedService;
    private List<BannedGroupUserDto> _bannedUsers = [];
    private GroupFullInfoDto _groupFullInfo = null!;
    private bool _isModerator = false;
    private bool _isOwner = false;
    private int _multiInvites = 30;
    private string _newPassword = string.Empty;
    private bool _pwChangeSuccess = true;

    public SyncshellAdminPopupHandler(ApiController apiController, UiSharedService uiSharedService, PairManager pairManager)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
    }

    public Vector2 PopupSize => new(700, 500);

    public void DrawContent()
    {
        if (!_isModerator && !_isOwner) return;

        _groupFullInfo = _pairManager.Groups[_groupFullInfo.Group];

        using (ImRaii.PushFont(_uiSharedService.UidFont))
            ImGui.TextUnformatted(_groupFullInfo.GroupAliasOrGID + " Administrative Panel");

        ImGui.Separator();
        var perm = _groupFullInfo.GroupPermissions;

        var inviteNode = ImRaii.TreeNode("Invites");
        if (inviteNode)
        {
            bool isInvitesDisabled = perm.IsDisableInvites();

            if (ImGuiComponents.IconButtonWithText(isInvitesDisabled ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock,
                isInvitesDisabled ? "Unlock Syncshell" : "Lock Syncshell"))
            {
                perm.SetDisableInvites(!isInvitesDisabled);
                _ = _apiController.GroupChangeGroupPermissionState(new(_groupFullInfo.Group, perm));
            }

            ImGui.Dummy(new(2f));

            UiSharedService.TextWrapped("One-time invites work as single-use passwords. Use those if you do not want to distribute your Syncshell password.");
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Envelope, "Single one-time invite"))
            {
                ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(new(_groupFullInfo.Group), 1).Result.FirstOrDefault() ?? string.Empty);
            }
            UiSharedService.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");
            ImGui.InputInt("##amountofinvites", ref _multiInvites);
            ImGui.SameLine();
            using (ImRaii.Disabled(_multiInvites <= 1 || _multiInvites > 100))
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Envelope, "Generate " + _multiInvites + " one-time invites"))
                {
                    _oneTimeInvites.AddRange(_apiController.GroupCreateTempInvite(new(_groupFullInfo.Group), _multiInvites).Result);
                }
            }

            if (_oneTimeInvites.Any())
            {
                var invites = string.Join(Environment.NewLine, _oneTimeInvites);
                ImGui.InputTextMultiline("Generated Multi Invites", ref invites, 5000, new(0, 0), ImGuiInputTextFlags.ReadOnly);
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, "Copy Invites to clipboard"))
                {
                    ImGui.SetClipboardText(invites);
                }
            }
        }
        inviteNode.Dispose();

        var mgmtNode = ImRaii.TreeNode("User Management");
        if (mgmtNode)
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Broom, "Clear Syncshell"))
            {
                _ = _apiController.GroupClear(new(_groupFullInfo.Group));
            }
            UiSharedService.AttachToolTip("This will remove all non-pinned, non-moderator users from the Syncshell");

            ImGui.Dummy(new(2f));

            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
            {
                _bannedUsers = _apiController.GroupGetBannedUsers(new GroupDto(_groupFullInfo.Group)).Result;
            }

            if (ImGui.BeginTable("bannedusertable" + _groupFullInfo.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("By", ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.None, 2);
                ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 3);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 1);

                ImGui.TableHeadersRow();

                foreach (var bannedUser in _bannedUsers.ToList())
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.UID);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.UserAlias ?? string.Empty);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.BannedBy);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(bannedUser.BannedOn.ToLocalTime().ToString(CultureInfo.CurrentCulture));
                    ImGui.TableNextColumn();
                    UiSharedService.TextWrapped(bannedUser.Reason);
                    ImGui.TableNextColumn();
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, "Unban#" + bannedUser.UID))
                    {
                        _ = _apiController.GroupUnbanUser(bannedUser);
                        _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                    }
                }

                ImGui.EndTable();
            }
        }
        mgmtNode.Dispose();

        var permNode = ImRaii.TreeNode("Permissions");
        if (permNode)
        {
            bool isDisableAnimations = perm.IsPreferDisableAnimations();
            bool isDisableSounds = perm.IsPreferDisableSounds();
            bool isDisableVfx = perm.IsPreferDisableVFX();

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Suggest Sound Sync");
            UiSharedService.BooleanToColoredIcon(!isDisableSounds);
            ImGui.SameLine(230);
            if (ImGuiComponents.IconButtonWithText(isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute,
                isDisableSounds ? "Suggest to enable sound sync" : "Suggest to disable sound sync"))
            {
                perm.SetPreferDisableSounds(!perm.IsPreferDisableSounds());
                _ = _apiController.GroupChangeGroupPermissionState(new(_groupFullInfo.Group, perm));
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Suggest Animation Sync");
            UiSharedService.BooleanToColoredIcon(!isDisableAnimations);
            ImGui.SameLine(230);
            if (ImGuiComponents.IconButtonWithText(isDisableAnimations ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop,
                isDisableAnimations ? "Suggest to enable animation sync" : "Suggest to disable animation sync"))
            {
                perm.SetPreferDisableAnimations(!perm.IsPreferDisableAnimations());
                _ = _apiController.GroupChangeGroupPermissionState(new(_groupFullInfo.Group, perm));
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Suggest VFX Sync");
            UiSharedService.BooleanToColoredIcon(!isDisableVfx);
            ImGui.SameLine(230);
            if (ImGuiComponents.IconButtonWithText(isDisableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle,
                isDisableVfx ? "Suggest to enable vfx sync" : "Suggest to disable vfx sync"))
            {
                perm.SetPreferDisableVFX(!perm.IsPreferDisableVFX());
                _ = _apiController.GroupChangeGroupPermissionState(new(_groupFullInfo.Group, perm));
            }

            UiSharedService.TextWrapped("Note: those suggested permissions will be shown to users on joining the Syncshell.");
        }
        permNode.Dispose();

        if (_isOwner)
        {
            var ownerNode = ImRaii.TreeNode("Owner Settings");
            if (ownerNode)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("New Password");
                ImGui.SameLine();
                ImGui.InputTextWithHint("##changepw", "Min 10 characters", ref _newPassword, 50);
                ImGui.SameLine();
                using (ImRaii.Disabled(_newPassword.Length < 10))
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Passport, "Change Password"))
                    {
                        _pwChangeSuccess = _apiController.GroupChangePassword(new GroupPasswordDto(_groupFullInfo.Group, _newPassword)).Result;
                        _newPassword = string.Empty;
                    }
                }
                UiSharedService.AttachToolTip("Password requires to be at least 10 characters long. This action is irreversible.");

                if (!_pwChangeSuccess)
                {
                    UiSharedService.ColorTextWrapped("Failed to change the password. Password requires to be at least 10 characters long.", ImGuiColors.DalamudYellow);
                }

                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "Delete Syncshell") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = _apiController.GroupDelete(new(_groupFullInfo.Group));
                }
                UiSharedService.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
            }
            ownerNode.Dispose();
        }
    }

    public void Open(GroupFullInfoDto groupFullInfo)
    {
        _groupFullInfo = groupFullInfo;
        _isOwner = string.Equals(_groupFullInfo.OwnerUID, _apiController.UID, StringComparison.Ordinal);
        _isModerator = _groupFullInfo.GroupUserInfo.IsModerator();
        _newPassword = string.Empty;
        _bannedUsers.Clear();
        _oneTimeInvites.Clear();
        _multiInvites = 30;
        _pwChangeSuccess = true;
    }
}