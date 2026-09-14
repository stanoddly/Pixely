using System.Numerics;

namespace Pixely.Ui;

/// <summary>
/// An element that takes the mouse wheel. The root owns hit testing and finds the target, so an
/// implementer only decides how far it can move.
/// </summary>
/// <remarks>
/// <para>
/// The wheel goes to the topmost element under the pointer, of any kind, and from there to its
/// ancestors: a dialog over a list does not scroll the list, and a wheel over a button inside a
/// list does. Each target along the way is offered what is left of the delta and takes the axes
/// it can use; the rest carries on upward, and then to whatever is outside the UI.
/// </para>
/// <para>
/// Nothing is captured and nothing is remembered between wheels. A target that wants to
/// accumulate — fractions from a touchpad too small to move a pixel — keeps that itself.
/// </para>
/// </remarks>
public interface IScrollTarget
{
    /// <param name="delta">
    /// How far the user asked to scroll, in wheel notches. Positive Y asks for content before the
    /// current position (up) and positive X for content before it horizontally (left), which is
    /// what a conventional wheel rolled away from the user produces. Fractional from a touchpad.
    /// The value is what the platform delivers after the user's own settings, natural scrolling
    /// included.
    /// </param>
    /// <returns>
    /// The axes on which this element took the delta. Taking it includes banking a fraction too
    /// small to move a pixel yet; what is refused is a component that points past an end the
    /// element is already at, or along an axis it does not scroll. A refused component reaches
    /// the ancestors, while a taken one stops here.
    /// </returns>
    ScrollAxes OnScroll(Vector2Int position, Vector2 delta);
}
