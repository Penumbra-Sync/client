using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Runtime.InteropServices;
using MareSynchronos.Utils;
using Penumbra.String;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Mediator;
using System;
using Microsoft.Extensions.Logging.Abstractions;

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

    public GameObjectHandler(MareMediator mediator, ObjectKind objectKind, Func<IntPtr> getAddress, bool watchedPlayer = true) : base(mediator)
    {
        _mediator = mediator;
        ObjectKind = objectKind;
        this._getAddress = getAddress;
        _sendUpdates = watchedPlayer;
        _name = string.Empty;

        if (watchedPlayer)
        {
            Mediator.Subscribe<TransientResourceChangedMessage>(this, (msg) =>
            {
                var actualMsg = (TransientResourceChangedMessage)msg;
                if (actualMsg.Address != Address) return;
                Mediator.Publish(new CreateCacheForObjectMessage(this));
            });

            Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => CheckAndUpdateObject());
        }
    }

    public byte[] EquipSlotData { get; set; } = new byte[40];
    public byte[] CustomizeData { get; set; } = new byte[26];
    private Task? _petClearTask;
    private CancellationTokenSource? _petCts = new();
    public byte? HatState { get; set; }
    public byte? VisorWeaponState { get; set; }
    private bool _doNotSendUpdate;

    public unsafe bool CheckAndUpdateObject()
    {
        var curPtr = CurrentAddress;
        if (curPtr != IntPtr.Zero && (IntPtr)((Character*)curPtr)->GameObject.DrawObject != IntPtr.Zero)
        {
            if (ObjectKind == ObjectKind.Pet && _petCts != null)
            {
                Logger.Debug("Cancelling PetClearTask for " + ObjectKind);
                _petCts?.Cancel();
                _petCts = null;
            }
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
            if (_sendUpdates && ObjectKind == ObjectKind.Pet)
            {
                _petCts?.Cancel();
                _petCts?.Dispose();
                _petCts = new();
                var token = _petCts.Token;
                _petClearTask = Task.Run(() => PetClearTask(token), token);
            }
            else if (_sendUpdates)
            {
                Logger.Debug("Sending ClearCachedForObjectMessage for " + ObjectKind);
                Mediator.Publish(new ClearCacheForObjectMessage(this));
            }
        }

        return false;
    }

    private async Task PetClearTask(CancellationToken token)
    {
        Logger.Debug("Running PetClearTask for " + ObjectKind);
        await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        Logger.Debug("Sending ClearCachedForObjectMessage for " + ObjectKind);
        Mediator.Publish(new ClearCacheForObjectMessage(this));
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
