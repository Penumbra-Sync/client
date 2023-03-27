using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.MareConfiguration;
using ImGuiScene;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.VM;

namespace MareSynchronos.UI.Handlers;

public class UidDisplayHandler
{
    private readonly MareConfigService _mareConfigService;
    private readonly MareMediator _mediator;
    private string _editUserComment = string.Empty;
    private DrawPairVMBase? _lastEditNickEntry = null;
    private string _lastMouseOverUid = string.Empty;
    private bool _popupShown = false;
    private DateTime? _popupTime;
    private TextureWrap? _textureWrap;

    public UidDisplayHandler(MareMediator mediator, MareConfigService mareConfigService)
    {
        _mediator = mediator;
        _mareConfigService = mareConfigService;
    }

    public void DrawPairText(DrawPairVMBase drawPairBase, float textPosX, float originalY, Func<float> editBoxWidth)
    {
        string id = drawPairBase.DrawPairId;
        ImGui.SameLine(textPosX);
        (bool textIsUid, string playerText) = drawPairBase.GetPlayerText();
        if (drawPairBase != _lastEditNickEntry)
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
                        + "Right click to change nick for " + drawPairBase.DisplayName + Environment.NewLine
                        + "Middle Mouse Button to open their profile in a separate window");
                }
                else if (_popupTime < DateTime.UtcNow && !_popupShown)
                {
                    _popupShown = true;
                    _mediator.Publish(new ProfilePopoutToggle(drawPairBase));
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
                drawPairBase.ToggleDisplay();
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _lastEditNickEntry?.SetNote(_editUserComment);
                _editUserComment = drawPairBase.GetNote() ?? string.Empty;
                _lastEditNickEntry = drawPairBase;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
            {
                _mediator.Publish(new ProfileOpenStandaloneMessage(drawPairBase));
            }
        }
        else
        {
            ImGui.SetCursorPosY(originalY);

            ImGui.SetNextItemWidth(editBoxWidth.Invoke());
            if (ImGui.InputTextWithHint("", "Nick/Notes", ref _editUserComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                drawPairBase.SetNote(_editUserComment);
                _lastEditNickEntry = null;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _lastEditNickEntry = null;
            }
            UiSharedService.AttachToolTip("Hit ENTER to save\nRight click to cancel");
        }
    }
}