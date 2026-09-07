using System.Numerics;
using Pixely.Text;

namespace Pixely.Ui;

/// <summary>
/// A <see cref="TextBox"/> that only takes numbers, refusing what cannot become one while it is
/// still being typed and refusing to finish on anything that is not one.
/// </summary>
/// <remarks>
/// Here rather than left to each application because getting it wrong is invisible: a field that
/// only accepts complete numbers cannot be typed into at all, since "-" and "1." are neither.
/// </remarks>
/// <typeparam name="T">The number type. Its own parsing decides what counts, so a culture that
/// writes decimals with a comma is honoured by <see cref="FormatProvider"/> alone.</typeparam>
public sealed class NumberBox<T> : TextBox
    where T : struct, INumber<T>
{
    public NumberBox(IFont? font = null, IFormatProvider? formatProvider = null)
        : base(font) =>
        FormatProvider = formatProvider;

    /// <summary>Raised when an edit finishes with a value that parses.</summary>
    public event Action<T>? ValueCommitted;

    /// <summary>The culture the value is written and read in. Null means the current one.</summary>
    public IFormatProvider? FormatProvider { get; }

    /// <summary>The committed value, or null when the field is empty or holds something unparseable.</summary>
    public T? Value => TryParseFinite(Text, out T value) ? value : null;

    /// <summary>Writes <paramref name="value"/> into the field, formatted for its culture.</summary>
    public void SetValue(T value) => Text = value.ToString(null, FormatProvider);

    /// <summary>
    /// Accepts anything that is a number, is empty, or becomes one with a digit appended. That last
    /// case is what lets "-", "1." and "1e" be typed: they are on the way to a number, and refusing
    /// them would make the number they lead to unreachable.
    /// </summary>
    protected override bool AcceptsEdit(string candidate) =>
        candidate.Length == 0 || TryParseFinite(candidate, out T _) || TryParseFinite(candidate + "0", out T _);

    /// <summary>
    /// Only a complete, finite number finishes an edit. Infinity and NaN are parseable and are still
    /// refused: they are not values a field like this is asking for.
    /// </summary>
    protected override bool CanCommit(string text) => TryParseFinite(text, out T _);

    protected override void OnCommitted(string text)
    {
        if (TryParseFinite(text, out T value))
        {
            ValueCommitted?.Invoke(value);
        }
    }

    private bool TryParseFinite(string text, out T value) =>
        T.TryParse(text, FormatProvider, out value) && T.IsFinite(value);
}
