using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Text;
using SDL;

[assembly: SupportedOSPlatform("browser")]

namespace Pixely.Tutorials.ImageTextBrowser;

/// <summary>
/// What the image and the text sides of this tutorial share, so the renderer and the console
/// probes describe the same asset.
/// </summary>
public static class ImageTextProbe
{
    public const string ImagePath = "images/sample.png";
    public const string FontPath = "fonts/GohuFont-Medium.ttf";
    public const ushort FontSize = 32;
    public const string Text = "Pixely in the browser";
}

public static partial class Program
{
    private static IPixelyApp? _app;
    private static IntPtr _window;

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

            SDL_Window* window = SDL3.SDL_CreateWindow("Image and text", 640, 480, default);
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
                .ConfigureContent(contentSourceBuilder =>
                    contentSourceBuilder.AddSource(EmbeddedContentSource.Create(typeof(Program).Assembly)))
                .UseDefaultRendering(new WindowConfig(Size: (640, 480), Title: "Image and text"));

            builder.AddSingleton<IRenderer<BasicRenderContext>>(ImageTextRenderer.Create);

            _app = builder.Build();
            return string.Empty;
        }
        catch (Exception exception)
        {
            return exception.ToString();
        }
    }

    /// <summary>
    /// Decodes the PNG through SDL3_image and reports what came back. Nothing here is awaited:
    /// <see cref="IImageLoader.Load"/> is the same synchronous call the desktop makes.
    /// </summary>
    [JSExport]
    public static string ImageProbe()
    {
        try
        {
            IImageLoader imageLoader = _app!.GetRequiredService<IImageLoader>();
            using Image image = imageLoader.Load(ImageTextProbe.ImagePath);

            ReadOnlySpan<byte> data = image.Data;

            // A blank surface of the right size would also report the right size, so summarise the
            // pixels: how many are opaque, and what the first opaque one is.
            int opaqueCount = 0;
            int firstOpaque = -1;
            for (int offset = 0; offset + 3 < data.Length; offset += 4)
            {
                if (data[offset + 3] == 0xFF)
                {
                    opaqueCount++;
                    if (firstOpaque < 0)
                    {
                        firstOpaque = offset;
                    }
                }
            }

            string firstOpaqueText = firstOpaque < 0
                ? "none"
                : $"[{firstOpaque / 4 % image.Size.Width},{firstOpaque / 4 / image.Size.Width}]="
                    + $"{data[firstOpaque]},{data[firstOpaque + 1]},{data[firstOpaque + 2]},{data[firstOpaque + 3]}";

            return $"IMAGE size={image.Size.Width}x{image.Size.Height} format={image.PixelFormat} "
                + $"bytes={data.Length} opaquePixels={opaqueCount} firstOpaque={firstOpaqueText}";
        }
        catch (Exception exception)
        {
            return $"IMAGE FAILED {exception}";
        }
    }

    /// <summary>
    /// Opens the font through SDL3_ttf, measures text and rasterises it. Also synchronous.
    /// </summary>
    [JSExport]
    public static string FontProbe()
    {
        try
        {
            IFontSystem fontSystem = _app!.GetRequiredService<IFontSystem>();
            Font font = fontSystem.Load(ImageTextProbe.FontPath, ImageTextProbe.FontSize);

            ShortSize measured = fontSystem.MeasureTextSprite(ImageTextProbe.Text, font);
            ShortSize oneGlyph = fontSystem.MeasureTextSprite("M", font);
            TextSpriteAsset sprite = fontSystem.CreateTextSprite(ImageTextProbe.Text, font);

            return $"FONT size={ImageTextProbe.FontSize} measured=\"{ImageTextProbe.Text}\"->"
                + $"{measured.Width}x{measured.Height} glyph\"M\"->{oneGlyph.Width}x{oneGlyph.Height} "
                + $"rasterised={sprite.ImageRegion.Width}x{sprite.ImageRegion.Height} "
                + $"texture={sprite.Texture.Size.Width}x{sprite.Texture.Size.Height}";
        }
        catch (Exception exception)
        {
            return $"FONT FAILED {exception}";
        }
    }

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
