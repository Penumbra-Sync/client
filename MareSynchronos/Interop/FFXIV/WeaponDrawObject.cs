using System.Runtime.InteropServices;

namespace MareSynchronos.Interop.FFXIV;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct WeaponDrawObject
{
    [FieldOffset(0x00)] public RenderModel* RenderModel;
}
