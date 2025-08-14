using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MareSynchronos.Services;

public class DalamudUtilService : IHostedService, IMediatorSubscriber
{
    private readonly List<uint> _classJobIdsIgnoredForPets = [30];
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IDataManager _gameData;
    private readonly IGameConfig _gameConfig;
    private readonly BlockedCharacterHandler _blockedCharacterHandler;
    private readonly IFramework _framework;
    private readonly IGameGui _gameGui;
    private readonly ILogger<DalamudUtilService> _logger;
    private readonly IObjectTable _objectTable;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly MareConfigService _configService;
    private uint? _classJobId = 0;
    private DateTime _delayedFrameworkUpdateCheck = DateTime.UtcNow;
    private string _lastGlobalBlockPlayer = string.Empty;
    private string _lastGlobalBlockReason = string.Empty;
    private ushort _lastZone = 0;
    private readonly Dictionary<string, (string Name, nint Address)> _playerCharas = new(StringComparer.Ordinal);
    private readonly List<string> _notUpdatedCharas = [];
    private bool _sentBetweenAreas = false;
    private Lazy<ulong> _cid;

    public DalamudUtilService(ILogger<DalamudUtilService> logger, IClientState clientState, IObjectTable objectTable, IFramework framework,
        IGameGui gameGui, ICondition condition, IDataManager gameData, ITargetManager targetManager, IGameConfig gameConfig,
        BlockedCharacterHandler blockedCharacterHandler, MareMediator mediator, PerformanceCollectorService performanceCollector,
        MareConfigService configService)
    {
        _logger = logger;
        _clientState = clientState;
        _objectTable = objectTable;
        _framework = framework;
        _gameGui = gameGui;
        _condition = condition;
        _gameData = gameData;
        _gameConfig = gameConfig;
        _blockedCharacterHandler = blockedCharacterHandler;
        Mediator = mediator;
        _performanceCollector = performanceCollector;
        _configService = configService;
        WorldData = new(() =>
        {
            return gameData.GetExcelSheet<Lumina.Excel.Sheets.World>(Dalamud.Game.ClientLanguage.English)!
                .Where(w => !w.Name.IsEmpty && w.DataCenter.RowId != 0 && (w.IsPublic || char.IsUpper(w.Name.ToString()[0])))
                .ToDictionary(w => (ushort)w.RowId, w => w.Name.ToString());
        });
        JobData = new(() =>
        {
            return gameData.GetExcelSheet<ClassJob>(Dalamud.Game.ClientLanguage.English)!
                .ToDictionary(k => k.RowId, k => k.NameEnglish.ToString());
        });
        TerritoryData = new(() =>
        {
            return gameData.GetExcelSheet<TerritoryType>(Dalamud.Game.ClientLanguage.English)!
            .Where(w => w.RowId != 0)
            .ToDictionary(w => w.RowId, w =>
            {
                StringBuilder sb = new();
                sb.Append(w.PlaceNameRegion.Value.Name);
                if (w.PlaceName.ValueNullable != null)
                {
                    sb.Append(" - ");
                    sb.Append(w.PlaceName.Value.Name);
                }
                return sb.ToString();
            });
        });
        MapData = new(() =>
        {
            return gameData.GetExcelSheet<Map>(Dalamud.Game.ClientLanguage.English)!
            .Where(w => w.RowId != 0)
            .ToDictionary(w => w.RowId, w =>
            {
                StringBuilder sb = new();
                sb.Append(w.PlaceNameRegion.Value.Name);
                if (w.PlaceName.ValueNullable != null)
                {
                    sb.Append(" - ");
                    sb.Append(w.PlaceName.Value.Name);
                }
                if (w.PlaceNameSub.ValueNullable != null && !string.IsNullOrEmpty(w.PlaceNameSub.Value.Name.ToString()))
                {
                    sb.Append(" - ");
                    sb.Append(w.PlaceNameSub.Value.Name);
                }
                return (w, sb.ToString());
            });
        });
        mediator.Subscribe<TargetPairMessage>(this, (msg) =>
        {
            if (clientState.IsPvP) return;
            var name = msg.Pair.PlayerName;
            if (string.IsNullOrEmpty(name)) return;
            var addr = _playerCharas.FirstOrDefault(f => string.Equals(f.Value.Name, name, StringComparison.Ordinal)).Value.Address;
            if (addr == nint.Zero) return;
            var useFocusTarget = _configService.Current.UseFocusTarget;
            _ = RunOnFrameworkThread(() =>
            {
                if (useFocusTarget)
                    targetManager.FocusTarget = CreateGameObject(addr);
                else
                    targetManager.Target = CreateGameObject(addr);
            }).ConfigureAwait(false);
        });
        IsWine = Util.IsWine();
        _cid = RebuildCID();
    }

