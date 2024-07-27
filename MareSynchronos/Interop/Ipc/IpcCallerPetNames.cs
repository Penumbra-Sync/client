using Dalamud.Plugin;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerPetNames : IIpcCaller
{
    private readonly ILogger<IpcCallerPetNames> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;

    public IpcCallerPetNames(ILogger<IpcCallerPetNames> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil,
        MareMediator mareMediator)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;

        // todo: implement this, bitch, look at moodles as example implementation
    }

    public bool APIAvailable => throw new NotImplementedException();

    public void CheckAPI()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
