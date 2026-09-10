namespace Pixely;

/// <summary>
/// The update order the framework's own updatables run at. A game places its own updatables relative
/// to these, for example <c>UpdateOrders.Ui - 1</c> to settle state before the UI tree is built.
/// Updatables with the same order run in registration order.
/// </summary>
public static class UpdateOrders
{
    /// <summary>Frame instrumentation, before anything the frame does. Used by <c>PerformanceTracker</c>.</summary>
    public const int Diagnostics = -20_000;

    /// <summary>What an updatable runs at when it does not choose. Used by <c>TimerSystem</c> and <c>UpdateSystem</c>.</summary>
    public const int Default = 0;

    /// <summary>UI tree building, after the game has settled the state the UI reads. Used by Pixely.Ui and Pencuil.</summary>
    public const int Ui = 10_000;

    /// <summary>Housekeeping that must see the finished frame, such as trimming caches. Used by <c>FontSystem</c>.</summary>
    public const int Maintenance = 20_000;
}
