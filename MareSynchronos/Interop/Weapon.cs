using System.Runtime.InteropServices;

namespace MareSynchronos.Interop;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct Weapon
{
    [FieldOffset(0x18)] public IntPtr Parent;
    [FieldOffset(0x20)] public IntPtr NextSibling;
    [FieldOffset(0x28)] public IntPtr PreviousSibling;
    [FieldOffset(0xA8)] public WeaponDrawObject* WeaponRenderModel;
}
