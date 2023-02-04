using Dalamud.Interface.Windowing;
using MareSynchronos.Utils;

namespace MareSynchronos.Mediator;

public abstract class WindowMediatorSubscriberBase : Window, IMediatorSubscriber
{
    public MareMediator Mediator { get; }
    protected WindowMediatorSubscriberBase(MareMediator mediator, string name) : base(name)
    {
        Mediator = mediator;
    }

    public virtual void Dispose()
    {
        Logger.Verbose($"Disposing {GetType()}");
        Mediator.UnsubscribeAll(this);
    }
}
