using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.MareConfiguration;
using ImGuiScene;
using MareSynchronos.Services.Mediator;

namespace MareSynchronos.UI.Handlers;

public class UidDisplayHandler
{
    private readonly MareConfigService _mareConfigService;
    private readonly MareMediator _mediator;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Dictionary<string, bool> _showUidForEntry = new(StringComparer.Ordinal);
    private string _editNickEntry = string.Empty;
    private string _editUserComment = string.Empty;
    private string _lastMouseOverUid = string.Empty;
    private bool _popupShown = false;
    private DateTime? _popupTime;
    private TextureWrap? _textureWrap;

    public UidDisplayHandler(MareMediator mediator, PairManager pairManager,
        ServerConfigurationManager serverManager, MareConfigService mareConfigService)
    {
        _mediator = mediator;
        _pairManager = pairManager;
        _serverManager = serverManager;
        _mareConfigService = mareConfigService;
    }

    public void DrawPairText(string id, Pair pair, float textPosX, float originalY, Func<float> editBoxWidth)
    {
        ImGui.SameLine(textPosX);
        (bool textIsUid, string playerText) = GetPlayerText(pair);
        if (!string.Equals(_editNickEntry, pair.UserData.UID, StringComparison.Ordinal))
        {
            ImGui.SetCursorPosY(originalY);
            UiSharedService.FontText(playerText, textIsUid ? UiBuilder.MonoFont : UiBuilder.DefaultFont);
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
                    _mediator.Publish(new ProfilePopoutToggle(null));
                    _lastMouseOverUid = string.Empty;
                    _popupShown = false;
                    _textureWrap?.Dispose();
                    _textureWrap = null;
                }
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var prevState = textIsUid;
                if (_showUidForEntry.ContainsKey(pair.UserData.UID))
                {
                    prevState = _showUidForEntry[pair.UserData.UID];
                }
                _showUidForEntry[pair.UserData.UID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                var nickEntryPair = _pairManager.DirectPairs.Find(p => string.Equals(p.UserData.UID, _editNickEntry, StringComparison.Ordinal));
                nickEntryPair?.SetNote(_editUserComment);
                _editUserComment = pair.GetNote() ?? string.Empty;
                _editNickEntry = pair.UserData.UID;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
            {
                _mediator.Publish(new ProfileOpenStandaloneMessage(pair));
            }
        }
        else
        {
            ImGui.SetCursorPosY(originalY);

            ImGui.SetNextItemWidth(editBoxWidth.Invoke());
            if (ImGui.InputTextWithHint("", "Nick/Notes", ref _editUserComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _serverManager.SetNoteForUid(pair.UserData.UID, _editUserComment);
                _serverManager.SaveNotes();
                _editNickEntry = string.Empty;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _editNickEntry = string.Empty;
            }
            UiSharedService.AttachToolTip("Hit ENTER to save\nRight click to cancel");
        }
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
        }

        return (textIsUid, playerText!);
    }

    internal void Clear()
    {
        _editNickEntry = string.Empty;
        _editUserComment = string.Empty;
    }

    internal void OpenProfile(Pair entry)
    {
        _mediator.Publish(new ProfileOpenStandaloneMessage(entry));
    }

    private bool ShowUidInsteadOfName(Pair pair)
    {
        _showUidForEntry.TryGetValue(pair.UserData.UID, out var showUidInsteadOfName);

        return showUidInsteadOfName;
    }
}