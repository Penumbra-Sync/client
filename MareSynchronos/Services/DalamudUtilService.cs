using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Runtime.CompilerServices;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MareSynchronos.Services;

public class DalamudUtilService : IHostedService, IMediatorSubscriber
{
    private readonly List<uint> _classJobIdsIgnoredForPets = [30];
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IFramework _framework;
    private readonly IGameGui _gameGui;
    private readonly ILogger<DalamudUtilService> _logger;
    private readonly IObjectTable _objectTable;
    private readonly PerformanceCollectorService _performanceCollector;
    private uint? _classJobId = 0;
    private DateTime _delayedFrameworkUpdateCheck = DateTime.Now;
    private string _lastGlobalBlockPlayer = string.Empty;
    private string _lastGlobalBlockReason = string.Empty;
    private ushort _lastZone = 0;
    private Dictionary<string, (string Name, nint Address)> _playerCharas = new(StringComparer.Ordinal);
    private bool _sentBetweenAreas = false;

    public DalamudUtilService(ILogger<DalamudUtilService> logger, IClientState clientState, IObjectTable objectTable, IFramework framework,
        IGameGui gameGui, ICondition condition, IDataManager gameData, ITargetManager targetManager, MareMediator mediator, PerformanceCollectorService performanceCollector)
    {
        _logger = logger;
        _clientState = clientState;
        _objectTable = objectTable;
        _framework = framework;
        _gameGui = gameGui;
        _condition = condition;
        Mediator = mediator;
        _performanceCollector = performanceCollector;
        WorldData = new(() =>
        {
            return gameData.GetExcelSheet<Lumina.Excel.GeneratedSheets.World>(Dalamud.ClientLanguage.English)!
                .Where(w => w.IsPublic && !w.Name.RawData.IsEmpty)
                .ToDictionary(w => (ushort)w.RowId, w => w.Name.ToString());
        });
        mediator.Subscribe<TargetPairMessage>(this, async (msg) =>
        {
            if (clientState.IsPvP) return;
            var name = msg.Pair.PlayerName;
            if (string.IsNullOrEmpty(name)) return;
            var addr = _playerCharas.FirstOrDefault(f => string.Equals(f.Value.Name, name, StringComparison.Ordinal)).Value.Address;
            if (addr == nint.Zero) return;
            await RunOnFrameworkThread(() =>
            {
                targetManager.Target = CreateGameObject(addr);
            }).ConfigureAwait(false);
        });
    }

