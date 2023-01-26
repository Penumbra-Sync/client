using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;


namespace MareSynchronos.Utils;

public delegate void PlayerChange(Dalamud.Game.ClientState.Objects.Types.Character actor);

public delegate void VoidDelegate();

public class DalamudUtil : IDisposable
{
    private readonly ClientState _clientState;
    private readonly ObjectTable _objectTable;
    private readonly Framework _framework;
    private readonly Condition _condition;
    private readonly ChatGui _chatGui;

    public event VoidDelegate? LogIn;
    public event VoidDelegate? LogOut;
    public event VoidDelegate? FrameworkUpdate;
    public event VoidDelegate? ClassJobChanged;
    private uint? classJobId = 0;
    public event VoidDelegate? DelayedFrameworkUpdate;
    public event VoidDelegate? ZoneSwitchStart;
    public event VoidDelegate? ZoneSwitchEnd;
    public event VoidDelegate? GposeStart;
    public event VoidDelegate? GposeEnd;
    public event VoidDelegate? GposeFrameworkUpdate;
    private DateTime _delayedFrameworkUpdateCheck = DateTime.Now;
    private bool _sentBetweenAreas = false;
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

    public DalamudUtil(ClientState clientState, ObjectTable objectTable, Framework framework, Condition condition, ChatGui chatGui)
    {
        _clientState = clientState;
        _objectTable = objectTable;
        _framework = framework;
        _condition = condition;
        _chatGui = chatGui;
        _framework.Update += FrameworkOnUpdate;
        if (IsLoggedIn)
        {
            classJobId = _clientState.LocalPlayer!.ClassJob.Id;
            ClientStateOnLogin(null, EventArgs.Empty);
        }
    }

    public void PrintInfoChat(string message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[Mare Synchronos] Info: ").AddItalics(message);
        _chatGui.Print(se.BuiltString);
    }

    public void PrintWarnChat(string message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[Mare Synchronos] ").AddUiForeground("Warning: " + message, 31).AddUiForegroundOff();
        _chatGui.Print(se.BuiltString);
    }

    public void PrintErrorChat(string message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[Mare Synchronos] ").AddUiForeground("Error: ", 534).AddItalicsOn().AddUiForeground(message, 534).AddUiForegroundOff().AddItalicsOff();
        _chatGui.Print(se.BuiltString);
    }

