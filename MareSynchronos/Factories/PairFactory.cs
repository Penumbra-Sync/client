using MareSynchronos.Models;

namespace MareSynchronos.Managers;

public class PairFactory
{
    private readonly Configuration _configuration;

    public PairFactory(Configuration configuration)
    {
        _configuration = configuration;
    }

    public Pair Create()
    {
        return new Pair(_configuration);
    }
}
