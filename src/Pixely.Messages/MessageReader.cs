using System.Diagnostics.CodeAnalysis;

namespace Pixely.Messages;

/// <summary>
/// One reader's own position in <see cref="Messages{TMessage}"/>. Several readers read the same messages, each
/// through its own instance. A reader starts after the last message written, and it must read at the same point in
/// every frame, in a system or an updatable.
/// </summary>
public sealed class MessageReader<TMessage>
{
    private readonly Messages<TMessage> _messages;
    private long _next;

    public MessageReader(Messages<TMessage> messages)
    {
        _messages = messages;
        _next = messages.End;
    }

    public bool TryRead([MaybeNullWhen(false)] out TMessage message) => _messages.TryRead(ref _next, out message);
}
