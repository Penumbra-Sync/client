using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Penumbra.PlayerWatch;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MareSynchronos.Utils
{
    public delegate void PlayerChange(Character actor);

    public delegate void LogIn();
    public delegate void LogOut();

    public class DalamudUtil : IDisposable
    {
        private readonly ClientState _clientState;
        private readonly ObjectTable _objectTable;
        private readonly IPlayerWatcher _watcher;
        public event PlayerChange? PlayerChanged;
        public event LogIn? LogIn;
        public event LogOut? LogOut;

        public DalamudUtil(ClientState clientState, ObjectTable objectTable, IPlayerWatcher watcher)
        {
            _clientState = clientState;
            _objectTable = objectTable;
            _watcher = watcher;
            _watcher.Enable();
            _watcher.PlayerChanged += WatcherOnPlayerChanged;
            _clientState.Login += ClientStateOnLogin;
            _clientState.Logout += ClientStateOnLogout;
            if (IsLoggedIn)
            {
                ClientStateOnLogin(null, EventArgs.Empty);
            }
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

        private void WatcherOnPlayerChanged(Character actor)
        {
            PlayerChanged?.Invoke(actor);
        }


        public void AddPlayerToWatch(string playerName)
        {
            _watcher.AddPlayerToWatch(playerName);
        }

        public void RemovePlayerFromWatch(string playerName)
        {
            _watcher.RemovePlayerFromWatch(playerName);
        }

        public bool IsPlayerPresent => _clientState.LocalPlayer != null;

        public string PlayerName => _clientState.LocalPlayer?.Name.ToString() ?? "--";

        public int PlayerJobId => (int)_clientState.LocalPlayer!.ClassJob.Id;

        public IntPtr PlayerPointer => _clientState.LocalPlayer!.Address;

        public string PlayerNameHashed => Crypto.GetHash256(PlayerName + _clientState.LocalPlayer!.HomeWorld.Id);

        public Dictionary<string, PlayerCharacter> GetLocalPlayers()
        {
            if (!_clientState.IsLoggedIn)
            {
                return new Dictionary<string, PlayerCharacter>();
            }

            Dictionary<string, PlayerCharacter> allLocalPlayers = new();
            foreach (var obj in _objectTable)
            {
                if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
                string playerName = obj.Name.ToString();
                if (playerName == PlayerName) continue;
                var playerObject = (PlayerCharacter)obj;
                allLocalPlayers[Crypto.GetHash256(playerObject.Name.ToString() + playerObject.HomeWorld.Id.ToString())] = playerObject;
            }

            return allLocalPlayers;
        }

        public List<PlayerCharacter> GetPlayerCharacters()
        {
            return _objectTable.Where(obj =>
                obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player &&
                obj.Name.ToString() != PlayerName).Select(p => (PlayerCharacter)p).ToList();
        }

        public PlayerCharacter? GetPlayerCharacterFromObjectTableIndex(int index)
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
                Logger.Debug("Waiting for character to finish drawing");
                Thread.Sleep(1000);
            }

            if (ct?.IsCancellationRequested ?? false) return;
            // wait half a second just in case
            Thread.Sleep(500);
        }

        public void WaitWhileSelfIsDrawing(CancellationToken token) => WaitWhileCharacterIsDrawing(_clientState.LocalPlayer?.Address ?? new IntPtr(), token);

        public void Dispose()
        {
            _watcher.Disable();
            _watcher.Dispose();
        }
    }
}
