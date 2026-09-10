using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Text;

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

    public static void Main()
    {
    }

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
