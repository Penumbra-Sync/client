using System.Numerics;

namespace MareSynchronos.UI3.Themes;

public record StyleValue<T>
{
    public bool IsVector => Value != null;
    public StyleValue(T value)
    {
        Value = value;
    }

    public T Value { get; }

    public static explicit operator T(StyleValue<T> s) => s.Value;
}