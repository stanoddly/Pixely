using System.Collections.Immutable;
using System.Globalization;

namespace Pixely.Text;

// What a font is loaded with, and the key both font systems cache it by. The settings are kept in the comma-separated syntax that
// XDL_SetFontVariations and XDL_SetFontFeatures parse.
internal readonly record struct FontKey(string Path, float Size, FontRasterizationMode RasterizationMode, FontHintingMode HintingMode, FontStyle Style,
    string VariationSettings, string FeatureSettings, float LetterSpacing)
{
    // FreeType raises a size below 1 px to 1 px, and the old ushort size stopped at 65535.
    private const float MinSize = 1;
    private const float MaxSize = 65535;
    // XDL_SetFontCharSpacing takes this range, so the spacing in 26.6 fixed point fits an int.
    internal const float MinLetterSpacing = -32768;
    internal const float MaxLetterSpacing = 32767;

    // Returns the key with the lists it was made from, without repeated tags and sorted by tag, so lists that differ only in order or in
    // an overridden tag give the same font. HarfBuzz applies the last of repeated tags, and the order of different tags does not matter.
    public static (FontKey Key, ImmutableArray<FontVariation> Variations, ImmutableArray<FontFeature> Features) Create(string path, float size,
        FontRasterizationMode rasterizationMode, FontHintingMode hintingMode, FontStyle style, IReadOnlyList<FontVariation>? variations,
        IReadOnlyList<FontFeature>? features, float letterSpacing)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!(size >= MinSize && size <= MaxSize))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, $"A font size must be from {MinSize} to {MaxSize} pixels.");
        }

        if (!(letterSpacing >= MinLetterSpacing && letterSpacing <= MaxLetterSpacing))
        {
            throw new ArgumentOutOfRangeException(nameof(letterSpacing), letterSpacing, $"Letter spacing must be from {MinLetterSpacing} to {MaxLetterSpacing} pixels.");
        }

        foreach (FontVariation variation in variations ?? [])
        {
            ThrowIfNotTag(variation.Tag, nameof(variations));
            if (!float.IsFinite(variation.Value))
            {
                throw new ArgumentException($"The value of the font variation '{variation.Tag}' is {variation.Value}, which is not a finite number.", nameof(variations));
            }
        }

        foreach (FontFeature feature in features ?? [])
        {
            ThrowIfNotTag(feature.Tag, nameof(features));
            if (feature.Value < 0)
            {
                throw new ArgumentException($"The value of the font feature '{feature.Tag}' is {feature.Value}, below 0.", nameof(features));
            }
        }

        ImmutableArray<FontVariation> normalizedVariations = Normalize(variations, variation => variation.Tag);
        ImmutableArray<FontFeature> normalizedFeatures = Normalize(features, feature => feature.Tag);
        string variationSettings = string.Join(',', normalizedVariations.Select(variation => $"{variation.Tag}={variation.Value.ToString(CultureInfo.InvariantCulture)}"));
        string featureSettings = string.Join(',', normalizedFeatures.Select(feature => $"{feature.Tag}={feature.Value.ToString(CultureInfo.InvariantCulture)}"));
        FontKey key = new(path, size, rasterizationMode, hintingMode, style, variationSettings, featureSettings, letterSpacing);
        return (key, normalizedVariations, normalizedFeatures);
    }

    private static ImmutableArray<T> Normalize<T>(IReadOnlyList<T>? items, Func<T, string> tag)
    {
        Dictionary<string, T> lastByTag = new(StringComparer.Ordinal);
        foreach (T item in items ?? [])
        {
            lastByTag[tag(item)] = item;
        }

        return [.. lastByTag.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value)];
    }

    // OpenType tags are four bytes. Letters and digits are what every registered tag uses, and they cannot break the list syntax.
    private static void ThrowIfNotTag(string tag, string parameterName)
    {
        if (tag is not { Length: 4 } || !tag.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException($"'{tag}' is not an OpenType tag of four ASCII letters or digits.", parameterName);
        }
    }
}
