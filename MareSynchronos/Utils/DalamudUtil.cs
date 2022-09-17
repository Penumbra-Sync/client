using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MareSynchronos.Utils
{
    public delegate void PlayerChange(Dalamud.Game.ClientState.Objects.Types.Character actor);

    public delegate void LogIn();
    public delegate void LogOut();
    public delegate void ClassJobChanged();

    public delegate void FrameworkUpdate();
    public delegate void VoidDelegate();

    public class DalamudUtil : IDisposable
    {
        private readonly ClientState _clientState;
        private readonly ObjectTable _objectTable;
        private readonly Framework _framework;
        private readonly Condition _condition;

        public event LogIn? LogIn;
        public event LogOut? LogOut;
        public event FrameworkUpdate? FrameworkUpdate;
        public event ClassJobChanged? ClassJobChanged;
        private uint? classJobId = 0;
        public event FrameworkUpdate? DelayedFrameworkUpdate;
        public event VoidDelegate? ZoneSwitchStart;
        public event VoidDelegate? ZoneSwitchEnd;
        private DateTime _delayedFrameworkUpdateCheck = DateTime.Now;
        private bool _sentBetweenAreas = false;

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

        public DalamudUtil(ClientState clientState, ObjectTable objectTable, Framework framework, Condition condition)
        {
            _clientState = clientState;
            _objectTable = objectTable;
            _framework = framework;
            _condition = condition;
            _clientState.Login += ClientStateOnLogin;
            _clientState.Logout += ClientStateOnLogout;
            _framework.Update += FrameworkOnUpdate;
            if (IsLoggedIn)
            {
                classJobId = _clientState.LocalPlayer!.ClassJob.Id;
                ClientStateOnLogin(null, EventArgs.Empty);
            }
        }

        private void FrameworkOnUpdate(Framework framework)
        {
            if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.BetweenAreas51] || IsInGpose)
            {
                if (!_sentBetweenAreas)
                {
                    Logger.Debug("Zone switch start");
                    _sentBetweenAreas = true;
                    ZoneSwitchStart?.Invoke();
                }

                return;
            }
            else if (_sentBetweenAreas)
            {
                Logger.Debug("Zone switch end");
                _sentBetweenAreas = false;
                ZoneSwitchEnd?.Invoke();
            }

            foreach (FrameworkUpdate? frameworkInvocation in (FrameworkUpdate?.GetInvocationList() ?? Array.Empty<FrameworkUpdate>()).Cast<FrameworkUpdate>())
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
            if (_clientState.LocalPlayer != null && _clientState.LocalPlayer.IsValid())
            {
                var newclassJobId = _clientState.LocalPlayer.ClassJob.Id;

                if (classJobId != newclassJobId)
                {
                    classJobId = newclassJobId;
                    ClassJobChanged?.Invoke();
                }
            }

            foreach (FrameworkUpdate? frameworkInvocation in (DelayedFrameworkUpdate?.GetInvocationList() ?? Array.Empty<FrameworkUpdate>()).Cast<FrameworkUpdate>())
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

        public bool IsLoggedIn => _clientState.IsLoggedIn;

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

        public bool IsInGpose => _objectTable[201] != null;

        public List<PlayerCharacter> GetPlayerCharacters()
        {
            return _objectTable.Where(obj =>
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player &&
                obj.Name.ToString() != PlayerName).Select(p => (PlayerCharacter)p).ToList();
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
                if (item.Name.ToString() == characterName) return (PlayerCharacter)item;
            }

            return null;
        }

        public async Task<T> RunOnFrameworkThread<T>(Func<T> func)
        {
            return await _framework.RunOnFrameworkThread(func);
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

        public void Dispose()
        {
            _clientState.Login -= ClientStateOnLogin;
            _clientState.Logout -= ClientStateOnLogout;
            _framework.Update -= FrameworkOnUpdate;
        }
    }
}
