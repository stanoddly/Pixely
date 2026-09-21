namespace Pixely.Gpu;

/// <summary>
/// The GPU device the app renders with is gone. A loss is terminal: the frame loop has ended and every GPU resource went with the
/// device, so the app can only present the loss and stop, which the generated <c>OnException</c> is the place for. Raised by
/// <see cref="App.BrowserHost.RunAsync"/> when the browser reports <c>device.lost</c>; <see cref="Reason"/> is the WebGPU
/// <c>GPUDeviceLostReason</c>: <c>"unknown"</c> for a GPU reset, a driver update or an eviction by the browser, <c>"destroyed"</c> for
/// a device the page destroyed.
/// </summary>
public sealed class GpuDeviceLostException : PixelyException
{
    public GpuDeviceLostException(string reason, string? message, Exception? inner)
        : base(message, inner)
    {
        Reason = reason;
    }

    public string Reason { get; }
}
