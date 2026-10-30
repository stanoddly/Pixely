using System.Diagnostics.CodeAnalysis;

namespace Pixely.Messages;

/// <summary>
/// The messages of one type written in the current frame and in the previous one. Write them through a
/// <see cref="MessageWriter{TMessage}"/>, and read them through a <see cref="MessageReader{TMessage}"/>, which holds
/// its own position.
/// </summary>
/// <remarks>
/// The frame loop updates it at <see cref="UpdateOrders.Maintenance"/>, and each update drops the messages of the
/// older frame. So a reader that reads at the same point in every frame sees every message. A reader that misses
/// messages throws on its next read, then continues from the oldest message kept.
/// </remarks>
public sealed class Messages<TMessage> : IUpdatable
{
    private List<TMessage> _previous = new List<TMessage>();
    private List<TMessage> _current = new List<TMessage>();

    // The sequence number of the first message in _previous. Sequence numbers count every message ever written.
    private long _previousStart;

    int IUpdatable.UpdateOrder => UpdateOrders.Maintenance;

    internal long End => _previousStart + _previous.Count + _current.Count;

    void IUpdatable.Update()
    {
        _previousStart += _previous.Count;
        _previous.Clear();
        (_previous, _current) = (_current, _previous);
    }

    internal void Write(in TMessage message)
    {
        _current.Add(message);
    }

    internal bool TryRead(ref long next, [MaybeNullWhen(false)] out TMessage message)
    {
        if (next < _previousStart)
        {
            long missed = _previousStart - next;
            next = _previousStart;
            throw new InvalidOperationException(
                $"A reader of {typeof(TMessage).Name} missed {missed} of its messages, which were dropped before it read them. A reader must read at the same point in every frame.");
        }

        int index = (int)(next - _previousStart);
        if (index < _previous.Count)
        {
            message = _previous[index];
        }
        else if (index - _previous.Count < _current.Count)
        {
            message = _current[index - _previous.Count];
        }
        else
        {
            message = default;
            return false;
        }

        next++;
        return true;
    }
}
