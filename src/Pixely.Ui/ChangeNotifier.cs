namespace Pixely.Ui;

/// <summary>
/// Delivers a value to subscribers and stops as soon as one of them causes a newer delivery, so a
/// subscriber is never told something an earlier one has already moved on from.
/// </summary>
/// <remarks>
/// <para>
/// A plain multicast delegate cannot do this. Once its invocation is under way there is no point at
/// which it can be told to stop, so a subscriber that reacts by changing the value again leaves
/// every subscriber after it in the list being told the old one — after the new one has already been
/// delivered to all of them by the nested call.
/// </para>
/// <para>
/// The generation, rather than the value, is what says a newer delivery happened: a subscriber that
/// moves the pointer away and back again has still superseded this delivery, and comparing values
/// would decide it had not.
/// </para>
/// </remarks>
internal sealed class ChangeNotifier<T>
{
    private Action<T>? _handlers;

    // The subscribers as an array, so delivery can stop partway through. Cached because these fire on
    // every pointer move, and asking the delegate for its invocation list each time would allocate on
    // exactly the path a retained tree exists to keep quiet.
    private Action<T>[]? _subscribers;

    private int _generation;

    internal void Add(Action<T>? handler)
    {
        _handlers += handler;
        _subscribers = null;
    }

    internal void Remove(Action<T>? handler)
    {
        _handlers -= handler;
        _subscribers = null;
    }

    internal void Notify(T value)
    {
        // Advanced before the early return, not after it. A delivery with nobody left to tell is
        // still a newer delivery, and an outer one that carried on because this looked like it never
        // happened would go on to hand its own stale value to the rest of its list.
        int generation = ++_generation;

        if (_handlers == null)
        {
            return;
        }

        _subscribers ??= Array.ConvertAll(_handlers.GetInvocationList(), handler => (Action<T>)handler);

        // This delivery goes to the subscribers there were when it began. Add and Remove drop the
        // cache rather than edit it, so a callback that subscribes is heard from the next delivery
        // on, which is what a multicast delegate does too.
        Action<T>[] subscribers = _subscribers;

        foreach (Action<T> subscriber in subscribers)
        {
            subscriber(value);

            if (_generation != generation)
            {
                return;
            }
        }
    }
}
