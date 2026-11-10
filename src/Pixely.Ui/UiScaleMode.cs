namespace Pixely.Ui;

/// <summary>How a <see cref="UiRoot"/> turns its logical pixels into target pixels, see <see cref="UiRoot.Scale"/>.</summary>
public enum UiScaleMode
{
    /// <summary>
    /// The tree is painted at its logical size and presented at the scale with nearest sampling, so each logical pixel is a
    /// block of target pixels. For pixel art: pixel fonts and sprites stay crisp at an integer scale. The scale must be at least 1.
    /// </summary>
    Pixelated,

    /// <summary>
    /// The tree is laid out at its logical size and painted at the target's: text is rasterised at the scaled size, edges are
    /// rounded to target pixels, and sprites are sampled linearly unless an <see cref="Image"/> chooses otherwise. For a UI of
    /// vector fonts and smooth pictures, at any scale above 0.
    /// </summary>
    Smooth
}
