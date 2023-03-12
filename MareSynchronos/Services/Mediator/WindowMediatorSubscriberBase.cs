using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services.Mediator;

public abstract class WindowMediatorSubscriberBase : Window, IMediatorSubscriber, IHostedService
{
    protected readonly ILogger _logger;
    private readonly WindowSystem _windowSystem;

    public MareMediator Mediator { get; }
    protected WindowMediatorSubscriberBase(ILogger logger, WindowSystem windowSystem, MareMediator mediator, string name) : base(name)
    {
        _logger = logger;
        _windowSystem = windowSystem;
        Mediator = mediator;
    }

    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogTrace("[Host] Starting {type}", GetType());

        Mediator.Subscribe<UiToggleMessage>(this, (msg) =>
        {
            if (((UiToggleMessage)msg).UiType == GetType())
            {
                Toggle();
            }
        });

        _windowSystem.AddWindow(this);

        return Task.CompletedTask;
    }

    public virtual Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogTrace("[Host] Stopping {type}", GetType());
        _windowSystem.RemoveWindow(this);
        Mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }
}
