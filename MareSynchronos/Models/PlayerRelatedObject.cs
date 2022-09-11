using System;
using MareSynchronos.API;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Runtime.InteropServices;
using MareSynchronos.Utils;
using Penumbra.GameData.ByteString;

namespace MareSynchronos.Models
{
    public class PlayerRelatedObject
    {
        private readonly Func<IntPtr> getAddress;

        public unsafe Character* Character => (Character*)Address;

        private string _name;

        public ObjectKind ObjectKind { get; }
        public IntPtr Address { get; set; }
        public IntPtr DrawObjectAddress { get; set; }

        public IntPtr CurrentAddress
        {
            get
            {
                try
                {
                    return getAddress.Invoke();
                }
                catch
                { return IntPtr.Zero; }
            }
        }

        public PlayerRelatedObject(ObjectKind objectKind, IntPtr address, IntPtr drawObjectAddress, Func<IntPtr> getAddress)
        {
            ObjectKind = objectKind;
            Address = address;
            DrawObjectAddress = drawObjectAddress;
            this.getAddress = getAddress;
            _name = string.Empty;
        }

        public byte[] EquipSlotData { get; set; } = new byte[40];
        public byte[] CustomizeData { get; set; } = new byte[26];
        public byte? HatState { get; set; }
        public byte? VisorWeaponState { get; set; }

        public bool HasTransientsUpdate { get; set; } = false;
        public bool HasUnprocessedUpdate { get; set; } = false;
        public bool DoNotSendUpdate { get; set; } = false;
        public bool IsProcessing { get; set; } = false;

        public unsafe void CheckAndUpdateObject()
        {
            var curPtr = CurrentAddress;
            if (curPtr != IntPtr.Zero)
            {
                var chara = (Character*)curPtr;
                bool addr = Address == IntPtr.Zero || Address != curPtr;
                bool equip = CompareAndUpdateByteData(chara->EquipSlotData, chara->CustomizeData);
                bool drawObj = (IntPtr)chara->GameObject.DrawObject != DrawObjectAddress;
                var name = new Utf8String(chara->GameObject.Name).ToString();
                bool nameChange = (name != _name);
                if (addr || equip || drawObj || nameChange)
                {
                    _name = name;
                    Logger.Verbose($"{ObjectKind} changed: {_name}, now: {curPtr:X}, {(IntPtr)chara->GameObject.DrawObject:X}");

                    Address = curPtr;
                    DrawObjectAddress = (IntPtr)chara->GameObject.DrawObject;
                    HasUnprocessedUpdate = true;
                }
            }
            else if (Address != IntPtr.Zero || DrawObjectAddress != IntPtr.Zero)
            {
                Address = IntPtr.Zero;
                DrawObjectAddress = IntPtr.Zero;
                Logger.Verbose(ObjectKind + " Changed: " + _name + ", now: " + Address + ", " + DrawObjectAddress);
            }
        }

        private unsafe bool CompareAndUpdateByteData(byte* equipSlotData, byte* customizeData)
        {
            bool hasChanges = false;
            DoNotSendUpdate = false;
            for (int i = 0; i < EquipSlotData.Length; i++)
            {
                var data = Marshal.ReadByte((IntPtr)equipSlotData, i);
                if (EquipSlotData[i] != data)
                {
                    EquipSlotData[i] = data;
                    hasChanges = true;
                }
            }

            for (int i = 0; i < CustomizeData.Length; i++)
            {
                var data = Marshal.ReadByte((IntPtr)customizeData, i);
                if (CustomizeData[i] != data)
                {
                    CustomizeData[i] = data;
                    hasChanges = true;
                }
            }

            var newHatState = Marshal.ReadByte((IntPtr)customizeData + 30, 0);
            var newWeaponOrVisorState = Marshal.ReadByte((IntPtr)customizeData + 31, 0);
            if (newHatState != HatState)
            {
                if (HatState != null && !hasChanges && !HasUnprocessedUpdate)
                {
                    Logger.Debug("Not Sending Update, only Hat changed");
                    DoNotSendUpdate = true;
                }
                HatState = newHatState;
                hasChanges = true;
            }

            newWeaponOrVisorState &= 0b1101; // ignore drawing weapon

            if (newWeaponOrVisorState != VisorWeaponState)
            {
                if (VisorWeaponState != null && !hasChanges && !HasUnprocessedUpdate)
                {
                    Logger.Debug("Not Sending Update, only Visor/Weapon changed");
                    DoNotSendUpdate = true;
                }
                VisorWeaponState = newWeaponOrVisorState;
                hasChanges = true;
            }

            return hasChanges;
        }
    }
}
