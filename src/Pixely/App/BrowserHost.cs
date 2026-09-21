using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Pixely.Gpu;

namespace Pixely.App;

#if BROWSER
/// <summary>
/// The WebGPU objects the page requested and imported into the runtime's WebGPU binding, as the pointers SDL adopts through its
/// device creation properties.
/// </summary>
internal sealed record WebGpuHandles(IntPtr Instance, IntPtr Adapter, IntPtr Device);
#endif

/// <summary>
/// Runs an app in a browser, where the page owns the frame loop: <see cref="IPixelyApp.RunFrame"/> is called once per animation frame
/// from the package's <c>pixely-host.js</c> until it returns <see langword="false"/>. Scheduling is host policy, so it stays out of
/// <see cref="IPixelyApp"/>. The generated entry point awaits <see cref="PrepareAsync"/> before building the app and
/// <see cref="RunAsync"/> after; a hand-written one can too. The desktop build declares the same surface and throws
/// <see cref="PlatformNotSupportedException"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class BrowserHost
{
#if BROWSER
    // Resolved against the runtime's own module URL, so the script beside _framework/ is found wherever the page is served from.
    private const string HostModuleName = "pixely-host";
    private const string HostModuleUrl = "../pixely-host.js";

    internal static WebGpuHandles? WebGpuHandles { get; private set; }

    [JSImport("runFrameLoop", HostModuleName)]
    private static partial Task RunFrameLoop([JSMarshalAs<JSType.Function<JSType.Boolean>>] Func<bool> runFrame);

    [JSImport("createGpuDevice", HostModuleName)]
    private static partial Task<JSObject> CreateGpuDevice();

    [JSImport("waitForGpuIdle", HostModuleName)]
    private static partial Task WaitForGpuIdle();

    [JSImport("releaseGpuDevice", HostModuleName)]
    private static partial void ReleaseGpuDeviceHandles();
#endif

    /// <summary>
    /// Imports the host module and, when the app uses the GPU, has the page request a WebGPU adapter and device for SDL to adopt.
    /// Requesting them is asynchronous and building the app resolves every singleton, the device included, so this runs before
    /// <see cref="PixelyAppBuilder.Build"/>.
    /// </summary>
    public static async Task PrepareAsync(PixelyAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
#if BROWSER
        await JSHost.ImportAsync(HostModuleName, HostModuleUrl);
        if (!builder.IsRegistered<GpuDevice>())
        {
            return;
        }

        using JSObject handles = await CreateGpuDevice();
        WebGpuHandles = new WebGpuHandles(
            (IntPtr)handles.GetPropertyAsInt32("instance"),
            (IntPtr)handles.GetPropertyAsInt32("adapter"),
            (IntPtr)handles.GetPropertyAsInt32("device"));
#else
        throw new PlatformNotSupportedException("BrowserHost runs an app in a browser; build for browser-wasm.");
#endif
    }

    /// <summary>
    /// Completes with 0 when <see cref="IPixelyApp.RunFrame"/> returns <see langword="false"/>. An exception thrown by a frame rejects the
    /// loop's promise and is rethrown here as the original managed exception, so a caller's catch and finally run as they would after
    /// <see cref="IPixelyApp.Run"/>. Before returning either way it waits for the GPU queue to drain: destroying SDL's WebGPU device
    /// spins until every submission has completed, and a submission completes only after the page's event loop turns, which the
    /// caller's synchronous Dispose cannot wait for.
    /// </summary>
    public static async Task<int> RunAsync(IPixelyApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
#if BROWSER
        await JSHost.ImportAsync(HostModuleName, HostModuleUrl);
        try
        {
            await RunFrameLoop(app.RunFrame);
        }
        finally
        {
            await WaitForGpuIdle();
        }

        return 0;
#else
        throw new PlatformNotSupportedException("BrowserHost runs an app in a browser; build for browser-wasm.");
#endif
    }

#if BROWSER
    // After SDL_DestroyGPUDevice, which dropped SDL's own references: the page drops its references and destroys the WebGPU device.
    internal static void ReleaseGpuDevice()
    {
        WebGpuHandles = null;
        ReleaseGpuDeviceHandles();
    }
#endif
}
