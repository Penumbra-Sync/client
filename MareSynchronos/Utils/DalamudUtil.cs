using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using MareSynchronos.Mediator;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MareSynchronos.Utils;

public class DalamudUtil : IDisposable
{
    private readonly ClientState _clientState;
    private readonly ObjectTable _objectTable;
    private readonly Framework _framework;
    private readonly Condition _condition;
    private readonly MareMediator _mediator;

    private uint? _classJobId = 0;
    private DateTime _delayedFrameworkUpdateCheck = DateTime.Now;
    private bool _sentBetweenAreas = false;
    public bool IsInCutscene { get; private set; } = false;
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

    public DalamudUtil(ClientState clientState, ObjectTable objectTable, Framework framework,
        Condition condition, Dalamud.Data.DataManager gameData, MareMediator mediator)
    {
        _clientState = clientState;
        _objectTable = objectTable;
        _framework = framework;
        _condition = condition;
        _mediator = mediator;
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

    private unsafe void FrameworkOnUpdate(Framework framework)
    {
        if (GposeTarget != null && !IsInGpose)
        {
            Logger.Debug("Gpose start");
            IsInGpose = true;
            _mediator.Publish(new GposeStartMessage());
        }
        else if (GposeTarget == null && IsInGpose)
        {
            Logger.Debug("Gpose end");
            IsInGpose = false;
            _mediator.Publish(new GposeEndMessage());
        }

        if (_condition[ConditionFlag.WatchingCutscene] && !IsInCutscene)
        {
            Logger.Debug("Cutscene start");
            IsInCutscene = true;
            _mediator.Publish(new CutsceneStartMessage());
            _mediator.Publish(new HaltScanMessage("Cutscene"));

        }
        else if (!_condition[ConditionFlag.WatchingCutscene] && IsInCutscene)
        {
            Logger.Debug("Cutscene end");
            IsInCutscene = false;
            _mediator.Publish(new CutsceneEndMessage());
            _mediator.Publish(new ResumeScanMessage("Cutscene"));
        }

        if (IsInCutscene) { _mediator.Publish(new CutsceneFrameworkUpdateMessage()); return; }

        if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51])
        {
            if (!_sentBetweenAreas)
            {
                Logger.Debug("Zone switch/Gpose start");
                _sentBetweenAreas = true;
                _mediator.Publish(new ZoneSwitchStartMessage());
                _mediator.Publish(new HaltScanMessage("Zone switch"));
            }

            return;
        }

        if (_sentBetweenAreas)
        {
            Logger.Debug("Zone switch/Gpose end");
            _sentBetweenAreas = false;
            _mediator.Publish(new ZoneSwitchEndMessage());
            _mediator.Publish(new ResumeScanMessage("Zone switch"));
        }

        _mediator.Publish(new FrameworkUpdateMessage());

        if (DateTime.Now < _delayedFrameworkUpdateCheck.AddSeconds(1)) return;

        var localPlayer = _clientState.LocalPlayer;

        if (localPlayer != null && !IsLoggedIn)
        {
            Logger.Debug("Logged in");
            IsLoggedIn = true;
            _mediator.Publish(new DalamudLoginMessage());
        }
        else if (localPlayer == null && IsLoggedIn)
        {
            Logger.Debug("Logged out");
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

    public unsafe IntPtr GetMinion()
    {
        return (IntPtr)((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)PlayerPointer)->CompanionObject;
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

    public string PlayerName => _clientState.LocalPlayer?.Name.ToString() ?? "--";
    public uint WorldId => _clientState.LocalPlayer!.HomeWorld.Id;

    public IntPtr PlayerPointer => _clientState.LocalPlayer?.Address ?? IntPtr.Zero;

    public PlayerCharacter PlayerCharacter => _clientState.LocalPlayer!;

    public string PlayerNameHashed => Crypto.GetHash256(PlayerName + _clientState.LocalPlayer!.HomeWorld.Id);

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

    public async Task<T> RunOnFrameworkThread<T>(Func<T> func)
    {
        return await _framework.RunOnFrameworkThread(func).ConfigureAwait(false);
    }

    public unsafe void WaitWhileCharacterIsDrawing(string name, IntPtr characterAddress, int timeOut = 5000, CancellationToken? ct = null)
    {
        if (!_clientState.IsLoggedIn || characterAddress == IntPtr.Zero) return;

        Logger.Verbose($"Starting wait for {name} to draw");

        var obj = (GameObject*)characterAddress;
        const int tick = 250;
        int curWaitTime = 0;
        try
        {
            // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
            var stillDrawing = _framework.RunOnFrameworkThread(() => ((obj->GetDrawObject() == null
                        || ((CharacterBase*)obj->GetDrawObject())->HasModelInSlotLoaded != 0
                        || ((CharacterBase*)obj->GetDrawObject())->HasModelFilesInSlotLoaded != 0))
                    || ((obj->RenderFlags & 0b100000000000) == 0b100000000000)).Result;
            while ((!ct?.IsCancellationRequested ?? true)
                && curWaitTime < timeOut
                && stillDrawing) // 0b100000000000 is "still rendering" or something
            {
                Logger.Verbose($"Waiting for {name} to finish drawing");
                curWaitTime += tick;
                Thread.Sleep(tick);
                stillDrawing = _framework.RunOnFrameworkThread(() => ((obj->GetDrawObject() == null
                        || ((CharacterBase*)obj->GetDrawObject())->HasModelInSlotLoaded != 0
                        || ((CharacterBase*)obj->GetDrawObject())->HasModelFilesInSlotLoaded != 0))
                    || ((obj->RenderFlags & 0b100000000000) == 0b100000000000)).Result;
            }
        }
        catch (NullReferenceException ex)
        {
            Logger.Warn("Error accessing " + characterAddress.ToString("X") + ", object does not exist anymore?", ex);
        }
        catch (AccessViolationException ex)
        {
            Logger.Warn("Error accessing " + characterAddress.ToString("X") + ", object does not exist anymore?", ex);
        }
    }

    public unsafe void WaitWhileGposeCharacterIsDrawing(IntPtr characterAddress, int timeOut = 5000)
    {
        Thread.Sleep(500);
        var obj = (GameObject*)characterAddress;
        const int tick = 250;
        int curWaitTime = 0;
        Logger.Verbose("RenderFlags:" + obj->RenderFlags.ToString("X"));
        // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
        while (obj->RenderFlags != 0x00 && curWaitTime < timeOut)
        {
            Logger.Verbose($"Waiting for gpose actor to finish drawing");
            curWaitTime += tick;
            Thread.Sleep(tick);
        }

        Thread.Sleep(tick * 2);
    }

    public void Dispose()
    {
        Logger.Verbose($"Disposing {GetType()}");
        _framework.Update -= FrameworkOnUpdate;
    }
}
