namespace MareSynchronos.Utils;

public class ValueProgress<T> : Progress<T>
{
    public T? Value { get; private set; }

    protected override void OnReport(T value)
    {
        base.OnReport(value);
        Value = value;
    }

    public void Clear()
    {
        Value = default;
    }
}
