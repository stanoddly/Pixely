using Pixely.Content;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Input;

// A zero duration stops the animation on its frame.
public readonly record struct CursorFrame(Image Image, TimeSpan Duration);

public sealed class Cursor : IDisposable
{
    private Pointer<SDL_Cursor> _handle;

    internal Cursor(ICursorOwner owner, Pointer<SDL_Cursor> handle)
    {
        Owner = owner;
        _handle = handle;
    }

    internal ICursorOwner Owner { get; }

    internal bool IsDisposed => _handle.IsNull;

    internal Pointer<SDL_Cursor> Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _handle;
        }
    }

    internal void Invalidate()
    {
        _handle = Pointer<SDL_Cursor>.Null;
    }

    public void Dispose()
    {
        Owner.Destroy(this);
    }
}
