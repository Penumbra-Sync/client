using System.Numerics;

namespace MareSynchronos.UI3.Themes.Colors;

public record Color
{
    public Color(byte r, byte g, byte b, byte a)
    {
        Vector = new(r / 255f, g / 255f, b / 255f, a / 255f);
        Uint = ToColorUint(new Vector4(r, g, b, a));
    }

    protected Vector4 Vector { get; }
    protected uint Uint { get; }
    private static uint ToColorUint(Vector4 vec)
    {
        uint ret = (byte)vec.W;
        ret <<= 8;
        ret += (byte)vec.Z;
        ret <<= 8;
        ret += (byte)vec.Y;
        ret <<= 8;
        ret += (byte)vec.X;
        return ret;
    }

    public static explicit operator Vector4(Color c) => c.Vector;
    public static implicit operator uint(Color c) => c.Uint;
}