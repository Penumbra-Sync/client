using Dalamud.Interface;
using Dalamud.Interface.Colors;
using System.Numerics;

namespace MareSynchronos.UI.VM;

public class ButtonCommand
{
    private readonly Dictionary<int, State> _dict = new Dictionary<int, State>();

    public bool ClosePopup { get; private set; } = false;
    public Func<int> InternalState { get; private set; } = () => 0;
    public bool RequireCtrl { get; private set; } = false;
    public State StatefulCommandContent => _dict.GetValueOrDefault(InternalState.Invoke(), new State());

    public ButtonCommand WithClosePopup()
    {
        ClosePopup = true;
        return this;
    }

    public ButtonCommand WithRequireCtrl()
    {
        RequireCtrl = true;
        return this;
    }

    public ButtonCommand WithState(int state, State command)
    {
        _dict[state] = command;
        return this;
    }

    public ButtonCommand WithStateSelector(Func<int> func)
    {
        InternalState = func;
        return this;
    }

    public class State
    {
        public Func<string> ButtonText { get; private set; } = () => string.Empty;
        public Func<bool> Enabled { get; private set; } = () => true;
        public Func<Vector4> Foreground { get; internal set; } = () => ImGuiColors.DalamudWhite;
        public Func<FontAwesomeIcon> Icon { get; private set; } = () => FontAwesomeIcon.None;
        public Action OnClick { get; private set; } = () => { };
        public Func<string> Tooltip { get; private set; } = () => string.Empty;
        public Func<bool> Visibility { get; private set; } = () => true;

        public State WithAction(Action act)
        {
            OnClick = act;
            return this;
        }

        public State WithEnabled(bool enabled)
        {
            Enabled = () => enabled;
            return this;
        }

        public State WithEnabled(Func<bool> enabled)
        {
            Enabled = enabled;
            return this;
        }

        public State WithForeground(Func<Vector4> color)
        {
            Foreground = color;
            return this;
        }

        public State WithForeground(Vector4 color)
        {
            Foreground = () => color;
            return this;
        }

        public State WithIcon(FontAwesomeIcon icon)
        {
            Icon = () => icon;
            return this;
        }

        public State WithIcon(Func<FontAwesomeIcon> icon)
        {
            Icon = icon;
            return this;
        }

        public State WithText(string text)
        {
            ButtonText = () => text;
            return this;
        }

        public State WithText(Func<string> text)
        {
            ButtonText = text;
            return this;
        }

        public State WithTooltip(string text)
        {
            Tooltip = () => text;
            return this;
        }

        public State WithTooltip(Func<string> text)
        {
            Tooltip = text;
            return this;
        }

        public State WithVisibility(Func<bool> visibility)
        {
            Visibility = visibility;
            return this;
        }

        public State WithVisibility(bool visibility)
        {
            Visibility = () => visibility;
            return this;
        }
    }
}