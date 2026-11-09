using System.Buffers;
using System.Text;

namespace Pixely;

// A string for an SDL parameter of type const char*: UTF-8 with its terminator inside Span, in a buffer rented for the call.
// A null string is a default span, which the bindings pass as NULL; an empty string is the terminator alone, which is not NULL.
internal ref struct NullTerminatedUtf8
{
    private byte[]? _rentedBuffer;

    public NullTerminatedUtf8(string? text)
    {
        if (text is null)
        {
            return;
        }

        _rentedBuffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(text.Length) + 1);
        int byteCount = Encoding.UTF8.GetBytes(text, _rentedBuffer);
        _rentedBuffer[byteCount] = 0;
        Span = _rentedBuffer.AsSpan(0, byteCount + 1);
    }

    public ReadOnlySpan<byte> Span { get; }

    public void Dispose()
    {
        if (_rentedBuffer is null)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(_rentedBuffer);
        _rentedBuffer = null;
    }
}
