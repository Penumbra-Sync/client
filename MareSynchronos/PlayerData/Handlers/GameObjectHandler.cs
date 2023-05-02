using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using Penumbra.String;
using System.Runtime.InteropServices;
using ObjectKind = MareSynchronos.API.Data.Enum.ObjectKind;

namespace MareSynchronos.PlayerData.Handlers;

public sealed class GameObjectHandler : DisposableMediatorSubscriberBase
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly Func<IntPtr> _getAddress;
    private readonly bool _isOwnedObject;
    private readonly PerformanceCollectorService _performanceCollector;
    private CancellationTokenSource? _clearCts = new();
    private Task? _delayedZoningTask;
    private bool _haltProcessing = false;
    private bool _ignoreSendAfterRedraw = false;
    private CancellationTokenSource _zoningCts = new();

    public GameObjectHandler(ILogger<GameObjectHandler> logger, PerformanceCollectorService performanceCollector,
        MareMediator mediator, DalamudUtilService dalamudUtil, ObjectKind objectKind, Func<IntPtr> getAddress, bool watchedObject = true) : base(logger, mediator)
    {
        _performanceCollector = performanceCollector;
        ObjectKind = objectKind;
        _dalamudUtil = dalamudUtil;
        _getAddress = () =>
        {
            _dalamudUtil.EnsureIsOnFramework();
            return getAddress.Invoke();
        };
        _isOwnedObject = watchedObject;
        Name = string.Empty;

        if (watchedObject)
        {
            Mediator.Subscribe<TransientResourceChangedMessage>(this, (msg) =>
            {
                if (_delayedZoningTask?.IsCompleted ?? true)
                {
                    if (msg.Address != Address) return;
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
            if (msg.Address == Address)
            {
                _haltProcessing = true;
            }
        });
        Mediator.Subscribe<PenumbraEndRedrawMessage>(this, (msg) =>
        {
            if (msg.Address == Address)
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

        _dalamudUtil.RunOnFrameworkThread(CheckAndUpdateObject).GetAwaiter().GetResult();
    }

    public IntPtr Address { get; private set; }
    public string Name { get; private set; }
    public ObjectKind ObjectKind { get; }
    private byte[] CustomizeData { get; set; } = new byte[26];
    private IntPtr DrawObjectAddress { get; set; }
    private byte[] EquipSlotData { get; set; } = new byte[40];

    public async Task ActOnFrameworkAfterEnsureNoDrawAsync(Action act, CancellationToken token)
    {
        while (await _dalamudUtil.RunOnFrameworkThread(() =>
               {
                   if (IsBeingDrawn()) return true;
                   act();
                   return false;
               }).ConfigureAwait(false))
        {
            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    public IntPtr CurrentAddress()
    {
        _dalamudUtil.EnsureIsOnFramework();
        return _getAddress.Invoke();
    }

    public Dalamud.Game.ClientState.Objects.Types.GameObject? GetGameObject()
    {
        return _dalamudUtil.CreateGameObject(Address);
    }

    public async Task<bool> IsBeingDrawnRunOnFrameworkAsync()
    {
        return await _dalamudUtil.RunOnFrameworkThread(IsBeingDrawn).ConfigureAwait(false);
    }

    public override string ToString()
    {
        var owned = _isOwnedObject ? "Self" : "Other";
        return $"{owned}/{ObjectKind}:{Name} ({Address:X},{DrawObjectAddress:X})";
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (_isOwnedObject)
            Mediator.Publish(new RemoveWatchedGameObjectHandler(this));
    }

    private unsafe void CheckAndUpdateObject()
    {
        if (_haltProcessing) return;

        var prevAddr = Address;
        var prevDrawObj = DrawObjectAddress;

        Address = _getAddress();
        if (Address != IntPtr.Zero)
        {
            var drawObjAddr = (IntPtr)((GameObject*)Address)->DrawObject;
            DrawObjectAddress = drawObjAddr;
        }
        else
        {
            DrawObjectAddress = IntPtr.Zero;
        }

        bool drawObjDiff = DrawObjectAddress != prevDrawObj;
        bool addrDiff = Address != prevAddr;

        if (Address != IntPtr.Zero && DrawObjectAddress != IntPtr.Zero)
        {
            if (_clearCts != null)
            {
                Logger.LogDebug("[{this}] Cancelling Clear Task", this);
                _clearCts?.Cancel();
                _clearCts = null;
            }
            var chara = (Character*)Address;
            var name = new ByteString(chara->GameObject.Name).ToString();
            bool nameChange = !string.Equals(name, Name, StringComparison.Ordinal);
            Name = name;
            bool equipDiff = CompareAndUpdateEquipByteData(chara->EquipSlotData);
            if (equipDiff && !_isOwnedObject && !_ignoreSendAfterRedraw) // send the message out immediately and cancel out, no reason to continue if not self
            {
                Logger.LogTrace("[{this}] Changed", this);
                Mediator.Publish(new CharacterChangedMessage(this));
                return;
            }

            var customizeDiff = CompareAndUpdateCustomizeData(chara->CustomizeData);

            if ((addrDiff || drawObjDiff || equipDiff || customizeDiff || nameChange) && _isOwnedObject)
            {
                Logger.LogDebug("[{this}] Changed, Sending CreateCacheObjectMessage", this);
                Mediator.Publish(new CreateCacheForObjectMessage(this));
            }
        }
        else if (addrDiff || drawObjDiff)
        {
            Logger.LogTrace("[{this}] Changed", this);
            if (_isOwnedObject && ObjectKind != ObjectKind.Player)
            {
                _clearCts?.Cancel();
                _clearCts?.Dispose();
                _clearCts = new();
                var token = _clearCts.Token;
                _ = Task.Run(() => ClearAsync(token), token);
            }
        }
    }

    private async Task ClearAsync(CancellationToken token)
    {
        Logger.LogDebug("[{this}] Running Clear Task", this);
        await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        Logger.LogDebug("[{this}] Sending ClearCachedForObjectMessage", this);
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
            Logger.LogWarning(ex, "Error during FrameworkUpdate of {this}", this);
        }
    }

    private unsafe IntPtr GetDrawObj(nint curPtr)
    {
        Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, Getting new DrawObject", this);
        return (IntPtr)((GameObject*)curPtr)->DrawObject;
    }

    private bool IsBeingDrawn()
    {
        var curPtr = _getAddress();
        Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, CurPtr: {ptr}", this, curPtr.ToString("X"));

        if (curPtr == IntPtr.Zero)
        {
            Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, CurPtr is ZERO, returning", this);

            Address = IntPtr.Zero;
            DrawObjectAddress = IntPtr.Zero;
            return false;
        }

        var drawObj = GetDrawObj(curPtr);
        Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, DrawObjPtr: {ptr}", this, drawObj.ToString("X"));
        return IsBeingDrawn(drawObj, curPtr);
    }

    private unsafe bool IsBeingDrawn(IntPtr drawObj, IntPtr curPtr)
    {
        Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, Checking IsBeingDrawn for Ptr {curPtr} : DrawObj {drawObj}", this, curPtr.ToString("X"), drawObj.ToString("X"));
        if (ObjectKind == ObjectKind.Player)
        {
            var drawObjZero = drawObj == IntPtr.Zero;
            Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, Condition IsDrawObjZero: {cond}", this, drawObjZero);
            if (drawObjZero) return true;
            var renderFlags = (((GameObject*)curPtr)->RenderFlags) != 0x0;
            Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, Condition RenderFlags: {cond}", this, renderFlags);
            if (renderFlags) return true;
            var modelInSlotLoaded = (((CharacterBase*)drawObj)->HasModelInSlotLoaded != 0);
            Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, Condition ModelInSlotLoaded: {cond}", this, modelInSlotLoaded);
            if (modelInSlotLoaded) return true;
            var modelFilesInSlotLoaded = (((CharacterBase*)drawObj)->HasModelFilesInSlotLoaded != 0);
            Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, Condition ModelFilesInSlotLoaded: {cond}", this, modelFilesInSlotLoaded);
            if (modelFilesInSlotLoaded) return true;
            Logger.LogTrace("[{this}] IsBeingDrawnRunOnFramework, Is not being drawn", this);
            return false;
        }

        return drawObj == IntPtr.Zero
            || ((GameObject*)curPtr)->RenderFlags != 0x0;
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
        Logger.LogDebug("[{obj}] Starting Delay After Zoning", this);
        _delayedZoningTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(120), _zoningCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // ignore cancelled
            }
            finally
            {
                Logger.LogDebug("[{this}] Delay after zoning complete", this);
                _zoningCts.Dispose();
            }
        });
    }
}