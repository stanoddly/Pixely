namespace Pixely.Text;

/// <summary>
/// The value of one axis of a variable font: <c>new FontVariation("wght", 600)</c> sets the weight axis to 600. The tag is the
/// axis's four-letter OpenType tag. A value outside the axis's range is clamped to it.
/// </summary>
public readonly record struct FontVariation(string Tag, float Value);
