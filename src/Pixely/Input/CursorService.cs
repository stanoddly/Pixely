using Pixely.Content;
using Pixely.Utilities;
using SDL;

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
            return SDL3.SDL_CursorVisible();
        }
        set
        {
            ThrowIfUnusable();
            if (value ? !SDL3.SDL_ShowCursor() : !SDL3.SDL_HideCursor())
            {
                throw new PixelyException($"{(value ? "SDL_ShowCursor" : "SDL_HideCursor")} failed: {SDL3.SDL_GetError()}");
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
            return Track(SDL3.SDL_CreateSystemCursor((SDL_SystemCursor)shape), "SDL_CreateSystemCursor");
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

        uint[] durations = new uint[frames.Length];
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

            durations[i] = ToMilliseconds(frames[i].Duration, i);
        }

        if (hotspot.X < 0 || hotspot.Y < 0 || hotspot.X >= size.Width || hotspot.Y >= size.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(hotspot), hotspot, $"The hotspot must lie within the {size.Width}x{size.Height} cursor image.");
        }

        unsafe
        {
            SDL_CursorFrameInfo[] frameInfos = new SDL_CursorFrameInfo[frames.Length];
            try
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    frameInfos[i].surface = ImageSurfaces.CreateCopy(frames[i].Image, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888);
                    frameInfos[i].duration = durations[i];
                }

                fixed (SDL_CursorFrameInfo* frameInfosPointer = frameInfos)
                {
                    // SDL makes a static cursor out of one frame.
                    return Track(SDL3.SDL_CreateAnimatedCursor(frameInfosPointer, frameInfos.Length, hotspot.X, hotspot.Y), "SDL_CreateAnimatedCursor");
                }
            }
            finally
            {
                // A native cursor that keeps a surface holds its own reference to it, so this releases only Pixely's.
                foreach (SDL_CursorFrameInfo frameInfo in frameInfos)
                {
                    if (frameInfo.surface != null)
                    {
                        SDL3.SDL_DestroySurface(frameInfo.surface);
                    }
                }
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
            if (!SDL3.SDL_SetCursor(cursor.Handle))
            {
                throw new PixelyException($"SDL_SetCursor failed: {SDL3.SDL_GetError()}");
            }
        }
    }

    public void ResetCursor()
    {
        ThrowIfUnusable();
        unsafe
        {
            Pointer<SDL_Cursor> defaultCursor = SDL3.SDL_GetDefaultCursor();
            if (defaultCursor.IsNull)
            {
                throw new PixelyException($"SDL_GetDefaultCursor failed: {SDL3.SDL_GetError()}");
            }

            if (!SDL3.SDL_SetCursor(defaultCursor))
            {
                throw new PixelyException($"SDL_SetCursor failed: {SDL3.SDL_GetError()}");
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
            SDL3.SDL_DestroyCursor(cursor.Handle);
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
                SDL3.SDL_DestroyCursor(cursor.Handle);
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
            throw new PixelyException($"{function} failed: {SDL3.SDL_GetError()}");
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
        if (!SDL3.SDL_IsMainThread())
        {
            throw new PixelyException("Cursors must be used from the main thread.");
        }
    }
}
