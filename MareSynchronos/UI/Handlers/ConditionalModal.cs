using MareSynchronos.UI.VM;

namespace MareSynchronos.UI.Handlers;

public class ConditionalModal : ImguiVM
{
    public string Name { get; private set; } = string.Empty;
    public Action OnClose { get; private set; } = () => { };
    public Func<bool> OpenCondition { get; private set; } = () => false;
    public bool OpenState { get; set; } = false;

    public ConditionalModal WithCondition(Func<bool> condition)
    {
        OpenCondition = condition;
        return this;
    }

    public ConditionalModal WithOnClose(Action onClose)
    {
        OnClose = onClose;
        return this;
    }

    public ConditionalModal WithTitle(string name)
    {
        Name = name + "###" + Guid.NewGuid().ToString("d");
        return this;
    }
}