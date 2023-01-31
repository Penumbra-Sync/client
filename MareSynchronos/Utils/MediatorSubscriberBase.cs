using MareSynchronos.Mediator;

namespace MareSynchronos.Utils;

public abstract class MediatorSubscriberBase : IDisposable
{
    protected MediatorSubscriberBase(MareMediator mediator)
    {
        Mediator = mediator;
    }

    protected MareMediator Mediator;

    public virtual void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }
}
