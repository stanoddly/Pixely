using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pixely.App;
using Pixely.DependencyInjection;

namespace BrowserLoopConsumer;

// A hand-written browser Main around a fake app, so BrowserHost and pixely-host.js run under node without SDL: the loop
// ends after three frames, or the third frame throws when PackageIntegrationTests defines BROWSER_LOOP_THROWS. With
// BROWSER_LOOP_NATIVE the frame limit comes from native.c, relinked into the runtime through NativeFileReference.
static class Program
{
    [SupportedOSPlatform("browser")]
    private static async Task<int> Main()
    {
        FrameApp app = new();
        try
        {
            int exitCode = await BrowserHost.RunAsync(app);
            Console.WriteLine($"Loop ended after {app.Frames} frames with {exitCode}.");
            return exitCode + 40;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Caught {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
        finally
        {
            app.Dispose();
        }
    }
}

sealed partial class FrameApp : IPixelyApp
{
#if BROWSER_LOOP_NATIVE
    [LibraryImport("native", EntryPoint = "pixely_native_frame_limit")]
    private static partial int NativeFrameLimit();

    private static readonly int FrameLimit = NativeFrameLimit();
#else
    private const int FrameLimit = 3;
#endif

    public int Frames { get; private set; }

    public ServiceProvider ServiceProvider => throw new NotSupportedException();

    public T GetRequiredService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>() where T : class => throw new NotSupportedException();

    public int Run() => throw new NotSupportedException();

    public bool RunFrame()
    {
        Frames++;
#if BROWSER_LOOP_THROWS
        if (Frames == 3)
        {
            throw new InvalidOperationException("Frame 3 failed on purpose.");
        }
#endif
        Console.WriteLine($"Frame {Frames} of {FrameLimit}.");
        return Frames < FrameLimit;
    }

    public void Dispose()
    {
        Console.WriteLine("Disposed.");
    }
}
