using Dalamud.Interface.Colors;
using ImGuiNET;
using ImGuiScene;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;
using MareSynchronos.MareConfiguration;
using Dalamud.Interface;
using MareSynchronos.UI.VM;

namespace MareSynchronos.UI;

public class PopoutProfileUi : WindowMediatorSubscriberBase
{
    private readonly MareProfileManager _mareProfileManager;
    private readonly UiSharedService _uiSharedService;
    private Vector2 _lastMainPos = Vector2.Zero;
    private Vector2 _lastMainSize = Vector2.Zero;
    private byte[] _lastProfilePicture = Array.Empty<byte>();
    private byte[] _lastSupporterPicture = Array.Empty<byte>();
    private DrawPairVMBase? _pair;
    private TextureWrap? _supporterTextureWrap;
    private TextureWrap? _textureWrap;

    public PopoutProfileUi(ILogger<PopoutProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        MareConfigService mareConfigService, MareProfileManager mareProfileManager) : base(logger, mediator, "###MareSynchronosPopoutProfileUI")
    {
        _uiSharedService = uiBuilder;
        _mareProfileManager = mareProfileManager;

        Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoScrollWithMouse;

        Mediator.Subscribe<ProfilePopoutToggle>(this, (msg) =>
        {
            IsOpen = msg.Pair != null;
            _pair = msg.Pair;
            _lastProfilePicture = Array.Empty<byte>();
            _lastSupporterPicture = Array.Empty<byte>();
            _textureWrap?.Dispose();
            _textureWrap = null;
            _supporterTextureWrap?.Dispose();
            _supporterTextureWrap = null;
        });

        Mediator.Subscribe<CompactUiChange>(this, (msg) =>
        {
            if (msg.Size != Vector2.Zero)
            {
                var border = ImGui.GetStyle().WindowBorderSize / ImGuiHelpers.GlobalScale;
                var padding = ImGui.GetStyle().WindowPadding / ImGuiHelpers.GlobalScale;
                var spacing = ImGui.GetStyle().ItemSpacing / ImGuiHelpers.GlobalScale;
                Size = new(256 + (padding.X * 2) + border, msg.Size.Y / ImGuiHelpers.GlobalScale);
                _lastMainSize = msg.Size;
            }
            var mainPos = msg.Position == Vector2.Zero ? _lastMainPos : msg.Position;
            if (mareConfigService.Current.ProfilePopoutRight)
            {
                Position = new(mainPos.X + _lastMainSize.X, mainPos.Y);
            }
            else
            {
                Position = new(mainPos.X - Size.Value.X, mainPos.Y);
            }

            if (msg.Position != Vector2.Zero)
            {
                _lastMainPos = msg.Position;
            }
        });

        IsOpen = false;
    }

    public override void Draw()
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

            UiSharedService.ColorFontText(_pair.DisplayName, _uiSharedService.UidFont, ImGuiColors.HealerGreen);
            ImGui.Dummy(new(spacing.Y, spacing.Y));
            var textPos = ImGui.GetCursorPosY();
            ImGui.Separator();
            var imagePos = ImGui.GetCursorPos();
            ImGui.Dummy(new(256, 256 * ImGuiHelpers.GlobalScale + spacing.Y));
            var note = _pair.GetNote();
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
            if (_pair.IsDirectlyPaired)
            {
                ImGui.TextUnformatted("Directly paired");
                if (_pair.IsPausedFromSource)
                {
                    ImGui.SameLine();
                    UiSharedService.ColorText("You: paused", ImGuiColors.DalamudYellow);
                }
                if (_pair.IsPausedFromTarget)
                {
                    ImGui.SameLine();
                    UiSharedService.ColorText("They: paused", ImGuiColors.DalamudYellow);
                }
            }
            if (_pair.IsIndirectlyPaired)
            {
                ImGui.TextUnformatted("Paired through Syncshells:");
                foreach (var group in _pair.GroupPairs.Value)
                {
                    ImGui.TextUnformatted("- " + group);
                }
            }

            ImGui.Separator();
            ImGui.PushFont(_uiSharedService.GetGameFontHandle());
            var remaining = ImGui.GetWindowContentRegionMax().Y - ImGui.GetCursorPosY();
            var descText = mareProfile.Description;
            var textSize = ImGui.CalcTextSize(descText, 256f * ImGuiHelpers.GlobalScale);
            bool trimmed = textSize.Y > remaining;
            while (textSize.Y > remaining && descText.Contains(' '))
            {
                descText = descText.Substring(0, descText.LastIndexOf(' ')).TrimEnd();
                textSize = ImGui.CalcTextSize(descText + $"...{Environment.NewLine}[Open Full Profile for complete description]", 256f * ImGuiHelpers.GlobalScale);
            }
            UiSharedService.TextWrapped(trimmed ? descText + $"...{Environment.NewLine}[Open Full Profile for complete description]" : mareProfile.Description);
            ImGui.PopFont();

            var padding = ImGui.GetStyle().WindowPadding.X / 2;
            bool tallerThanWide = _textureWrap.Height >= _textureWrap.Width;
            var stretchFactor = tallerThanWide ? 256f * ImGuiHelpers.GlobalScale / _textureWrap.Height : 256f * ImGuiHelpers.GlobalScale / _textureWrap.Width;
            var newWidth = _textureWrap.Width * stretchFactor;
            var newHeight = _textureWrap.Height * stretchFactor;
            var remainingWidth = (256f * ImGuiHelpers.GlobalScale - newWidth) / 2f;
            var remainingHeight = (256f * ImGuiHelpers.GlobalScale - newHeight) / 2f;
            drawList.AddImage(_textureWrap.ImGuiHandle, new Vector2(rectMin.X + padding + remainingWidth, rectMin.Y + spacing.Y + imagePos.Y + remainingHeight),
                new Vector2(rectMin.X + padding + remainingWidth + newWidth, rectMin.Y + spacing.Y + imagePos.Y + remainingHeight + newHeight));
            if (_supporterTextureWrap != null)
            {
                float iconSize = 38 * ImGuiHelpers.GlobalScale;
                drawList.AddImage(_supporterTextureWrap.ImGuiHandle,
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