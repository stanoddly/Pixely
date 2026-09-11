namespace Pixely.Observations;

/// <summary>
/// An entry that names the participant who perceived it, so a <see cref="ParticipantObservationReader{TEntry}"/>
/// can hand its consumer only the entries one participant perceived. Implement it on the entry type of a log
/// whose entries are addressed that way.
/// </summary>
public interface IObservationParticipation
{
    /// <summary>
    /// The participant that perceived this entry, as a 12 character <see cref="Base40Encoding"/> string. Two
    /// entries name the same participant when the strings are equal.
    /// </summary>
    string Perceiver { get; }
}
