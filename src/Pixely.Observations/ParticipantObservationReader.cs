using System.Diagnostics.CodeAnalysis;

namespace Pixely.Observations;

/// <summary>
/// A reader bound to one participant. It holds its own position in an <see cref="ObservationLog{TEntry}"/> and
/// hands its consumer only the entries that participant perceived, so nothing repeats the check at each place
/// that drains.
/// </summary>
/// <remarks>
/// The entries other participants perceived are drained and passed over rather than left behind, which is what
/// keeps a reader bound to a quiet participant from holding the log's trim point and filling it.
/// </remarks>
public sealed class ParticipantObservationReader<TEntry, TParticipantId> : IDisposable where TEntry : IObservationParticipation<TParticipantId>
{
    private readonly ObservationCursor<TEntry> _cursor;
    private readonly TParticipantId _participant;

    /// <param name="participant">The participant whose entries this reader hands on; the rest are passed over.</param>
    /// <param name="name">
    /// Identifies this reader, under the same rules as an <see cref="ObservationReader{TEntry}"/> name. One
    /// consumer type reading for several participants gives each reader a name that says which.
    /// </param>
    public ParticipantObservationReader(ObservationLog<TEntry> log, TParticipantId participant, string name)
    {
        _cursor = new ObservationCursor<TEntry>(log, name);
        _participant = participant;
    }

    public string Name => _cursor.Name;

    public bool TryRead([MaybeNullWhen(false)] out TEntry entry)
    {
        while (_cursor.TryRead(this, out entry))
        {
            if (EqualityComparer<TParticipantId>.Default.Equals(entry.Perceiver, _participant))
            {
                return true;
            }
        }

        entry = default;
        return false;
    }

    public void Dispose() => _cursor.Dispose();
}
