using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.MareConfiguration;

namespace MareSynchronos.UI.Handlers;

public class UidDisplayHandler
{
    private readonly MareConfigService _mareConfigService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Dictionary<string, bool> _showUidForEntry = new(StringComparer.Ordinal);
    private string _editNickEntry = string.Empty;
    private string _editUserComment = string.Empty;

    public UidDisplayHandler(PairManager pairManager, ServerConfigurationManager serverManager, MareConfigService mareConfigService)
    {
        _pairManager = pairManager;
        _serverManager = serverManager;
        _mareConfigService = mareConfigService;
    }

    public void DrawPairText(Pair entry, float textPosX, float originalY, Func<float> editBoxWidth)
    {
        ImGui.SameLine(textPosX);
        (bool textIsUid, string playerText) = GetPlayerText(entry);
        if (!string.Equals(_editNickEntry, entry.UserData.UID, StringComparison.Ordinal))
        {
            ImGui.SetCursorPosY(originalY);
            if (textIsUid) ImGui.PushFont(UiBuilder.MonoFont);
            ImGui.TextUnformatted(playerText);
            if (textIsUid) ImGui.PopFont();
            UiSharedService.AttachToolTip("Left click to switch between UID display and nick" + Environment.NewLine +
                          "Right click to change nick for " + entry.UserData.UID);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var prevState = textIsUid;
                if (_showUidForEntry.ContainsKey(entry.UserData.UID))
                {
                    prevState = _showUidForEntry[entry.UserData.UID];
                }
                _showUidForEntry[entry.UserData.UID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                var pair = _pairManager.DirectPairs.Find(p => string.Equals(p.UserData.UID, _editNickEntry, StringComparison.Ordinal));
                pair?.SetNote(_editUserComment);
                _editUserComment = entry.GetNote() ?? string.Empty;
                _editNickEntry = entry.UserData.UID;
            }
        }
        else
        {
            ImGui.SetCursorPosY(originalY);

            ImGui.SetNextItemWidth(editBoxWidth.Invoke());
            if (ImGui.InputTextWithHint("", "Nick/Notes", ref _editUserComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _serverManager.SetNoteForUid(entry.UserData.UID, _editUserComment);
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
                playerText = pair.UserData.UID;
            }
            else
            {
                textIsUid = false;
            }
        }
        else
        {
            playerText = pair.UserData.UID;
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

    private bool ShowUidInsteadOfName(Pair pair)
    {
        _showUidForEntry.TryGetValue(pair.UserData.UID, out var showUidInsteadOfName);

        return showUidInsteadOfName;
    }
}