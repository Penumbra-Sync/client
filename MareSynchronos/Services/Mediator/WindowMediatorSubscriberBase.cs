using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services.Mediator;

public abstract class WindowMediatorSubscriberBase : Window, IMediatorSubscriber, IDisposable
{
    protected readonly ILogger _logger;

    protected WindowMediatorSubscriberBase(ILogger logger, MareMediator mediator, string name) : base(name)
    {
        _logger = logger;
        Mediator = mediator;

        _logger.LogTrace("Creating {type}", GetType());

        Mediator.Subscribe<UiToggleMessage>(this, (msg) =>
        {
            if (msg.UiType == GetType())
            {
                Toggle();
            }
        });
    }

    public MareMediator Mediator { get; }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public virtual Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        _logger.LogTrace("Disposing {type}", GetType());

        Mediator.UnsubscribeAll(this);
    }
}