using System.Diagnostics.CodeAnalysis;

namespace Pixely.Messages;

/// <summary>
/// A reader bound to one participant. It reads through its own <see cref="MessageReader{TMessage}"/> and hands its
/// consumer only the messages that participant perceived, so nothing repeats the check at each place that reads.
/// </summary>
/// <remarks>
/// The messages other participants perceived are read and passed over, so it must still read at the same point in
/// every frame.
/// </remarks>
public sealed class ParticipantMessageReader<TMessage, TParticipantId> where TMessage : IMessageParticipation<TParticipantId>
{
    private readonly MessageReader<TMessage> _reader;
    private readonly TParticipantId _participant;

    public ParticipantMessageReader(MessageReader<TMessage> reader, TParticipantId participant)
    {
        _reader = reader;
        _participant = participant;
    }

    public bool TryRead([MaybeNullWhen(false)] out TMessage message)
    {
        while (_reader.TryRead(out message))
        {
            if (EqualityComparer<TParticipantId>.Default.Equals(message.Perceiver, _participant))
            {
                return true;
            }
        }

        message = default;
        return false;
    }
}
