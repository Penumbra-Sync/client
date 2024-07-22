using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed class IpcCallerMoodles : IIpcCaller
{
    private readonly ICallGateSubscriber<int> _moodlesApiVersion;
    private readonly ICallGateSubscriber<IPlayerCharacter, object> _moodlesOnChange;
    private readonly ICallGateSubscriber<nint, string> _moodlesGetStatus;
    private readonly ICallGateSubscriber<nint, string, object> _moodlesSetStatus;
    private readonly ICallGateSubscriber<nint, object> _moodlesRevertStatus;
    private readonly ILogger<IpcCallerMoodles> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareMediator _mareMediator;

    public IpcCallerMoodles(ILogger<IpcCallerMoodles> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil,
        MareMediator mareMediator)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _mareMediator = mareMediator;

        _moodlesApiVersion = pi.GetIpcSubscriber<int>("Moodles.Version");
        _moodlesOnChange = pi.GetIpcSubscriber<IPlayerCharacter, object>("Moodles.StatusManagerModified");
        _moodlesGetStatus = pi.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtr");
        _moodlesSetStatus = pi.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtr");
        _moodlesRevertStatus = pi.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtr");

        _moodlesOnChange.Subscribe(OnMoodlesChange);

        CheckAPI();
    }

    private void OnMoodlesChange(IPlayerCharacter character)
    {
        _mareMediator.Publish(new MoodlesMessage(character.Address));
    }

    public bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            APIAvailable = _moodlesApiVersion.InvokeFunc() == 1;
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public void Dispose()
    {
        _moodlesOnChange.Unsubscribe(OnMoodlesChange);
    }

    public async Task<string?> GetStatusAsync(nint address)
    {
        if (!APIAvailable) return null;

        try
        {
            return await _dalamudUtil.RunOnFrameworkThread(() => _moodlesGetStatus.InvokeFunc(address)).ConfigureAwait(false);

        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Get Moodles Status");
            return null;
        }
    }

    public async Task SetStatusAsync(nint pointer, string status)
    {
        if (!APIAvailable) return;
        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _moodlesSetStatus.InvokeAction(pointer, status)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }

    public async Task RevertStatusAsync(nint pointer)
    {
        if (!APIAvailable) return;
        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _moodlesRevertStatus.InvokeAction(pointer)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }
}
