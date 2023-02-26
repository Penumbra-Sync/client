namespace MareSynchronos.Services.Mediator;

public interface IMediatorSubscriber : IDisposable
{
    MareMediator Mediator { get; }
}
