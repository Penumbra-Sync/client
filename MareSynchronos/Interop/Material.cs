using System.Runtime.InteropServices;

namespace MareSynchronos.Interop;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct Material
{
    [FieldOffset(0x10)]
    public ResourceHandle* ResourceHandle;
}
