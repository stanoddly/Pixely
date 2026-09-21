using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Pixely.App;

/// <summary>
/// Runs an app in a browser, where the page owns the frame loop: <see cref="IPixelyApp.RunFrame"/> is called once per animation frame
/// from the package's <c>pixely-host.js</c> until it returns <see langword="false"/>. Scheduling is host policy, so it stays out of
/// <see cref="IPixelyApp"/>. The generated entry point awaits <see cref="RunAsync"/> on browser-wasm; a hand-written one can too.
/// The desktop build declares the same surface and throws <see cref="PlatformNotSupportedException"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class BrowserHost
{
#if BROWSER
    // Resolved against the runtime's own module URL, so the script beside _framework/ is found wherever the page is served from.
    private const string HostModuleName = "pixely-host";
    private const string HostModuleUrl = "../pixely-host.js";

    [JSImport("runFrameLoop", HostModuleName)]
    private static partial Task RunFrameLoop([JSMarshalAs<JSType.Function<JSType.Boolean>>] Func<bool> runFrame);
#endif

    /// <summary>
    /// Completes with 0 when <see cref="IPixelyApp.RunFrame"/> returns <see langword="false"/>. An exception thrown by a frame rejects the
    /// loop's promise and is rethrown here as the original managed exception, so a caller's catch and finally run as they would after
    /// <see cref="IPixelyApp.Run"/>.
    /// </summary>
    public static async Task<int> RunAsync(IPixelyApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
#if BROWSER
        await JSHost.ImportAsync(HostModuleName, HostModuleUrl);
        await RunFrameLoop(app.RunFrame);
        return 0;
#else
        throw new PlatformNotSupportedException("BrowserHost runs an app in a browser; build for browser-wasm.");
#endif
    }
}
