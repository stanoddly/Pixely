using System.Diagnostics.CodeAnalysis;

namespace Pixely.Observations;

/// <summary>
/// What the log tracks for one reader: its name and how far it has read. The log creates it, each public reader
/// owns one, and so the log sees every kind of reader the same way.
/// </summary>
internal sealed class ObservationCursor<TEntry>
{
    private readonly ObservationLog<TEntry> _log;
    private bool _disposed;

    internal ObservationCursor(ObservationLog<TEntry> log, string name)
    {
        _log = log;
        Name = name;
    }

    internal string Name { get; }

    internal long NextSequence { get; set; }

    // False for a cursor restored from a save until the consumer it belongs to creates its reader.
    internal bool Claimed { get; set; }

    internal bool TryRead(object owner, [MaybeNullWhen(false)] out TEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, owner);
        return _log.TryRead(this, out entry);
    }

    internal void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _log.RemoveCursor(this);
    }
}
