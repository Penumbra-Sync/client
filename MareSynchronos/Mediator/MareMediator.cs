using Microsoft.Extensions.Logging;

namespace MareSynchronos.Mediator;

public class MareMediator : IDisposable
{
    private record MediatorSubscriber(IMediatorSubscriber Subscriber, Action<IMessage> Action);

    private readonly Dictionary<Type, HashSet<MediatorSubscriber>> _subscriberDict = new();
    private readonly ILogger<MareMediator> _logger;

    public MareMediator(ILogger<MareMediator> logger)
    {
        _logger = logger;
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
            foreach (var subscriber in subscribers.ToList())
            {
                try
                {
                    subscriber.Action.Invoke(message);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical("Error executing " + message.GetType() + " for subscriber " + subscriber + ", removing from Mediator", ex);
                    subscribers.RemoveWhere(s => s == subscriber);
                }
            }
        }
    }

    internal void UnsubscribeAll(IMediatorSubscriber subscriber)
    {
        foreach (var kvp in _subscriberDict.ToList())
        {
            var unSubbed = kvp.Value.RemoveWhere(p => p.Subscriber == subscriber);
            if (unSubbed > 0)
                _logger.LogTrace(subscriber + " unsubscribed from " + kvp.Key.Name);
        }
    }

    public void Dispose()
    {
        _logger.LogTrace($"Disposing {GetType()}");
        _subscriberDict.Clear();
    }
}