using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely;

/// <summary>
/// A window whose frames are presented through the swapchain of the GPU device it is claimed for. It is claimed the first time
/// <see cref="ColorTargetFormat"/> or <see cref="TryWaitAndAcquireSwapchainTexture"/> is used, so a window nothing renders to
/// stays unclaimed, and on the desktop the claim is released on a frame no render coordinator runs. Without a GPU device, when
/// the app registers no rendering, it has no swapchain, and both throw.
/// </summary>
public sealed partial class SwapchainWindow : Window
{
    internal Pointer<SDL_GPUDevice> SdlGpuDevice { get; }

    private bool _claimed;
    // Kept after a release, so building a pipeline from the format does not claim the window again.
    private TextureFormat? _colorTargetFormat;

    internal SwapchainWindow(
        ViewScope viewScope,
        Pointer<SDL_Window> sdlWindow,
        Pointer<SDL_GPUDevice> sdlGpuDevice,
        uint sdlId,
        PixelyFrameClock frameClock,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior)
        : base(viewScope, sdlWindow, sdlId, frameClock, platformInfo, closeBehavior)
    {
        SdlGpuDevice = sdlGpuDevice;
    }

    public override TextureFormat ColorTargetFormat
    {
        get
        {
            if (_colorTargetFormat is TextureFormat colorTargetFormat)
            {
                return colorTargetFormat;
            }

            EnsureClaimed();
            return _colorTargetFormat!.Value;
        }
    }

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
    {
        ThrowIfNoGpuDevice();
        swapchainTexture = default!;
        // A frame of a minimized window is not drawn anyway, and an unclaimed window needs no request for SDL's Vulkan backend
        // to free finished work. See EnsureClaimed for why a minimized window is not claimed. While it stays unclaimed, another
        // window claimed without a coordinator of its own, only by reading its ColorTargetFormat, keeps Vulkan from freeing
        // finished work, since no submit requests a swapchain texture.
        if (!_claimed && IsMinimized)
        {
            return false;
        }

        EnsureClaimed();
        uint width, height;

        unsafe
        {
            SDL_GPUTexture* swapchainTexturePointer;
            if (!AcquireSwapchainTexture(commandBuffer.SdlGpuCommandBuffer, &swapchainTexturePointer, &width, &height))
            {
                throw new PixelyInitializationException($"{AcquireSwapchainTextureCall} failed: {SDL3.SDL_GetError()}");
            }

            if (swapchainTexturePointer == null)
            {
                return false;
            }

            swapchainTexture = new SwapchainTexture(swapchainTexturePointer, new ShortSize((ushort)width, (ushort)height), _colorTargetFormat!.Value);
        }

        return true;
    }

    public override void Dispose()
    {
        ReleaseClaim();
        base.Dispose();
    }

    // Called by the frame loop on a frame no render coordinator runs, so a window that stopped being rendered, such as one
    // whose rendering stage was unloaded, does not keep SDL's Vulkan backend from freeing finished work; see EnsureClaimed.
    // SDL waits for the device to go idle before it releases a window, so this waits once each time rendering stops. The next
    // use of the swapchain claims the window again, which recreates the swapchain.
    internal void ReleaseClaim()
    {
        if (!_claimed)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseWindowFromGPUDevice(SdlGpuDevice, SdlWindow);
        }

        _claimed = false;
    }

    private unsafe bool IsMinimized => (SDL3.SDL_GetWindowFlags(SdlWindow) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0;

    // The claim waits for the swapchain's first use because a claimed window changes when SDL's Vulkan backend frees finished
    // GPU work: only on a submit whose command buffer requested a swapchain texture, or on a fence wait. With no window claimed
    // it frees finished work on every submit. A window nothing renders to, as in an app with UseGpu() but no window rendering,
    // would never request a texture, so it would keep every finished submission and every buffer copy it holds alive.
    //
    // On a driver that reports a zero extent for a minimized window, such as NVIDIA on Win32, SDL's Vulkan claim returns true
    // without claiming the window: it stores no window data, so reading the swapchain format returns INVALID and an acquire
    // fails as for an unclaimed window. The acquire therefore does not claim a minimized window. ColorTargetFormat must
    // return a format, so it still claims one, and SDL's minimized flag is set only once SDL processes the event, after the
    // driver may already report a zero extent. The format is read after every claim to catch both: the window stays unclaimed
    // and the claim throws. A later claim, once the window is restored, can succeed. Each failed claim leaks SDL's window data
    // and its Vulkan surface, which SDL never stored where a release would find them.
    //
    // The format is cached after the first claim. A window claimed again keeps its device and the SDR composition SDL's claim
    // defaults to, so SDL picks the same format for it.
    private void EnsureClaimed()
    {
        ThrowIfNoGpuDevice();
        if (_claimed)
        {
            return;
        }

        TextureFormat colorTargetFormat;
        unsafe
        {
            if (!SDL3.SDL_ClaimWindowForGPUDevice(SdlGpuDevice, SdlWindow))
            {
                throw new PixelyInitializationException($"SDL_ClaimWindowForGPUDevice failed: {SDL3.SDL_GetError()}");
            }

            colorTargetFormat = (TextureFormat)SDL3.SDL_GetGPUSwapchainTextureFormat(SdlGpuDevice, SdlWindow);
        }

        if (colorTargetFormat == TextureFormat.None)
        {
            throw new PixelyInitializationException($"SDL claimed the window for the GPU device but reports no swapchain format, as its Vulkan backend does for a minimized window on some drivers: {SDL3.SDL_GetError()}");
        }

        _claimed = true;
        _colorTargetFormat = colorTargetFormat;
    }

    private void ThrowIfNoGpuDevice()
    {
        if (SdlGpuDevice.IsNull)
        {
            throw new InvalidOperationException("The window has no GPU device. Register rendering with UseDefaultRendering or call UseGpu().");
        }
    }
}
