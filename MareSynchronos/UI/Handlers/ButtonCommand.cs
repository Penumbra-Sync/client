using Dalamud.Interface;
using ImGuiNET;
using System.Numerics;

namespace MareSynchronos.UI.Handlers;

public class ButtonCommand
{
    private readonly Dictionary<int, State> _dict = new();

    public bool ClosePopup { get; private set; } = false;
    public string CommandId { get; } = Guid.NewGuid().ToString("n");
    public Func<int> InternalState { get; private set; } = () => 0;
    public bool RequireCtrl { get; private set; } = false;
    public float Scale { get; private set; } = 1f;
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

    public ButtonCommand WithScale(float scale)
    {
        Scale = scale;
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
        public Func<Vector4?> Background { get; private set; } = () => null;
        public Func<string> ButtonText { get; private set; } = () => string.Empty;
        public Func<bool> Enabled { get; private set; } = () => true;
        public Func<ImFontPtr?> Font { get; private set; } = () => null;
        public Func<Vector4?> Foreground { get; internal set; } = () => null;
        public Func<FontAwesomeIcon> Icon { get; private set; } = () => FontAwesomeIcon.None;
        public Action OnClick { get; private set; } = () => { };
        public Func<string> Tooltip { get; private set; } = () => string.Empty;
        public Func<bool> Visibility { get; private set; } = () => true;

        public State WithAction(Action act)
        {
            OnClick = act;
            return this;
        }

        public State WithBackground(Func<Vector4?> color)
        {
            Background = color;
            return this;
        }

        public State WithBackground(Vector4? color)
        {
            return WithBackground(() => color);
        }

        public State WithEnabled(bool enabled)
        {
            return WithEnabled(() => enabled);
        }

        public State WithEnabled(Func<bool> enabled)
        {
            Enabled = enabled;
            return this;
        }

        public State WithFont(ImFontPtr? imFontPtr)
        {
            return WithFont(() => imFontPtr);
        }

        public State WithFont(Func<ImFontPtr?> font)
        {
            Font = font;
            return this;
        }

        public State WithForeground(Func<Vector4?> color)
        {
            Foreground = color;
            return this;
        }

        public State WithForeground(Vector4? color)
        {
            return WithForeground(() => color);
        }

        public State WithIcon(FontAwesomeIcon icon)
        {
            return WithIcon(() => icon);
        }

        public State WithIcon(Func<FontAwesomeIcon> icon)
        {
            Icon = icon;
            return this;
        }

        public State WithText(string text)
        {
            return WithText(() => text);
        }

        public State WithText(Func<string> text)
        {
            ButtonText = text;
            return this;
        }

        public State WithTooltip(string text)
        {
            return WithTooltip(() => text);
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
            return WithVisibility(() => visibility);
        }
    }
}