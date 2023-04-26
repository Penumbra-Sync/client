using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Runtime.CompilerServices;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MareSynchronos.Services;

public class DalamudUtilService : IHostedService
{
    private readonly ClientState _clientState;
    private readonly Condition _condition;
    private readonly Framework _framework;
    private readonly GameGui _gameGui;
    private readonly ILogger<DalamudUtilService> _logger;
    private readonly MareMediator _mediator;
    private readonly ObjectTable _objectTable;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly List<uint> ClassJobIdsIgnoredForPets = new() { 30 };
    private uint? _classJobId = 0;
    private DateTime _delayedFrameworkUpdateCheck = DateTime.Now;
    private bool _sentBetweenAreas = false;

    public DalamudUtilService(ILogger<DalamudUtilService> logger, ClientState clientState, ObjectTable objectTable, Framework framework,
        GameGui gameGui, Condition condition, Dalamud.Data.DataManager gameData, MareMediator mediator, PerformanceCollectorService performanceCollector)
    {
        _logger = logger;
        _clientState = clientState;
        _objectTable = objectTable;
        _framework = framework;
        _gameGui = gameGui;
        _condition = condition;
        _mediator = mediator;
        _performanceCollector = performanceCollector;
        WorldData = new(() =>
        {
            return gameData.GetExcelSheet<Lumina.Excel.GeneratedSheets.World>(Dalamud.ClientLanguage.English)!
                .Where(w => w.IsPublic && !w.Name.RawData.IsEmpty)
                .ToDictionary(w => (ushort)w.RowId, w => w.Name.ToString());
        });
    }

