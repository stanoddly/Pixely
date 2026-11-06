namespace Pixely.Ecs;

// A query type argument, Optional<Span<T>> or Optional<ReadOnlySpan<T>>, that matches entities with or without T. Every entity of an archetype has the
// same components, so the span holds all rows of the current archetype or is absent for all of them.
public readonly ref struct Optional<TSpan> where TSpan : allows ref struct
{
    private readonly TSpan _value;

    internal Optional(TSpan value)
    {
        _value = value;
        HasValue = true;
    }

    public bool HasValue { get; }

    public TSpan Value => HasValue ? _value : throw new InvalidOperationException("The archetype has no component of this optional span. Check HasValue or use TryGet.");

    public bool TryGet(out TSpan value)
    {
        value = _value;
        return HasValue;
    }
}
