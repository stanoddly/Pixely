namespace Pixely.RenderOrchestration;

/// <summary>
/// The render order the framework's own renderers draw at. A game places its own renderers relative
/// to these, for example <c>RenderOrders.Ui + 1</c> to draw over the UI. Renderers with the same
/// order draw in registration order.
/// </summary>
public static class RenderOrders
{
    /// <summary>What a renderer draws at when it does not choose.</summary>
    public const int Default = 0;

    /// <summary>UI, drawn over the game.</summary>
    public const int Ui = 10_000;
}
