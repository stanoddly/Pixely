using System.Diagnostics.CodeAnalysis;

namespace Pixely.Observations;

/// <summary>
/// One reader's own position in an <see cref="ObservationLog{TEntry}"/>. Several readers drain the same log at
/// their own pace, each through its own instance. Dispose it when the reader goes away, otherwise it holds the
/// log's trim point where it stopped and the log fills.
/// </summary>
/// <remarks>Create it with <see cref="ObservationLogExtensions.CreateReader{TEntry}"/>.</remarks>
public sealed class ObservationReader<TEntry> : IDisposable
{
    private readonly ObservationCursor<TEntry> _cursor;

    internal ObservationReader(ObservationCursor<TEntry> cursor)
    {
        _cursor = cursor;
    }

    public string Name => _cursor.Name;

    public bool TryRead([MaybeNullWhen(false)] out TEntry entry) => _cursor.TryRead(this, out entry);

    public void Dispose() => _cursor.Dispose();
}
