using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using System.Runtime.InteropServices;
using Penumbra.String;
using MareSynchronos.Mediator;
using ObjectKind = MareSynchronos.API.Data.Enum.ObjectKind;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Models;

public class GameObjectHandler : MediatorSubscriberBase
{
    private readonly MareMediator _mediator;
    private readonly Func<IntPtr> _getAddress;
    private readonly bool _sendUpdates;

    public unsafe Character* Character => (Character*)Address;

    public string Name { get; private set; }
    public ObjectKind ObjectKind { get; }
    public IntPtr Address { get; set; }
    private IntPtr DrawObjectAddress { get; set; }
    private Task? _delayedZoningTask;
    private CancellationTokenSource _zoningCts = new();
    private bool _haltProcessing = false;

    public override string ToString()
    {
        return $"{Name} ({Address.ToString("X")},{DrawObjectAddress.ToString("X")})";
    }

    public GameObjectHandler(ILogger<GameObjectHandler> logger, MareMediator mediator, ObjectKind objectKind, Func<IntPtr> getAddress, bool watchedObject = true) : base(logger, mediator)
    {
        _mediator = mediator;
        ObjectKind = objectKind;
        _getAddress = getAddress;
        _sendUpdates = watchedObject;
        Name = string.Empty;

        if (watchedObject)
        {
            Mediator.Subscribe<TransientResourceChangedMessage>(this, (msg) =>
            {
                if (_delayedZoningTask?.IsCompleted ?? true)
                {
                    var actualMsg = (TransientResourceChangedMessage)msg;
                    if (actualMsg.Address != Address) return;
                    Mediator.Publish(new CreateCacheForObjectMessage(this));
                }
            });
        }

        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => FrameworkUpdate());

        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (_) => ZoneSwitchEnd());
        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) => ZoneSwitchStart());

        Mediator.Subscribe<CutsceneStartMessage>(this, (_) =>
        {
            _haltProcessing = true;
        });
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) =>
        {
            _haltProcessing = false;
        });
        Mediator.Subscribe<PenumbraStartRedrawMessage>(this, (msg) =>
        {
            if (((PenumbraStartRedrawMessage)msg).Address == Address)
            {
                _haltProcessing = true;
            }
        });
        Mediator.Subscribe<PenumbraEndRedrawMessage>(this, (msg) =>
        {
            if (((PenumbraEndRedrawMessage)msg).Address == Address)
            {
                _haltProcessing = false;
            }
        });

        CheckAndUpdateObject();
    }

    private void FrameworkUpdate()
    {
        if (!_delayedZoningTask?.IsCompleted ?? false) return;

        CheckAndUpdateObject();
    }

    private void ZoneSwitchEnd()
    {
        if (!_sendUpdates || _haltProcessing) return;

        _clearCts?.Cancel();
        _clearCts?.Dispose();
        _clearCts = null;
        _zoningCts.CancelAfter(2500);
    }

    private void ZoneSwitchStart()
    {
        if (!_sendUpdates || _haltProcessing) return;

        _zoningCts = new();
        _logger.LogDebug($"[{this}] Starting Delay After Zoning");
        _delayedZoningTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(120), _zoningCts.Token).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                _logger.LogDebug($"[{this}] Delay after zoning complete");
                _zoningCts.Dispose();
            }
        });
    }

    public bool IsBeingDrawn { get; private set; }
    private byte[] EquipSlotData { get; set; } = new byte[40];
    private byte[] CustomizeData { get; set; } = new byte[26];
    private Task? _clearTask;
    private CancellationTokenSource? _clearCts = new();
    private byte? HatState { get; set; }
    private byte? VisorWeaponState { get; set; }
    private bool _doNotSendUpdate;

    private unsafe void CheckAndUpdateObject()
    {
        var curPtr = _getAddress.Invoke();
        bool drawObjDiff = false;
        try
        {
            var drawObjAddr = (IntPtr)((GameObject*)curPtr)->GetDrawObject();
            drawObjDiff = drawObjAddr != DrawObjectAddress;
            DrawObjectAddress = drawObjAddr;

            IsBeingDrawn = (((CharacterBase*)drawObjAddr)->HasModelInSlotLoaded != 0)
                           || (((CharacterBase*)drawObjAddr)->HasModelFilesInSlotLoaded != 0)
                           || (((GameObject*)curPtr)->RenderFlags & 0b100000000000) == 0b100000000000;
        }
        catch
        {
            if (curPtr != IntPtr.Zero)
            {
                IsBeingDrawn = true;
            }
        }

        if (_haltProcessing) return;

        if (curPtr != IntPtr.Zero && DrawObjectAddress != IntPtr.Zero)
        {
            if (_clearCts != null)
            {
                _logger.LogDebug($"[{this}] Cancelling Clear Task");
                _clearCts?.Cancel();
                _clearCts = null;
            }
            bool addrDiff = Address != curPtr;
            Address = curPtr;
            var chara = (Character*)curPtr;
            bool equipDiff = CompareAndUpdateEquipByteData(chara->EquipSlotData);
            var customizeDiff = _sendUpdates ? CompareAndUpdateCustomizeData(chara->CustomizeData) : false;
            var name = new ByteString(chara->GameObject.Name).ToString();
            bool nameChange = (!string.Equals(name, Name, StringComparison.Ordinal));
            if (addrDiff || equipDiff || customizeDiff || drawObjDiff || nameChange)
            {
                Name = name;
                _logger.LogTrace($"[{this}] Changed");

                if (_sendUpdates && !_doNotSendUpdate)
                {
                    _logger.LogDebug($"[{this}] Sending CreateCacheObjectMessage");
                    Mediator.Publish(new CreateCacheForObjectMessage(this));
                }

                if (equipDiff && !_sendUpdates)
                {
                    Mediator.Publish(new CharacterChangedMessage(this));
                }
            }
        }
        else if (Address != IntPtr.Zero || DrawObjectAddress != IntPtr.Zero)
        {
            Address = IntPtr.Zero;
            DrawObjectAddress = IntPtr.Zero;
            _logger.LogTrace($"[{this}] Changed -> Null");
            if (_sendUpdates && ObjectKind != ObjectKind.Player)
            {
                _clearCts?.Cancel();
                _clearCts?.Dispose();
                _clearCts = new();
                var token = _clearCts.Token;
                _clearTask = Task.Run(() => ClearTask(token), token);
            }
        }
    }

    private async Task ClearTask(CancellationToken token)
    {
        _logger.LogDebug($"[{this}] Running Clear Task");
        await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        _logger.LogDebug($"[{this}] Sending ClearCachedForObjectMessage");
        Mediator.Publish(new ClearCacheForObjectMessage(this));
        _clearCts = null;
    }

    private unsafe bool CompareAndUpdateEquipByteData(byte* equipSlotData)
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

        return hasChanges;
    }

    private unsafe bool CompareAndUpdateCustomizeData(byte* customizeData)
    {
        bool hasChanges = false;
        _doNotSendUpdate = false;

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
                _logger.LogDebug($"[{this}] Not Sending Update, only Hat changed");
                _doNotSendUpdate = true;
            }
            HatState = newHatState;
        }

        newWeaponOrVisorState &= 0b1101; // ignore drawing weapon

        if (newWeaponOrVisorState != VisorWeaponState)
        {
            if (VisorWeaponState != null && !hasChanges)
            {
                _logger.LogDebug($"[{this}] Not Sending Update, only Visor/Weapon changed");
                _doNotSendUpdate = true;
            }
            VisorWeaponState = newWeaponOrVisorState;
        }

        return hasChanges;
    }
}
