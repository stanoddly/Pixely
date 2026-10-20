using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.Gpu;
using SDL;

namespace Pixely.Tests;

public class ColorConversionTests
{
    [Test]
    public void SrgbToLinearAndBack_EveryByte_RoundTrips()
    {
        for (int value = 0; value <= 255; value++)
        {
            SrgbColor color = new((byte)value, (byte)value, (byte)value, (byte)value);
            LinearColor linear = color;

            Assert.That((SrgbColor)linear, Is.EqualTo(color), $"byte {value}");
        }
    }

    [Test]
    public void SrgbToLinear_Byte10_UsesTheLinearSegment()
    {
        LinearColor linear = new SrgbColor(10, 10, 10, 255);

        Assert.That(linear.Red, Is.EqualTo(10.0 / 255.0 / 12.92).Within(1e-7));
    }

    [Test]
    public void SrgbToLinear_Byte11_UsesThePowerCurve()
    {
        LinearColor linear = new SrgbColor(11, 11, 11, 255);

        Assert.That(linear.Red, Is.EqualTo(Math.Pow((11.0 / 255.0 + 0.055) / 1.055, 2.4)).Within(1e-7));
    }

    [Test]
    public void SrgbToLinear_KnownValues_MatchTheStandard()
    {
        LinearColor linear = new SrgbColor(0, 128, 255, 255);

        Assert.Multiple(() =>
        {
            Assert.That(linear.Red, Is.EqualTo(0f));
            Assert.That(linear.Green, Is.EqualTo(0.2158605).Within(1e-6));
            Assert.That(linear.Blue, Is.EqualTo(1f));
        });
    }

    [Test]
    public void SrgbToLinear_Alpha_IsLinearAndIndependentOfColor()
    {
        LinearColor dark = new SrgbColor(0, 0, 0, 128);
        LinearColor bright = new SrgbColor(255, 255, 255, 128);

        Assert.Multiple(() =>
        {
            Assert.That(dark.Alpha, Is.EqualTo(128 / 255f));
            Assert.That(bright.Alpha, Is.EqualTo(128 / 255f));
        });
    }

    [Test]
    public void LinearToSrgb_Alpha_IsLinear()
    {
        SrgbColor color = (SrgbColor)new LinearColor(0f, 0f, 0f, 0.25f);

        Assert.That(color.Alpha, Is.EqualTo(64));
    }

    [Test]
    public void LinearToSrgb_HalfwayAlpha_RoundsAwayFromZero()
    {
        // 0.5 * 255 is 127.5 exactly.
        SrgbColor color = (SrgbColor)new LinearColor(0f, 0f, 0f, 0.5f);

        Assert.That(color.Alpha, Is.EqualTo(128));
    }

    [TestCase(float.NaN, 0)]
    [TestCase(-0.5f, 0)]
    [TestCase(float.NegativeInfinity, 0)]
    [TestCase(1.5f, 255)]
    [TestCase(float.PositiveInfinity, 255)]
    public void LinearToSrgb_ValueOutsideZeroToOne_IsClampedInEveryChannel(float value, int expected)
    {
        SrgbColor color = (SrgbColor)new LinearColor(value, value, value, value);

        Assert.That(color, Is.EqualTo(new SrgbColor((byte)expected, (byte)expected, (byte)expected, (byte)expected)));
    }

    [Test]
    public void Sizes_MatchTheirGpuLayouts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Unsafe.SizeOf<SrgbColor>(), Is.EqualTo(4));
            Assert.That(Unsafe.SizeOf<LinearColor>(), Is.EqualTo(16));
        });
    }

    [Test]
    public void SdlColorConversions_KeepEveryField()
    {
        SDL_Color sdlColor = new SrgbColor(1, 2, 3, 4);
        SrgbColor color = sdlColor;

        Assert.Multiple(() =>
        {
            Assert.That((sdlColor.r, sdlColor.g, sdlColor.b, sdlColor.a), Is.EqualTo(((byte)1, (byte)2, (byte)3, (byte)4)));
            Assert.That(color, Is.EqualTo(new SrgbColor(1, 2, 3, 4)));
        });
    }

    [Test]
    public void SdlFColorConversions_KeepEveryField()
    {
        SDL_FColor sdlColor = new LinearColor(0.1f, 0.2f, 0.3f, 0.4f);
        LinearColor color = sdlColor;

        Assert.Multiple(() =>
        {
            Assert.That((sdlColor.r, sdlColor.g, sdlColor.b, sdlColor.a), Is.EqualTo((0.1f, 0.2f, 0.3f, 0.4f)));
            Assert.That(color, Is.EqualTo(new LinearColor(0.1f, 0.2f, 0.3f, 0.4f)));
        });
    }

    [Test]
    public void LinearColors_MatchSrgbColorsByName()
    {
        FieldInfo[] srgbFields = typeof(SrgbColors).GetFields(BindingFlags.Public | BindingFlags.Static);
        FieldInfo[] linearFields = typeof(LinearColors).GetFields(BindingFlags.Public | BindingFlags.Static);

        Assert.That(linearFields.Select(field => field.Name), Is.EquivalentTo(srgbFields.Select(field => field.Name)));
        foreach (FieldInfo srgbField in srgbFields)
        {
            LinearColor expected = (SrgbColor)srgbField.GetValue(null)!;
            Assert.That(typeof(LinearColors).GetField(srgbField.Name)!.GetValue(null), Is.EqualTo(expected), srgbField.Name);
        }
    }

    [Test]
    public void Vector4Conversions_KeepEveryComponentWithoutClamping()
    {
        LinearColor color = new(-1f, 2f, 0.5f, 3f);

        Assert.Multiple(() =>
        {
            Assert.That((Vector4)color, Is.EqualTo(new Vector4(-1f, 2f, 0.5f, 3f)));
            Assert.That((LinearColor)new Vector4(-1f, 2f, 0.5f, 3f), Is.EqualTo(color));
        });
    }
}
