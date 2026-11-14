using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Audio;

// An audio stream's content as an SDL stream, which SDL_mixer reads while the stream plays. SDL opens it when the stream is
// first attached to a track, and closing it closes the content.
internal unsafe sealed class SdlAudioStream
{
    private readonly Stream _stream;
    private GCHandle _selfHandle;
    private Pointer<SDL_IOStream> _sdlIoStream;

    internal SdlAudioStream(Stream stream)
    {
        _stream = stream;
    }

    internal Pointer<SDL_IOStream> GetOrCreateIoStream()
    {
        if (_sdlIoStream.IsNull)
        {
            _sdlIoStream = CreateIoStream();
        }

        return _sdlIoStream;
    }

    private Pointer<SDL_IOStream> CreateIoStream()
    {
        SDL_IOStreamInterface ioInterface;
        SDL_INIT_INTERFACE(&ioInterface);
        ioInterface.size = &Size;
        ioInterface.seek = &Seek;
        ioInterface.read = &Read;
        ioInterface.write = &Write;
        ioInterface.flush = &Flush;
        ioInterface.close = &Close;

        _selfHandle = GCHandle.Alloc(this);
        Pointer<SDL_IOStream> ioStream = SDL_OpenIO(&ioInterface, GCHandle.ToIntPtr(_selfHandle));
        if (ioStream.IsNull)
        {
            _selfHandle.Free();
            SdlError.ThrowOnNull(ioStream, nameof(SDL_OpenIO));
        }

        return ioStream;
    }

    private void CloseIoStream()
    {
        Pointer<SDL_IOStream> ioStream = _sdlIoStream;
        if (ioStream.IsNull)
        {
            return;
        }

        _sdlIoStream = Pointer<SDL_IOStream>.Null;
        SDL_CloseIO(ioStream);
    }

    // Closes the SDL stream, which then closes the content, or the content when SDL never opened it.
    internal void Close()
    {
        if (_sdlIoStream.IsNull)
        {
            _stream.Dispose();
            return;
        }

        CloseIoStream();
    }

    private void CloseFromSdl()
    {
        _sdlIoStream = Pointer<SDL_IOStream>.Null;
        _stream.Dispose();

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private static SdlAudioStream? GetStream(nint userdata)
    {
        if (userdata == 0)
        {
            return null;
        }

        return GCHandle.FromIntPtr(userdata).Target as SdlAudioStream;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Size(nint userdata)
    {
        try
        {
            SdlAudioStream? audioStream = GetStream(userdata);
            if (audioStream == null || !audioStream._stream.CanSeek)
            {
                return -1;
            }

            return audioStream._stream.Length;
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Seek(nint userdata, long offset, SDL_IOWhence whence)
    {
        try
        {
            SdlAudioStream? audioStream = GetStream(userdata);
            if (audioStream == null || !audioStream._stream.CanSeek)
            {
                return -1;
            }

            SeekOrigin origin = whence switch
            {
                SDL_IOWhence.SDL_IO_SEEK_SET => SeekOrigin.Begin,
                SDL_IOWhence.SDL_IO_SEEK_CUR => SeekOrigin.Current,
                SDL_IOWhence.SDL_IO_SEEK_END => SeekOrigin.End,
                _ => throw new ArgumentOutOfRangeException(nameof(whence), whence, null)
            };

            return audioStream._stream.Seek(offset, origin);
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint Read(nint userdata, nint pointer, nuint size, SDL_IOStatus* status)
    {
        try
        {
            SdlAudioStream? audioStream = GetStream(userdata);
            if (audioStream == null || pointer == 0 || !audioStream._stream.CanRead)
            {
                SetStatus(status, SDL_IOStatus.SDL_IO_STATUS_ERROR);
                return 0;
            }

            int bytesRequested = (int)Math.Min(size, (nuint)int.MaxValue);
            Span<byte> buffer = new((void*)pointer, bytesRequested);
            int bytesRead = audioStream._stream.Read(buffer);

            SetStatus(
                status,
                bytesRead == 0 ? SDL_IOStatus.SDL_IO_STATUS_EOF : SDL_IOStatus.SDL_IO_STATUS_READY);
            return (nuint)bytesRead;
        }
        catch
        {
            SetStatus(status, SDL_IOStatus.SDL_IO_STATUS_ERROR);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint Write(nint userdata, nint pointer, nuint size, SDL_IOStatus* status)
    {
        SetStatus(status, SDL_IOStatus.SDL_IO_STATUS_READONLY);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte Flush(nint userdata, SDL_IOStatus* status)
    {
        SetStatus(status, SDL_IOStatus.SDL_IO_STATUS_READY);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte Close(nint userdata)
    {
        try
        {
            GetStream(userdata)?.CloseFromSdl();
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    private static void SetStatus(SDL_IOStatus* status, SDL_IOStatus value)
    {
        if (status != null)
        {
            *status = value;
        }
    }
}
