using System.Runtime.InteropServices;
using Penumbra.Interop.Structs;

namespace MareSynchronos.Interop;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct WeaponDrawObject
{
    [FieldOffset(0x00)] public RenderModel* RenderModel;
}
