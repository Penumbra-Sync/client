using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI.VM;

public abstract class ImguiVM : MediatorSubscriberBase
{
    protected ImguiVM(ILogger logger, MareMediator mediator) : base(logger, mediator)
    {
    }

    public void ExecuteWithProp<T>(string nameOfProp, Func<T, T> act)
    {
        var prop = GetType().GetProperty(nameOfProp)
            ?? throw new ArgumentException("Property Not found", nameof(nameOfProp));
        var val = (T)prop.GetValue(this)!;
        var newVal = act.Invoke(val);
        if (!Equals(newVal, val))
        {
            prop.SetValue(this, newVal);
        }
    }

    internal T As<T>() where T : ImguiVM
    {
        if (GetType() == typeof(T))
        {
            return (T)this;
        }

        throw new InvalidCastException($"Cannot cast {GetType()} to {typeof(T)}");
    }
}