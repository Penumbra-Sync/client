using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using MareSynchronos.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using Penumbra.String;
using System.Runtime.InteropServices;
using ObjectKind = MareSynchronos.API.Data.Enum.ObjectKind;

namespace MareSynchronos.Models;

public class GameObjectHandler : MediatorSubscriberBase
{
    private readonly Func<IntPtr> _getAddress;
    private readonly bool _isOwnedObject;
    private readonly MareMediator _mediator;
    private readonly DalamudUtil _dalamudUtil;
    private readonly PerformanceCollector _performanceCollector;
    private CancellationTokenSource? _clearCts = new();
    private Task? _clearTask;
    private Task? _delayedZoningTask;
    private bool _haltProcessing = false;
    private bool _ignoreSendAfterRedraw = false;
    private CancellationTokenSource _zoningCts = new();
    public GameObjectHandler(ILogger<GameObjectHandler> logger, PerformanceCollector performanceCollector,
        MareMediator mediator, DalamudUtil dalamudUtil, ObjectKind objectKind, Func<IntPtr> getAddress, bool watchedObject = true) : base(logger, mediator)
    {
        _performanceCollector = performanceCollector;
        _mediator = mediator;
        ObjectKind = objectKind;
        _dalamudUtil = dalamudUtil;
        _getAddress = getAddress;
        _isOwnedObject = watchedObject;
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
            Mediator.Publish(new AddWatchedGameObjectHandler(this));
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
                Task.Run(async () =>
                {
                    _ignoreSendAfterRedraw = true;
                    await Task.Delay(500).ConfigureAwait(false);
                    _ignoreSendAfterRedraw = false;
                });
            }
        });

        CheckAndUpdateObject();
    }

    public IntPtr Address { get; set; }
    public unsafe Character* Character => (Character*)Address;

    public IntPtr CurrentAddress => _getAddress.Invoke();
    public string Name { get; private set; }
    public ObjectKind ObjectKind { get; }
    private byte[] CustomizeData { get; set; } = new byte[26];
    private IntPtr DrawObjectAddress { get; set; }
    private byte[] EquipSlotData { get; set; } = new byte[40];

    private byte? HatState { get; set; }

    private byte? VisorWeaponState { get; set; }

    public override void Dispose()
    {
        base.Dispose();
        if (_isOwnedObject)
            Mediator.Publish(new RemoveWatchedGameObjectHandler(this));
    }

    public override string ToString()
    {
        var owned = (_isOwnedObject ? "Self" : "Other");
        return $"{owned}/{ObjectKind}:{Name} ({Address:X},{DrawObjectAddress:X})";
    }

    private unsafe IntPtr GetDrawObj()
    {
        return (IntPtr)((GameObject*)_getAddress.Invoke())->GetDrawObject();
    }

    private unsafe bool IsBeingDrawn(IntPtr drawObj, IntPtr curPtr)
    {
        _logger.LogTrace("IsBeingDrawn for ptr {curPtr} : {drawObj}", curPtr.ToString("X"), drawObj.ToString("X"));
        return drawObj == IntPtr.Zero
                       || (((CharacterBase*)drawObj)->HasModelInSlotLoaded != 0)
                       || (((CharacterBase*)drawObj)->HasModelFilesInSlotLoaded != 0)
                       || (((GameObject*)curPtr)->RenderFlags & 0b100000000000) == 0b100000000000;
    }

    public async Task<bool> IsBeingDrawnRunOnFramework()
    {
        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var curPtr = _getAddress.Invoke();

            try
            {
                var drawObj = GetDrawObj();
                return IsBeingDrawn(drawObj, curPtr);
            }
            catch (Exception ex)
            {
                if (curPtr != IntPtr.Zero)
                {
                    return true;
                }

                return false;
            }
        }).ConfigureAwait(false);
    }

    private unsafe void CheckAndUpdateObject()
    {
        if (_haltProcessing) return;

        var curPtr = _getAddress.Invoke();
        bool drawObjDiff = false;
        try
        {
            if (curPtr != IntPtr.Zero)
            {
                var drawObjAddr = (IntPtr)((GameObject*)curPtr)->GetDrawObject();
                drawObjDiff = drawObjAddr != DrawObjectAddress;
                DrawObjectAddress = drawObjAddr;
            }
        }
        catch (Exception ex)
        {
            var name = new ByteString(((Character*)curPtr)->GameObject.Name).ToString();
            _logger.LogError(ex, "Error during checking for draw object for {name}", this);
        }

        if (curPtr != IntPtr.Zero && DrawObjectAddress != IntPtr.Zero)
        {
            if (_clearCts != null)
            {
                _logger.LogDebug("[{this}] Cancelling Clear Task", this);
                _clearCts?.Cancel();
                _clearCts = null;
            }
            bool addrDiff = Address != curPtr;
            Address = curPtr;
            var chara = (Character*)curPtr;
            var name = new ByteString(chara->GameObject.Name).ToString();
            bool nameChange = (!string.Equals(name, Name, StringComparison.Ordinal));
            Name = name;
            bool equipDiff = CompareAndUpdateEquipByteData(chara->EquipSlotData);
            if (equipDiff && !_isOwnedObject) // send the message out immediately and cancel out, no reason to continue if not self
            {
                if (!_ignoreSendAfterRedraw)
                {
                    _logger.LogTrace("[{this}] Changed", this);
                    Mediator.Publish(new CharacterChangedMessage(this));
                    return;
                }
            }

            var customizeDiff = CompareAndUpdateCustomizeData(chara->CustomizeData);

            if (addrDiff || equipDiff || customizeDiff || drawObjDiff || nameChange)
            {
                if (_isOwnedObject)
                {
                    _logger.LogTrace("[{this}] Changed", this);

                    _logger.LogDebug("[{this}] Sending CreateCacheObjectMessage", this);
                    Mediator.Publish(new CreateCacheForObjectMessage(this));
                }
            }
        }
        else if (Address != IntPtr.Zero || DrawObjectAddress != IntPtr.Zero)
        {
            Address = IntPtr.Zero;
            DrawObjectAddress = IntPtr.Zero;
            _logger.LogTrace("[{this}] Changed -> Null", this);
            if (_isOwnedObject && ObjectKind != ObjectKind.Player)
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
        _logger.LogDebug("[{this}] Running Clear Task", this);
        await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        _logger.LogDebug("[{this}] Sending ClearCachedForObjectMessage", this);
        Mediator.Publish(new ClearCacheForObjectMessage(this));
        _clearCts = null;
    }

    private unsafe bool CompareAndUpdateCustomizeData(byte* customizeData)
    {
        bool hasChanges = false;

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

    private void FrameworkUpdate()
    {
        if (!_delayedZoningTask?.IsCompleted ?? false) return;

        try
        {
            _performanceCollector.LogPerformance(this, "CheckAndUpdateObject>" + (_isOwnedObject ? "Self+" : "Other+") + ObjectKind + "/"
                + (string.IsNullOrEmpty(Name) ? "Unk" : Name) + "+" + Address.ToString("X"), CheckAndUpdateObject);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during FrameworkUpdate of {this}", this);
        }
    }

    private void ZoneSwitchEnd()
    {
        if (!_isOwnedObject || _haltProcessing) return;

        _clearCts?.Cancel();
        _clearCts?.Dispose();
        _clearCts = null;
        _zoningCts.CancelAfter(2500);
    }

    private void ZoneSwitchStart()
    {
        if (!_isOwnedObject || _haltProcessing) return;

        _zoningCts = new();
        _logger.LogDebug("[{obj}] Starting Delay After Zoning", this);
        _delayedZoningTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(120), _zoningCts.Token).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                _logger.LogDebug("[{this}] Delay after zoning complete", this);
                _zoningCts.Dispose();
            }
        });
    }
}