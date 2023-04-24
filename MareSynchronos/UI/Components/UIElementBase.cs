using MareSynchronos.UI.VM;

namespace MareSynchronos.UI.Components;

public abstract class UIElementBase<T> where T : ImguiVM
{
    protected UIElementBase(T context)
    {
        DataContext = context;
    }

    public T DataContext { get; }
}
