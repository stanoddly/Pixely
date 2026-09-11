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
public sealed class ParticipantObservationReader<TEntry> : IDisposable where TEntry : IObservationParticipation
{
    private readonly ObservationReader<TEntry> _reader;
    private readonly string _participant;

    /// <param name="participant">
    /// The participant whose entries this reader hands on; the rest are passed over. It has to be a 12 character
    /// <see cref="Base40Encoding"/> string, which is what lets it be part of the reader's name.
    /// </param>
    /// <param name="name">
    /// Names the consumer. The reader is named after it and the participant together, so one consumer type may
    /// read for several participants on the same log.
    /// </param>
    public ParticipantObservationReader(ObservationLog<TEntry> log, string participant, string name)
    {
        if (!Base40Encoding.TryDecode(participant, out ulong _))
        {
            throw new ArgumentException($"Participant '{participant}' is not a 12 character base-40 string.", nameof(participant));
        }

        _reader = new ObservationReader<TEntry>(log, $"{name}:{participant}");
        _participant = participant;
    }

    public bool TryRead([MaybeNullWhen(false)] out TEntry entry)
    {
        while (_reader.TryRead(out entry))
        {
            if (string.Equals(entry.Perceiver, _participant, StringComparison.Ordinal))
            {
                return true;
            }
        }

        entry = default;
        return false;
    }

    public void Dispose() => _reader.Dispose();
}
