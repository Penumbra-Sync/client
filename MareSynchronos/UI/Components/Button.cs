using Dalamud.Interface;
using Dalamud.Interface.Colors;
using ImGuiNET;
using MareSynchronos.UI.Handlers;
using MareSynchronos.Utils;
using System.Numerics;

namespace MareSynchronos.UI.Components;

public class Button
{
    private const float _toolTipWidth = 300f;
    private readonly ButtonCommand _command;
    private readonly ResettableLazy<Vector2> _sizeLazy;
    private ButtonCommand.State? _lastState;

    private Button(ButtonCommand command)
    {
        _command = command;
        _lastState = command.StatefulCommandContent;
        _sizeLazy = new(() =>
        {
            var visible = _command.StatefulCommandContent.Visibility.Invoke();

            if (!visible) return Vector2.Zero;

            try
            {
                ImGui.SetWindowFontScale(_command.Scale);

                var text = _command.StatefulCommandContent.ButtonText.Invoke();
                var icon = _command.StatefulCommandContent.Icon.Invoke();

                if (!string.IsNullOrEmpty(text) && icon != FontAwesomeIcon.None)
                {
                    return UiSharedService.GetIconTextButtonSize(icon, text);
                }

                if (icon != FontAwesomeIcon.None)
                {
                    return UiSharedService.GetIconButtonSize(icon);
                }

                if (!string.IsNullOrEmpty(text))
                {
                    return ImGuiHelpers.GetButtonSize(text);
                }

                return Vector2.Zero;
            }
            finally
            {
                ImGui.SetWindowFontScale(1f);
            }
        });
    }

    public Vector2 Size
    {
        get
        {
            if (_lastState != _command.StatefulCommandContent)
            {
                _sizeLazy.Reset();
                _lastState = _command.StatefulCommandContent;
            }

            return _sizeLazy;
        }
    }

    public static Button FromCommand(ButtonCommand command)
    {
        return new Button(command);
    }

    public void Draw(Vector2? size = null)
    {
        var visible = _command.StatefulCommandContent.Visibility.Invoke();
        if (!visible) return;

        var enabled = _command.StatefulCommandContent.Enabled.Invoke();
        var text = _command.StatefulCommandContent.ButtonText.Invoke();
        var icon = _command.StatefulCommandContent.Icon.Invoke();
        var tooltip = _command.StatefulCommandContent.Tooltip.Invoke();
        var color = _command.StatefulCommandContent.Foreground.Invoke();
        var bgcolor = _command.StatefulCommandContent.Background.Invoke();
        var font = _command.StatefulCommandContent.Font.Invoke();

        ImGui.PushID(_command.CommandId);

        if (font != null) ImGui.PushFont(font.Value);
        if (_command.Scale != 1f) ImGui.SetWindowFontScale(_command.Scale);
        if (color != null) ImGui.PushStyleColor(ImGuiCol.Text, color.Value);
        if (bgcolor != null) ImGui.PushStyleColor(ImGuiCol.Button, bgcolor.Value);
        if (!enabled) ImGui.BeginDisabled();
        if (!string.IsNullOrEmpty(text) && icon != FontAwesomeIcon.None)
        {
            if (UiSharedService.IconTextButton(icon, text, size) && (!_command.RequireCtrl || (_command.RequireCtrl && UiSharedService.CtrlPressed())))
            {
                _command.StatefulCommandContent.OnClick();
                if (_command.ClosePopup) ImGui.CloseCurrentPopup();
            }
        }
        else if (icon != FontAwesomeIcon.None)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            var button = size == null ? ImGui.Button(icon.ToIconString()) : ImGui.Button(icon.ToIconString(), size.Value);
            if (button && (!_command.RequireCtrl || (_command.RequireCtrl && UiSharedService.CtrlPressed())))
            {
                _command.StatefulCommandContent.OnClick();
                if (_command.ClosePopup) ImGui.CloseCurrentPopup();
            }
            ImGui.PopFont();
        }
        else if (!string.IsNullOrEmpty(text))
        {
            var button = size == null ? ImGui.Button(text) : ImGui.Button(text, size.Value);
            if (button && (!_command.RequireCtrl || (_command.RequireCtrl && UiSharedService.CtrlPressed())))
            {
                _command.StatefulCommandContent.OnClick();
                if (_command.ClosePopup) ImGui.CloseCurrentPopup();
            }
        }
        if (!enabled) ImGui.EndDisabled();
        if (color != null) ImGui.PopStyleColor();
        if (bgcolor != null) ImGui.PopStyleColor();
        if (_command.Scale != 1f) ImGui.SetWindowFontScale(1f);
        if (font != null) ImGui.PopFont();

        ImGui.PopID();

        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.BeginTooltip();
            UiSharedService.TextWrapped(tooltip, _toolTipWidth);
            if (_command.RequireCtrl)
            {
                ImGui.Separator();
                UiSharedService.ColorIcon(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);
                ImGui.SameLine();
                UiSharedService.TextWrapped("Hold CTRL while pressing this button", _toolTipWidth);
            }
            ImGui.EndTooltip();
        }
    }
}