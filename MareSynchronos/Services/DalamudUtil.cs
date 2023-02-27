using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MareSynchronos.Services;

public class DalamudUtil : IDisposable
{
    private readonly ILogger<DalamudUtil> _logger;
    private readonly ClientState _clientState;
    private readonly ObjectTable _objectTable;
    private readonly Framework _framework;
    private readonly Condition _condition;
    private readonly MareMediator _mediator;
    private readonly PerformanceCollectorService _performanceCollector;
    private uint? _classJobId = 0;
    private DateTime _delayedFrameworkUpdateCheck = DateTime.Now;
    private bool _sentBetweenAreas = false;
    public bool IsInCutscene { get; private set; } = false;
    public bool IsZoning => _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51];
    public bool IsInGpose { get; private set; } = false;

    public unsafe bool IsGameObjectPresent(IntPtr key)
    {
        foreach (var obj in _objectTable)
        {
            if (obj.Address == key)
            {
                return true;
            }
        }

        return false;
    }

    public DalamudUtil(ILogger<DalamudUtil> logger, ClientState clientState, ObjectTable objectTable, Framework framework,
        Condition condition, Dalamud.Data.DataManager gameData, MareMediator mediator, PerformanceCollectorService performanceCollector)
    {
        _logger = logger;
        _clientState = clientState;
        _objectTable = objectTable;
        _framework = framework;
        _condition = condition;
        _mediator = mediator;
        _performanceCollector = performanceCollector;
        _framework.Update += FrameworkOnUpdate;
        if (IsLoggedIn)
        {
            _classJobId = _clientState.LocalPlayer!.ClassJob.Id;
        }
        WorldData = new(() =>
        {
            return gameData.GetExcelSheet<Lumina.Excel.GeneratedSheets.World>(Dalamud.ClientLanguage.English)!
                .Where(w => w.IsPublic && !w.Name.RawData.IsEmpty)
                .ToDictionary(w => (ushort)w.RowId, w => w.Name.ToString());
        });
    }

    public Lazy<Dictionary<ushort, string>> WorldData { get; private set; }

    private void FrameworkOnUpdate(Framework framework)
    {
        _performanceCollector.LogPerformance(this, "FrameworkOnUpdate", FrameworkOnUpdateInternal);
    }

    private unsafe void FrameworkOnUpdateInternal()
    {
        if (GposeTarget != null && !IsInGpose)
        {
            _logger.LogDebug("Gpose start");
            IsInGpose = true;
            _mediator.Publish(new GposeStartMessage());
        }
        else if (GposeTarget == null && IsInGpose)
        {
            _logger.LogDebug("Gpose end");
            IsInGpose = false;
            _mediator.Publish(new GposeEndMessage());
        }

        if (_condition[ConditionFlag.WatchingCutscene] && !IsInCutscene)
        {
            _logger.LogDebug("Cutscene start");
            IsInCutscene = true;
            _mediator.Publish(new CutsceneStartMessage());
            _mediator.Publish(new HaltScanMessage("Cutscene"));

        }
        else if (!_condition[ConditionFlag.WatchingCutscene] && IsInCutscene)
        {
            _logger.LogDebug("Cutscene end");
            IsInCutscene = false;
            _mediator.Publish(new CutsceneEndMessage());
            _mediator.Publish(new ResumeScanMessage("Cutscene"));
        }

        if (IsInCutscene) { _mediator.Publish(new CutsceneFrameworkUpdateMessage()); return; }

        if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51])
        {
            if (!_sentBetweenAreas)
            {
                _logger.LogDebug("Zone switch/Gpose start");
                _sentBetweenAreas = true;
                _mediator.Publish(new ZoneSwitchStartMessage());
                _mediator.Publish(new HaltScanMessage("Zone switch"));
            }

            return;
        }

        if (_sentBetweenAreas)
        {
            _logger.LogDebug("Zone switch/Gpose end");
            _sentBetweenAreas = false;
            _mediator.Publish(new ZoneSwitchEndMessage());
            _mediator.Publish(new ResumeScanMessage("Zone switch"));
        }

        _mediator.Publish(new FrameworkUpdateMessage());

        if (DateTime.Now < _delayedFrameworkUpdateCheck.AddSeconds(1)) return;

        var localPlayer = _clientState.LocalPlayer;

        if (localPlayer != null && !IsLoggedIn)
        {
            _logger.LogDebug("Logged in");
            IsLoggedIn = true;
            _mediator.Publish(new DalamudLoginMessage());
        }
        else if (localPlayer == null && IsLoggedIn)
        {
            _logger.LogDebug("Logged out");
            IsLoggedIn = false;
            _mediator.Publish(new DalamudLogoutMessage());
        }

        if (_clientState.LocalPlayer != null && _clientState.LocalPlayer.IsValid())
        {
            var newclassJobId = _clientState.LocalPlayer.ClassJob.Id;

            if (_classJobId != newclassJobId)
            {
                _classJobId = newclassJobId;
                _mediator.Publish(new ClassJobChangedMessage());
            }
        }

        _mediator.Publish(new DelayedFrameworkUpdateMessage());

        _delayedFrameworkUpdateCheck = DateTime.Now;
    }

    public Dalamud.Game.ClientState.Objects.Types.GameObject? CreateGameObject(IntPtr reference)
    {
        return _objectTable.CreateObjectReference(reference);
    }

    public unsafe GameObject* GposeTarget => TargetSystem.Instance()->GPoseTarget;

    public unsafe Dalamud.Game.ClientState.Objects.Types.GameObject? GposeTargetGameObject => GposeTarget == null ? null : _objectTable[GposeTarget->ObjectIndex];

    public bool IsLoggedIn { get; private set; }

    public bool IsPlayerPresent => _clientState.LocalPlayer != null && _clientState.LocalPlayer.IsValid();

    public static bool IsObjectPresent(Dalamud.Game.ClientState.Objects.Types.GameObject? obj)
    {
        return obj != null && obj.IsValid();
    }

    public unsafe IntPtr GetMinion(IntPtr? playerPointer = null)
    {
        playerPointer ??= PlayerPointer;
        return (IntPtr)((Character*)playerPointer)->CompanionObject;
    }

    public unsafe IntPtr GetPet(IntPtr? playerPointer = null)
    {
        var mgr = CharacterManager.Instance();
        playerPointer ??= PlayerPointer;
        return (IntPtr)mgr->LookupPetByOwnerObject((BattleChara*)playerPointer);
    }

    public unsafe IntPtr GetCompanion(IntPtr? playerPointer = null)
    {
        var mgr = CharacterManager.Instance();
        playerPointer ??= PlayerPointer;
        return (IntPtr)mgr->LookupBuddyByOwnerObject((BattleChara*)playerPointer);
    }

    public unsafe IntPtr GetMinionOrMount(IntPtr? playerPointer = null)
    {
        playerPointer ??= PlayerPointer;
        if (playerPointer == IntPtr.Zero) return IntPtr.Zero;
        return _objectTable.GetObjectAddress(((GameObject*)playerPointer)->ObjectIndex + 1);
    }

    public string PlayerName => _clientState.LocalPlayer?.Name.ToString() ?? "--";
    public uint WorldId => _clientState.LocalPlayer!.HomeWorld.Id;

    public IntPtr PlayerPointer => _clientState.LocalPlayer?.Address ?? IntPtr.Zero;

    public PlayerCharacter PlayerCharacter => _clientState.LocalPlayer!;

    public string PlayerNameHashed => (PlayerName + _clientState.LocalPlayer!.HomeWorld.Id).GetHash256();

    public List<PlayerCharacter> GetPlayerCharacters()
    {
        return _objectTable.Where(obj =>
            obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player &&
            !string.Equals(obj.Name.ToString(), PlayerName, StringComparison.Ordinal)).Cast<PlayerCharacter>().ToList();
    }

    public Dalamud.Game.ClientState.Objects.Types.Character? GetCharacterFromObjectTableByIndex(int index)
    {
        var objTableObj = _objectTable[index];
        if (objTableObj!.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) return null;
        return (Dalamud.Game.ClientState.Objects.Types.Character)objTableObj;
    }

    public PlayerCharacter? GetPlayerCharacterFromObjectTableByName(string characterName)
    {
        foreach (var item in _objectTable)
        {
            if (item.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
            if (string.Equals(item.Name.ToString(), characterName, StringComparison.Ordinal)) return (PlayerCharacter)item;
        }

        return null;
    }

    public int? GetIndexFromObjectTableByName(string characterName)
    {
        for (int i = 0; i < _objectTable.Length; i++)
        {
            if (_objectTable[i] == null) continue;
            if (_objectTable[i]!.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
            if (string.Equals(_objectTable[i]!.Name.ToString(), characterName, StringComparison.Ordinal)) return i;
        }

        return null;
    }

    public async Task RunOnFrameworkThread(Action act)
    {
        await _framework.RunOnFrameworkThread(act).ConfigureAwait(false);
    }

    public async Task<T> RunOnFrameworkThread<T>(Func<T> func)
    {
        return await _framework.RunOnFrameworkThread(func).ConfigureAwait(false);
    }

    public unsafe void WaitWhileCharacterIsDrawing(ILogger logger, GameObjectHandler handler, Guid redrawId, int timeOut = 5000, CancellationToken? ct = null)
    {
        if (!_clientState.IsLoggedIn || handler.Address == IntPtr.Zero) return;

        logger.LogTrace($"[{redrawId}] Starting wait for {handler} to draw");

        const int tick = 250;
        int curWaitTime = 0;
        try
        {
            // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
            while ((!ct?.IsCancellationRequested ?? true)
                   && curWaitTime < timeOut
                   && handler.IsBeingDrawn) // 0b100000000000 is "still rendering" or something
            {
                logger.LogTrace($"[{redrawId}] Waiting for {handler} to finish drawing");
                curWaitTime += tick;
                Thread.Sleep(tick);
            }

            logger.LogTrace($"[{redrawId}] Finished drawing after {curWaitTime}ms");
        }
        catch (NullReferenceException ex)
        {
            logger.LogWarning(ex, "Error accessing " + handler + ", object does not exist anymore?");
        }
        catch (AccessViolationException ex)
        {
            logger.LogWarning(ex, "Error accessing " + handler + ", object does not exist anymore?");
        }
    }

    public unsafe void WaitWhileGposeCharacterIsDrawing(IntPtr characterAddress, int timeOut = 5000)
    {
        Thread.Sleep(500);
        var obj = (GameObject*)characterAddress;
        const int tick = 250;
        int curWaitTime = 0;
        _logger.LogTrace("RenderFlags:" + obj->RenderFlags.ToString("X"));
        // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
        while (obj->RenderFlags != 0x00 && curWaitTime < timeOut)
        {
            _logger.LogTrace($"Waiting for gpose actor to finish drawing");
            curWaitTime += tick;
            Thread.Sleep(tick);
        }

        Thread.Sleep(tick * 2);
    }

    public void Dispose()
    {
        _logger.LogTrace($"Disposing {GetType()}");
        _framework.Update -= FrameworkOnUpdate;
    }
}
