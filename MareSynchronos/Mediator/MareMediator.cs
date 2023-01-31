using MareSynchronos.Utils;

namespace MareSynchronos.Mediator;

public class MareMediator : IDisposable
{
    private record MediatorSubscriber(object Subscriber, Action<IMessage> Action);

    private readonly Dictionary<Type, HashSet<MediatorSubscriber>> _subscriberDict = new();

    public void Subscribe<T>(object subscriber, Action<IMessage> action) where T : IMessage
    {
        _subscriberDict.TryAdd(typeof(T), new HashSet<MediatorSubscriber>());

        if (!_subscriberDict[typeof(T)].Add(new(subscriber, action)))
        {
            throw new InvalidOperationException("Already subscribed");
        }
    }

    public void Unsubscribe<T>(object subscriber) where T : IMessage
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
            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber.Action.Invoke(message);
                }
                catch (Exception ex)
                {
                    Logger.Error("Error executing " + subscriber.Action.Method, ex);
                }
            }
        }
    }

    internal void UnsubscribeAll(object subscriber)
    {
        foreach (var kvp in _subscriberDict)
        {
            kvp.Value.RemoveWhere(p => p.Subscriber == subscriber);
        }
    }

    public void Dispose()
    {
        _subscriberDict.Clear();
    }
}