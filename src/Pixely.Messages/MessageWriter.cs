namespace Pixely.Messages;

/// <summary>
/// Writes to <see cref="Messages{TMessage}"/>. Take it where code writes messages, so nothing that writes can also
/// read.
/// </summary>
public sealed class MessageWriter<TMessage>
{
    private readonly Messages<TMessage> _messages;

    public MessageWriter(Messages<TMessage> messages)
    {
        _messages = messages;
    }

    public void Write(in TMessage message)
    {
        _messages.Write(message);
    }
}
