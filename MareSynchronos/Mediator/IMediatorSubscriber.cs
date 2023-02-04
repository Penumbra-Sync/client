namespace MareSynchronos.Mediator;

public interface IMediatorSubscriber : IDisposable
{
    MareMediator Mediator { get; }
}
