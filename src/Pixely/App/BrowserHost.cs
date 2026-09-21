using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Pixely.Gpu;
using SDL;

namespace Pixely.App;

/// <summary>
/// The WebGPU objects the page requested and imported into the runtime's WebGPU binding, as the pointers SDL adopts through its
/// device creation properties.
/// </summary>
internal sealed record WebGpuHandles(IntPtr Instance, IntPtr Adapter, IntPtr Device);

/// <summary>
/// Runs an app in a browser, where the page owns the frame loop: <see cref="IPixelyApp.RunFrame"/> is called once per animation frame
/// from the package's <c>pixely-host.js</c> until it returns <see langword="false"/>. Scheduling is host policy, so it stays out of
/// <see cref="IPixelyApp"/>. The generated entry point awaits <see cref="PrepareAsync"/> before building the app and
/// <see cref="RunAsync"/> after; a hand-written one can too.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class BrowserHost
{
    // Resolved against the runtime's own module URL, so the script beside _framework/ is found wherever the page is served from.
    private const string HostModuleName = "pixely-host";
    private const string HostModuleUrl = "../pixely-host.js";

    internal static WebGpuHandles? WebGpuHandles { get; private set; }

    // SDL_Quit handed over by PixelyFactory while a device destruction is pending, to run after it.
    private static Action? _deferredQuit;
    private static bool _destroyPending;

    [JSImport("runFrameLoop", HostModuleName)]
    private static partial Task RunFrameLoop([JSMarshalAs<JSType.Function<JSType.Boolean>>] Func<bool> runFrame);

    [JSImport("createGpuDevice", HostModuleName)]
    private static partial Task<JSObject> CreateGpuDevice();

    [JSImport("destroyGpuDevice", HostModuleName)]
    private static partial Task DestroyGpuDeviceAsync([JSMarshalAs<JSType.Function>] Action destroy);

    /// <summary>
    /// Imports the host module and, when the app uses the GPU, has the page request a WebGPU adapter and device for SDL to adopt.
    /// Requesting them is asynchronous and building the app resolves every singleton, the device included, so this runs before
    /// <see cref="PixelyAppBuilder.Build"/>.
    /// </summary>
    public static async Task PrepareAsync(PixelyAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
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
    }

    /// <summary>
    /// Completes with 0 when <see cref="IPixelyApp.RunFrame"/> returns <see langword="false"/>. An exception thrown by a frame rejects the
    /// loop's promise and is rethrown here as the original managed exception, so a caller's catch and finally run as they would after
    /// <see cref="IPixelyApp.Run"/>.
    /// </summary>
    public static async Task<int> RunAsync(IPixelyApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        await JSHost.ImportAsync(HostModuleName, HostModuleUrl);
        await RunFrameLoop(app.RunFrame);
        return 0;
    }

    // Destroying SDL's WebGPU device spins until every submission has drained, and the fence callbacks that drain them only run
    // once the page's event loop turns, so the page calls back to destroy the device after the queue reports idle. Nothing awaits
    // it: the app is being disposed from a synchronous Dispose, and a failure has nowhere to go but the console.
    internal static void DestroyGpuDevice(IntPtr device)
    {
        WebGpuHandles = null;
        _destroyPending = true;
        _ = DestroyGpuDeviceAsync(() =>
        {
            unsafe
            {
                SDL3.SDL_DestroyGPUDevice((SDL_GPUDevice*)device);
            }

            _destroyPending = false;
            Action? quit = _deferredQuit;
            _deferredQuit = null;
            quit?.Invoke();
        });
    }

    // SDL_Quit must follow the device destruction, and PixelyFactory is disposed before the deferred destruction runs, so it
    // hands its quit here: run after the pending destruction, or now when none is pending.
    internal static void QuitSdl(Action quit)
    {
        if (_destroyPending)
        {
            _deferredQuit = quit;
        }
        else
        {
            quit();
        }
    }
}