    public unsafe GameObject* GposeTarget => TargetSystem.Instance()->GPoseTarget;
    public unsafe Dalamud.Game.ClientState.Objects.Types.GameObject? GposeTargetGameObject => GposeTarget == null ? null : _objectTable[GposeTarget->ObjectIndex];
    public bool IsInCutscene { get; private set; } = false;
    public bool IsInFrameworkThread => _framework.IsInFrameworkUpdateThread;
    public bool IsInGpose { get; private set; } = false;
    public bool IsLoggedIn { get; private set; }
    public bool IsPlayerPresent => _clientState.LocalPlayer != null && _clientState.LocalPlayer.IsValid();
    public bool IsZoning => _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51];
    public PlayerCharacter PlayerCharacter => _clientState.LocalPlayer!;

    public string PlayerName => _clientState.LocalPlayer?.Name.ToString() ?? "--";

    public string PlayerNameHashed => (PlayerName + _clientState.LocalPlayer!.HomeWorld.Id).GetHash256();

    public IntPtr PlayerPointer => _clientState.LocalPlayer?.Address ?? IntPtr.Zero;

    public Lazy<Dictionary<ushort, string>> WorldData { get; private set; }

    public uint WorldId => _clientState.LocalPlayer!.HomeWorld.Id;

    public static bool IsObjectPresent(Dalamud.Game.ClientState.Objects.Types.GameObject? obj)
    {
        return obj != null && obj.IsValid();
    }

    public Dalamud.Game.ClientState.Objects.Types.GameObject? CreateGameObject(IntPtr reference)
    {
        return _objectTable.CreateObjectReference(reference);
    }

    public Dalamud.Game.ClientState.Objects.Types.Character? GetCharacterFromObjectTableByIndex(int index)
    {
        var objTableObj = _objectTable[index];
        if (objTableObj!.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) return null;
        return (Dalamud.Game.ClientState.Objects.Types.Character)objTableObj;
    }

    public async Task<IntPtr> GetCompanion(IntPtr? playerPointer = null)
    {
        return await RunOnFrameworkThread(() => GetCompanionInternal(playerPointer)).ConfigureAwait(false);
    }

    public unsafe IntPtr GetMinion(IntPtr? playerPointer = null)
    {
        playerPointer ??= PlayerPointer;
        return (IntPtr)((Character*)playerPointer)->CompanionObject;
    }

    public unsafe IntPtr GetMinionOrMount(IntPtr? playerPointer = null)
    {
        playerPointer ??= PlayerPointer;
        if (playerPointer == IntPtr.Zero) return IntPtr.Zero;
        return _objectTable.GetObjectAddress(((GameObject*)playerPointer)->ObjectIndex + 1);
    }

    public async Task<IntPtr> GetPet(IntPtr? playerPointer = null)
    {
        return await RunOnFrameworkThread(() => GetPetInternal(playerPointer)).ConfigureAwait(false);
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

    public List<PlayerCharacter> GetPlayerCharacters()
    {
        return _objectTable.Where(obj =>
            obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player &&
            !string.Equals(obj.Name.ToString(), PlayerName, StringComparison.Ordinal)).Cast<PlayerCharacter>().ToList();
    }

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

    public async Task RunOnFrameworkThread(Action act, [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
    {
        if (!_framework.IsInFrameworkUpdateThread)
        {
            _logger.LogTrace("Running Action on framework thread (FrameworkContext: {ctx}): {member} in {file}:{line}", _framework.IsInFrameworkUpdateThread, callerMember, callerFilePath, lineNumber);

            await _framework.RunOnFrameworkThread(act).ContinueWith((_) => Task.CompletedTask).ConfigureAwait(false);
            while (_framework.IsInFrameworkUpdateThread) // yield the thread again, should technically never be triggered
            {
                _logger.LogTrace("Still on framework");
                await Task.Delay(1).ConfigureAwait(false);
            }
        }
        else
            act();
    }

    public async Task<T> RunOnFrameworkThread<T>(Func<T> func, [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int lineNumber = 0)
    {
        if (!_framework.IsInFrameworkUpdateThread)
        {
            _logger.LogTrace("Running Func on framework thread (FrameworkContext: {ctx}): {member} in {file}:{line}", _framework.IsInFrameworkUpdateThread, callerMember, callerFilePath, lineNumber);

            var result = await _framework.RunOnFrameworkThread(func).ContinueWith((task) => task.Result).ConfigureAwait(false);
            while (_framework.IsInFrameworkUpdateThread) // yield the thread again, should technically never be triggered
            {
                _logger.LogTrace("Still on framework");
                await Task.Delay(1).ConfigureAwait(false);
            }
            return result;
        }
        else
            return func.Invoke();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _framework.Update += FrameworkOnUpdate;
        if (IsLoggedIn)
        {
            _classJobId = _clientState.LocalPlayer!.ClassJob.Id;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogTrace("Stopping {type}", GetType());

        _framework.Update -= FrameworkOnUpdate;
        return Task.CompletedTask;
    }

    public async Task WaitWhileCharacterIsDrawing(ILogger logger, GameObjectHandler handler, Guid redrawId, int timeOut = 5000, CancellationToken? ct = null)
    {
        if (!_clientState.IsLoggedIn || handler.CurrentAddress == IntPtr.Zero) return;

        logger.LogTrace("[{redrawId}] Starting wait for {handler} to draw", redrawId, handler);

        const int tick = 250;
        int curWaitTime = 0;
        try
        {
            while ((!ct?.IsCancellationRequested ?? true)
                   && curWaitTime < timeOut
                   && await handler.IsBeingDrawnRunOnFramework().ConfigureAwait(true)) // 0b100000000000 is "still rendering" or something
            {
                logger.LogTrace("[{redrawId}] Waiting for {handler} to finish drawing", redrawId, handler);
                curWaitTime += tick;
                await Task.Delay(tick).ConfigureAwait(true);
            }

            logger.LogTrace("[{redrawId}] Finished drawing after {curWaitTime}ms", redrawId, curWaitTime);
        }
        catch (NullReferenceException ex)
        {
            logger.LogWarning(ex, "Error accessing {handler}, object does not exist anymore?", handler);
        }
        catch (AccessViolationException ex)
        {
            logger.LogWarning(ex, "Error accessing {handler}, object does not exist anymore?", handler);
        }
    }

    public unsafe void WaitWhileGposeCharacterIsDrawing(IntPtr characterAddress, int timeOut = 5000)
    {
        Thread.Sleep(500);
        var obj = (GameObject*)characterAddress;
        const int tick = 250;
        int curWaitTime = 0;
        _logger.LogTrace("RenderFlags: {flags}", obj->RenderFlags.ToString("X"));
        // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
        while (obj->RenderFlags != 0x00 && curWaitTime < timeOut)
        {
            _logger.LogTrace($"Waiting for gpose actor to finish drawing");
            curWaitTime += tick;
            Thread.Sleep(tick);
        }

        Thread.Sleep(tick * 2);
    }

    public Vector2 WorldToScreen(Dalamud.Game.ClientState.Objects.Types.GameObject? obj)
    {
        if (obj == null) return Vector2.Zero;
        return _gameGui.WorldToScreen(obj.Position, out var screenPos) ? screenPos : Vector2.Zero;
    }

    private void FrameworkOnUpdate(Framework framework)
    {
        _performanceCollector.LogPerformance(this, "FrameworkOnUpdate", FrameworkOnUpdateInternal);
    }

    private unsafe void FrameworkOnUpdateInternal()
    {
        if (_clientState.LocalPlayer?.IsDead ?? false) return;
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
                _mediator.Publish(new ClassJobChangedMessage(_classJobId));
            }
        }

        _mediator.Publish(new DelayedFrameworkUpdateMessage());

        _delayedFrameworkUpdateCheck = DateTime.Now;
    }

    private unsafe IntPtr GetCompanionInternal(IntPtr? playerPointer = null)
    {
        var mgr = CharacterManager.Instance();
        playerPointer ??= PlayerPointer;
        if (playerPointer == IntPtr.Zero || (IntPtr)mgr == IntPtr.Zero) return IntPtr.Zero;
        return (IntPtr)mgr->LookupBuddyByOwnerObject((BattleChara*)playerPointer);
    }

    private unsafe IntPtr GetPetInternal(IntPtr? playerPointer = null)
    {
        if (ClassJobIdsIgnoredForPets.Contains(_classJobId ?? 0)) return IntPtr.Zero;
        var mgr = CharacterManager.Instance();
        playerPointer ??= PlayerPointer;
        if (playerPointer == IntPtr.Zero || (IntPtr)mgr == IntPtr.Zero) return IntPtr.Zero;
        return (IntPtr)mgr->LookupPetByOwnerObject((BattleChara*)playerPointer);
    }
}