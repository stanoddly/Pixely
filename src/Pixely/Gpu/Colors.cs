using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// A color as stored in images and authored by hand: red, green and blue encoded with the sRGB transfer function, alpha linear.
/// Converts implicitly to <see cref="LinearColor"/>, which is what shaders and the GPU's blending work with.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct SrgbColor(byte Red, byte Green, byte Blue, byte Alpha)
{
    public static implicit operator SDL_Color(SrgbColor color) => Unsafe.As<SrgbColor, SDL_Color>(ref color);
    public static implicit operator SrgbColor(SDL_Color color) => Unsafe.As<SDL_Color, SrgbColor>(ref color);

    // Decoding loses nothing and never throws, so the conversion is implicit.
    public static implicit operator LinearColor(SrgbColor color) =>
        new(SrgbTransferFunction.Decode(color.Red), SrgbTransferFunction.Decode(color.Green), SrgbTransferFunction.Decode(color.Blue), color.Alpha / 255f);

    public static SrgbColor FromRgb(uint value)
    {
        return new SrgbColor(
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF),
            255
        );
    }
}

/// <summary>
/// A color in linear light with the sRGB primaries: what shaders output, what clear values and uniforms take, and what the GPU blends.
/// Alpha is linear in both color types.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct LinearColor(float Red, float Green, float Blue, float Alpha)
{
    public float R => Red;
    public float G => Green;
    public float B => Blue;
    public float A => Alpha;

    public static implicit operator SDL_FColor(LinearColor color) => Unsafe.As<LinearColor, SDL_FColor>(ref color);
    public static implicit operator LinearColor(SDL_FColor color) => Unsafe.As<SDL_FColor, LinearColor>(ref color);

    // Encoding rounds to bytes and clamps, so the conversion is explicit.
    public static explicit operator SrgbColor(LinearColor color) =>
        new(SrgbTransferFunction.Encode(color.Red), SrgbTransferFunction.Encode(color.Green), SrgbTransferFunction.Encode(color.Blue), SrgbTransferFunction.ToByte(color.Alpha));

    public static explicit operator Vector4(LinearColor color) => new(color.Red, color.Green, color.Blue, color.Alpha);
    public static explicit operator LinearColor(Vector4 vector) => new(vector.X, vector.Y, vector.Z, vector.W);
}

/// <summary>The sRGB transfer function of IEC 61966-2-1, applied to red, green and blue only.</summary>
internal static class SrgbTransferFunction
{
    // An 8-bit input has 256 values, so a table of the decoded values, each rounded once to float, is exact and faster than the formula.
    private static readonly float[] _decodeTable = CreateDecodeTable();

    public static float Decode(byte encoded) => _decodeTable[encoded];

    public static byte Encode(float linear)
    {
        double clamped = Clamp(linear);
        double encoded = clamped <= 0.0031308 ? clamped * 12.92 : 1.055 * Math.Pow(clamped, 1.0 / 2.4) - 0.055;
        return (byte)Math.Round(encoded * 255.0, MidpointRounding.AwayFromZero);
    }

    public static byte ToByte(float value) => (byte)Math.Round(Clamp(value) * 255.0, MidpointRounding.AwayFromZero);

    // NaN first, because clamping NaN leaves it NaN; the clamp also maps the infinities to 0 and 1.
    private static double Clamp(float value) => float.IsNaN(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);

    private static float[] CreateDecodeTable()
    {
        float[] table = new float[256];
        for (int i = 0; i < table.Length; i++)
        {
            double encoded = i / 255.0;
            table[i] = (float)(encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4));
        }

        return table;
    }
}

/// <summary>Named colors as authored, in sRGB. <see cref="LinearColors"/> holds the same palette in linear light.</summary>
public static class SrgbColors
{
    public static readonly SrgbColor AliceBlue = new(240, 248, 255, 255);
    public static readonly SrgbColor AntiqueWhite = new(250, 235, 215, 255);
    public static readonly SrgbColor Aqua = new(0, 255, 255, 255);
    public static readonly SrgbColor Aquamarine = new(127, 255, 212, 255);
    public static readonly SrgbColor Azure = new(240, 255, 255, 255);
    public static readonly SrgbColor Beige = new(245, 245, 220, 255);
    public static readonly SrgbColor Black = new(0, 0, 0, 255);
    public static readonly SrgbColor Blue = new(0, 0, 255, 255);
    public static readonly SrgbColor Brown = new(165, 42, 42, 255);
    public static readonly SrgbColor Coral = new(255, 127, 80, 255);
    public static readonly SrgbColor Crimson = new(220, 20, 60, 255);
    public static readonly SrgbColor Cyan = new(0, 255, 255, 255);
    public static readonly SrgbColor DarkBlue = new(0, 0, 139, 255);
    public static readonly SrgbColor DarkGrey = new(169, 169, 169, 255);
    public static readonly SrgbColor DarkGreen = new(0, 100, 0, 255);
    public static readonly SrgbColor DarkRed = new(139, 0, 0, 255);
    public static readonly SrgbColor DeepPink = new(255, 20, 147, 255);
    public static readonly SrgbColor ForestGreen = new(34, 139, 34, 255);
    public static readonly SrgbColor Gold = new(255, 215, 0, 255);
    public static readonly SrgbColor Grey = new(128, 128, 128, 255);
    public static readonly SrgbColor Green = new(0, 255, 0, 255);
    public static readonly SrgbColor HotPink = new(255, 105, 180, 255);
    public static readonly SrgbColor Indigo = new(75, 0, 130, 255);
    public static readonly SrgbColor Ivory = new(255, 255, 240, 255);
    public static readonly SrgbColor LightBlue = new(173, 216, 230, 255);
    public static readonly SrgbColor LightGrey = new(211, 211, 211, 255);
    public static readonly SrgbColor LightGreen = new(144, 238, 144, 255);
    public static readonly SrgbColor LightPink = new(255, 182, 193, 255);
    public static readonly SrgbColor Lime = new(0, 255, 0, 255);
    public static readonly SrgbColor Magenta = new(255, 0, 255, 255);
    public static readonly SrgbColor Maroon = new(128, 0, 0, 255);
    public static readonly SrgbColor Navy = new(0, 0, 128, 255);
    public static readonly SrgbColor Olive = new(128, 128, 0, 255);
    public static readonly SrgbColor Orange = new(255, 165, 0, 255);
    public static readonly SrgbColor Pink = new(255, 192, 203, 255);
    public static readonly SrgbColor Purple = new(128, 0, 128, 255);
    public static readonly SrgbColor Red = new(255, 0, 0, 255);
    public static readonly SrgbColor RoyalBlue = new(65, 105, 225, 255);
    public static readonly SrgbColor Silver = new(192, 192, 192, 255);
    public static readonly SrgbColor SkyBlue = new(135, 206, 235, 255);
    public static readonly SrgbColor Snow = new(255, 250, 250, 255);
    public static readonly SrgbColor Teal = new(0, 128, 128, 255);
    public static readonly SrgbColor Transparent = new(0, 0, 0, 0);
    public static readonly SrgbColor Turquoise = new(64, 224, 208, 255);
    public static readonly SrgbColor Violet = new(238, 130, 238, 255);
    public static readonly SrgbColor White = new(255, 255, 255, 255);
    public static readonly SrgbColor Yellow = new(255, 255, 0, 255);
}