    private Lazy<ulong> RebuildCID() =>  new(GetCID);

    public bool IsWine { get; init; }

    public unsafe GameObject* GposeTarget
    {
        get => TargetSystem.Instance()->GPoseTarget;
        set => TargetSystem.Instance()->GPoseTarget = value;
    }

    private unsafe bool HasGposeTarget => GposeTarget != null;
    private unsafe int GPoseTargetIdx => !HasGposeTarget ? -1 : GposeTarget->ObjectIndex;

    public async Task<IGameObject?> GetGposeTargetGameObjectAsync()
    {
        if (!HasGposeTarget)
            return null;

        return await _framework.RunOnFrameworkThread(() => _objectTable[GPoseTargetIdx]).ConfigureAwait(true);
    }
    public bool IsAnythingDrawing { get; private set; } = false;
    public bool IsInCutscene { get; private set; } = false;
    public bool IsInGpose { get; private set; } = false;
    public bool IsLoggedIn { get; private set; }
    public bool IsOnFrameworkThread => _framework.IsInFrameworkUpdateThread;
    public bool IsZoning => _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51];
    public bool IsInCombatOrPerforming { get; private set; } = false;
    public bool HasModifiedGameFiles => _gameData.HasModifiedGameDataFiles;
    public uint ClassJobId => _classJobId!.Value;
    public Lazy<Dictionary<uint, string>> JobData { get; private set; }
    public Lazy<Dictionary<ushort, string>> WorldData { get; private set; }
    public Lazy<Dictionary<uint, string>> TerritoryData { get; private set; }
    public Lazy<Dictionary<uint, (Map Map, string MapName)>> MapData { get; private set; }
    public bool IsLodEnabled { get; private set; }
    public MareMediator Mediator { get; }

    public IGameObject? CreateGameObject(IntPtr reference)
    {
        EnsureIsOnFramework();
        return _objectTable.CreateObjectReference(reference);
    }

    public async Task<IGameObject?> CreateGameObjectAsync(IntPtr reference)
    {
        return await RunOnFrameworkThread(() => _objectTable.CreateObjectReference(reference)).ConfigureAwait(false);
    }

    public void EnsureIsOnFramework()
    {
        if (!_framework.IsInFrameworkUpdateThread) throw new InvalidOperationException("Can only be run on Framework");
    }

    public ICharacter? GetCharacterFromObjectTableByIndex(int index)
    {
        EnsureIsOnFramework();
        var objTableObj = _objectTable[index];
        if (objTableObj!.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) return null;
        return (ICharacter)objTableObj;
    }

    public unsafe IntPtr GetCompanionPtr(IntPtr? playerPointer = null)
    {
        EnsureIsOnFramework();
        var mgr = CharacterManager.Instance();
        playerPointer ??= GetPlayerPtr();
        if (playerPointer == IntPtr.Zero || (IntPtr)mgr == IntPtr.Zero) return IntPtr.Zero;
        return (IntPtr)mgr->LookupBuddyByOwnerObject((BattleChara*)playerPointer);
    }

    public async Task<IntPtr> GetCompanionAsync(IntPtr? playerPointer = null)
    {
        return await RunOnFrameworkThread(() => GetCompanionPtr(playerPointer)).ConfigureAwait(false);
    }

    public async Task<ICharacter?> GetGposeCharacterFromObjectTableByNameAsync(string name, bool onlyGposeCharacters = false)
    {
        return await RunOnFrameworkThread(() => GetGposeCharacterFromObjectTableByName(name, onlyGposeCharacters)).ConfigureAwait(false);
    }

    public ICharacter? GetGposeCharacterFromObjectTableByName(string name, bool onlyGposeCharacters = false)
    {
        EnsureIsOnFramework();
        return (ICharacter?)_objectTable
            .FirstOrDefault(i => (!onlyGposeCharacters || i.ObjectIndex >= 200) && string.Equals(i.Name.ToString(), name, StringComparison.Ordinal));
    }

    public IEnumerable<ICharacter?> GetGposeCharactersFromObjectTable()
    {
        return _objectTable.Where(o => o.ObjectIndex > 200 && o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player).Cast<ICharacter>();
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

    public unsafe IntPtr GetMinionOrMountPtr(IntPtr? playerPointer = null)
    {
        EnsureIsOnFramework();
        playerPointer ??= GetPlayerPtr();
        if (playerPointer == IntPtr.Zero) return IntPtr.Zero;
        return _objectTable.GetObjectAddress(((GameObject*)playerPointer)->ObjectIndex + 1);
    }

    public async Task<IntPtr> GetMinionOrMountAsync(IntPtr? playerPointer = null)
    {
        return await RunOnFrameworkThread(() => GetMinionOrMountPtr(playerPointer)).ConfigureAwait(false);
    }

    public unsafe IntPtr GetPetPtr(IntPtr? playerPointer = null)
    {
        EnsureIsOnFramework();
        if (_classJobIdsIgnoredForPets.Contains(_classJobId ?? 0)) return IntPtr.Zero;
        var mgr = CharacterManager.Instance();
        playerPointer ??= GetPlayerPtr();
        if (playerPointer == IntPtr.Zero || (IntPtr)mgr == IntPtr.Zero) return IntPtr.Zero;
        return (IntPtr)mgr->LookupPetByOwnerObject((BattleChara*)playerPointer);
    }

    public async Task<IntPtr> GetPetAsync(IntPtr? playerPointer = null)
    {
        return await RunOnFrameworkThread(() => GetPetPtr(playerPointer)).ConfigureAwait(false);
    }

    public async Task<IPlayerCharacter> GetPlayerCharacterAsync()
    {
        return await RunOnFrameworkThread(GetPlayerCharacter).ConfigureAwait(false);
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

    public async Task<ulong> GetCIDAsync()
    {
        return await RunOnFrameworkThread(GetCID).ConfigureAwait(false);
    }

    public unsafe ulong GetCID()
    {
        EnsureIsOnFramework();
        var playerChar = GetPlayerCharacter();
        return ((BattleChara*)playerChar.Address)->Character.ContentId;
    }

    public async Task<string> GetPlayerNameHashedAsync()
    {
        return await RunOnFrameworkThread(() => _cid.Value.ToString().GetHash256()).ConfigureAwait(false);
    }

    private unsafe static string GetHashedCIDFromPlayerPointer(nint ptr)
    {
        return ((BattleChara*)ptr)->Character.ContentId.ToString().GetHash256();
    }

    public IntPtr GetPlayerPtr()
    {
        EnsureIsOnFramework();
        return _clientState.LocalPlayer?.Address ?? IntPtr.Zero;
    }

    public async Task<IntPtr> GetPlayerPointerAsync()
    {
        return await RunOnFrameworkThread(GetPlayerPtr).ConfigureAwait(false);
    }

    public uint GetHomeWorldId()
    {
        EnsureIsOnFramework();
        return _clientState.LocalPlayer?.HomeWorld.RowId ?? 0;
    }

    public uint GetWorldId()
    {
        EnsureIsOnFramework();
        return _clientState.LocalPlayer!.CurrentWorld.RowId;
    }

    public unsafe LocationInfo GetMapData()
    {
        EnsureIsOnFramework();
        var agentMap = AgentMap.Instance();
        var houseMan = HousingManager.Instance();
        uint serverId = 0;
        if (_clientState.LocalPlayer == null) serverId = 0;
        else serverId = _clientState.LocalPlayer.CurrentWorld.RowId;
        uint mapId = agentMap == null ? 0 : agentMap->CurrentMapId;
        uint territoryId = agentMap == null ? 0 : agentMap->CurrentTerritoryId;
        uint divisionId = houseMan == null ? 0 : (uint)(houseMan->GetCurrentDivision());
        uint wardId = houseMan == null ? 0 : (uint)(houseMan->GetCurrentWard() + 1);
        uint houseId = 0;
        var tempHouseId = houseMan == null ? 0 : (houseMan->GetCurrentPlot());
        if (!houseMan->IsInside()) tempHouseId = 0;
        if (tempHouseId < -1)
        {
            divisionId = tempHouseId == -127 ? 2 : (uint)1;
            tempHouseId = 100;
        }
        if (tempHouseId == -1) tempHouseId = 0;
        houseId = (uint)tempHouseId;
        if (houseId != 0)
        {
            territoryId = HousingManager.GetOriginalHouseTerritoryTypeId();
        }
        uint roomId = houseMan == null ? 0 : (uint)(houseMan->GetCurrentRoom());

        return new LocationInfo()
        {
            ServerId = serverId,
            MapId = mapId,
            TerritoryId = territoryId,
            DivisionId = divisionId,
            WardId = wardId,
            HouseId = houseId,
            RoomId = roomId
        };
    }

    public unsafe void SetMarkerAndOpenMap(Vector3 position, Map map)
    {
        EnsureIsOnFramework();
        var agentMap = AgentMap.Instance();
        if (agentMap == null) return;
        agentMap->OpenMapByMapId(map.RowId);
        agentMap->SetFlagMapMarker(map.TerritoryType.RowId, map.RowId, position);
    }

    public async Task<LocationInfo> GetMapDataAsync()
    {
        return await RunOnFrameworkThread(GetMapData).ConfigureAwait(false);
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

    public bool IsObjectPresent(IGameObject? obj)
    {
        EnsureIsOnFramework();
        return obj != null && obj.IsValid();
    }

    public async Task<bool> IsObjectPresentAsync(IGameObject? obj)
    {
        return await RunOnFrameworkThread(() => IsObjectPresent(obj)).ConfigureAwait(false);
    }

    public async Task RunOnFrameworkThread(System.Action act, [CallerMemberName] string callerMember = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
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
            _classJobId = _clientState.LocalPlayer!.ClassJob.RowId;
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

        if (ct == null)
            ct = CancellationToken.None;

        const int tick = 250;
        int curWaitTime = 0;
        try
        {
            logger.LogTrace("[{redrawId}] Starting wait for {handler} to draw", redrawId, handler);
            await Task.Delay(tick, ct.Value).ConfigureAwait(true);
            curWaitTime += tick;

            while ((!ct.Value.IsCancellationRequested)
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

    public Vector2 WorldToScreen(IGameObject? obj)
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
        if ((_clientState.LocalPlayer?.IsDead ?? false) && _condition[ConditionFlag.BoundByDuty])
        {
            return;
        }

        bool isNormalFrameworkUpdate = DateTime.UtcNow < _delayedFrameworkUpdateCheck.AddSeconds(1);

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

                        if (_blockedCharacterHandler.IsCharacterBlocked(chara.Address, out bool firstTime) && firstTime)
                        {
                            _logger.LogTrace("Skipping character {addr}, blocked/muted", chara.Address.ToString("X"));
                            continue;
                        }

                        var charaName = ((GameObject*)chara.Address)->NameString;
                        var hash = GetHashedCIDFromPlayerPointer(chara.Address);
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

            if (_clientState.IsGPosing && !IsInGpose)
            {
                _logger.LogDebug("Gpose start");
                IsInGpose = true;
                Mediator.Publish(new GposeStartMessage());
            }
            else if (!_clientState.IsGPosing && IsInGpose)
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
                        _logger.LogDebug("Zone switch start");
                        _sentBetweenAreas = true;
                        Mediator.Publish(new ZoneSwitchStartMessage());
                        Mediator.Publish(new HaltScanMessage(nameof(ConditionFlag.BetweenAreas)));
                    }
                }

                return;
            }

            if (_sentBetweenAreas)
            {
                _logger.LogDebug("Zone switch end");
                _sentBetweenAreas = false;
                Mediator.Publish(new ZoneSwitchEndMessage());
                Mediator.Publish(new ResumeScanMessage(nameof(ConditionFlag.BetweenAreas)));
            }

            var localPlayer = _clientState.LocalPlayer;
            if (localPlayer != null)
            {
                _classJobId = localPlayer.ClassJob.RowId;
            }

            if (!IsInCombatOrPerforming)
                Mediator.Publish(new FrameworkUpdateMessage());

            Mediator.Publish(new PriorityFrameworkUpdateMessage());

            if (isNormalFrameworkUpdate)
                return;

            if (localPlayer != null && !IsLoggedIn)
            {
                _logger.LogDebug("Logged in");
                IsLoggedIn = true;
                _lastZone = _clientState.TerritoryType;
                _cid = RebuildCID();
                Mediator.Publish(new DalamudLoginMessage());
            }
            else if (localPlayer == null && IsLoggedIn)
            {
                _logger.LogDebug("Logged out");
                IsLoggedIn = false;
                Mediator.Publish(new DalamudLogoutMessage());
            }

            if (_gameConfig != null
                && _gameConfig.TryGet(Dalamud.Game.Config.SystemConfigOption.LodType_DX11, out bool lodEnabled))
            {
                IsLodEnabled = lodEnabled;
            }

            if (IsInCombatOrPerforming)
                Mediator.Publish(new FrameworkUpdateMessage());

            Mediator.Publish(new DelayedFrameworkUpdateMessage());

            _delayedFrameworkUpdateCheck = DateTime.UtcNow;
        });
    }
}