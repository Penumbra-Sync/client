using ImGuiNET;
using MareSynchronos.UI.VM;

namespace MareSynchronos.UI.Components;

public class PopupModal
{
    private readonly ConditionalModalVM _modal;

    private PopupModal(ConditionalModalVM modal)
    {
        _modal = modal;
    }

    public static PopupModal FromConditionalModal(ConditionalModalVM conditionalModal)
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

        _modal.ExecuteWithProp<bool>(nameof(ConditionalModalVM.OpenState), (openState) =>
        {
            if (ImGui.BeginPopupModal(_modal.Name, ref openState, UiSharedService.PopupWindowFlags))
            {
                drawContent();
                ImGui.EndPopup();
            }
            return openState;
        });
    }
}
