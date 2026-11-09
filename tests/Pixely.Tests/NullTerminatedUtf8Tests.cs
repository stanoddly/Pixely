using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixely.Tests;

public sealed class NullTerminatedUtf8Tests
{
    [Test]
    public void Null_IsADefaultSpan()
    {
        using NullTerminatedUtf8 utf8 = new NullTerminatedUtf8(null);

        Assert.That(Unsafe.IsNullRef(ref MemoryMarshal.GetReference(utf8.Span)), Is.True);
    }

    [Test]
    public void Empty_IsTheTerminatorAlone()
    {
        using NullTerminatedUtf8 utf8 = new NullTerminatedUtf8("");

        Assert.That(utf8.Span.ToArray(), Is.EqualTo(new byte[] { 0 }));
    }

    [Test]
    public void Text_IsUtf8WithTheTerminator()
    {
        using NullTerminatedUtf8 utf8 = new NullTerminatedUtf8("Žluťoučký kůň 🐎");

        Assert.That(utf8.Span.ToArray(), Is.EqualTo("Žluťoučký kůň 🐎\0"u8.ToArray()));
    }
}
