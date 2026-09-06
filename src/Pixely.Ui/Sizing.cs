namespace Pixely.Ui;

public enum SizingMode
{
    /// <summary>Size to content.</summary>
    Fit,

    /// <summary>An exact pixel size, honoured even when it exceeds the space offered.</summary>
    Fixed,

    /// <summary>Share of the space left over after non-growing siblings have been measured.</summary>
    Grow,

    /// <summary>A fraction of the parent's content extent.</summary>
    Percent
}

/// <summary>
/// How an element sizes itself on one axis. Construct through the factories; the combinations
/// they produce are the only valid ones.
/// </summary>
public readonly record struct Sizing
{
    private Sizing(SizingMode mode, int pixels, float factor)
    {
        Mode = mode;
        Pixels = pixels;
        Factor = factor;
    }

    public SizingMode Mode { get; }

    /// <summary>Pixel size for <see cref="SizingMode.Fixed"/>; zero otherwise.</summary>
    public int Pixels { get; }

    /// <summary>Weight for <see cref="SizingMode.Grow"/> or fraction for <see cref="SizingMode.Percent"/>; zero otherwise.</summary>
    public float Factor { get; }

    public static Sizing Fit => default;

    public static Sizing Fixed(int pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pixels);
        return new Sizing(SizingMode.Fixed, pixels, 0f);
    }

    public static Sizing Grow(float weight = 1f)
    {
        if (!float.IsFinite(weight) || weight <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "Grow weight must be finite and greater than zero.");
        }

        return new Sizing(SizingMode.Grow, 0, weight);
    }

    public static Sizing Percent(float fraction)
    {
        if (!float.IsFinite(fraction) || fraction <= 0f || fraction > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Percent fraction must lie in (0, 1].");
        }

        return new Sizing(SizingMode.Percent, 0, fraction);
    }

    /// <summary>
    /// Grow and Percent both need a number the parent has already committed to. On an indefinite
    /// axis there is no such number, so they degrade to Fit rather than resolving circularly.
    /// </summary>
    internal Sizing ResolveFor(bool axisIsDefinite)
    {
        if (axisIsDefinite)
        {
            return this;
        }

        return Mode is SizingMode.Grow or SizingMode.Percent ? Fit : this;
    }

    public override string ToString() => Mode switch
    {
        SizingMode.Fixed => $"Fixed({Pixels})",
        SizingMode.Grow => $"Grow({Factor})",
        SizingMode.Percent => $"Percent({Factor})",
        _ => "Fit"
    };
}
