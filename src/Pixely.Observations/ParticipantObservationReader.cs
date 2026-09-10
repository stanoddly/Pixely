using System.Diagnostics.CodeAnalysis;

namespace Pixely.Observations;

/// <summary>
/// A reader bound to one participant. It reads an <see cref="ObservationLog{TEntry}"/> through a reader of its
/// own and hands its consumer only the entries that participant perceived, so nothing repeats the check at each
/// place that drains.
/// </summary>
/// <remarks>
/// The entries other participants perceived are drained and passed over rather than left behind, which is what
/// keeps a reader bound to a quiet participant from holding the log's trim point and filling it.
/// </remarks>
public sealed class ParticipantObservationReader<TEntry, TParticipantId> : IDisposable where TEntry : IObservationParticipation<TParticipantId>
{
    private readonly ObservationReader<TEntry> _reader;
    private readonly TParticipantId _participant;

    /// <param name="participant">The participant whose entries this reader hands on; the rest are passed over.</param>
    /// <param name="name">
    /// Identifies this reader when it stops draining and fills the log, so give it the consumer's own name.
    /// </param>
    public ParticipantObservationReader(ObservationLog<TEntry> log, TParticipantId participant, string name = "unnamed")
    {
        _reader = new ObservationReader<TEntry>(log, name);
        _participant = participant;
    }

    public bool TryRead([MaybeNullWhen(false)] out TEntry entry)
    {
        while (_reader.TryRead(out entry))
        {
            if (EqualityComparer<TParticipantId>.Default.Equals(entry.Perceiver, _participant))
            {
                return true;
            }
        }

        entry = default;
        return false;
    }

    public void Dispose() => _reader.Dispose();
}
