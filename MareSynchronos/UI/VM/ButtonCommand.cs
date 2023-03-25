using Dalamud.Interface;
using Dalamud.Interface.Colors;
using System.Numerics;

namespace MareSynchronos.UI.VM;

public class ButtonCommand
{
    private readonly Dictionary<int, ButtonCommandContent> _dict = new Dictionary<int, ButtonCommandContent>();

    public bool RequireCtrl { get; private set; } = false;
    public Func<int> State { get; private set; } = () => 0;
    public ButtonCommandContent StatefulCommandContent => _dict.GetValueOrDefault(State.Invoke(), new ButtonCommandContent());

    public ButtonCommand WithRequireCtrl()
    {
        RequireCtrl = true;
        return this;
    }

    public ButtonCommand WithState(int state, ButtonCommandContent command)
    {
        _dict[state] = command;
        return this;
    }

    public ButtonCommand WithStateSelector(Func<int> func)
    {
        State = func;
        return this;
    }

    public class ButtonCommandContent
    {
        public Func<string> ButtonText { get; private set; } = () => string.Empty;
        public Func<bool> Enabled { get; private set; } = () => true;
        public Func<Vector4> Foreground { get; internal set; } = () => ImGuiColors.DalamudWhite;
        public Func<FontAwesomeIcon> Icon { get; private set; } = () => FontAwesomeIcon.None;
        public Action OnClick { get; private set; } = () => { };
        public Func<string> Tooltip { get; private set; } = () => string.Empty;

        public ButtonCommandContent WithAction(Action act)
        {
            OnClick = act;
            return this;
        }

        public ButtonCommandContent WithEnabled(bool enabled)
        {
            Enabled = () => enabled;
            return this;
        }

        public ButtonCommandContent WithEnabled(Func<bool> enabled)
        {
            Enabled = enabled;
            return this;
        }

        public ButtonCommandContent WithForeground(Func<Vector4> color)
        {
            Foreground = color;
            return this;
        }

        public ButtonCommandContent WithForeground(Vector4 color)
        {
            Foreground = () => color;
            return this;
        }

        public ButtonCommandContent WithIcon(FontAwesomeIcon icon)
        {
            Icon = () => icon;
            return this;
        }

        public ButtonCommandContent WithIcon(Func<FontAwesomeIcon> icon)
        {
            Icon = icon;
            return this;
        }

        public ButtonCommandContent WithText(string text)
        {
            ButtonText = () => text;
            return this;
        }

        public ButtonCommandContent WithText(Func<string> text)
        {
            ButtonText = text;
            return this;
        }

        public ButtonCommandContent WithTooltip(string text)
        {
            Tooltip = () => text;
            return this;
        }

        public ButtonCommandContent WithTooltip(Func<string> text)
        {
            Tooltip = text;
            return this;
        }
    }
}