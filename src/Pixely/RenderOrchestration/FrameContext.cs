using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public readonly record struct FrameContext(Window Window, CommandBuffer CommandBuffer, SwapchainTexture SwapchainTexture);
