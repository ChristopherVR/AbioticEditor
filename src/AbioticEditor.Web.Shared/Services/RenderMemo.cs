namespace AbioticEditor.Web.Services;

/// <summary>
/// Remembers one computed value until its key changes. Razor properties are re-evaluated every
/// time the markup mentions them, so a list that is built, filtered and sorted in a property was
/// rebuilt several times per render (once per keystroke in a search box). The key must be cheap
/// to compare: counts, strings and identity hashes, never a deep value.
/// </summary>
public sealed class RenderMemo<T>
{
    private object? _key;
    private T _value = default!;
    private bool _has;

    public T Get(object key, Func<T> factory)
    {
        if (_has && Equals(_key, key)) return _value;
        _value = factory();
        _key = key;
        _has = true;
        return _value;
    }

    /// <summary>Forget the value so the next <see cref="Get"/> recomputes it.</summary>
    public void Invalidate()
    {
        _has = false;
        _value = default!;
    }
}
