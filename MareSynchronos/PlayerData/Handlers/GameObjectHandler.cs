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
        _getAddress = getAddress;
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

        CheckAndUpdateObject();
    }

    public IntPtr Address { get; set; }
    public unsafe Character* Character => (Character*)Address;
    public IntPtr CurrentAddress => _getAddress.Invoke();
    public Lazy<Dalamud.Game.ClientState.Objects.Types.GameObject?> GameObjectLazy { get; private set; }
    public string Name { get; private set; }
    public ObjectKind ObjectKind { get; }
    private byte[] CustomizeData { get; set; } = new byte[26];
    private IntPtr DrawObjectAddress { get; set; }
    private byte[] EquipSlotData { get; set; } = new byte[40];

    public async Task<bool> IsBeingDrawnRunOnFramework()
    {
        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            nint curPtr = IntPtr.Zero;
            try
            {
                curPtr = _getAddress.Invoke();

                if (curPtr == IntPtr.Zero) return true;

                var drawObj = GetDrawObj(curPtr);
                return IsBeingDrawn(drawObj, curPtr);
            }
            catch (Exception)
            {
                if (curPtr != IntPtr.Zero)
                {
                    return true;
                }

                return false;
            }
        }).ConfigureAwait(false);
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
            Logger.LogError(ex, "Error during checking for draw object for {name}", this);
        }

        if (curPtr != IntPtr.Zero && DrawObjectAddress != IntPtr.Zero)
        {
            if (_clearCts != null)
            {
                Logger.LogDebug("[{this}] Cancelling Clear Task", this);
                _clearCts?.Cancel();
                _clearCts = null;
            }
            bool addrDiff = Address != curPtr;
            Address = curPtr;
            if (addrDiff)
            {
                GameObjectLazy = new(() => _dalamudUtil.CreateGameObject(curPtr));
            }
            var chara = (Character*)curPtr;
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

            if ((addrDiff || equipDiff || customizeDiff || drawObjDiff || nameChange) && _isOwnedObject)
            {
                Logger.LogTrace("[{this}] Changed", this);

                Logger.LogDebug("[{this}] Sending CreateCacheObjectMessage", this);
                Mediator.Publish(new CreateCacheForObjectMessage(this));
            }
        }
        else if (Address != IntPtr.Zero || DrawObjectAddress != IntPtr.Zero)
        {
            Address = IntPtr.Zero;
            DrawObjectAddress = IntPtr.Zero;
            Logger.LogTrace("[{this}] Changed -> Null", this);
            if (_isOwnedObject && ObjectKind != ObjectKind.Player)
            {
                _clearCts?.Cancel();
                _clearCts?.Dispose();
                _clearCts = new();
                var token = _clearCts.Token;
                _ = Task.Run(() => ClearTask(token), token);
            }
        }
    }

    private async Task ClearTask(CancellationToken token)
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
        return (IntPtr)((GameObject*)curPtr)->GetDrawObject();
    }

    private unsafe bool IsBeingDrawn(IntPtr drawObj, IntPtr curPtr)
    {
        Logger.LogTrace("IsBeingDrawn for {kind} ptr {curPtr} : {drawObj}", ObjectKind, curPtr.ToString("X"), drawObj.ToString("X"));
        if (ObjectKind == ObjectKind.Player)
        {
            return drawObj == IntPtr.Zero
                           || (((CharacterBase*)drawObj)->HasModelInSlotLoaded != 0)
                           || (((CharacterBase*)drawObj)->HasModelFilesInSlotLoaded != 0)
                           || (((GameObject*)curPtr)->RenderFlags & 0b100000000000) == 0b100000000000;
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