/// <summary>
/// The <see cref="SrgbColors"/> palette in linear light, for clear values, uniform and storage-buffer colors. Each value is converted from
/// its <see cref="SrgbColors"/> twin, so the palette is defined once.
/// </summary>
public static class LinearColors
{
    public static readonly LinearColor AliceBlue = SrgbColors.AliceBlue;
    public static readonly LinearColor AntiqueWhite = SrgbColors.AntiqueWhite;
    public static readonly LinearColor Aqua = SrgbColors.Aqua;
    public static readonly LinearColor Aquamarine = SrgbColors.Aquamarine;
    public static readonly LinearColor Azure = SrgbColors.Azure;
    public static readonly LinearColor Beige = SrgbColors.Beige;
    public static readonly LinearColor Black = SrgbColors.Black;
    public static readonly LinearColor Blue = SrgbColors.Blue;
    public static readonly LinearColor Brown = SrgbColors.Brown;
    public static readonly LinearColor Coral = SrgbColors.Coral;
    public static readonly LinearColor Crimson = SrgbColors.Crimson;
    public static readonly LinearColor Cyan = SrgbColors.Cyan;
    public static readonly LinearColor DarkBlue = SrgbColors.DarkBlue;
    public static readonly LinearColor DarkGrey = SrgbColors.DarkGrey;
    public static readonly LinearColor DarkGreen = SrgbColors.DarkGreen;
    public static readonly LinearColor DarkRed = SrgbColors.DarkRed;
    public static readonly LinearColor DeepPink = SrgbColors.DeepPink;
    public static readonly LinearColor ForestGreen = SrgbColors.ForestGreen;
    public static readonly LinearColor Gold = SrgbColors.Gold;
    public static readonly LinearColor Grey = SrgbColors.Grey;
    public static readonly LinearColor Green = SrgbColors.Green;
    public static readonly LinearColor HotPink = SrgbColors.HotPink;
    public static readonly LinearColor Indigo = SrgbColors.Indigo;
    public static readonly LinearColor Ivory = SrgbColors.Ivory;
    public static readonly LinearColor LightBlue = SrgbColors.LightBlue;
    public static readonly LinearColor LightGrey = SrgbColors.LightGrey;
    public static readonly LinearColor LightGreen = SrgbColors.LightGreen;
    public static readonly LinearColor LightPink = SrgbColors.LightPink;
    public static readonly LinearColor Lime = SrgbColors.Lime;
    public static readonly LinearColor Magenta = SrgbColors.Magenta;
    public static readonly LinearColor Maroon = SrgbColors.Maroon;
    public static readonly LinearColor Navy = SrgbColors.Navy;
    public static readonly LinearColor Olive = SrgbColors.Olive;
    public static readonly LinearColor Orange = SrgbColors.Orange;
    public static readonly LinearColor Pink = SrgbColors.Pink;
    public static readonly LinearColor Purple = SrgbColors.Purple;
    public static readonly LinearColor Red = SrgbColors.Red;
    public static readonly LinearColor RoyalBlue = SrgbColors.RoyalBlue;
    public static readonly LinearColor Silver = SrgbColors.Silver;
    public static readonly LinearColor SkyBlue = SrgbColors.SkyBlue;
    public static readonly LinearColor Snow = SrgbColors.Snow;
    public static readonly LinearColor Teal = SrgbColors.Teal;
    public static readonly LinearColor Transparent = SrgbColors.Transparent;
    public static readonly LinearColor Turquoise = SrgbColors.Turquoise;
    public static readonly LinearColor Violet = SrgbColors.Violet;
    public static readonly LinearColor White = SrgbColors.White;
    public static readonly LinearColor Yellow = SrgbColors.Yellow;
}
