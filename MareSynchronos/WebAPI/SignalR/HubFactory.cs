using Dalamud.Plugin.Services;
using MareSynchronos.API.SignalR;
using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI.SignalR.Utils;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.WebAPI.SignalR;

public class HubFactory : MediatorSubscriberBase
{
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly MareConfigService _configService;
    private readonly IPluginLog _pluginLog;
    private HubConnection? _instance;
    private bool _isDisposed = false;

    public HubFactory(ILogger<HubFactory> logger, MareMediator mediator, ServerConfigurationManager serverConfigurationManager, MareConfigService configService,
        IPluginLog pluginLog) : base(logger, mediator)
    {
        _serverConfigurationManager = serverConfigurationManager;
        _configService = configService;
        _pluginLog = pluginLog;
    }

    private HubConnection BuildHubConnection()
    {
        Logger.LogDebug("Building new HubConnection");

        _instance = new HubConnectionBuilder()
            .WithUrl(_serverConfigurationManager.CurrentApiUrl + IMareHub.Path, options =>
            {
                options.Headers.Add("Authorization", "Bearer " + _serverConfigurationManager.GetToken());
                options.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol(opt =>
            {
                var resolver = CompositeResolver.Create(StandardResolverAllowPrivate.Instance,
                    BuiltinResolver.Instance,
                    AttributeFormatterResolver.Instance,
                    // replace enum resolver
                    DynamicEnumAsStringResolver.Instance,
                    DynamicGenericResolver.Instance,
                    DynamicUnionResolver.Instance,
                    DynamicObjectResolver.Instance,
                    PrimitiveObjectResolver.Instance,
                    // final fallback(last priority)
                    StandardResolver.Instance);

                opt.SerializerOptions =
                    MessagePackSerializerOptions.Standard
                        .WithCompression(MessagePackCompression.Lz4Block)
                        .WithResolver(resolver);
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy(Mediator))
            .ConfigureLogging(a =>
            {
                a.ClearProviders().AddProvider(new DalamudLoggingProvider(_configService, _pluginLog));
                a.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        _instance.Closed += HubOnClosed;
        _instance.Reconnecting += HubOnReconnecting;
        _instance.Reconnected += HubOnReconnected;

        _isDisposed = false;

        return _instance;
    }

    private Task HubOnReconnected(string? arg)
    {
        Mediator.Publish(new HubReconnectedMessage(arg));
        return Task.CompletedTask;
    }

    private Task HubOnReconnecting(Exception? arg)
    {
        Mediator.Publish(new HubReconnectingMessage(arg));
        return Task.CompletedTask;
    }

    private Task HubOnClosed(Exception? arg)
    {
        Mediator.Publish(new HubClosedMessage(arg));
        return Task.CompletedTask;
    }

    public HubConnection GetOrCreate()
    {
        if (!_isDisposed && _instance != null) return _instance;

        return BuildHubConnection();
    }

    public async Task DisposeHubAsync()
    {
        if (_instance == null || _isDisposed) return;

        Logger.LogDebug("Disposing current HubConnection");

        _isDisposed = true;

        _instance.Closed -= HubOnClosed;
        _instance.Reconnecting -= HubOnReconnecting;
        _instance.Reconnected -= HubOnReconnected;

        await _instance.StopAsync().ConfigureAwait(false);
        await _instance.DisposeAsync().ConfigureAwait(false);

        _instance = null;
    }
}
