using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Tutorials.Triangle;

[assembly: SupportedOSPlatform("browser")]

namespace Pixely.Tutorials.TriangleBrowser;

public static partial class Program
{
    private static IPixelyApp? _app;

    // dotnet.js needs an entry point, but the browser host drives everything through the exports
    // below, so this does nothing.
    public static void Main()
    {
    }

    /// <summary>
    /// Builds the application around the SDL objects the boot shim already created. Returns an
    /// empty string on success and the failure text otherwise, because a managed exception reaches
    /// JavaScript as an opaque marshalling error.
    /// </summary>
    [JSExport]
    public static string Start()
    {
        try
        {
            if (Boot.State() != 2)
            {
                return $"boot shim failed: {Boot.Error()}";
            }

            PixelyAppBuilder builder = new();
            builder.AddSingleton(new PixelyConfig(
                EnableSdlLogging: false,
                EnableGpuValidation: false,
                GpuBackend: GpuBackend.WebGpu,
                AdoptedSdlHandles: new AdoptedSdlHandles(Boot.Device(), Boot.Window())));

            builder
                // The filesystem the default content probe walks does not exist in the browser, so
                // the shaders travel inside the assembly instead.
                .ConfigureContent(contentSourceBuilder =>
                    contentSourceBuilder.AddSource(EmbeddedContentSource.Create(typeof(Program).Assembly)))
                .UseDefaultRendering(new WindowConfig(Size: (640, 480), Title: "Triangle"));

            builder.AddSingleton<IRenderer<BasicRenderContext>>(TriangleRenderer.Create);

            _app = builder.Build();
            return string.Empty;
        }
        catch (Exception exception)
        {
            return exception.ToString();
        }
    }

    /// <summary>
    /// Runs one frame. requestAnimationFrame drives this; <see cref="IPixelyApp.Run"/> cannot be
    /// used because its loop never returns to the browser's event loop.
    /// </summary>
    [JSExport]
    public static string Frame()
    {
        try
        {
            _app!.RunFrame();
            return string.Empty;
        }
        catch (Exception exception)
        {
            return exception.ToString();
        }
    }

    [JSExport]
    public static string GpuDriver()
    {
        return _app!.GetRequiredService<GpuDevice>().Driver;
    }
}

/// <summary>
/// The C shim that ran SDL_Init, SDL_CreateWindow, SDL_CreateGPUDevice and
/// SDL_ClaimWindowForGPUDevice from JavaScript, before any managed frame existed.
/// </summary>
internal static class Boot
{
    [DllImport("pixelyboot", EntryPoint = "pixely_gpu_state")]
    public static extern int State();

    [DllImport("pixelyboot", EntryPoint = "pixely_gpu_device")]
    public static extern IntPtr Device();

    [DllImport("pixelyboot", EntryPoint = "pixely_gpu_window")]
    public static extern IntPtr Window();

    [DllImport("pixelyboot", EntryPoint = "pixely_gpu_error")]
    private static extern IntPtr GetError();

    public static string Error() => Marshal.PtrToStringUTF8(GetError()) ?? "(none)";
}
