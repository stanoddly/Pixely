using System.Diagnostics.CodeAnalysis;

namespace Pixely.Observations;

/// <summary>
/// One reader's position in an <see cref="ObservationLog{TEntry}"/>. Dispose it when the reader goes away,
/// otherwise it holds the log's trim point where it stopped and the log fills.
/// </summary>
public sealed class ObservationCursor<TEntry> : IDisposable
{
    private readonly ObservationLog<TEntry> _log;
    private bool _disposed;

    internal ObservationCursor(ObservationLog<TEntry> log, string name, long nextSequence)
    {
        _log = log;
        Name = name;
        NextSequence = nextSequence;
    }

    public string Name { get; }

    internal long NextSequence { get; set; }

    public bool TryRead([MaybeNullWhen(false)] out TEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _log.TryRead(this, out entry);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _log.RemoveCursor(this);
    }
}
