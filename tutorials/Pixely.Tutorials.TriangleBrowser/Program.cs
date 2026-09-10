using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Tutorials.Triangle;
using SDL;

[assembly: SupportedOSPlatform("browser")]

namespace Pixely.Tutorials.TriangleBrowser;

public static partial class Program
{
    private static IPixelyApp? _app;
    private static IntPtr _window;

    // dotnet.js needs an entry point, but the browser host drives everything through the exports
    // below, so this does nothing.
    public static void Main()
    {
    }

    /// <summary>
    /// Everything that has to happen before the GPU device exists. JavaScript creates the device
    /// itself, by calling SDL_CreateGPUDevice as a JSPI export: that call suspends the wasm stack
    /// while the WebGPU adapter and device futures resolve, and a suspension can only unwind to a
    /// promising export, which rules out any Mono frame underneath it. Nothing here blocks.
    /// Returns an empty string on success and the failure text otherwise, because a managed
    /// exception reaches JavaScript as an opaque marshalling error.
    /// </summary>
    [JSExport]
    public static unsafe string BeginBoot()
    {
        try
        {
            if (SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_EVENTS) == false)
            {
                return $"SDL_Init failed: {SDL3.SDL_GetError()}";
            }

            // Pixely builds a GamepadService as part of its event service. Failure is not fatal:
            // the browser may expose no gamepad support at all.
            SDL3.SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_JOYSTICK | SDL_InitFlags.SDL_INIT_GAMEPAD);

            SDL_Window* window = SDL3.SDL_CreateWindow("Triangle", 640, 480, default);
            if (window == null)
            {
                return $"SDL_CreateWindow failed: {SDL3.SDL_GetError()}";
            }

            _window = (IntPtr)window;
            return string.Empty;
        }
        catch (Exception exception)
        {
            return exception.ToString();
        }
    }

    /// <summary>
    /// Builds the application around the device JavaScript just created and the window
    /// <see cref="BeginBoot"/> left behind.
    /// </summary>
    [JSExport]
    public static unsafe string Start(IntPtr device)
    {
        try
        {
            if (device == IntPtr.Zero)
            {
                return $"SDL_CreateGPUDevice failed: {SDL3.SDL_GetError()}";
            }

            if (SDL3.SDL_ClaimWindowForGPUDevice((SDL_GPUDevice*)device, (SDL_Window*)_window) == false)
            {
                return $"SDL_ClaimWindowForGPUDevice failed: {SDL3.SDL_GetError()}";
            }

            PixelyAppBuilder builder = new();
            builder.AddSingleton(new PixelyConfig(
                EnableSdlLogging: false,
                EnableGpuValidation: false,
                GpuBackend: GpuBackend.WebGpu,
                AdoptedSdlHandles: new AdoptedSdlHandles(device, _window)));

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
