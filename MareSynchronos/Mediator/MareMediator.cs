using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MareSynchronos.Mediator;

public class MareMediator : IDisposable
{
    private record MediatorSubscriber(IMediatorSubscriber Subscriber, Action<IMessage> Action);

    private readonly Dictionary<Type, HashSet<MediatorSubscriber>> _subscriberDict = new();
    private readonly ILogger<MareMediator> _logger;
    private readonly PerformanceCollector _performanceCollector;

    public MareMediator(ILogger<MareMediator> logger, PerformanceCollector performanceCollector)
    {
        _logger = logger;
        _performanceCollector = performanceCollector;
    }

    public void Subscribe<T>(IMediatorSubscriber subscriber, Action<IMessage> action) where T : IMessage
    {
        _subscriberDict.TryAdd(typeof(T), new HashSet<MediatorSubscriber>());

        if (!_subscriberDict[typeof(T)].Add(new(subscriber, action)))
        {
            throw new InvalidOperationException("Already subscribed");
        }
    }

    public void Unsubscribe<T>(IMediatorSubscriber subscriber) where T : IMessage
    {
        if (_subscriberDict.TryGetValue(typeof(T), out var subscribers))
        {
            subscribers.RemoveWhere(p => p.Subscriber == subscriber);
        }
    }

    public void Publish(IMessage message)
    {
        if (_subscriberDict.TryGetValue(message.GetType(), out var subscribers))
        {
            Stopwatch globalStopwatch = Stopwatch.StartNew();
            _performanceCollector.LogPerformance(this, $"Publish>{message.GetType().Name}", () =>
            {
                foreach (var subscriber in subscribers.Where(s => s.Subscriber != null).ToList())
                {
                    try
                    {
                        _performanceCollector.LogPerformance(this, $"Publish>{message.GetType().Name}+{subscriber.Subscriber.GetType().Name}", () => subscriber.Action.Invoke(message));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical(ex, "Error executing {type} for subscriber {subscriber}, removing from Mediator", message.GetType(), subscriber);
                        _subscriberDict[message.GetType()].RemoveWhere(s => s == subscriber);
                    }
                }
            });
        }
    }

    internal void UnsubscribeAll(IMediatorSubscriber subscriber)
    {
        foreach (var kvp in _subscriberDict.ToList())
        {
            var unSubbed = kvp.Value.RemoveWhere(p => p.Subscriber == subscriber);
            if (unSubbed > 0)
                _logger.LogDebug("{sub} unsubscribed from {msg}", subscriber, kvp.Key.Name);
        }
    }

    public void Dispose()
    {
        _logger.LogTrace("Disposing {type}", GetType());
        _subscriberDict.Clear();
    }
}