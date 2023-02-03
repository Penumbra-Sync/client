using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Runtime.InteropServices;
using MareSynchronos.Utils;
using Penumbra.String;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Mediator;

namespace MareSynchronos.Models;

public class GameObjectHandler : MediatorSubscriberBase
{
    private readonly MareMediator _mediator;
    private readonly Func<IntPtr> _getAddress;
    private readonly bool _sendUpdates;

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
                return _getAddress.Invoke();
            }
            catch
            { return IntPtr.Zero; }
        }
    }

    public GameObjectHandler(MareMediator mediator, ObjectKind objectKind, Func<IntPtr> getAddress, bool sendUpdates = true) : base(mediator)
    {
        _mediator = mediator;
        ObjectKind = objectKind;
        this._getAddress = getAddress;
        _sendUpdates = sendUpdates;
        _name = string.Empty;

        Mediator.Subscribe<TransientResourceChangedMessage>(this, (msg) =>
        {
            var actualMsg = (TransientResourceChangedMessage)msg;
            if (actualMsg.Address != Address || !sendUpdates) return;
            Mediator.Publish(new CreateCacheForObjectMessage(this));
        });

        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => CheckAndUpdateObject());
    }

    public byte[] EquipSlotData { get; set; } = new byte[40];
    public byte[] CustomizeData { get; set; } = new byte[26];
    public byte? HatState { get; set; }
    public byte? VisorWeaponState { get; set; }
    private bool _doNotSendUpdate;

    public unsafe bool CheckAndUpdateObject()
    {
        var curPtr = CurrentAddress;
        if (curPtr != IntPtr.Zero)
        {
            var chara = (Character*)curPtr;
            bool addr = Address == IntPtr.Zero || Address != curPtr;
            bool equip = CompareAndUpdateByteData(chara->EquipSlotData, chara->CustomizeData);
            bool drawObj = (IntPtr)chara->GameObject.DrawObject != DrawObjectAddress;
            var name = new ByteString(chara->GameObject.Name).ToString();
            bool nameChange = (!string.Equals(name, _name, StringComparison.Ordinal));
            if (addr || equip || drawObj || nameChange)
            {
                _name = name;
                Logger.Verbose($"{ObjectKind} changed: {_name}, now: {curPtr:X}, {(IntPtr)chara->GameObject.DrawObject:X}");

                Address = curPtr;
                DrawObjectAddress = (IntPtr)chara->GameObject.DrawObject;
                if (_sendUpdates && !_doNotSendUpdate && DrawObjectAddress != IntPtr.Zero)
                {
                    Logger.Debug("Sending CreateCacheObjectMessage for " + ObjectKind);
                    Mediator.Publish(new CreateCacheForObjectMessage(this));
                }

                return true;
            }
        }
        else if (Address != IntPtr.Zero || DrawObjectAddress != IntPtr.Zero)
        {
            Address = IntPtr.Zero;
            DrawObjectAddress = IntPtr.Zero;
            Logger.Verbose(ObjectKind + " Changed: " + _name + ", now: " + Address + ", " + DrawObjectAddress);
        }

        return false;
    }

    private unsafe bool CompareAndUpdateByteData(byte* equipSlotData, byte* customizeData)
    {
        bool hasChanges = false;
        _doNotSendUpdate = false;
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
            if (HatState != null && !hasChanges)
            {
                Logger.Debug("Not Sending Update, only Hat changed");
                _doNotSendUpdate = true;
            }
            HatState = newHatState;
            hasChanges = true;
        }

        newWeaponOrVisorState &= 0b1101; // ignore drawing weapon

        if (newWeaponOrVisorState != VisorWeaponState)
        {
            if (VisorWeaponState != null && !hasChanges)
            {
                Logger.Debug("Not Sending Update, only Visor/Weapon changed");
                _doNotSendUpdate = true;
            }
            VisorWeaponState = newWeaponOrVisorState;
            hasChanges = true;
        }

        return hasChanges;
    }
}
