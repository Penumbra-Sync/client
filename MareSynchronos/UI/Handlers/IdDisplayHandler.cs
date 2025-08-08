using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;

namespace MareSynchronos.UI.Handlers;

public class IdDisplayHandler
{
    private readonly MareConfigService _mareConfigService;
    private readonly MareMediator _mediator;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Dictionary<string, bool> _showIdForEntry = new(StringComparer.Ordinal);
    private string _editComment = string.Empty;
    private string _editEntry = string.Empty;
    private bool _editIsUid = false;
    private string _lastMouseOverUid = string.Empty;
    private bool _popupShown = false;
    private DateTime? _popupTime;

    public IdDisplayHandler(MareMediator mediator, ServerConfigurationManager serverManager, MareConfigService mareConfigService)
    {
        _mediator = mediator;
        _serverManager = serverManager;
        _mareConfigService = mareConfigService;
    }

    public void DrawGroupText(string id, GroupFullInfoDto group, float textPosX, Func<float> editBoxWidth)
    {
        ImGui.SameLine(textPosX);
        (bool textIsUid, string playerText) = GetGroupText(group);
        if (!string.Equals(_editEntry, group.GID, StringComparison.Ordinal))
        {
            ImGui.AlignTextToFramePadding();

            using (ImRaii.PushFont(UiBuilder.MonoFont, textIsUid))
                ImGui.TextUnformatted(playerText);

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var prevState = textIsUid;
                if (_showIdForEntry.ContainsKey(group.GID))
                {
                    prevState = _showIdForEntry[group.GID];
                }
                _showIdForEntry[group.GID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                if (_editIsUid)
                {
                    _serverManager.SetNoteForUid(_editEntry, _editComment, save: true);
                }
                else
                {
                    _serverManager.SetNoteForGid(_editEntry, _editComment, save: true);
                }

                _editComment = _serverManager.GetNoteForGid(group.GID) ?? string.Empty;
                _editEntry = group.GID;
                _editIsUid = false;
            }
        }
        else
        {
            ImGui.AlignTextToFramePadding();

            ImGui.SetNextItemWidth(editBoxWidth.Invoke());
            if (ImGui.InputTextWithHint("", "Name/Notes", ref _editComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _serverManager.SetNoteForGid(group.GID, _editComment, save: true);
                _editEntry = string.Empty;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _editEntry = string.Empty;
            }
            UiSharedService.AttachToolTip("Hit ENTER to save\nRight click to cancel");
        }
    }

    public void DrawPairText(string id, Pair pair, float textPosX, Func<float> editBoxWidth)
    {
        ImGui.SameLine(textPosX);
        (bool textIsUid, string playerText) = GetPlayerText(pair);
        if (!string.Equals(_editEntry, pair.UserData.UID, StringComparison.Ordinal))
        {
            ImGui.AlignTextToFramePadding();

            using (ImRaii.PushFont(UiBuilder.MonoFont, textIsUid)) ImGui.TextUnformatted(playerText);

            if (ImGui.IsItemHovered())
            {
                if (!string.Equals(_lastMouseOverUid, id))
                {
                    _popupTime = DateTime.UtcNow.AddSeconds(_mareConfigService.Current.ProfileDelay);
                }

                _lastMouseOverUid = id;

                if (_popupTime > DateTime.UtcNow || !_mareConfigService.Current.ProfilesShow)
                {
                    ImGui.SetTooltip("Left click to switch between UID display and nick" + Environment.NewLine
                        + "Right click to change nick for " + pair.UserData.AliasOrUID + Environment.NewLine
                        + "Middle Mouse Button to open their profile in a separate window");
                }
                else if (_popupTime < DateTime.UtcNow && !_popupShown)
                {
                    _popupShown = true;
                    _mediator.Publish(new ProfilePopoutToggle(pair));
                }
            }
            else
            {
                if (string.Equals(_lastMouseOverUid, id))
                {
                    _mediator.Publish(new ProfilePopoutToggle(Pair: null));
                    _lastMouseOverUid = string.Empty;
                    _popupShown = false;
                }
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var prevState = textIsUid;
                if (_showIdForEntry.ContainsKey(pair.UserData.UID))
                {
                    prevState = _showIdForEntry[pair.UserData.UID];
                }
                _showIdForEntry[pair.UserData.UID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                if (_editIsUid)
                {
                    _serverManager.SetNoteForUid(_editEntry, _editComment, save: true);
                }
                else
                {
                    _serverManager.SetNoteForGid(_editEntry, _editComment, save: true);
                }

                _editComment = pair.GetNote() ?? string.Empty;
                _editEntry = pair.UserData.UID;
                _editIsUid = true;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
            {
                _mediator.Publish(new ProfileOpenStandaloneMessage(pair));
            }
        }
        else
        {
            ImGui.AlignTextToFramePadding();

            ImGui.SetNextItemWidth(editBoxWidth.Invoke());
            if (ImGui.InputTextWithHint("##" + pair.UserData.UID, "Nick/Notes", ref _editComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _serverManager.SetNoteForUid(pair.UserData.UID, _editComment);
                _serverManager.SaveNotes();
                _editEntry = string.Empty;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _editEntry = string.Empty;
            }
            UiSharedService.AttachToolTip("Hit ENTER to save\nRight click to cancel");
        }
    }

    public (bool isGid, string text) GetGroupText(GroupFullInfoDto group)
    {
        var textIsGid = true;
        bool showUidInsteadOfName = ShowGidInsteadOfName(group);
        string? groupText = _serverManager.GetNoteForGid(group.GID);
        if (!showUidInsteadOfName && groupText != null)
        {
            if (string.IsNullOrEmpty(groupText))
            {
                groupText = group.GroupAliasOrGID;
            }
            else
            {
                textIsGid = false;
            }
        }
        else
        {
            groupText = group.GroupAliasOrGID;
        }

        return (textIsGid, groupText!);
    }

    public (bool isUid, string text) GetPlayerText(Pair pair)
    {
        var textIsUid = true;
        bool showUidInsteadOfName = ShowUidInsteadOfName(pair);
        string? playerText = _serverManager.GetNoteForUid(pair.UserData.UID);
        if (!showUidInsteadOfName && playerText != null)
        {
            if (string.IsNullOrEmpty(playerText))
            {
                playerText = pair.UserData.AliasOrUID;
            }
            else
            {
                textIsUid = false;
            }
        }
        else
        {
            playerText = pair.UserData.AliasOrUID;
        }

        if (_mareConfigService.Current.ShowCharacterNameInsteadOfNotesForVisible && pair.IsVisible && !showUidInsteadOfName)
        {
            playerText = pair.PlayerName;
            textIsUid = false;
            if (_mareConfigService.Current.PreferNotesOverNamesForVisible)
            {
                var note = pair.GetNote();
                if (note != null)
                {
                    playerText = note;
                }
            }
        }

        return (textIsUid, playerText!);
    }

    internal void Clear()
    {
        _editEntry = string.Empty;
        _editComment = string.Empty;
    }

    internal void OpenProfile(Pair entry)
    {
        _mediator.Publish(new ProfileOpenStandaloneMessage(entry));
    }

    private bool ShowGidInsteadOfName(GroupFullInfoDto group)
    {
        _showIdForEntry.TryGetValue(group.GID, out var showidInsteadOfName);

        return showidInsteadOfName;
    }

    private bool ShowUidInsteadOfName(Pair pair)
    {
        _showIdForEntry.TryGetValue(pair.UserData.UID, out var showidInsteadOfName);

        return showidInsteadOfName;
    }
}