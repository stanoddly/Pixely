namespace Pixely.Input;

internal sealed class OrderedEventHandlers<TEventArgs>
    where TEventArgs : ConsumableInputEventArgs
{
    private readonly List<(int Order, InputEventHandler<TEventArgs> Handler)> _handlers = new();
    private bool _dirty;

    public void Add(int order, InputEventHandler<TEventArgs> handler)
    {
        _handlers.Add((order, handler));
        _dirty = true;
    }

    public void Remove(InputEventHandler<TEventArgs> handler)
    {
        _handlers.RemoveAll(entry => entry.Handler == handler);
    }

    public void Invoke(TEventArgs eventArgs)
    {
        if (_dirty)
        {
            _handlers.StableSort(static entry => entry.Order);
            _dirty = false;
        }

        eventArgs.Consumed = false;

        foreach ((_, InputEventHandler<TEventArgs> handler) in _handlers)
        {
            handler(eventArgs);

            if (eventArgs.Consumed)
            {
                break;
            }
        }
    }
}

internal sealed class ViewScopedOrderedEventHandlers<TEventArgs>
    where TEventArgs : ConsumableInputEventArgs
{
    private readonly List<(
        ViewScope ViewScope,
        int Order,
        InputEventHandler<TEventArgs> Handler)> _handlers = new();
    private bool _dirty;

    public void Add(
        ViewScope viewScope,
        int order,
        InputEventHandler<TEventArgs> handler)
    {
        _handlers.Add((viewScope, order, handler));
        _dirty = true;
    }

    public void Remove(
        ViewScope viewScope,
        InputEventHandler<TEventArgs> handler)
    {
        _handlers.RemoveAll(entry =>
            entry.ViewScope == viewScope && entry.Handler == handler);
    }

    public void Invoke(
        ViewScope viewScope,
        TEventArgs eventArgs)
    {
        if (_dirty)
        {
            _handlers.StableSort(static entry => entry.Order);
            _dirty = false;
        }

        eventArgs.Consumed = false;

        foreach ((ViewScope registeredViewScope, _, InputEventHandler<TEventArgs> handler) in _handlers)
        {
            if (registeredViewScope != viewScope)
            {
                continue;
            }

            handler(eventArgs);

            if (eventArgs.Consumed)
            {
                break;
            }
        }
    }
}
