using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services.Mediator;

public abstract class WindowMediatorSubscriberBase : Window, IMediatorSubscriber
{
    protected readonly ILogger _logger;
    private readonly WindowSystem _windowSystem;

    public MareMediator Mediator { get; }
    protected WindowMediatorSubscriberBase(ILogger logger, WindowSystem windowSystem, MareMediator mediator, string name) : base(name)
    {
        _logger = logger;
        _windowSystem = windowSystem;
        Mediator = mediator;
        windowSystem.AddWindow(this);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _logger.LogTrace($"Disposing {GetType()}");
        _windowSystem.RemoveWindow(this);
        Mediator.UnsubscribeAll(this);
    }
}
