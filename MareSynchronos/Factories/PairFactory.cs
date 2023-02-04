using MareSynchronos.Managers;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Models;

namespace MareSynchronos.Factories;

public class PairFactory
{
    private readonly MareConfigService _configService;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public PairFactory(MareConfigService configService, ServerConfigurationManager serverConfigurationManager)
    {
        _configService = configService;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public Pair Create()
    {
        return new Pair(_configService, _serverConfigurationManager);
    }
}
