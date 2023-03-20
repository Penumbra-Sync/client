using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.MareConfiguration;
using Dalamud.Interface.Colors;
using MareSynchronos.API.Data.Extensions;
using ImGuiScene;
using System.Numerics;
using Microsoft.Extensions.Logging;
using MareSynchronos.Services;
using Dalamud.Interface.GameFonts;

namespace MareSynchronos.UI.Handlers;

public class UidDisplayHandler
{
    private readonly ILogger<UidDisplayHandler> _logger;
    private readonly MareConfigService _mareConfigService;
    private readonly MareProfileManager _mareProfileManager;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Dictionary<string, bool> _showUidForEntry = new(StringComparer.Ordinal);
    private readonly UiBuilder _uiBuilder;
    private readonly UiSharedService _uiSharedService;
    private string _editNickEntry = string.Empty;
    private string _editUserComment = string.Empty;
    private string _lastMouseOverUid = string.Empty;
    private byte[] _lastProfilePicture = Array.Empty<byte>();
    private DateTime? _popupTime;
    private TextureWrap? _textureWrap;

    public UidDisplayHandler(ILogger<UidDisplayHandler> logger, UiBuilder uiBuilder, MareProfileManager mareProfileManager,
        UiSharedService uiSharedService, PairManager pairManager, ServerConfigurationManager serverManager, MareConfigService mareConfigService)
    {
        _logger = logger;
        _uiBuilder = uiBuilder;
        _mareProfileManager = mareProfileManager;
        _uiSharedService = uiSharedService;
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
            if (textIsUid) ImGui.PushFont(UiBuilder.MonoFont);
            ImGui.TextUnformatted(playerText);
            if (textIsUid) ImGui.PopFont();
            if (ImGui.IsItemHovered())
            {
                if (!string.Equals(_lastMouseOverUid, id))
                {
                    _popupTime = DateTime.UtcNow.AddSeconds(_mareConfigService.Current.ProfileDelay);
                }

                _lastMouseOverUid = id;

                if (_popupTime > DateTime.UtcNow || !_mareConfigService.Current.ProfilesShow)
                {
                    ImGui.SetTooltip("Left click to switch between UID display and nick" + Environment.NewLine + "Right click to change nick for " + pair.UserData.AliasOrUID);
                }
                else
                {
                    try
                    {
                        var spacing = ImGui.GetStyle().ItemSpacing;

                        ImGui.SetNextWindowSizeConstraints(new Vector2(512 + spacing.X * 4, 256 + spacing.Y * 3), new Vector2(512 + spacing.X * 4, 512 + spacing.Y * 3));
                        ImGui.BeginTooltip();

                        var mareProfile = _mareProfileManager.GetMareProfile(pair.UserData);

                        if (_textureWrap == null || !mareProfile.Profile.ImageData.Value.SequenceEqual(_lastProfilePicture))
                        {
                            _textureWrap?.Dispose();
                            _lastProfilePicture = mareProfile.Profile.ImageData.Value;
                            _textureWrap = _uiBuilder.LoadImage(_lastProfilePicture);
                        }

                        var drawList = ImGui.GetWindowDrawList();
                        var rect = drawList.GetClipRectMin();
                        var rectMax = drawList.GetClipRectMax();

                        ImGui.Indent(256 + spacing.X * 2);
                        if (_uiSharedService.UidFontBuilt) ImGui.PushFont(_uiSharedService.UidFont);
                        UiSharedService.ColorText(pair.UserData.AliasOrUID, ImGuiColors.HealerGreen);
                        if (_uiSharedService.UidFontBuilt) ImGui.PopFont();
                        var pos = ImGui.GetCursorPos();
                        var note = _serverManager.GetNoteForUid(pair.UserData.UID);
                        if (!string.IsNullOrEmpty(note))
                        {
                            UiSharedService.ColorText(note, ImGuiColors.DalamudGrey);
                        }
                        string status = pair.IsVisible ? "Visible" : (pair.IsOnline ? "Online" : "Offline");
                        UiSharedService.ColorText(status, (pair.IsVisible || pair.IsOnline) ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
                        if (pair.IsVisible)
                        {
                            ImGui.SameLine();
                            ImGui.TextUnformatted($"({pair.PlayerName})");
                        }
                        if (pair.UserPair != null)
                        {
                            ImGui.TextUnformatted("Directly paired");
                            if (pair.UserPair.OwnPermissions.IsPaused())
                            {
                                ImGui.SameLine();
                                UiSharedService.ColorText("You: paused", ImGuiColors.DalamudYellow);
                            }
                            if (pair.UserPair.OtherPermissions.IsPaused())
                            {
                                ImGui.SameLine();
                                UiSharedService.ColorText("They: paused", ImGuiColors.DalamudYellow);
                            }
                        }
                        if (pair.GroupPair.Any())
                        {
                            ImGui.TextUnformatted("Paired through Syncshells:");
                            foreach (var groupPair in pair.GroupPair)
                            {
                                ImGui.TextUnformatted("- " + groupPair.Key.GroupAliasOrGID);
                            }
                        }

                        var posDone = ImGui.GetCursorPos();
                        ImGui.PushFont(_uiBuilder.GetGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12)).ImFont);
                        UiSharedService.TextWrapped(mareProfile.Profile.Description);
                        ImGui.PopFont();
                        ImGui.Unindent();

                        var sepColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Separator];

                        bool tallerThanWide = _textureWrap.Height >= _textureWrap.Width;
                        var stretchFactor = tallerThanWide ? 256f / _textureWrap.Height : 256f / _textureWrap.Width;
                        var newWidth = _textureWrap.Width * stretchFactor;
                        var newHeight = _textureWrap.Height * stretchFactor;
                        var remainingWidth = (256f - newWidth) / 2f;
                        var remainingHeight = (256f - newHeight) / 2f;
                        drawList.AddImage(_textureWrap.ImGuiHandle, new Vector2(rect.X + spacing.X + remainingWidth, rect.Y + spacing.Y + remainingHeight),
                            new Vector2(rect.X + spacing.X + remainingWidth + newWidth, rect.Y + spacing.Y + remainingHeight + newHeight));

                        drawList.AddLine(new Vector2(rect.X + 256 + spacing.X * 2, rect.Y + pos.Y - spacing.Y),
                            new Vector2(rectMax.X - spacing.X, rect.Y + pos.Y - spacing.Y),
                            UiSharedService.Color((byte)(sepColor.X * 255), (byte)(sepColor.Y * 255), (byte)(sepColor.Z * 255), (byte)(sepColor.W * 255)));
                        drawList.AddLine(new Vector2(rect.X + 256 + spacing.X * 2, rect.Y + posDone.Y - spacing.Y),
                            new Vector2(rectMax.X - spacing.X, rect.Y + posDone.Y - spacing.Y),
                            UiSharedService.Color((byte)(sepColor.X * 255), (byte)(sepColor.Y * 255), (byte)(sepColor.Z * 255), (byte)(sepColor.W * 255)));

                        ImGui.EndTooltip();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Error during draw tooltip", ex);
                    }
                }
            }
            else
            {
                if (string.Equals(_lastMouseOverUid, id))
                {
                    _lastProfilePicture = Array.Empty<byte>();
                    _lastMouseOverUid = string.Empty;
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

    private bool ShowUidInsteadOfName(Pair pair)
    {
        _showUidForEntry.TryGetValue(pair.UserData.UID, out var showUidInsteadOfName);

        return showUidInsteadOfName;
    }
}