using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Cursor;

static partial class Program
{
    private const ushort CursorSize = 32;

    static void Configure(PixelyAppBuilder builder)
    {
        builder.UseDefaultRendering(new WindowConfig(Size: (640, 480), Title: "Cursor"));

        builder.OnBuilt((ICursorService cursorService, IKeyboardService keyboardService, AppControl appControl) =>
        {
            Vector2Int center = new(CursorSize / 2, CursorSize / 2);
            Input.Cursor ring = cursorService.CreateCursor(CreateRing(radius: 12, red: 255, green: 64, blue: 64), center);
            CursorFrame[] pulse = Enumerable.Range(0, 8)
                .Select(index => new CursorFrame(CreateRing(radius: 4 + Math.Min(index, 8 - index) * 3, red: 64, green: 160, blue: 255), TimeSpan.FromMilliseconds(80)))
                .ToArray();
            Input.Cursor pulsingRing = cursorService.CreateCursor(pulse, center);
            Input.Cursor? pointer = null;

            Console.WriteLine("1: system pointer, 2: image, 3: animated image, 0: default, H: hide or show, Escape: quit.");

            keyboardService.KeyDown += eventArgs =>
            {
                switch (eventArgs.Key)
                {
                    case VirtualKey.Number1:
                        // Video drivers without system cursors, such as SDL's dummy driver, fail to create one.
                        try
                        {
                            pointer ??= cursorService.CreateSystemCursor(SystemCursor.Pointer);
                            cursorService.SetCursor(pointer);
                        }
                        catch (PixelyException exception)
                        {
                            Console.WriteLine(exception.Message);
                        }
                        break;
                    case VirtualKey.Number2:
                        cursorService.SetCursor(ring);
                        break;
                    case VirtualKey.Number3:
                        cursorService.SetCursor(pulsingRing);
                        break;
                    case VirtualKey.Number0:
                        cursorService.ResetCursor();
                        break;
                    case VirtualKey.H:
                        cursorService.IsCursorVisible = !cursorService.IsCursorVisible;
                        break;
                    case VirtualKey.Escape:
                        appControl.Quit();
                        break;
                }
            };
        });
    }

    private static RawImage CreateRing(int radius, byte red, byte green, byte blue)
    {
        byte[] pixels = new byte[CursorSize * CursorSize * 4];
        for (int y = 0; y < CursorSize; y++)
        {
            for (int x = 0; x < CursorSize; x++)
            {
                float distance = MathF.Sqrt(MathF.Pow(x + 0.5f - CursorSize / 2, 2) + MathF.Pow(y + 0.5f - CursorSize / 2, 2));
                if (distance <= radius && distance >= radius - 2)
                {
                    int offset = (y * CursorSize + x) * 4;
                    pixels[offset] = red;
                    pixels[offset + 1] = green;
                    pixels[offset + 2] = blue;
                    pixels[offset + 3] = 255;
                }
            }
        }

        // Abgr8888 is a packed format, so on a little-endian machine its bytes are red, green, blue and alpha.
        return new RawImage(pixels, new ShortSize(CursorSize, CursorSize), PixelFormat.Abgr8888);
    }
}
