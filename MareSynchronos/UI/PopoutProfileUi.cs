using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class PopoutProfileUi : WindowMediatorSubscriberBase
{
    private readonly MareProfileManager _mareProfileManager;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly UiSharedService _uiSharedService;
    private Vector2 _lastMainPos = Vector2.Zero;
    private Vector2 _lastMainSize = Vector2.Zero;
    private byte[] _lastProfilePicture = [];
    private byte[] _lastSupporterPicture = [];
    private Pair? _pair;
    private IDalamudTextureWrap? _supporterTextureWrap;
    private IDalamudTextureWrap? _textureWrap;

    public PopoutProfileUi(ILogger<PopoutProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        ServerConfigurationManager serverManager, MareConfigService mareConfigService,
        MareProfileManager mareProfileManager, PairManager pairManager, PerformanceCollectorService performanceCollectorService) : base(logger, mediator, "###MareSynchronosPopoutProfileUI", performanceCollectorService)
    {
        _uiSharedService = uiBuilder;
        _serverManager = serverManager;
        _mareProfileManager = mareProfileManager;
        _pairManager = pairManager;
        Flags = ImGuiWindowFlags.NoDecoration;

        Mediator.Subscribe<ProfilePopoutToggle>(this, (msg) =>
        {
            IsOpen = msg.Pair != null;
            _pair = msg.Pair;
            _lastProfilePicture = [];
            _lastSupporterPicture = [];
            _textureWrap?.Dispose();
            _textureWrap = null;
            _supporterTextureWrap?.Dispose();
            _supporterTextureWrap = null;
        });

        Mediator.Subscribe<CompactUiChange>(this, (msg) =>
        {
            if (msg.Size != Vector2.Zero)
            {
                var border = ImGui.GetStyle().WindowBorderSize;
                var padding = ImGui.GetStyle().WindowPadding;
                Size = new(256 + (padding.X * 2) + border, msg.Size.Y / ImGuiHelpers.GlobalScale);
                _lastMainSize = msg.Size;
            }
            var mainPos = msg.Position == Vector2.Zero ? _lastMainPos : msg.Position;
            if (mareConfigService.Current.ProfilePopoutRight)
            {
                Position = new(mainPos.X + _lastMainSize.X * ImGuiHelpers.GlobalScale, mainPos.Y);
            }
            else
            {
                Position = new(mainPos.X - Size!.Value.X * ImGuiHelpers.GlobalScale, mainPos.Y);
            }

            if (msg.Position != Vector2.Zero)
            {
                _lastMainPos = msg.Position;
            }
        });

        IsOpen = false;
    }

    protected override void DrawInternal()
    {
        if (_pair == null) return;

        try
        {
            var spacing = ImGui.GetStyle().ItemSpacing;

            var mareProfile = _mareProfileManager.GetMareProfile(_pair.UserData);

            if (_textureWrap == null || !mareProfile.ImageData.Value.SequenceEqual(_lastProfilePicture))
            {
                _textureWrap?.Dispose();
                _lastProfilePicture = mareProfile.ImageData.Value;
                _textureWrap = _uiSharedService.LoadImage(_lastProfilePicture);
            }

            if (_supporterTextureWrap == null || !mareProfile.SupporterImageData.Value.SequenceEqual(_lastSupporterPicture))
            {
                _supporterTextureWrap?.Dispose();
                _supporterTextureWrap = null;
                if (!string.IsNullOrEmpty(mareProfile.Base64SupporterPicture))
                {
                    _lastSupporterPicture = mareProfile.SupporterImageData.Value;
                    _supporterTextureWrap = _uiSharedService.LoadImage(_lastSupporterPicture);
                }
            }

            var drawList = ImGui.GetWindowDrawList();
            var rectMin = drawList.GetClipRectMin();
            var rectMax = drawList.GetClipRectMax();

            using (_uiSharedService.UidFont.Push())
                UiSharedService.ColorText(_pair.UserData.AliasOrUID, ImGuiColors.HealerGreen);

            ImGuiHelpers.ScaledDummy(spacing.Y, spacing.Y);
            var textPos = ImGui.GetCursorPosY();
            ImGui.Separator();
            var imagePos = ImGui.GetCursorPos();
            ImGuiHelpers.ScaledDummy(256, 256 * ImGuiHelpers.GlobalScale + spacing.Y);
            var note = _serverManager.GetNoteForUid(_pair.UserData.UID);
            if (!string.IsNullOrEmpty(note))
            {
                UiSharedService.ColorText(note, ImGuiColors.DalamudGrey);
            }
            string status = _pair.IsVisible ? "Visible" : (_pair.IsOnline ? "Online" : "Offline");
            UiSharedService.ColorText(status, (_pair.IsVisible || _pair.IsOnline) ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
            if (_pair.IsVisible)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"({_pair.PlayerName})");
            }
            if (_pair.UserPair.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional)
            {
                ImGui.TextUnformatted("Directly paired");
                if (_pair.UserPair.OwnPermissions.IsPaused())
                {
                    ImGui.SameLine();
                    UiSharedService.ColorText("You: paused", ImGuiColors.DalamudYellow);
                }
                if (_pair.UserPair.OtherPermissions.IsPaused())
                {
                    ImGui.SameLine();
                    UiSharedService.ColorText("They: paused", ImGuiColors.DalamudYellow);
                }
            }
            if (_pair.UserPair.Groups.Any())
            {
                ImGui.TextUnformatted("Paired through Syncshells:");
                foreach (var group in _pair.UserPair.Groups)
                {
                    var groupNote = _serverManager.GetNoteForGid(group);
                    var groupName = _pairManager.GroupPairs.First(f => string.Equals(f.Key.GID, group, StringComparison.Ordinal)).Key.GroupAliasOrGID;
                    var groupString = string.IsNullOrEmpty(groupNote) ? groupName : $"{groupNote} ({groupName})";
                    ImGui.TextUnformatted("- " + groupString);
                }
            }

            ImGui.Separator();
            var font = _uiSharedService.GameFont.Push();
            var remaining = ImGui.GetWindowContentRegionMax().Y - ImGui.GetCursorPosY();
            var descText = mareProfile.Description;
            var textSize = ImGui.CalcTextSize(descText, wrapWidth: 256f * ImGuiHelpers.GlobalScale);
            bool trimmed = textSize.Y > remaining;
            while (textSize.Y > remaining && descText.Contains(' '))
            {
                descText = descText[..descText.LastIndexOf(' ')].TrimEnd();
                textSize = ImGui.CalcTextSize(descText + $"...{Environment.NewLine}[Open Full Profile for complete description]", wrapWidth: 256f * ImGuiHelpers.GlobalScale);
            }
            UiSharedService.TextWrapped(trimmed ? descText + $"...{Environment.NewLine}[Open Full Profile for complete description]" : mareProfile.Description);
            font.Dispose();

            var padding = ImGui.GetStyle().WindowPadding.X / 2;
            bool tallerThanWide = _textureWrap.Height >= _textureWrap.Width;
            var stretchFactor = tallerThanWide ? 256f * ImGuiHelpers.GlobalScale / _textureWrap.Height : 256f * ImGuiHelpers.GlobalScale / _textureWrap.Width;
            var newWidth = _textureWrap.Width * stretchFactor;
            var newHeight = _textureWrap.Height * stretchFactor;
            var remainingWidth = (256f * ImGuiHelpers.GlobalScale - newWidth) / 2f;
            var remainingHeight = (256f * ImGuiHelpers.GlobalScale - newHeight) / 2f;
            drawList.AddImage(_textureWrap.Handle, new Vector2(rectMin.X + padding + remainingWidth, rectMin.Y + spacing.Y + imagePos.Y + remainingHeight),
                new Vector2(rectMin.X + padding + remainingWidth + newWidth, rectMin.Y + spacing.Y + imagePos.Y + remainingHeight + newHeight));
            if (_supporterTextureWrap != null)
            {
                const float iconSize = 38;
                drawList.AddImage(_supporterTextureWrap.Handle,
                    new Vector2(rectMax.X - iconSize - spacing.X, rectMin.Y + (textPos / 2) - (iconSize / 2)),
                    new Vector2(rectMax.X - spacing.X, rectMin.Y + iconSize + (textPos / 2) - (iconSize / 2)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during draw tooltip");
        }
    }
}