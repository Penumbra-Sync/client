using MareSynchronos.Managers;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Models;

namespace MareSynchronos.Factories;

public class PairFactory
{
    private readonly Configuration _configuration;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public PairFactory(Configuration configuration, ServerConfigurationManager serverConfigurationManager)
    {
        _configuration = configuration;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public Pair Create()
    {
        return new Pair(_configuration, _serverConfigurationManager);
    }
}
