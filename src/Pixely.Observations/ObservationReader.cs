using System.Diagnostics.CodeAnalysis;

namespace Pixely.Observations;

/// <summary>
/// One reader's own position in an <see cref="ObservationLog{TEntry}"/>. Several readers drain the same log at
/// their own pace, each through its own instance. Dispose it when the reader goes away, otherwise it holds the
/// log's trim point where it stopped and the log fills.
/// </summary>
public sealed class ObservationReader<TEntry> : IDisposable
{
    private readonly ObservationLog<TEntry> _log;
    private bool _disposed;

    /// <param name="name">
    /// Identifies this reader when it stops draining and fills the log, so give it the reader's own name.
    /// </param>
    public ObservationReader(ObservationLog<TEntry> log, string name = "unnamed")
    {
        _log = log;
        Name = name;
        log.AddReader(this);
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
        _log.RemoveReader(this);
    }
}
