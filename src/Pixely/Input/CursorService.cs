using Pixely.Content;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Input;

// SDL has one active cursor for the whole application and allows cursor calls on the main thread only. The service owns every
// cursor it creates and destroys those still alive when it is disposed, which happens before PixelyFactory shuts SDL down.
public sealed class CursorService : ICursorService, IDisposable
{
    private readonly HashSet<Cursor> _cursors = new();
    private bool _disposed;

    internal CursorService()
    {
    }

    public bool IsCursorVisible
    {
        get
        {
            ThrowIfUnusable();
            return SDL_CursorVisible();
        }
        set
        {
            ThrowIfUnusable();
            if (value ? !SDL_ShowCursor() : !SDL_HideCursor())
            {
                throw new PixelyException($"{(value ? "SDL_ShowCursor" : "SDL_HideCursor")} failed: {SdlError.GetError()}");
            }
        }
    }

    public Cursor CreateSystemCursor(SystemCursor shape)
    {
        ThrowIfUnusable();
        if (!Enum.IsDefined(shape))
        {
            throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown system cursor.");
        }

        unsafe
        {
            return Track(SDL_CreateSystemCursor((SDL_SystemCursor)shape), "SDL_CreateSystemCursor");
        }
    }

    public Cursor CreateCursor(Image image, Vector2Int hotspot)
    {
        ArgumentNullException.ThrowIfNull(image);
        return CreateCursor([new CursorFrame(image, TimeSpan.Zero)], hotspot);
    }

    public Cursor CreateCursor(ReadOnlySpan<CursorFrame> frames, Vector2Int hotspot)
    {
        ThrowIfUnusable();
        if (frames.IsEmpty)
        {
            throw new ArgumentException("A cursor needs at least one frame.", nameof(frames));
        }

        SDL_CursorFrameInfo[] frameInfos = new SDL_CursorFrameInfo[frames.Length];
        ShortSize size = default;
        for (int i = 0; i < frames.Length; i++)
        {
            Image? image = frames[i].Image;
            if (image == null)
            {
                throw new ArgumentException($"Frame {i} has no image.", nameof(frames));
            }

            if (i == 0)
            {
                size = image.Size;
            }
            else if (image.Size != size)
            {
                throw new ArgumentException($"All frames of a cursor must have the same size, but frame {i} is {image.Size.Width}x{image.Size.Height} and frame 0 is {size.Width}x{size.Height}.", nameof(frames));
            }

            frameInfos[i].duration = ToMilliseconds(frames[i].Duration, i);
        }

        if (hotspot.X < 0 || hotspot.Y < 0 || hotspot.X >= size.Width || hotspot.Y >= size.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(hotspot), hotspot, $"The hotspot must lie within the {size.Width}x{size.Height} cursor image.");
        }

        return Track(CreateAnimatedCursor(frames, frameInfos, 0, hotspot), "SDL_CreateAnimatedCursor");
    }

    // A surface from SDL_CreateSurfaceFrom borrows the pixels, so every frame stays pinned until SDL_CreateAnimatedCursor returns.
    // C# pins a span only for the scope of a fixed statement, so each call pins one frame and recurses for the next.
    // No video driver keeps a borrowed surface after the call: Windows duplicates it, and the others copy the pixels during the call.
    private static unsafe Pointer<SDL_Cursor> CreateAnimatedCursor(ReadOnlySpan<CursorFrame> frames, SDL_CursorFrameInfo[] frameInfos, int index, Vector2Int hotspot)
    {
        if (index == frames.Length)
        {
            fixed (SDL_CursorFrameInfo* frameInfosPointer = frameInfos)
            {
                // SDL makes a static cursor out of one frame.
                return SDL_CreateAnimatedCursor(frameInfosPointer, frameInfos.Length, hotspot.X, hotspot.Y);
            }
        }

        Image image = frames[index].Image;
        int pitch = ImageSurfaces.GetPackedPitch(image);
        fixed (byte* pixels = image.Data)
        {
            Pointer<SDL_Surface> surface = SDL_CreateSurfaceFrom(image.Size.Width, image.Size.Height, (SDL_PixelFormat)image.PixelFormat, (IntPtr)pixels, pitch);
            if (surface.IsNull)
            {
                throw new PixelyException($"SDL_CreateSurfaceFrom failed: {SdlError.GetError()}");
            }

            frameInfos[index].surface = surface;
            try
            {
                return CreateAnimatedCursor(frames, frameInfos, index + 1, hotspot);
            }
            finally
            {
                SDL_DestroySurface(surface);
            }
        }
    }

    public void SetCursor(Cursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ThrowIfUnusable();
        if (cursor.Owner != this)
        {
            throw new ArgumentException("The cursor was created by another cursor service.", nameof(cursor));
        }

        unsafe
        {
            if (!SDL_SetCursor(cursor.Handle))
            {
                throw new PixelyException($"SDL_SetCursor failed: {SdlError.GetError()}");
            }
        }
    }

    public void ResetCursor()
    {
        ThrowIfUnusable();
        unsafe
        {
            Pointer<SDL_Cursor> defaultCursor = SDL_GetDefaultCursor();
            if (defaultCursor.IsNull)
            {
                throw new PixelyException($"SDL_GetDefaultCursor failed: {SdlError.GetError()}");
            }

            if (!SDL_SetCursor(defaultCursor))
            {
                throw new PixelyException($"SDL_SetCursor failed: {SdlError.GetError()}");
            }
        }
    }

    // SDL reads 0 ms as the frame the animation stops on, so a positive duration rounds up to whole milliseconds, never down to 0.
    internal static uint ToMilliseconds(TimeSpan duration, int frameIndex)
    {
        if (duration < TimeSpan.Zero || duration.Ticks > uint.MaxValue * TimeSpan.TicksPerMillisecond)
        {
            throw new ArgumentOutOfRangeException("frames", duration, $"The duration of frame {frameIndex} must be between zero and {uint.MaxValue} ms.");
        }

        return (uint)((duration.Ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond);
    }

    internal void Destroy(Cursor cursor)
    {
        if (cursor.IsDisposed)
        {
            return;
        }

        ThrowIfNotMainThread();
        unsafe
        {
            // SDL falls back to the default cursor when the active one is destroyed.
            SDL_DestroyCursor(cursor.Handle);
        }
        cursor.Invalidate();
        _cursors.Remove(cursor);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ThrowIfNotMainThread();
        foreach (Cursor cursor in _cursors)
        {
            unsafe
            {
                SDL_DestroyCursor(cursor.Handle);
            }
            cursor.Invalidate();
        }
        _cursors.Clear();
        _disposed = true;
    }

    private Cursor Track(Pointer<SDL_Cursor> handle, string function)
    {
        if (handle.IsNull)
        {
            throw new PixelyException($"{function} failed: {SdlError.GetError()}");
        }

        Cursor cursor = new(this, handle);
        _cursors.Add(cursor);
        return cursor;
    }

    private void ThrowIfUnusable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfNotMainThread();
    }

    private static void ThrowIfNotMainThread()
    {
        if (!SDL_IsMainThread())
        {
            throw new PixelyException("Cursors must be used from the main thread.");
        }
    }
}
