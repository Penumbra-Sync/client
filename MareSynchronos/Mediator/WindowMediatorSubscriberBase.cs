using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Mediator;

public abstract class WindowMediatorSubscriberBase : Window, IMediatorSubscriber
{
    protected readonly ILogger _logger;

    public MareMediator Mediator { get; }
    protected WindowMediatorSubscriberBase(ILogger logger, MareMediator mediator, string name) : base(name)
    {
        _logger = logger;
        Mediator = mediator;
    }

    public virtual void Dispose()
    {
        _logger.LogTrace($"Disposing {GetType()}");
        Mediator.UnsubscribeAll(this);
    }
}
