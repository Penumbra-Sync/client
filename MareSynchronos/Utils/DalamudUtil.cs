using System;
using System.Collections.Generic;
using System.Threading;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace MareSynchronos.Utils
{
    public class DalamudUtil
    {
        private readonly ClientState _clientState;
        private readonly ObjectTable _objectTable;

        public DalamudUtil(ClientState clientState, ObjectTable objectTable)
        {
            _clientState = clientState;
            _objectTable = objectTable;
        }

        public bool IsPlayerPresent => _clientState.LocalPlayer != null;

        public string PlayerName => _clientState.LocalPlayer!.Name.ToString();

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

        public unsafe void WaitWhileCharacterIsDrawing(IntPtr characterAddress)
        {
            if (!_clientState.IsLoggedIn) return;

            var obj = (GameObject*)characterAddress;

            // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
            while ((obj->RenderFlags & 0b100000000000) == 0b100000000000) // 0b100000000000 is "still rendering" or something
            {
                Logger.Debug("Waiting for character to finish drawing");
                Thread.Sleep(1000);
            }

            // wait half a second just in case
            Thread.Sleep(500);
        }

        public void WaitWhileSelfIsDrawing() => WaitWhileCharacterIsDrawing(_clientState.LocalPlayer?.Address ?? new IntPtr());
    }
}
