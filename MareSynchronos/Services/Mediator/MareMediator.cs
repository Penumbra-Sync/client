using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace MareSynchronos.Services.Mediator;

public sealed class MareMediator : IHostedService
{
    private readonly object _addRemoveLock = new();
    private readonly Dictionary<object, DateTime> _lastErrorTime = [];
    private readonly ILogger<MareMediator> _logger;
    private readonly CancellationTokenSource _loopCts = new();
    private readonly ConcurrentQueue<MessageBase> _messageQueue = new();
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly Dictionary<Type, HashSet<SubscriberAction>> _subscriberDict = [];
    private bool _processQueue = false;

    public MareMediator(ILogger<MareMediator> logger, PerformanceCollectorService performanceCollector)
    {
        _logger = logger;
        _performanceCollector = performanceCollector;
    }

    public void PrintSubscriberInfo()
    {
        foreach (var subscriber in _subscriberDict.SelectMany(c => c.Value.Select(v => v.Subscriber))
            .DistinctBy(p => p).OrderBy(p => p.GetType().FullName, StringComparer.Ordinal).ToList())
        {
            _logger.LogInformation("Subscriber {type}: {sub}", subscriber.GetType().Name, subscriber.ToString());
            StringBuilder sb = new();
            sb.Append("=> ");
            foreach (var item in _subscriberDict.Where(item => item.Value.Any(v => v.Subscriber == subscriber)).ToList())
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
        _logger.LogInformation("Starting MareMediator");

        _ = Task.Run(async () =>
        {
            while (!_loopCts.Token.IsCancellationRequested)
            {
                while (!_processQueue)
                {
                    await Task.Delay(100, _loopCts.Token).ConfigureAwait(false);
                }

                await Task.Delay(100, _loopCts.Token).ConfigureAwait(false);

                HashSet<MessageBase> processedMessages = [];
                while (_messageQueue.TryDequeue(out var message))
                {
                    if (processedMessages.Contains(message)) { continue; }
                    processedMessages.Add(message);

                    ExecuteMessage(message);
                }
            }
        });

        _logger.LogInformation("Started MareMediator");

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
            _subscriberDict.TryAdd(typeof(T), []);

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
        if (!_subscriberDict.TryGetValue(message.GetType(), out HashSet<SubscriberAction>? subscribers) || subscribers == null || !subscribers.Any()) return;

        List<SubscriberAction> subscribersCopy = [];
        lock (_addRemoveLock)
        {
            subscribersCopy = subscribers?.Where(s => s.Subscriber != null).ToList() ?? [];
        }

#pragma warning disable S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields
        GetType()
           .GetMethod(nameof(ExecuteReflected), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
           .MakeGenericMethod(message.GetType())?
           .Invoke(this, [subscribersCopy, message]);
#pragma warning restore S3011 // Reflection should not be used to increase accessibility of classes, methods, or fields
    }

    private void ExecuteReflected<T>(List<SubscriberAction> subscribers, T message) where T : MessageBase
    {
        var msgTypeName = message.GetType().Name;
        foreach (SubscriberAction subscriber in subscribers)
        {
            try
            {
                var isSameThread = message.KeepThreadContext ? "$" : string.Empty;
                _performanceCollector.LogPerformance(this, $"{isSameThread}Execute>{msgTypeName}+{subscriber.Subscriber.GetType().Name}>{subscriber.Subscriber}",
                    () => ((Action<T>)subscriber.Action).Invoke(message));
            }
            catch (Exception ex)
            {
                if (_lastErrorTime.TryGetValue(subscriber, out var lastErrorTime) && lastErrorTime.Add(TimeSpan.FromSeconds(10)) > DateTime.UtcNow)
                    continue;

                _logger.LogError(ex.InnerException ?? ex, "Error executing {type} for subscriber {subscriber}",
                    message.GetType().Name, subscriber.Subscriber.GetType().Name);
                _lastErrorTime[subscriber] = DateTime.UtcNow;
            }
        }
    }

    public void StartQueueProcessing()
    {
        _logger.LogInformation("Starting Message Queue Processing");
        _processQueue = true;
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