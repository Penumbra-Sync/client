using System.Runtime.InteropServices;

namespace MareSynchronos.Interop;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct MaterialData
{
    [FieldOffset(0x0)]
    public byte* Data;
}