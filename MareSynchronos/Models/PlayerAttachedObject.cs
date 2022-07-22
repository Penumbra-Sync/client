using System;
using MareSynchronos.API;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Runtime.InteropServices;

namespace MareSynchronos.Models
{
    internal class PlayerAttachedObject
    {
        private readonly Func<IntPtr> getAddress;

        public unsafe Character* Character => (Character*)Address;

        public ObjectKind ObjectKind { get; }
        public IntPtr Address { get; set; }
        public IntPtr DrawObjectAddress { get; set; }

        public IntPtr CurrentAddress => getAddress.Invoke();

        public PlayerAttachedObject(ObjectKind objectKind, IntPtr address, IntPtr drawObjectAddress, Func<IntPtr> getAddress)
        {
            ObjectKind = objectKind;
            Address = address;
            DrawObjectAddress = drawObjectAddress;
            this.getAddress = getAddress;
        }

        public byte[] EquipSlotData { get; set; } = new byte[40];
        public byte[] CustomizeData { get; set; } = new byte[26];

        public unsafe bool CompareAndUpdateEquipment(byte* equipSlotData, byte* customizeData)
        {
            bool hasChanges = false;
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

            return hasChanges;
        }
    }
}
