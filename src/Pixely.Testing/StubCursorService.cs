using Pixely.Content;
using Pixely.Input;
using Pixely.Utilities;
using XdlSharp;

namespace Pixely.Testing;

// Cursors without a platform: it checks what Pixely's cursor service checks, and keeps the visibility and the cursors it created.
// System cursors are created too, which SDL's dummy video driver cannot.
internal sealed class StubCursorService : ICursorService, ICursorOwner, IDisposable
{
    private readonly HashSet<Cursor> _cursors = new();
    private long _lastHandle;
    private bool _cursorVisible = true;
    private bool _disposed;

    public bool IsCursorVisible
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _cursorVisible;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cursorVisible = value;
        }
    }

    public Cursor CreateSystemCursor(SystemCursor shape)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(shape))
        {
            throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown system cursor.");
        }

        return Track();
    }

    public Cursor CreateCursor(Image image, Vector2Int hotspot)
    {
        ArgumentNullException.ThrowIfNull(image);
        return CreateCursor([new CursorFrame(image, TimeSpan.Zero)], hotspot);
    }

    public Cursor CreateCursor(ReadOnlySpan<CursorFrame> frames, Vector2Int hotspot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CursorService.GetFrameDurations(frames, hotspot);
        return Track();
    }

    public void SetCursor(Cursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (cursor.Owner != this)
        {
            throw new ArgumentException("The cursor was created by another cursor service.", nameof(cursor));
        }

        ObjectDisposedException.ThrowIf(cursor.IsDisposed, cursor);
    }

    public void ResetCursor()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    void ICursorOwner.Destroy(Cursor cursor)
    {
        if (cursor.IsDisposed)
        {
            return;
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

        foreach (Cursor cursor in _cursors)
        {
            cursor.Invalidate();
        }

        _cursors.Clear();
        _disposed = true;
    }

    private Cursor Track()
    {
        Pointer<SDL_Cursor> handle;
        unsafe
        {
            handle = (SDL_Cursor*)(nint)Interlocked.Increment(ref _lastHandle);
        }

        Cursor cursor = new(this, handle);
        _cursors.Add(cursor);
        return cursor;
    }
}
