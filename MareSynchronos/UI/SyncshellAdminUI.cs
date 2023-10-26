using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace MareSynchronos.UI.Components.Popup;

public class SyncshellAdminUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly bool _isModerator = false;
    private readonly bool _isOwner = false;
    private readonly List<string> _oneTimeInvites = [];
    private readonly PairManager _pairManager;
    private readonly UiSharedService _uiSharedService;
    private List<BannedGroupUserDto> _bannedUsers = [];
    private int _multiInvites;
    private string _newPassword;
    private bool _pwChangeSuccess;
    public SyncshellAdminUI(ILogger<SyncshellAdminUI> logger, MareMediator mediator, GroupFullInfoDto groupFullInfo, ApiController apiController, UiSharedService uiSharedService, PairManager pairManager)
        : base(logger, mediator, "Syncshell Admin Panel (" + groupFullInfo.GID + ")")
    {
        GroupFullInfo = groupFullInfo;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _isOwner = string.Equals(GroupFullInfo.OwnerUID, _apiController.UID, StringComparison.Ordinal);
        _isModerator = GroupFullInfo.GroupUserInfo.IsModerator();
        _newPassword = string.Empty;
        _multiInvites = 30;
        _pwChangeSuccess = true;
        IsOpen = true;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new(700, 500),
            MaximumSize = new(700, 2000),
        };
    }

    public GroupFullInfoDto GroupFullInfo { get; private set; }

    public override void Draw()
    {
        if (!_isModerator && !_isOwner) return;

        GroupFullInfo = _pairManager.Groups[GroupFullInfo.Group];

        using var id = ImRaii.PushId("syncshell_admin_" + GroupFullInfo.GID);

        using (ImRaii.PushFont(_uiSharedService.UidFont))
            ImGui.TextUnformatted(GroupFullInfo.GroupAliasOrGID + " Administrative Panel");

        ImGui.Separator();
        var perm = GroupFullInfo.GroupPermissions;

        using var tabbar = ImRaii.TabBar("syncshell_tab_" + GroupFullInfo.GID);

        if (tabbar)
        {
            var inviteTab = ImRaii.TabItem("Invites");
            if (inviteTab)
            {
                bool isInvitesDisabled = perm.IsDisableInvites();

                if (UiSharedService.IconTextButton(isInvitesDisabled ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock,
                    isInvitesDisabled ? "Unlock Syncshell" : "Lock Syncshell"))
                {
                    perm.SetDisableInvites(!isInvitesDisabled);
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGui.Dummy(new(2f));

                UiSharedService.TextWrapped("One-time invites work as single-use passwords. Use those if you do not want to distribute your Syncshell password.");
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Single one-time invite"))
                {
                    ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), 1).Result.FirstOrDefault() ?? string.Empty);
                }
                UiSharedService.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");
                ImGui.InputInt("##amountofinvites", ref _multiInvites);
                ImGui.SameLine();
                using (ImRaii.Disabled(_multiInvites <= 1 || _multiInvites > 100))
                {
                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Generate " + _multiInvites + " one-time invites"))
                    {
                        _oneTimeInvites.AddRange(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), _multiInvites).Result);
                    }
                }

                if (_oneTimeInvites.Any())
                {
                    var invites = string.Join(Environment.NewLine, _oneTimeInvites);
                    ImGui.InputTextMultiline("Generated Multi Invites", ref invites, 5000, new(0, 0), ImGuiInputTextFlags.ReadOnly);
                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Copy, "Copy Invites to clipboard"))
                    {
                        ImGui.SetClipboardText(invites);
                    }
                }
            }
            inviteTab.Dispose();

            var mgmtTab = ImRaii.TabItem("User Management");
            if (mgmtTab)
            {
                if (UiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Clear Syncshell"))
                {
                    _ = _apiController.GroupClear(new(GroupFullInfo.Group));
                }
                UiSharedService.AttachToolTip("This will remove all non-pinned, non-moderator users from the Syncshell");

                ImGui.Dummy(new(2f));

                if (UiSharedService.IconTextButton(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
                {
                    _bannedUsers = _apiController.GroupGetBannedUsers(new GroupDto(GroupFullInfo.Group)).Result;
                }

                if (ImGui.BeginTable("bannedusertable" + GroupFullInfo.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
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
                        if (UiSharedService.IconTextButton(FontAwesomeIcon.Check, "Unban#" + bannedUser.UID))
                        {
                            _ = _apiController.GroupUnbanUser(bannedUser);
                            _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                        }
                    }

                    ImGui.EndTable();
                }
            }
            mgmtTab.Dispose();

            var permissionTab = ImRaii.TabItem("Permissions");
            if (permissionTab)
            {
                bool isDisableAnimations = perm.IsPreferDisableAnimations();
                bool isDisableSounds = perm.IsPreferDisableSounds();
                bool isDisableVfx = perm.IsPreferDisableVFX();

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Suggest Sound Sync");
                UiSharedService.BooleanToColoredIcon(!isDisableSounds);
                ImGui.SameLine(230);
                if (UiSharedService.IconTextButton(isDisableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeMute,
                    isDisableSounds ? "Suggest to enable sound sync" : "Suggest to disable sound sync"))
                {
                    perm.SetPreferDisableSounds(!perm.IsPreferDisableSounds());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Suggest Animation Sync");
                UiSharedService.BooleanToColoredIcon(!isDisableAnimations);
                ImGui.SameLine(230);
                if (UiSharedService.IconTextButton(isDisableAnimations ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop,
                    isDisableAnimations ? "Suggest to enable animation sync" : "Suggest to disable animation sync"))
                {
                    perm.SetPreferDisableAnimations(!perm.IsPreferDisableAnimations());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Suggest VFX Sync");
                UiSharedService.BooleanToColoredIcon(!isDisableVfx);
                ImGui.SameLine(230);
                if (UiSharedService.IconTextButton(isDisableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle,
                    isDisableVfx ? "Suggest to enable vfx sync" : "Suggest to disable vfx sync"))
                {
                    perm.SetPreferDisableVFX(!perm.IsPreferDisableVFX());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                UiSharedService.TextWrapped("Note: those suggested permissions will be shown to users on joining the Syncshell.");
            }
            permissionTab.Dispose();

            if (_isOwner)
            {
                var ownerTab = ImRaii.TabItem("Owner Settings");
                if (ownerTab)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("New Password");
                    var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                    var buttonSize = UiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Passport, "Change Password").X;
                    var textSize = ImGui.CalcTextSize("New Password").X;
                    var spacing = ImGui.GetStyle().ItemSpacing.X;

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(availableWidth - buttonSize - textSize - spacing * 2);
                    ImGui.InputTextWithHint("##changepw", "Min 10 characters", ref _newPassword, 50);
                    ImGui.SameLine();
                    using (ImRaii.Disabled(_newPassword.Length < 10))
                    {
                        if (UiSharedService.IconTextButton(FontAwesomeIcon.Passport, "Change Password"))
                        {
                            _pwChangeSuccess = _apiController.GroupChangePassword(new GroupPasswordDto(GroupFullInfo.Group, _newPassword)).Result;
                            _newPassword = string.Empty;
                        }
                    }
                    UiSharedService.AttachToolTip("Password requires to be at least 10 characters long. This action is irreversible.");

                    if (!_pwChangeSuccess)
                    {
                        UiSharedService.ColorTextWrapped("Failed to change the password. Password requires to be at least 10 characters long.", ImGuiColors.DalamudYellow);
                    }

                    if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Syncshell") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                    {
                        IsOpen = false;
                        _ = _apiController.GroupDelete(new(GroupFullInfo.Group));
                    }
                    UiSharedService.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
                }
                ownerTab.Dispose();
            }
        }
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}