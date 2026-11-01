namespace Pixely.Observations;

public static class ObservationLogExtensions
{
    /// <summary>Creates a reader positioned after the last appended entry, or where a save left it.</summary>
    /// <param name="name">
    /// Identifies the reader, so give it the consumer's own name. It must be unique within the log, because it
    /// names the reader that stopped draining when the log fills, and it is what a log restored from a save
    /// matches a reader against to put it back where it stopped.
    /// </param>
    public static ObservationReader<TEntry> CreateReader<TEntry>(this ObservationLog<TEntry> log, string name) => new(log.CreateCursor(name));

    /// <summary>Creates a reader that hands on only the entries one participant perceived.</summary>
    /// <param name="participant">The participant whose entries the reader hands on; the rest are passed over.</param>
    /// <param name="name">
    /// Identifies the reader, under the same rules as for <see cref="CreateReader{TEntry}"/>. One consumer type
    /// reading for several participants gives each reader a name that says which.
    /// </param>
    public static ParticipantObservationReader<TEntry, TParticipantId> CreateParticipantReader<TEntry, TParticipantId>(this ObservationLog<TEntry> log, TParticipantId participant, string name)
        where TEntry : IObservationParticipation<TParticipantId>
    {
        return new ParticipantObservationReader<TEntry, TParticipantId>(log.CreateCursor(name), participant);
    }
}
