using MareSynchronos.Managers;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Models;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Factories;

public class PairFactory
{
    private readonly MareConfigService _configService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly CachedPlayerFactory _cachedPlayerFactory;
    private readonly ILoggerFactory _loggerFactory;

    public PairFactory(MareConfigService configService, ServerConfigurationManager serverConfigurationManager, CachedPlayerFactory cachedPlayerFactory, ILoggerFactory loggerFactory)
    {
        _configService = configService;
        _serverConfigurationManager = serverConfigurationManager;
        _cachedPlayerFactory = cachedPlayerFactory;
        _loggerFactory = loggerFactory;
    }

    public Pair Create()
    {
        return new Pair(_loggerFactory.CreateLogger<Pair>(), _cachedPlayerFactory, _configService, _serverConfigurationManager);
    }
}
