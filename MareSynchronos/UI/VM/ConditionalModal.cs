namespace MareSynchronos.UI.VM;

public class ConditionalModalVM : ImguiVM
{
    public string Name { get; private set; } = string.Empty;
    public Func<bool> OpenCondition { get; private set; } = () => false;
    public bool OpenState { get; set; } = false;

    public ConditionalModalVM WithCondition(Func<bool> condition)
    {
        OpenCondition = condition;
        return this;
    }

    public ConditionalModalVM WithTitle(string name)
    {
        Name = name + "###" + Guid.NewGuid().ToString("d");
        return this;
    }
}