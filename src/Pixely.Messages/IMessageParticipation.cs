namespace Pixely.Messages;

/// <summary>
/// A message that names the participant who perceived it, so a
/// <see cref="ParticipantMessageReader{TMessage,TParticipantId}"/> can hand its consumer only the messages one
/// participant perceived. Implement it on the message type when messages are addressed that way.
/// </summary>
/// <typeparam name="TParticipantId">
/// The game's own participant identifier. Two are the same participant when they are equal.
/// </typeparam>
public interface IMessageParticipation<out TParticipantId>
{
    TParticipantId Perceiver { get; }
}
