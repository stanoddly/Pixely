using Pixely.Content;

namespace Pixely.RenderOrchestration;

public interface IFrameCapture
{
    /// <summary>
    /// Hands the pixels of the next rendered frame to <paramref name="onCaptured"/>, on the frame loop, once that frame has been rendered.
    /// </summary>
    void CaptureNextFrame(Action<Image> onCaptured);
}