    private unsafe void FrameworkOnUpdate(Framework framework)
    {
        if (GposeTarget != null && !IsInGpose)
        {
            Logger.Debug("Gpose start");
            IsInGpose = true;
            GposeStart?.Invoke();
        }
        else if (GposeTarget == null && IsInGpose)
        {
            Logger.Debug("Gpose end");
            IsInGpose = false;
            GposeEnd?.Invoke();
        }

        if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51] || IsInGpose)
        {
            if (!_sentBetweenAreas)
            {
                Logger.Debug("Zone switch/Gpose start");
                _sentBetweenAreas = true;
                ZoneSwitchStart?.Invoke();
            }

            if (IsInGpose) GposeFrameworkUpdate?.Invoke();

            return;
        }
        else if (_sentBetweenAreas)
        {
            Logger.Debug("Zone switch/Gpose end");
            _sentBetweenAreas = false;
            ZoneSwitchEnd?.Invoke();
        }

        foreach (VoidDelegate? frameworkInvocation in (FrameworkUpdate?.GetInvocationList() ?? Array.Empty<VoidDelegate>()).Cast<VoidDelegate>())
        {
            try
            {
                frameworkInvocation?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex.Message);
                Logger.Warn(ex.StackTrace ?? string.Empty);
            }
        }

        if (DateTime.Now < _delayedFrameworkUpdateCheck.AddSeconds(1)) return;

        var localPlayer = _clientState.LocalPlayer;

        if (localPlayer != null && !IsLoggedIn)
        {
            Logger.Debug("Logged in");
            IsLoggedIn = true;
            LogIn?.Invoke();
        }
        else if (localPlayer == null && IsLoggedIn)
        {
            Logger.Debug("Logged out");
            IsLoggedIn = false;
            LogOut?.Invoke();
        }

        if (_clientState.LocalPlayer != null && _clientState.LocalPlayer.IsValid())
        {
            var newclassJobId = _clientState.LocalPlayer.ClassJob.Id;

            if (classJobId != newclassJobId)
            {
                classJobId = newclassJobId;
                ClassJobChanged?.Invoke();
            }
        }

        foreach (VoidDelegate? frameworkInvocation in (DelayedFrameworkUpdate?.GetInvocationList() ?? Array.Empty<VoidDelegate>()).Cast<VoidDelegate>())
        {
            try
            {
                frameworkInvocation?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex.Message);
                Logger.Warn(ex.StackTrace ?? string.Empty);
            }
        }
        _delayedFrameworkUpdateCheck = DateTime.Now;
    }

    private void ClientStateOnLogout(object? sender, EventArgs e)
    {
        LogOut?.Invoke();
    }

    private void ClientStateOnLogin(object? sender, EventArgs e)
    {
        LogIn?.Invoke();
    }

    public Dalamud.Game.ClientState.Objects.Types.GameObject? CreateGameObject(IntPtr reference)
    {
        return _objectTable.CreateObjectReference(reference);
    }

    public unsafe GameObject* GposeTarget => TargetSystem.Instance()->GPoseTarget;

    public unsafe Dalamud.Game.ClientState.Objects.Types.GameObject? GposeTargetGameObject => GposeTarget == null ? null : _objectTable[GposeTarget->ObjectIndex];

    public bool IsLoggedIn { get; private set; }

    public bool IsPlayerPresent => _clientState.LocalPlayer != null && _clientState.LocalPlayer.IsValid();

    public bool IsObjectPresent(Dalamud.Game.ClientState.Objects.Types.GameObject? obj)
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
        if (playerPointer == null) playerPointer = PlayerPointer;
        return (IntPtr)mgr->LookupPetByOwnerObject((FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*)playerPointer);
    }

    public unsafe IntPtr GetCompanion(IntPtr? playerPointer = null)
    {
        var mgr = CharacterManager.Instance();
        if (playerPointer == null) playerPointer = PlayerPointer;
        return (IntPtr)mgr->LookupBuddyByOwnerObject((FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*)playerPointer);
    }

    public string PlayerName => _clientState.LocalPlayer?.Name.ToString() ?? "--";

    public IntPtr PlayerPointer => _clientState.LocalPlayer?.Address ?? IntPtr.Zero;

    public PlayerCharacter PlayerCharacter => _clientState.LocalPlayer!;

    public string PlayerNameHashed => Crypto.GetHash256(PlayerName + _clientState.LocalPlayer!.HomeWorld.Id);

    public List<PlayerCharacter> GetPlayerCharacters()
    {
        return _objectTable.Where(obj =>
            obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player &&
            !string.Equals(obj.Name.ToString(), PlayerName, StringComparison.Ordinal)).Select(p => (PlayerCharacter)p).ToList();
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

        var obj = (GameObject*)characterAddress;
        const int tick = 250;
        int curWaitTime = 0;
        // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
        while ((obj->RenderFlags & 0b100000000000) == 0b100000000000 && (!ct?.IsCancellationRequested ?? true) && curWaitTime < timeOut) // 0b100000000000 is "still rendering" or something
        {
            Logger.Verbose($"Waiting for {name} to finish drawing");
            curWaitTime += tick;
            Thread.Sleep(tick);
        }

        if (ct?.IsCancellationRequested ?? false) return;
        // wait quarter a second just in case
        Thread.Sleep(tick);
    }

    public unsafe void DisableDraw(IntPtr characterAddress)
    {
        var obj = (GameObject*)characterAddress;
        obj->DisableDraw();
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
        _clientState.Login -= ClientStateOnLogin;
        _clientState.Logout -= ClientStateOnLogout;
        _framework.Update -= FrameworkOnUpdate;
    }
}
