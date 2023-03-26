using System.Globalization;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using ImGuiNET;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.VM;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class CompactUi : WindowMediatorSubscriberBase
{
    private readonly CompactVM _compactVM;
    private readonly GroupPanel _groupPanel;
    private readonly IndividualPairListUiElement _pairUiElement;
    private readonly CompactTransferUiElement _transferUi;
    private readonly UiSharedService _uiShared;
    private float _filterHeight;
    private Vector2 _lastPosition = Vector2.One;
    private Vector2 _lastSize = Vector2.One;
    private bool _showSyncShells;
    private float _transferPartHeight;
    private bool _wasOpen;
    private float _windowContentWidth;

    public CompactUi(CompactVM compactVM, ILogger<CompactUi> logger, MareMediator mediator, UiSharedService uiShared,
        IndividualPairListUiElement pairUiElement, GroupPanel groupPanel, CompactTransferUiElement transferUi) : base(logger, mediator, "###MareSynchronosMainUI")
    {
        _uiShared = uiShared;
        _pairUiElement = pairUiElement;
        _compactVM = compactVM;

        _groupPanel = groupPanel;
        _transferUi = transferUi;

#if DEBUG
        string dev = "Dev Build";
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"Mare Synchronos {dev} ({ver.Major}.{ver.Minor}.{ver.Build})###MareSynchronosMainUI";
        Toggle();
#else
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        WindowName = "Mare Synchronos " + ver.Major + "." + ver.Minor + "." + ver.Build + "###MareSynchronosMainUI";
#endif
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = true);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(350, 400),
            MaximumSize = new Vector2(350, 2000),
        };
    }

    private float TransferPartHeight
    {
        get => _transferPartHeight;
        set
        {
            if (value != _transferPartHeight)
            {
                _transferPartHeight = value;
                Mediator.Publish(new CompactUiContentChangeMessage(_transferPartHeight, _windowContentWidth));
            }
        }
    }

    private float WindowContentWidth
    {
        get => _windowContentWidth;
        set
        {
            if (value != _windowContentWidth)
            {
                _windowContentWidth = value;
                Mediator.Publish(new CompactUiContentChangeMessage(_transferPartHeight, _windowContentWidth));
            }
        }
    }

    public override void Draw()
    {
        WindowContentWidth = UiSharedService.GetWindowContentRegionWidth();
        var versionInfo = _compactVM.Version;
        if (!versionInfo.IsCurrent)
        {
            var ver = versionInfo.Version;
            var unsupported = "UNSUPPORTED VERSION";
            var uidTextSize = ImGui.CalcTextSize(unsupported);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
            UiSharedService.ColorFontText(unsupported, _uiShared.UidFont, ImGuiColors.DalamudRed);
            UiSharedService.ColorText($"Your Mare Synchronos installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " +
                $"It is highly recommended to keep Mare Synchronos up to date. Open /xlplugins and update the plugin.", ImGuiColors.DalamudRed, true);
            ImGui.Separator();
        }

        UiSharedService.DrawWithID("header", DrawUIDHeader);
        ImGui.Separator();
        UiSharedService.DrawWithID("serverstatus", DrawServerStatus);

        if (_compactVM.IsConnected)
        {
            var hasShownSyncShells = _showSyncShells;

            ImGui.PushFont(UiBuilder.IconFont);
            if (!hasShownSyncShells)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]);
            }
            if (ImGui.Button(FontAwesomeIcon.User.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
            {
                _showSyncShells = false;
            }
            if (!hasShownSyncShells)
            {
                ImGui.PopStyleColor();
            }
            ImGui.PopFont();
            UiSharedService.AttachToolTip("Individual pairs");

            ImGui.SameLine();

            ImGui.PushFont(UiBuilder.IconFont);
            if (hasShownSyncShells)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered]);
            }
            if (ImGui.Button(FontAwesomeIcon.UserFriends.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
            {
                _showSyncShells = true;
            }
            if (hasShownSyncShells)
            {
                ImGui.PopStyleColor();
            }
            ImGui.PopFont();

            UiSharedService.AttachToolTip("Syncshells");

            ImGui.Separator();
            if (!hasShownSyncShells)
            {
                UiSharedService.DrawWithID("pairlist", () => _filterHeight = _pairUiElement.DrawPairList(TransferPartHeight, WindowContentWidth));
            }
            else
            {
                _filterHeight = 0;
                UiSharedService.DrawWithID("syncshells", _groupPanel.DrawSyncshells);
            }
            ImGui.Separator();
            var height = ImGui.GetCursorPosY();
            UiSharedService.DrawWithID("transfers", () => _transferUi.Draw(_windowContentWidth));
            TransferPartHeight = ImGui.GetCursorPosY() - height + _filterHeight;
        }

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (_lastSize != size || _lastPosition != pos)
        {
            _lastSize = size;
            _lastPosition = pos;
            Mediator.Publish(new CompactUiChange(_lastSize, _lastPosition));
        }
    }

    private void DrawAddCharacter()
    {
        ImGui.Dummy(new(10));
        var keys = _compactVM.SecretKeys;
        if (keys.TryGetValue(_compactVM.SecretKeyIdx, out var secretKey))
        {
            Button.FromCommand(_compactVM.AddCurrentUserCommand).Draw();

            _compactVM.SecretKeyIdx = _uiShared.DrawCombo("Secret Key##addCharacterSecretKey", keys, (f) => f.Value.FriendlyName, (f) => _compactVM.SecretKeyIdx = f.Key).Key;
        }
        else
        {
            UiSharedService.ColorText("No secret keys are configured for the current server.", ImGuiColors.DalamudYellow, true);
        }
    }

    private void DrawServerStatus()
    {
        var connectButton = Button.FromCommand(_compactVM.ConnectCommand);
        var buttonSize = connectButton.GetSize();
        var userCount = _compactVM.OnlineUserCount.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");
        string shardConnection = _compactVM.ShardString;
        var shardTextSize = ImGui.CalcTextSize(_compactVM.ShardString);
        var printShard = _compactVM.IsConnected && !string.IsNullOrEmpty(_compactVM.ShardString) && shardConnection != string.Empty;

        if (_compactVM.IsConnected)
        {
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - (userSize.X + textSize.X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.ParsedGreen, userCount);
            ImGui.SameLine();
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.Text("Users Online");
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiColors.DalamudRed, "Not connected to any server");
        }

        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - shardTextSize.X / 2);
            ImGui.TextUnformatted(shardConnection);
        }

        ImGui.SameLine();
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }

        if (_compactVM.IsConnected)
        {
            ImGui.SetCursorPosX(0 + ImGui.GetStyle().ItemSpacing.X);
            Button.FromCommand(_compactVM.EditUserProfileCommand).Draw();
        }

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }

        connectButton.Draw();
    }

    private void DrawUIDHeader()
    {
        var uidText = _compactVM.GetUidText();
        var buttonSizeX = 0f;

        var settingsButton = Button.FromCommand(_compactVM.OpenSettingsCommand);

        var uidTextSize = UiSharedService.CalcFontTextSize(uidText, _uiShared.UidFont);

        var originalPos = ImGui.GetCursorPos();
        ImGui.SetWindowFontScale(1.5f);
        var buttonSize = settingsButton.GetSize();
        buttonSizeX -= buttonSize.X - ImGui.GetStyle().ItemSpacing.X * 2;
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);

        settingsButton.Draw();

        ImGui.SameLine(); //Important to draw the uidText consistently
        ImGui.SetCursorPos(originalPos);

        if (_compactVM.IsConnected)
        {
            var copyUidButton = Button.FromCommand(_compactVM.CopyUidCommand);
            buttonSizeX += copyUidButton.GetSize().X - ImGui.GetStyle().ItemSpacing.X * 2;
            ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);
            copyUidButton.Draw();
            ImGui.SameLine();
        }

        ImGui.SetWindowFontScale(1f);

        ImGui.SetCursorPosY(originalPos.Y + buttonSize.Y / 2 - uidTextSize.Y / 2 - ImGui.GetStyle().ItemSpacing.Y / 2);
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 + buttonSizeX - uidTextSize.X / 2);

        UiSharedService.ColorFontText(uidText, _uiShared.UidFont, _compactVM.GetUidColor());

        if (!_compactVM.IsConnected)
        {
            UiSharedService.ColorText(_compactVM.GetServerError(), _compactVM.GetUidColor(), true);
            if (_compactVM.IsNoSecretKey)
            {
                DrawAddCharacter();
            }
        }
    }

    private void UiSharedService_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiSharedService_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }
}