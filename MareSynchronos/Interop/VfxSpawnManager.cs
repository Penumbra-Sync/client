using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace MareSynchronos.Interop;

/// <summary>
/// Code for spawning mostly taken from https://git.anna.lgbt/anna/OrangeGuidanceTomestone/src/branch/main/client/Vfx.cs
/// </summary>
public unsafe class VfxSpawnManager : DisposableMediatorSubscriberBase
{
    private static readonly byte[] _pool = "Client.System.Scheduler.Instance.VfxObject\0"u8.ToArray();

    [Signature("E8 ?? ?? ?? ?? F3 0F 10 35 ?? ?? ?? ?? 48 89 43 08")]
    private readonly delegate* unmanaged<byte*, byte*, VfxStruct*> _staticVfxCreate;

    [Signature("E8 ?? ?? ?? ?? ?? ?? ?? 8B 4A ?? 85 C9")]
    private readonly delegate* unmanaged<VfxStruct*, float, int, ulong> _staticVfxRun;

    [Signature("40 53 48 83 EC 20 48 8B D9 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 28 33 D2 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9")]
    private readonly delegate* unmanaged<VfxStruct*, nint> _staticVfxRemove;

    public VfxSpawnManager(ILogger<VfxSpawnManager> logger, IGameInteropProvider gameInteropProvider, MareMediator mareMediator)
        : base(logger, mareMediator)
    {
        gameInteropProvider.InitializeFromAttributes(this);
        mareMediator.Subscribe<GposeStartMessage>(this, (msg) =>
        {
            ChangeSpawnVisibility(0f);
        });
        mareMediator.Subscribe<GposeEndMessage>(this, (msg) =>
        {
            RestoreSpawnVisiblity();
        });
        mareMediator.Subscribe<CutsceneStartMessage>(this, (msg) =>
        {
            ChangeSpawnVisibility(0f);
        });
        mareMediator.Subscribe<CutsceneEndMessage>(this, (msg) =>
        {
            RestoreSpawnVisiblity();
        });
    }

    private unsafe void RestoreSpawnVisiblity()
    {
        foreach (var vfx in _spawnedObjects)
        {
            ((VfxStruct*)vfx.Value.Address)->Alpha = vfx.Value.Visibility;
        }
    }

    private unsafe void ChangeSpawnVisibility(float visibility)
    {
        foreach (var vfx in _spawnedObjects)
        {
            ((VfxStruct*)vfx.Value.Address)->Alpha = visibility;
        }
    }

    private readonly Dictionary<Guid, (nint Address, float Visibility)> _spawnedObjects = [];

    private VfxStruct* SpawnStatic(string path, Vector3 pos, Quaternion rotation, float r, float g, float b, float a, Vector3 scale)
    {
        VfxStruct* vfx;
        fixed (byte* terminatedPath = Encoding.UTF8.GetBytes(path).NullTerminate())
        {
            fixed (byte* pool = _pool)
            {
                vfx = _staticVfxCreate(terminatedPath, pool);
            }
        }

        if (vfx == null)
        {
            return null;
        }

        vfx->Position = new Vector3(pos.X, pos.Y + 1, pos.Z);
        vfx->Rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);

        vfx->SomeFlags &= 0xF7;
        vfx->Flags |= 2;
        vfx->Red = r;
        vfx->Green = g;
        vfx->Blue = b;
        vfx->Scale = scale;

        vfx->Alpha = a;

        _staticVfxRun(vfx, 0.0f, -1);

        return vfx;
    }

    public Guid? SpawnObject(Vector3 position, Quaternion rotation, Vector3 scale, float r = 1f, float g = 1f, float b = 1f, float a = 0.5f)
    {
        Logger.LogDebug("Trying to Spawn orb VFX at {pos}, {rot}", position, rotation);
        var vfx = SpawnStatic("bgcommon/world/common/vfx_for_event/eff/b0150_eext_y.avfx", position, rotation, r, g, b, a, scale);
        if (vfx == null || (nint)vfx == nint.Zero)
        {
            Logger.LogDebug("Failed to Spawn VFX at {pos}, {rot}", position, rotation);
            return null;
        }
        Guid guid = Guid.NewGuid();
        Logger.LogDebug("Spawned VFX at {pos}, {rot}: 0x{ptr:X}", position, rotation, (nint)vfx);

        _spawnedObjects[guid] = ((nint)vfx, a);

        return guid;
    }

    public unsafe void MoveObject(Guid id, Vector3 newPosition)
    {
        if (_spawnedObjects.TryGetValue(id, out var vfxValue))
        {
            if (vfxValue.Address == nint.Zero) return;
            var vfx = (VfxStruct*)vfxValue.Address;
            vfx->Position = newPosition with { Y = newPosition.Y + 1 };
            vfx->Flags |= 2;
        }
    }

    public void DespawnObject(Guid? id)
    {
        if (id == null) return;
        if (_spawnedObjects.Remove(id.Value, out var value))
        {
            Logger.LogDebug("Despawning {obj:X}", value.Address);
            _staticVfxRemove((VfxStruct*)value.Address);
        }
    }

    private void RemoveAllVfx()
    {
        foreach (var obj in _spawnedObjects.Values)
        {
            Logger.LogDebug("Despawning {obj:X}", obj);
            _staticVfxRemove((VfxStruct*)obj.Address);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            RemoveAllVfx();
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct VfxStruct
    {
        [FieldOffset(0x38)]
        public byte Flags;

        [FieldOffset(0x50)]
        public Vector3 Position;

        [FieldOffset(0x60)]
        public Quaternion Rotation;

        [FieldOffset(0x70)]
        public Vector3 Scale;

        [FieldOffset(0x128)]
        public int ActorCaster;

        [FieldOffset(0x130)]
        public int ActorTarget;

        [FieldOffset(0x1B8)]
        public int StaticCaster;

        [FieldOffset(0x1C0)]
        public int StaticTarget;

        [FieldOffset(0x248)]
        public byte SomeFlags;

        [FieldOffset(0x260)]
        public float Red;

        [FieldOffset(0x264)]
        public float Green;

        [FieldOffset(0x268)]
        public float Blue;

        [FieldOffset(0x26C)]
        public float Alpha;
    }
}
