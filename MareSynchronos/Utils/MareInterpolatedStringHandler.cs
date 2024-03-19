using System.Runtime.CompilerServices;
using System.Text;

namespace MareSynchronos.Utils;

[InterpolatedStringHandler]
public readonly ref struct MareInterpolatedStringHandler
{
    readonly StringBuilder _logMessageStringbuilder;

    public MareInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        _logMessageStringbuilder = new StringBuilder(literalLength);
    }

    public void AppendLiteral(string s)
    {
        _logMessageStringbuilder.Append(s);
    }

    public void AppendFormatted<T>(T t)
    {
        _logMessageStringbuilder.Append(t?.ToString());
    }

    public string BuildMessage() => _logMessageStringbuilder.ToString();
}
