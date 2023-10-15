using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace MareSynchronos.Interop;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct RenderModel
{
    [FieldOffset(0x18)]
    public RenderModel* PreviousModel;

    [FieldOffset(0x20)]
    public RenderModel* NextModel;

    [FieldOffset(0x30)]
    public ResourceHandle* ResourceHandle;

    [FieldOffset(0x40)]
    public Skeleton* Skeleton;

    [FieldOffset(0x58)]
    public void** BoneList;

    [FieldOffset(0x60)]
    public int BoneListCount;

    [FieldOffset(0x98)]
    public void** Materials;

    [FieldOffset(0xA0)]
    public int MaterialCount;
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct Weapon
{
    [FieldOffset(0x18)] public IntPtr Parent;
    [FieldOffset(0x20)] public IntPtr NextSibling;
    [FieldOffset(0x28)] public IntPtr PreviousSibling;
    [FieldOffset(0xA8)] public WeaponDrawObject* WeaponRenderModel;
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct WeaponDrawObject
{
    [FieldOffset(0x00)] public RenderModel* RenderModel;
}