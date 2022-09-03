using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MareSynchronos.Utils
{
    public delegate void PlayerChange(Dalamud.Game.ClientState.Objects.Types.Character actor);

    public delegate void LogIn();
    public delegate void LogOut();

    public delegate void FrameworkUpdate();

    public class DalamudUtil : IDisposable
    {
        private readonly ClientState _clientState;
        private readonly ObjectTable _objectTable;
        private readonly Framework _framework;
        public event LogIn? LogIn;
        public event LogOut? LogOut;
        public event FrameworkUpdate? FrameworkUpdate;

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

        public DalamudUtil(ClientState clientState, ObjectTable objectTable, Framework framework)
        {
            _clientState = clientState;
            _objectTable = objectTable;
            _framework = framework;
            _clientState.Login += ClientStateOnLogin;
            _clientState.Logout += ClientStateOnLogout;
            _framework.Update += FrameworkOnUpdate;
            if (IsLoggedIn)
            {
                ClientStateOnLogin(null, EventArgs.Empty);
            }
        }

        private void FrameworkOnUpdate(Framework framework)
        {
            FrameworkUpdate?.Invoke();
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

        public unsafe void WaitWhileCharacterIsDrawing(IntPtr characterAddress, CancellationToken? ct = null)
        {
            if (!_clientState.IsLoggedIn) return;

            var obj = (GameObject*)characterAddress;

            // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
            while ((obj->RenderFlags & 0b100000000000) == 0b100000000000 && (!ct?.IsCancellationRequested ?? true)) // 0b100000000000 is "still rendering" or something
            {
                Logger.Verbose("Waiting for character to finish drawing");
                Thread.Sleep(250);
            }

            if (ct?.IsCancellationRequested ?? false) return;
            // wait quarter a second just in case
            Thread.Sleep(250);
        }

        public void WaitWhileSelfIsDrawing(CancellationToken? token) => WaitWhileCharacterIsDrawing(_clientState.LocalPlayer?.Address ?? new IntPtr(), token);

        public void Dispose()
        {
            _clientState.Login -= ClientStateOnLogin;
            _clientState.Logout -= ClientStateOnLogout;
            _framework.Update -= FrameworkOnUpdate;
        }
    }
}
