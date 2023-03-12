using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services.Mediator;

public abstract class WindowMediatorSubscriberBase : Window, IMediatorSubscriber, IDisposable
{
    protected readonly ILogger _logger;
    private readonly WindowSystem _windowSystem;

    public MareMediator Mediator { get; }
    protected WindowMediatorSubscriberBase(ILogger logger, WindowSystem windowSystem, MareMediator mediator, string name) : base(name)
    {
        _logger = logger;
        _windowSystem = windowSystem;
        Mediator = mediator;

        _logger.LogTrace("Creating {type}", GetType());

        Mediator.Subscribe<UiToggleMessage>(this, (msg) =>
        {
            if (((UiToggleMessage)msg).UiType == GetType())
            {
                Toggle();
            }
        });
    }

    public virtual Task StopAsync(CancellationToken cancellationToken)
    {

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _logger.LogTrace("Disposing {type}", GetType());

        Mediator.UnsubscribeAll(this);
    }
}
