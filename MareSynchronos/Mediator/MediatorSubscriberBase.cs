namespace MareSynchronos.Mediator;

public abstract class MediatorSubscriberBase : IDisposable
{
    protected MediatorSubscriberBase(MareMediator mediator)
    {
        Mediator = mediator;
    }

    protected readonly MareMediator Mediator;

    public virtual void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }
}