    public unsafe GameObject* GposeTarget => TargetSystem.Instance()->GPoseTarget;
    public unsafe Dalamud.Game.ClientState.Objects.Types.GameObject? GposeTargetGameObject => GposeTarget == null ? null : _objectTable[GposeTarget->ObjectIndex];
    public bool IsAnythingDrawing { get; private set; } = false;
    public bool IsInCutscene { get; private set; } = false;
    public bool IsInGpose { get; private set; } = false;
    public bool IsLoggedIn { get; private set; }
    public bool IsOnFrameworkThread => _framework.IsInFrameworkUpdateThread;
    public bool IsZoning => _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51];

    public Lazy<Dictionary<ushort, string>> WorldData { get; private set; }

    public MareMediator Mediator { get; }

    public Dalamud.Game.ClientState.Objects.Types.GameObject? CreateGameObject(IntPtr reference)
    {
        EnsureIsOnFramework();
        return _objectTable.CreateObjectReference(reference);
    }

    public async Task<Dalamud.Game.ClientState.Objects.Types.GameObject?> CreateGameObjectAsync(IntPtr reference)
    {
        return await RunOnFrameworkThread(() => _objectTable.CreateObjectReference(reference)).ConfigureAwait(false);
    }

    public void EnsureIsOnFramework()
    {
        if (!_framework.IsInFrameworkUpdateThread) throw new InvalidOperationException("Can only be run on Framework");
    }

    public Dalamud.Game.ClientState.Objects.Types.Character? GetCharacterFromObjectTableByIndex(int index)
    {
        EnsureIsOnFramework();
        var objTableObj = _objectTable[index];
        if (objTableObj!.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) return null;
        return (Dalamud.Game.ClientState.Objects.Types.Character)objTableObj;
    }

    public unsafe IntPtr GetCompanion(IntPtr? playerPointer = null)
    {
        EnsureIsOnFramework();
        var mgr = CharacterManager.Instance();
        playerPointer ??= GetPlayerPointer();
        if (playerPointer == IntPtr.Zero || (IntPtr)mgr == IntPtr.Zero) return IntPtr.Zero;
        return (IntPtr)mgr->LookupBuddyByOwnerObject((BattleChara*)playerPointer);
    }

    public async Task<IntPtr> GetCompanionAsync(IntPtr? playerPointer = null)
    {
        return await RunOnFrameworkThread(() => GetCompanion(playerPointer)).ConfigureAwait(false);
    }

    public Dalamud.Game.ClientState.Objects.Types.Character? GetGposeCharacterFromObjectTableByName(string name, bool onlyGposeCharacters = false)
    {
        EnsureIsOnFramework();
        return (Dalamud.Game.ClientState.Objects.Types.Character?)_objectTable
            .FirstOrDefault(i => (!onlyGposeCharacters || i.ObjectIndex >= 200) && string.Equals(i.Name.ToString(), name, StringComparison.Ordinal));
    }

    public bool GetIsPlayerPresent()
    {
        EnsureIsOnFramework();
        return _clientState.LocalPlayer != null && _clientState.LocalPlayer.IsValid();
    }

    public async Task<bool> GetIsPlayerPresentAsync()
    {
        return await RunOnFrameworkThread(GetIsPlayerPresent).ConfigureAwait(false);
    }

    public unsafe IntPtr GetMinionOrMount(IntPtr? playerPointer = null)
    {
        EnsureIsOnFramework();
        playerPointer ??= GetPlayerPointer();
        if (playerPointer == IntPtr.Zero) return IntPtr.Zero;
        return _objectTable.GetObjectAddress(((GameObject*)playerPointer)->ObjectIndex + 1);
    }

    public async Task<IntPtr> GetMinionOrMountAsync(IntPtr? playerPointer = null)
    {
        return await RunOnFrameworkThread(() => GetMinionOrMount(playerPointer)).ConfigureAwait(false);
    }

    public unsafe IntPtr GetPet(IntPtr? playerPointer = null)
    {
        EnsureIsOnFramework();
        if (_classJobIdsIgnoredForPets.Contains(_classJobId ?? 0)) return IntPtr.Zero;
        var mgr = CharacterManager.Instance();
        playerPointer ??= GetPlayerPointer();
        if (playerPointer == IntPtr.Zero || (IntPtr)mgr == IntPtr.Zero) return IntPtr.Zero;
        return (IntPtr)mgr->LookupPetByOwnerObject((BattleChara*)playerPointer);
    }

    public async Task<IntPtr> GetPetAsync(IntPtr? playerPointer = null)
    {
        return await RunOnFrameworkThread(() => GetPet(playerPointer)).ConfigureAwait(false);
    }

    public PlayerCharacter GetPlayerCharacter()
    {
        EnsureIsOnFramework();
        return _clientState.LocalPlayer!;
    }

    public IntPtr GetPlayerCharacterFromCachedTableByIdent(string characterName)
    {
        if (_playerCharas.TryGetValue(characterName, out var pchar)) return pchar.Address;
        return IntPtr.Zero;
    }

    public string GetPlayerName()
    {
        EnsureIsOnFramework();
        return _clientState.LocalPlayer?.Name.ToString() ?? "--";
    }

    public async Task<string> GetPlayerNameAsync()
    {
        return await RunOnFrameworkThread(GetPlayerName).ConfigureAwait(false);
    }

    public async Task<string> GetPlayerNameHashedAsync()
    {
        return await RunOnFrameworkThread(() => (GetPlayerName() + GetHomeWorldId()).GetHash256()).ConfigureAwait(false);
    }

    public IntPtr GetPlayerPointer()
    {
        EnsureIsOnFramework();
        return _clientState.LocalPlayer?.Address ?? IntPtr.Zero;
    }

    public async Task<IntPtr> GetPlayerPointerAsync()
    {
        return await RunOnFrameworkThread(GetPlayerPointer).ConfigureAwait(false);
    }

    public uint GetHomeWorldId()
    {
        EnsureIsOnFramework();
        return _clientState.LocalPlayer!.HomeWorld.Id;
    }

    public uint GetWorldId()
    {
        EnsureIsOnFramework();
        return _clientState.LocalPlayer!.CurrentWorld.Id;
    }

    public async Task<uint> GetWorldIdAsync()
    {
        return await RunOnFrameworkThread(GetWorldId).ConfigureAwait(false);
    }

    public async Task<uint> GetHomeWorldIdAsync()
    {
        return await RunOnFrameworkThread(GetHomeWorldId).ConfigureAwait(false);
    }

    public unsafe bool IsGameObjectPresent(IntPtr key)
    {
        return _objectTable.Any(f => f.Address == key);
    }

    public bool IsObjectPresent(Dalamud.Game.ClientState.Objects.Types.GameObject? obj)
    {
        EnsureIsOnFramework();
        return obj != null && obj.IsValid();
    }

    public async Task<bool> IsObjectPresentAsync(Dalamud.Game.ClientState.Objects.Types.GameObject? obj)
    {
        return await RunOnFrameworkThread(() => IsObjectPresent(obj)).ConfigureAwait(false);
    }

    public async Task RunOnFrameworkThread(Action act, [CallerMemberName] string callerMember = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
    {
        var fileName = Path.GetFileNameWithoutExtension(callerFilePath);
        await _performanceCollector.LogPerformance(this, "RunOnFramework:Act/" + fileName + ">" + callerMember + ":" + callerLineNumber, async () =>
        {
            if (!_framework.IsInFrameworkUpdateThread)
            {
                await _framework.RunOnFrameworkThread(act).ContinueWith((_) => Task.CompletedTask).ConfigureAwait(false);
                while (_framework.IsInFrameworkUpdateThread) // yield the thread again, should technically never be triggered
                {
                    _logger.LogTrace("Still on framework");
                    await Task.Delay(1).ConfigureAwait(false);
                }
            }
            else
                act();
        }).ConfigureAwait(false);
    }

    public async Task<T> RunOnFrameworkThread<T>(Func<T> func, [CallerMemberName] string callerMember = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
    {
        var fileName = Path.GetFileNameWithoutExtension(callerFilePath);
        return await _performanceCollector.LogPerformance(this, "RunOnFramework:Func<" + typeof(T) + ">/" + fileName + ">" + callerMember + ":" + callerLineNumber, async () =>
        {
            if (!_framework.IsInFrameworkUpdateThread)
            {
                var result = await _framework.RunOnFrameworkThread(func).ContinueWith((task) => task.Result).ConfigureAwait(false);
                while (_framework.IsInFrameworkUpdateThread) // yield the thread again, should technically never be triggered
                {
                    _logger.LogTrace("Still on framework");
                    await Task.Delay(1).ConfigureAwait(false);
                }
                return result;
            }

            return func.Invoke();
        }).ConfigureAwait(false);
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

        Mediator.UnsubscribeAll(this);
        _framework.Update -= FrameworkOnUpdate;
        return Task.CompletedTask;
    }

    public async Task WaitWhileCharacterIsDrawing(ILogger logger, GameObjectHandler handler, Guid redrawId, int timeOut = 5000, CancellationToken? ct = null)
    {
        if (!_clientState.IsLoggedIn) return;

        logger.LogTrace("[{redrawId}] Starting wait for {handler} to draw", redrawId, handler);

        const int tick = 250;
        int curWaitTime = 0;
        try
        {
            while ((!ct?.IsCancellationRequested ?? true)
                   && curWaitTime < timeOut
                   && await handler.IsBeingDrawnRunOnFrameworkAsync().ConfigureAwait(false)) // 0b100000000000 is "still rendering" or something
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

    internal (string Name, nint Address) FindPlayerByNameHash(string ident)
    {
        _playerCharas.TryGetValue(ident, out var result);
        return result;
    }

    private unsafe void CheckCharacterForDrawing(PlayerCharacter p)
    {
        if (!IsAnythingDrawing)
        {
            var gameObj = (GameObject*)p.Address;
            var drawObj = gameObj->DrawObject;
            var playerName = p.Name.ToString();
            bool isDrawing = false;
            bool isDrawingChanged = false;
            if ((nint)drawObj != IntPtr.Zero)
            {
                isDrawing = gameObj->RenderFlags == 0b100000000000;
                if (!isDrawing)
                {
                    isDrawing = ((CharacterBase*)drawObj)->HasModelInSlotLoaded != 0;
                    if (!isDrawing)
                    {
                        isDrawing = ((CharacterBase*)drawObj)->HasModelFilesInSlotLoaded != 0;
                        if (isDrawing && !string.Equals(_lastGlobalBlockPlayer, playerName, StringComparison.Ordinal)
                            && !string.Equals(_lastGlobalBlockReason, "HasModelFilesInSlotLoaded", StringComparison.Ordinal))
                        {
                            _lastGlobalBlockPlayer = playerName;
                            _lastGlobalBlockReason = "HasModelFilesInSlotLoaded";
                            isDrawingChanged = true;
                        }
                    }
                    else
                    {
                        if (!string.Equals(_lastGlobalBlockPlayer, playerName, StringComparison.Ordinal)
                            && !string.Equals(_lastGlobalBlockReason, "HasModelInSlotLoaded", StringComparison.Ordinal))
                        {
                            _lastGlobalBlockPlayer = playerName;
                            _lastGlobalBlockReason = "HasModelInSlotLoaded";
                            isDrawingChanged = true;
                        }
                    }
                }
                else
                {
                    if (!string.Equals(_lastGlobalBlockPlayer, playerName, StringComparison.Ordinal)
                        && !string.Equals(_lastGlobalBlockReason, "RenderFlags", StringComparison.Ordinal))
                    {
                        _lastGlobalBlockPlayer = playerName;
                        _lastGlobalBlockReason = "RenderFlags";
                        isDrawingChanged = true;
                    }
                }
            }

            if (isDrawingChanged)
            {
                _logger.LogTrace("Global draw block: START => {name} ({reason})", playerName, _lastGlobalBlockReason);
            }

            IsAnythingDrawing |= isDrawing;
        }
    }

    private void FrameworkOnUpdate(IFramework framework)
    {
        _performanceCollector.LogPerformance(this, "FrameworkOnUpdate", FrameworkOnUpdateInternal);
    }

    private unsafe void FrameworkOnUpdateInternal()
    {
        if (_clientState.LocalPlayer?.IsDead ?? false)
        {
            return;
        }

        IsAnythingDrawing = false;
        _playerCharas = _performanceCollector.LogPerformance(this, "ObjTableToCharas",
            () => _objectTable.OfType<PlayerCharacter>().Where(o => o.ObjectIndex < 200)
                .ToDictionary(p => p.GetHash256(), p =>
                {
                    CheckCharacterForDrawing(p);
                    return (p.Name.ToString(), p.Address);
                }, StringComparer.Ordinal));

        if (!IsAnythingDrawing && !string.IsNullOrEmpty(_lastGlobalBlockPlayer))
        {
            _logger.LogTrace("Global draw block: END => {name}", _lastGlobalBlockPlayer);
            _lastGlobalBlockPlayer = string.Empty;
            _lastGlobalBlockReason = string.Empty;
        }

        if (GposeTarget != null && !IsInGpose)
        {
            _logger.LogDebug("Gpose start");
            IsInGpose = true;
            Mediator.Publish(new GposeStartMessage());
        }
        else if (GposeTarget == null && IsInGpose)
        {
            _logger.LogDebug("Gpose end");
            IsInGpose = false;
            Mediator.Publish(new GposeEndMessage());
        }

        if (_condition[ConditionFlag.WatchingCutscene] && !IsInCutscene)
        {
            _logger.LogDebug("Cutscene start");
            IsInCutscene = true;
            Mediator.Publish(new CutsceneStartMessage());
            Mediator.Publish(new HaltScanMessage("Cutscene"));
        }
        else if (!_condition[ConditionFlag.WatchingCutscene] && IsInCutscene)
        {
            _logger.LogDebug("Cutscene end");
            IsInCutscene = false;
            Mediator.Publish(new CutsceneEndMessage());
            Mediator.Publish(new ResumeScanMessage("Cutscene"));
        }

        if (IsInCutscene) { Mediator.Publish(new CutsceneFrameworkUpdateMessage()); return; }

        if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51])
        {
            var zone = _clientState.TerritoryType;
            if (_lastZone != zone)
            {
                _lastZone = zone;
                if (!_sentBetweenAreas)
                {
                    _logger.LogDebug("Zone switch/Gpose start");
                    _sentBetweenAreas = true;
                    Mediator.Publish(new ZoneSwitchStartMessage());
                    Mediator.Publish(new HaltScanMessage("Zone switch"));
                }
            }

            return;
        }

        if (_sentBetweenAreas)
        {
            _logger.LogDebug("Zone switch/Gpose end");
            _sentBetweenAreas = false;
            Mediator.Publish(new ZoneSwitchEndMessage());
            Mediator.Publish(new ResumeScanMessage("Zone switch"));
        }

        Mediator.Publish(new FrameworkUpdateMessage());

        if (DateTime.Now < _delayedFrameworkUpdateCheck.AddSeconds(1)) return;

        var localPlayer = _clientState.LocalPlayer;

        if (localPlayer != null && !IsLoggedIn)
        {
            _logger.LogDebug("Logged in");
            IsLoggedIn = true;
            _lastZone = _clientState.TerritoryType;
            Mediator.Publish(new DalamudLoginMessage());
        }
        else if (localPlayer == null && IsLoggedIn)
        {
            _logger.LogDebug("Logged out");
            IsLoggedIn = false;
            Mediator.Publish(new DalamudLogoutMessage());
        }

        Mediator.Publish(new DelayedFrameworkUpdateMessage());

        _delayedFrameworkUpdateCheck = DateTime.Now;
    }
}