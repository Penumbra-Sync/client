using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
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
    private readonly IDataManager _gameData;
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
    private readonly Dictionary<string, (string Name, nint Address)> _playerCharas = new(StringComparer.Ordinal);
    private readonly List<string> _notUpdatedCharas = [];
    private bool _sentBetweenAreas = false;

    public DalamudUtilService(ILogger<DalamudUtilService> logger, IClientState clientState, IObjectTable objectTable, IFramework framework,
        IGameGui gameGui, ICondition condition, IDataManager gameData, ITargetManager targetManager,
        MareMediator mediator, PerformanceCollectorService performanceCollector)
    {
        _logger = logger;
        _clientState = clientState;
        _objectTable = objectTable;
        _framework = framework;
        _gameGui = gameGui;
        _condition = condition;
        _gameData = gameData;
        Mediator = mediator;
        _performanceCollector = performanceCollector;
        WorldData = new(() =>
        {
            return gameData.GetExcelSheet<Lumina.Excel.GeneratedSheets.World>(Dalamud.Game.ClientLanguage.English)!
                .Where(w => !w.Name.RawData.IsEmpty && w.DataCenter.Row != 0 && (w.IsPublic || char.IsUpper((char)w.Name.RawData[0])))
                .ToDictionary(w => (ushort)w.RowId, w => w.Name.ToString());
        });
        mediator.Subscribe<TargetPairMessage>(this, (msg) =>
        {
            if (clientState.IsPvP) return;
            var name = msg.Pair.PlayerName;
            if (string.IsNullOrEmpty(name)) return;
            var addr = _playerCharas.FirstOrDefault(f => string.Equals(f.Value.Name, name, StringComparison.Ordinal)).Value.Address;
            if (addr == nint.Zero) return;
            _ = RunOnFrameworkThread(() =>
            {
                targetManager.Target = CreateGameObject(addr);
            }).ConfigureAwait(false);
        });
        IsWine = Util.IsWine();
    }

    public bool IsWine { get; init; }
    public unsafe GameObject* GposeTarget => TargetSystem.Instance()->GPoseTarget;
    public unsafe Dalamud.Game.ClientState.Objects.Types.IGameObject? GposeTargetGameObject => GposeTarget == null ? null : _objectTable[GposeTarget->ObjectIndex];
    public bool IsAnythingDrawing { get; private set; } = false;
    public bool IsInCutscene { get; private set; } = false;
    public bool IsInGpose { get; private set; } = false;
    public bool IsLoggedIn { get; private set; }
    public bool IsOnFrameworkThread => _framework.IsInFrameworkUpdateThread;
    public bool IsZoning => _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51];
    public bool IsInCombatOrPerforming { get; private set; } = false;
    public bool HasModifiedGameFiles => _gameData.HasModifiedGameDataFiles;

    public Lazy<Dictionary<ushort, string>> WorldData { get; private set; }

    public MareMediator Mediator { get; }

    public Dalamud.Game.ClientState.Objects.Types.IGameObject? CreateGameObject(IntPtr reference)
    {
        EnsureIsOnFramework();
        return _objectTable.CreateObjectReference(reference);
    }

    public async Task<Dalamud.Game.ClientState.Objects.Types.IGameObject?> CreateGameObjectAsync(IntPtr reference)
    {
        return await RunOnFrameworkThread(() => _objectTable.CreateObjectReference(reference)).ConfigureAwait(false);
    }

    public void EnsureIsOnFramework()
    {
        if (!_framework.IsInFrameworkUpdateThread) throw new InvalidOperationException("Can only be run on Framework");
    }

    public Dalamud.Game.ClientState.Objects.Types.ICharacter? GetCharacterFromObjectTableByIndex(int index)
    {
        EnsureIsOnFramework();
        var objTableObj = _objectTable[index];
        if (objTableObj!.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) return null;
        return (Dalamud.Game.ClientState.Objects.Types.ICharacter)objTableObj;
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

    public Dalamud.Game.ClientState.Objects.Types.ICharacter? GetGposeCharacterFromObjectTableByName(string name, bool onlyGposeCharacters = false)
    {
        EnsureIsOnFramework();
        return (Dalamud.Game.ClientState.Objects.Types.ICharacter?)_objectTable
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

    public IPlayerCharacter GetPlayerCharacter()
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
        return await RunOnFrameworkThread(() => GetHashedAccIdFromPlayerPointer(GetPlayerPointer())).ConfigureAwait(false);
    }

    private unsafe static string GetHashedAccIdFromPlayerPointer(nint ptr)
    {
        if (ptr == nint.Zero) return string.Empty;
        return ((BattleChara*)ptr)->Character.AccountId.ToString().GetHash256();
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

    public bool IsObjectPresent(Dalamud.Game.ClientState.Objects.Types.IGameObject? obj)
    {
        EnsureIsOnFramework();
        return obj != null && obj.IsValid();
    }

    public async Task<bool> IsObjectPresentAsync(Dalamud.Game.ClientState.Objects.Types.IGameObject? obj)
    {
        return await RunOnFrameworkThread(() => IsObjectPresent(obj)).ConfigureAwait(false);
    }

    public async Task RunOnFrameworkThread(Action act, [CallerMemberName] string callerMember = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
    {
        var fileName = Path.GetFileNameWithoutExtension(callerFilePath);
        await _performanceCollector.LogPerformance(this, $"RunOnFramework:Act/{fileName}>{callerMember}:{callerLineNumber}", async () =>
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
        return await _performanceCollector.LogPerformance(this, $"RunOnFramework:Func<{typeof(T)}>/{fileName}>{callerMember}:{callerLineNumber}", async () =>
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
        _logger.LogInformation("Starting DalamudUtilService");
        _framework.Update += FrameworkOnUpdate;
        if (IsLoggedIn)
        {
            _classJobId = _clientState.LocalPlayer!.ClassJob.Id;
        }

        _logger.LogInformation("Started DalamudUtilService");
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

    public Vector2 WorldToScreen(Dalamud.Game.ClientState.Objects.Types.IGameObject? obj)
    {
        if (obj == null) return Vector2.Zero;
        return _gameGui.WorldToScreen(obj.Position, out var screenPos) ? screenPos : Vector2.Zero;
    }

    internal (string Name, nint Address) FindPlayerByNameHash(string ident)
    {
        _playerCharas.TryGetValue(ident, out var result);
        return result;
    }

    private unsafe void CheckCharacterForDrawing(nint address, string characterName)
    {
        var gameObj = (GameObject*)address;
        var drawObj = gameObj->DrawObject;
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
                    if (isDrawing && !string.Equals(_lastGlobalBlockPlayer, characterName, StringComparison.Ordinal)
                        && !string.Equals(_lastGlobalBlockReason, "HasModelFilesInSlotLoaded", StringComparison.Ordinal))
                    {
                        _lastGlobalBlockPlayer = characterName;
                        _lastGlobalBlockReason = "HasModelFilesInSlotLoaded";
                        isDrawingChanged = true;
                    }
                }
                else
                {
                    if (!string.Equals(_lastGlobalBlockPlayer, characterName, StringComparison.Ordinal)
                        && !string.Equals(_lastGlobalBlockReason, "HasModelInSlotLoaded", StringComparison.Ordinal))
                    {
                        _lastGlobalBlockPlayer = characterName;
                        _lastGlobalBlockReason = "HasModelInSlotLoaded";
                        isDrawingChanged = true;
                    }
                }
            }
            else
            {
                if (!string.Equals(_lastGlobalBlockPlayer, characterName, StringComparison.Ordinal)
                    && !string.Equals(_lastGlobalBlockReason, "RenderFlags", StringComparison.Ordinal))
                {
                    _lastGlobalBlockPlayer = characterName;
                    _lastGlobalBlockReason = "RenderFlags";
                    isDrawingChanged = true;
                }
            }
        }

        if (isDrawingChanged)
        {
            _logger.LogTrace("Global draw block: START => {name} ({reason})", characterName, _lastGlobalBlockReason);
        }

        IsAnythingDrawing |= isDrawing;
    }

    private void FrameworkOnUpdate(IFramework framework)
    {
        _performanceCollector.LogPerformance(this, $"FrameworkOnUpdate", FrameworkOnUpdateInternal);
    }

    private unsafe void FrameworkOnUpdateInternal()
    {
        if (_clientState.LocalPlayer?.IsDead ?? false)
        {
            return;
        }

        bool isNormalFrameworkUpdate = DateTime.Now < _delayedFrameworkUpdateCheck.AddSeconds(1);

        _performanceCollector.LogPerformance(this, $"FrameworkOnUpdateInternal+{(isNormalFrameworkUpdate ? "Regular" : "Delayed")}", () =>
        {
            IsAnythingDrawing = false;
            _performanceCollector.LogPerformance(this, $"ObjTableToCharas",
                () =>
                {
                    _notUpdatedCharas.AddRange(_playerCharas.Keys);

                    for (int i = 0; i < 200; i += 2)
                    {
                        var chara = _objectTable[i];
                        if (chara == null || chara.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                            continue;

                        var charaName = ((GameObject*)chara.Address)->NameString;
                        var hash = GetHashedAccIdFromPlayerPointer(chara.Address);
                        if (!IsAnythingDrawing)
                            CheckCharacterForDrawing(chara.Address, charaName);
                        _notUpdatedCharas.Remove(hash);
                        _playerCharas[hash] = (charaName, chara.Address);
                    }

                    foreach (var notUpdatedChara in _notUpdatedCharas)
                    {
                        _playerCharas.Remove(notUpdatedChara);
                    }

                    _notUpdatedCharas.Clear();
                });

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

            if ((_condition[ConditionFlag.Performing] || _condition[ConditionFlag.InCombat]) && !IsInCombatOrPerforming)
            {
                _logger.LogDebug("Combat/Performance start");
                IsInCombatOrPerforming = true;
                Mediator.Publish(new CombatOrPerformanceStartMessage());
                Mediator.Publish(new HaltScanMessage(nameof(IsInCombatOrPerforming)));
            }
            else if ((!_condition[ConditionFlag.Performing] && !_condition[ConditionFlag.InCombat]) && IsInCombatOrPerforming)
            {
                _logger.LogDebug("Combat/Performance end");
                IsInCombatOrPerforming = false;
                Mediator.Publish(new CombatOrPerformanceEndMessage());
                Mediator.Publish(new ResumeScanMessage(nameof(IsInCombatOrPerforming)));
            }

            if (_condition[ConditionFlag.WatchingCutscene] && !IsInCutscene)
            {
                _logger.LogDebug("Cutscene start");
                IsInCutscene = true;
                Mediator.Publish(new CutsceneStartMessage());
                Mediator.Publish(new HaltScanMessage(nameof(IsInCutscene)));
            }
            else if (!_condition[ConditionFlag.WatchingCutscene] && IsInCutscene)
            {
                _logger.LogDebug("Cutscene end");
                IsInCutscene = false;
                Mediator.Publish(new CutsceneEndMessage());
                Mediator.Publish(new ResumeScanMessage(nameof(IsInCutscene)));
            }

            if (IsInCutscene)
            {
                Mediator.Publish(new CutsceneFrameworkUpdateMessage());
                return;
            }

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
                        Mediator.Publish(new HaltScanMessage(nameof(ConditionFlag.BetweenAreas)));
                    }
                }

                return;
            }

            if (_sentBetweenAreas)
            {
                _logger.LogDebug("Zone switch/Gpose end");
                _sentBetweenAreas = false;
                Mediator.Publish(new ZoneSwitchEndMessage());
                Mediator.Publish(new ResumeScanMessage(nameof(ConditionFlag.BetweenAreas)));
            }

            if (!IsInCombatOrPerforming)
                Mediator.Publish(new FrameworkUpdateMessage());

            Mediator.Publish(new PriorityFrameworkUpdateMessage());

            if (isNormalFrameworkUpdate)
                return;

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

            if (IsInCombatOrPerforming)
                Mediator.Publish(new FrameworkUpdateMessage());

            Mediator.Publish(new DelayedFrameworkUpdateMessage());

            _delayedFrameworkUpdateCheck = DateTime.Now;
        });
    }
}