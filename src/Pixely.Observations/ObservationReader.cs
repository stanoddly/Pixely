using System.Diagnostics.CodeAnalysis;

namespace Pixely.Observations;

/// <summary>
/// One reader's own position in an <see cref="ObservationLog{TEntry}"/>. Several readers drain the same log at
/// their own pace, each through its own instance. Dispose it when the reader goes away, otherwise it holds the
/// log's trim point where it stopped and the log fills.
/// </summary>
public sealed class ObservationReader<TEntry> : IDisposable
{
    private readonly ObservationCursor<TEntry> _cursor;

    /// <param name="name">
    /// Identifies this reader, so give it the reader's own name. It must be unique within the log, because it
    /// names the reader that stopped draining when the log fills, and it is what a log restored from a save
    /// matches a reader against to put it back where it stopped.
    /// </param>
    public ObservationReader(ObservationLog<TEntry> log, string name)
    {
        _cursor = new ObservationCursor<TEntry>(log, name);
    }

    public string Name => _cursor.Name;

    public bool TryRead([MaybeNullWhen(false)] out TEntry entry) => _cursor.TryRead(this, out entry);

    public void Dispose() => _cursor.Dispose();
}
