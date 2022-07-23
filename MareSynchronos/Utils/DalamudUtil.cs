using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MareSynchronos.Utils
{
    public delegate void PlayerChange(Character actor);

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

        public bool IsLoggedIn => _clientState.IsLoggedIn;

        public bool IsPlayerPresent => _clientState.LocalPlayer != null && _clientState.LocalPlayer.IsValid();

        public string PlayerName => _clientState.LocalPlayer?.Name.ToString() ?? "--";

        public IntPtr PlayerPointer => _clientState.LocalPlayer!.Address;

        public PlayerCharacter PlayerCharacter => _clientState.LocalPlayer!;

        public string PlayerNameHashed => Crypto.GetHash256(PlayerName + _clientState.LocalPlayer!.HomeWorld.Id);

        public bool IsInGpose => _objectTable[201] != null;

        public List<PlayerCharacter> GetPlayerCharacters()
        {
            return _objectTable.Where(obj =>
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player &&
                obj.Name.ToString() != PlayerName).Select(p => (PlayerCharacter)p).ToList();
        }

        public PlayerCharacter? GetPlayerCharacterFromObjectTableByIndex(int index)
        {
            var objTableObj = _objectTable[index];
            if (objTableObj!.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) return null;
            return (PlayerCharacter)objTableObj;
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
            // wait half a second just in case
            Thread.Sleep(500);
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
