using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace MareSynchronos.Interop.FFXIV;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct CharaExt
{
    [FieldOffset(0x0)] public Character Character;
    [FieldOffset(0x650)] public Character* Mount;
}
