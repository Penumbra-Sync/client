namespace MareSynchronos.Utils;

public class ResettableLazy<T>
{
    private readonly Func<T> _lazyFunc;
    private Lazy<T> _initializedLazy;

    public ResettableLazy(Func<T> lazyFunc)
    {
        _lazyFunc = lazyFunc;
        _initializedLazy = new Lazy<T>(_lazyFunc);
    }

    public Lazy<T> Lazy => _initializedLazy;
    public T Value => _initializedLazy.Value;

    public static explicit operator ResettableLazy<T>(Func<T> func) => new(func);

    public static implicit operator T(ResettableLazy<T> l) => l.Value;

    public void Reset()
    {
        _initializedLazy = new(_lazyFunc);
    }
}