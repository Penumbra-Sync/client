using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace MareSynchronos.Services.Mediator;

public sealed class MareMediator : IHostedService
{
    private readonly object _addRemoveLock = new();
    private readonly Dictionary<object, DateTime> _lastErrorTime = new();
    private readonly ILogger<MareMediator> _logger;
    private readonly CancellationTokenSource _loopCts = new();
    private readonly ConcurrentQueue<MessageBase> _messageQueue = new();
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly Dictionary<Type, HashSet<SubscriberAction>> _subscriberDict = new();

    public MareMediator(ILogger<MareMediator> logger, PerformanceCollectorService performanceCollector)
    {
        _logger = logger;
        _performanceCollector = performanceCollector;
    }

    public void PrintSubscriberInfo()
    {
        foreach (var kvp in _subscriberDict.SelectMany(c => c.Value.Select(v => v))
            .DistinctBy(p => p.Subscriber).OrderBy(p => p.Subscriber.GetType().FullName, StringComparer.Ordinal).ToList())
        {
            _logger.LogInformation("Subscriber {type}: {sub}", kvp.Subscriber.GetType().Name, kvp.Subscriber.ToString());
            StringBuilder sb = new();
            sb.Append("=> ");
            foreach (var item in _subscriberDict.Where(item => item.Value.Any(v => v.Subscriber == kvp.Subscriber)).ToList())
            {
                sb.Append(item.Key.Name).Append(", ");
            }

            if (!string.Equals(sb.ToString(), "=> ", StringComparison.Ordinal))
                _logger.LogInformation("{sb}", sb.ToString());
            _logger.LogInformation("---");
        }
    }

    public void Publish<T>(T message) where T : MessageBase
    {
        if (message.KeepThreadContext)
        {
            ExecuteMessage(message);
        }
        else
        {
            _messageQueue.Enqueue(message);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogTrace("Starting MareMediator");

        _ = Task.Run(async () =>
        {
            while (!_loopCts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, _loopCts.Token).ConfigureAwait(false);

                HashSet<MessageBase> processedMessages = new();
                while (_messageQueue.TryDequeue(out var message))
                {
                    if (processedMessages.Contains(message)) { continue; }
                    processedMessages.Add(message);

                    ExecuteMessage(message);
                }
            }
        });

        _logger.LogTrace("Started MareMediator");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _messageQueue.Clear();
        _loopCts.Cancel();
        return Task.CompletedTask;
    }

    public void Subscribe<T>(IMediatorSubscriber subscriber, Action<T> action) where T : MessageBase
    {
        lock (_addRemoveLock)
        {
            _subscriberDict.TryAdd(typeof(T), new HashSet<SubscriberAction>());

            if (!_subscriberDict[typeof(T)].Add(new(subscriber, action)))
            {
                throw new InvalidOperationException("Already subscribed");
            }

            _logger.LogDebug("Subscriber added for message {message}: {sub}", typeof(T).Name, subscriber.GetType().Name);
        }
    }

    public void Unsubscribe<T>(IMediatorSubscriber subscriber) where T : MessageBase
    {
        lock (_addRemoveLock)
        {
            if (_subscriberDict.ContainsKey(typeof(T)))
            {
                _subscriberDict[typeof(T)].RemoveWhere(p => p.Subscriber == subscriber);
            }
        }
    }

    internal void UnsubscribeAll(IMediatorSubscriber subscriber)
    {
        lock (_addRemoveLock)
        {
            foreach (Type kvp in _subscriberDict.Select(k => k.Key))
            {
                int unSubbed = _subscriberDict[kvp]?.RemoveWhere(p => p.Subscriber == subscriber) ?? 0;
                if (unSubbed > 0)
                {
                    _logger.LogDebug("{sub} unsubscribed from {msg}", subscriber.GetType().Name, kvp.Name);
                }
            }
        }
    }

    private void ExecuteMessage(MessageBase message)
    {
        if (_subscriberDict.TryGetValue(message.GetType(), out HashSet<SubscriberAction>? subscribers) && subscribers != null && subscribers.Any())
        {
            HashSet<SubscriberAction> subscribersCopy = new HashSet<SubscriberAction>();
            lock (_addRemoveLock)
            {
                subscribersCopy = subscribers?.Where(s => s.Subscriber != null).ToHashSet() ?? new HashSet<SubscriberAction>();
            }

            foreach (SubscriberAction subscriber in subscribersCopy)
            {
                try
                {
                    typeof(MareMediator)
                        .GetMethod(nameof(ExecuteSubscriber), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                        .MakeGenericMethod(message.GetType())
                        .Invoke(this, new object[] { subscriber, message });
                }
                catch (Exception ex)
                {
                    if (_lastErrorTime.TryGetValue(subscriber, out var lastErrorTime) && lastErrorTime.Add(TimeSpan.FromSeconds(10)) > DateTime.UtcNow)
                        continue;

                    _logger.LogCritical(ex, "Error executing {type} for subscriber {subscriber}", message.GetType().Name, subscriber.Subscriber.GetType().Name);
                    _lastErrorTime[subscriber] = DateTime.UtcNow;
                }
            }
        }
    }

    private void ExecuteSubscriber<T>(SubscriberAction subscriber, T message) where T : MessageBase
    {
        var isSameThread = message.KeepThreadContext ? "$" : string.Empty;
        _performanceCollector.LogPerformance(this, $"{isSameThread}Execute>{message.GetType().Name}+{subscriber.Subscriber.GetType().Name}", () => ((Action<T>)subscriber.Action).Invoke(message));
    }

    private sealed class SubscriberAction
    {
        public SubscriberAction(IMediatorSubscriber subscriber, object action)
        {
            Subscriber = subscriber;
            Action = action;
        }

        public object Action { get; }
        public IMediatorSubscriber Subscriber { get; }
    }
}