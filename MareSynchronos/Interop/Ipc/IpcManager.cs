using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed partial class IpcManager : DisposableMediatorSubscriberBase
{
    public IpcManager(ILogger<IpcManager> logger, MareMediator mediator,
        IpcCallerPenumbra penumbraIpc, IpcCallerGlamourer glamourerIpc, IpcCallerCustomize customizeIpc, IpcCallerHeels heelsIpc,
        IpcCallerHonorific honorificIpc, IpcCallerMoodles moodlesIpc, IpcCallerPetNames ipcCallerPetNames, IpcCallerBrio ipcCallerBrio) : base(logger, mediator)
    {
        CustomizePlus = customizeIpc;
        Heels = heelsIpc;
        Glamourer = glamourerIpc;
        Penumbra = penumbraIpc;
        Honorific = honorificIpc;
        Moodles = moodlesIpc;
        PetNames = ipcCallerPetNames;
        Brio = ipcCallerBrio;

        if (Initialized)
        {
            Mediator.Publish(new PenumbraInitializedMessage());
        }

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => PeriodicApiStateCheck());

        try
        {
            PeriodicApiStateCheck();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check for some IPC, plugin not installed?");
        }
    }

    public bool Initialized => Penumbra.APIAvailable && Glamourer.APIAvailable;

    public IpcCallerCustomize CustomizePlus { get; init; }
    public IpcCallerHonorific Honorific { get; init; }
    public IpcCallerHeels Heels { get; init; }
    public IpcCallerGlamourer Glamourer { get; }
    public IpcCallerPenumbra Penumbra { get; }
    public IpcCallerMoodles Moodles { get; }
    public IpcCallerPetNames PetNames { get; }

    public IpcCallerBrio Brio { get; }

    private void PeriodicApiStateCheck()
    {
        Penumbra.CheckAPI();
        Penumbra.CheckModDirectory();
        Glamourer.CheckAPI();
        Heels.CheckAPI();
        CustomizePlus.CheckAPI();
        Honorific.CheckAPI();
        Moodles.CheckAPI();
        PetNames.CheckAPI();
        Brio.CheckAPI();
    }
}