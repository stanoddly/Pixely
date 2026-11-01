namespace Pixely.Observations;

/// <summary>
/// An entry that names the participant who perceived it, so a
/// <see cref="ParticipantObservationReader{TEntry,TParticipantId}"/> can hand its consumer only the entries one
/// participant perceived. Implement it on the entry type of a log whose entries are addressed that way.
/// </summary>
/// <typeparam name="TParticipantId">
/// The game's own participant identifier. Two are the same participant when they are equal.
/// </typeparam>
public interface IObservationParticipation<out TParticipantId>
{
    TParticipantId Perceiver { get; }
}
