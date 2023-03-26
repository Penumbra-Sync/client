using ImGuiNET;
using MareSynchronos.UI.Handlers;

namespace MareSynchronos.UI.Components;

public class PopupModal
{
    private readonly ConditionalModal _modal;

    private PopupModal(ConditionalModal modal)
    {
        _modal = modal;
    }

    public static PopupModal FromConditionalModal(ConditionalModal conditionalModal)
    {
        return new PopupModal(conditionalModal);
    }

    public void Draw(Action drawContent)
    {
        if (_modal.OpenCondition.Invoke())
        {
            _modal.OpenState = true;
            ImGui.OpenPopup(_modal.Name);
        }
        else
        {
            _modal.OpenState = false;
        }

        _modal.ExecuteWithProp<bool>(nameof(ConditionalModal.OpenState), (openState) =>
        {
            if (ImGui.BeginPopupModal(_modal.Name, ref openState, UiSharedService.PopupWindowFlags))
            {
                drawContent();
                ImGui.EndPopup();
            }
            return openState;
        });

        if (!_modal.OpenState)
        {
            _modal.OnClose.Invoke();
        }
    }
}