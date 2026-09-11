using System.Diagnostics.CodeAnalysis;

namespace Pixely.Observations;

/// <summary>
/// What the log tracks for one reader: its name and how far it has read. Each public reader owns one, so the log
/// sees every kind of reader the same way.
/// </summary>
internal sealed class ObservationCursor<TEntry>
{
    private readonly ObservationLog<TEntry> _log;
    private bool _disposed;

    internal ObservationCursor(ObservationLog<TEntry> log, string name)
    {
        _log = log;
        Name = name;
        log.AddCursor(this);
    }

    internal string Name { get; }

    internal long NextSequence { get; set; }

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
