using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class StandaloneProfileUi : WindowMediatorSubscriberBase
{
    private readonly MareProfileManager _mareProfileManager;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly UiSharedService _uiSharedService;
    private bool _adjustedForScrollBars = false;
    private byte[] _lastProfilePicture = [];
    private byte[] _lastSupporterPicture = [];
    private IDalamudTextureWrap? _supporterTextureWrap;
    private IDalamudTextureWrap? _textureWrap;

    public StandaloneProfileUi(ILogger<StandaloneProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        ServerConfigurationManager serverManager, MareProfileManager mareProfileManager, PairManager pairManager, Pair pair,
        PerformanceCollectorService performanceCollector)
        : base(logger, mediator, "Mare Profile of " + pair.UserData.AliasOrUID + "##MareSynchronosStandaloneProfileUI" + pair.UserData.AliasOrUID, performanceCollector)
    {
        _uiSharedService = uiBuilder;
        _serverManager = serverManager;
        _mareProfileManager = mareProfileManager;
        Pair = pair;
        _pairManager = pairManager;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize;

        var spacing = ImGui.GetStyle().ItemSpacing;

        Size = new(512 + spacing.X * 3 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 512);

        IsOpen = true;
    }

    public Pair Pair { get; init; }

    protected override void DrawInternal()
    {
        try
        {
            var spacing = ImGui.GetStyle().ItemSpacing;

            var mareProfile = _mareProfileManager.GetMareProfile(Pair.UserData);

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
            var headerSize = ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y;

            using (_uiSharedService.UidFont.Push())
                UiSharedService.ColorText(Pair.UserData.AliasOrUID, ImGuiColors.HealerGreen);

            ImGuiHelpers.ScaledDummy(new Vector2(spacing.Y, spacing.Y));
            var textPos = ImGui.GetCursorPosY() - headerSize;
            ImGui.Separator();
            var pos = ImGui.GetCursorPos() with { Y = ImGui.GetCursorPosY() - headerSize };
            ImGuiHelpers.ScaledDummy(new Vector2(256, 256 + spacing.Y));
            var postDummy = ImGui.GetCursorPosY();
            ImGui.SameLine();
            var descriptionTextSize = ImGui.CalcTextSize(mareProfile.Description, wrapWidth: 256f);
            var descriptionChildHeight = rectMax.Y - pos.Y - rectMin.Y - spacing.Y * 2;
            if (descriptionTextSize.Y > descriptionChildHeight && !_adjustedForScrollBars)
            {
                Size = Size!.Value with { X = Size.Value.X + ImGui.GetStyle().ScrollbarSize };
                _adjustedForScrollBars = true;
            }
            else if (descriptionTextSize.Y < descriptionChildHeight && _adjustedForScrollBars)
            {
                Size = Size!.Value with { X = Size.Value.X - ImGui.GetStyle().ScrollbarSize };
                _adjustedForScrollBars = false;
            }
            var childFrame = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, descriptionChildHeight);
            childFrame = childFrame with
            {
                X = childFrame.X + (_adjustedForScrollBars ? ImGui.GetStyle().ScrollbarSize : 0),
                Y = childFrame.Y / ImGuiHelpers.GlobalScale
            };
            if (ImGui.BeginChildFrame(1000, childFrame))
            {
                using var _ = _uiSharedService.GameFont.Push();
                ImGui.TextWrapped(mareProfile.Description);
            }
            ImGui.EndChildFrame();

            ImGui.SetCursorPosY(postDummy);
            var note = _serverManager.GetNoteForUid(Pair.UserData.UID);
            if (!string.IsNullOrEmpty(note))
            {
                UiSharedService.ColorText(note, ImGuiColors.DalamudGrey);
            }
            string status = Pair.IsVisible ? "Visible" : (Pair.IsOnline ? "Online" : "Offline");
            UiSharedService.ColorText(status, (Pair.IsVisible || Pair.IsOnline) ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
            if (Pair.IsVisible)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"({Pair.PlayerName})");
            }
            if (Pair.UserPair != null)
            {
                ImGui.TextUnformatted("Directly paired");
                if (Pair.UserPair.OwnPermissions.IsPaused())
                {
                    ImGui.SameLine();
                    UiSharedService.ColorText("You: paused", ImGuiColors.DalamudYellow);
                }
                if (Pair.UserPair.OtherPermissions.IsPaused())
                {
                    ImGui.SameLine();
                    UiSharedService.ColorText("They: paused", ImGuiColors.DalamudYellow);
                }
            }

            if (Pair.UserPair.Groups.Any())
            {
                ImGui.TextUnformatted("Paired through Syncshells:");
                foreach (var group in Pair.UserPair.Groups)
                {
                    var groupNote = _serverManager.GetNoteForGid(group);
                    var groupName = _pairManager.GroupPairs.First(f => string.Equals(f.Key.GID, group, StringComparison.Ordinal)).Key.GroupAliasOrGID;
                    var groupString = string.IsNullOrEmpty(groupNote) ? groupName : $"{groupNote} ({groupName})";
                    ImGui.TextUnformatted("- " + groupString);
                }
            }

            var padding = ImGui.GetStyle().WindowPadding.X / 2;
            bool tallerThanWide = _textureWrap.Height >= _textureWrap.Width;
            var stretchFactor = tallerThanWide ? 256f * ImGuiHelpers.GlobalScale / _textureWrap.Height : 256f * ImGuiHelpers.GlobalScale / _textureWrap.Width;
            var newWidth = _textureWrap.Width * stretchFactor;
            var newHeight = _textureWrap.Height * stretchFactor;
            var remainingWidth = (256f * ImGuiHelpers.GlobalScale - newWidth) / 2f;
            var remainingHeight = (256f * ImGuiHelpers.GlobalScale - newHeight) / 2f;
            drawList.AddImage(_textureWrap.Handle, new Vector2(rectMin.X + padding + remainingWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight),
                new Vector2(rectMin.X + padding + remainingWidth + newWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight + newHeight));
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

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}